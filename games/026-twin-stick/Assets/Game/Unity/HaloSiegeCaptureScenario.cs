using System;
using System.Linq;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using TileMotion;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 証跡の台本。実時間と入力を使わず、意図と Tick を順に渡して場面を作って撮る。シードは撮影用の固定値（<see cref="EvidenceCapture.Seed"/>）。
    /// 意図の時刻はすべて 0 にする（時計で Tick を進めず、台本の StepTick だけで進める）。
    /// </summary>
    public sealed class HaloSiegeCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        private const double ClockTime = 0;

        private HaloSiegeGame _game;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            _game = GetComponent<HaloSiegeGame>();
            await capture.WaitUntilAsync(() => _game.HudRoot != null, 10);
            capture.Record("settings", JsonUtility.ToJson(_game.Settings));
            var rules = _game.Settings.Rules;

            _game.Render();
            await capture.ShootAsync("01-title", "タイトル（アリーナの中央に自機。試合はまだない）");

            Apply(Intent.Of(IntentKind.Submit, ClockTime));
            _game.Flow.Pilot = new FixedPilot(1, 1, true, new Vec2i(-8 * Units.Tile, -3 * Units.Tile));
            Step(36);
            await Shoot(capture, "02-move-and-aim", "右上へ動きながら、照準（カーソル）の左下へ撃つ。移動と射撃の向きは独立");

            _game.Flow.Pilot = new Autopilot();
            StepUntil(() => R.Enemies.Any(e => !e.IsActive) && R.Enemies.Count(e => e.IsActive) >= 2, "出現の予告");
            await Shoot(capture, "03-warning", "光の輪が出現の予告（予告中は動かず当たらない）。自動操縦が近い敵を撃つ");

            StepUntil(() => Enum.GetValues(typeof(EnemyKind)).Cast<EnemyKind>().All(kind => R.Enemies.Any(e => e.IsActive && e.Kind == kind && R.View.Overlaps(e.Position, e.Radius))), "敵 3 種");
            await Shoot(capture, "04-three-kinds", "画面に敵 3 種（桃：Chaser、緑：Bouncer、橙：Lancer）");

            StepUntil(() => R.Enemies.Any(e => e.Kind == EnemyKind.Lancer && e.Mode == LancerMode.Aim && e.ModeTicks >= 8 && R.View.Overlaps(e.Position, e.Radius)), "Lancer の狙い");
            await Shoot(capture, "05-lancer-aim", "Lancer が止まって狙う間、突進の通り道を筋で示す");

            _game.Flow.Pilot = new ChasePilot();
            StepUntil(() => Happened(FlowEvents.Hurt), "被弾");
            Step(10);
            await Shoot(capture, "06-hurt", "敵に触れて残機が 1 減った。被弾した Tick を含めて 120 Tick は無敵（自機が点滅）");

            Apply(Intent.Of(IntentKind.TogglePause, ClockTime));
            await Shoot(capture, "07-pause", "一時停止");
            Apply(Intent.Of(IntentKind.Submit, ClockTime));

            // 敵の密度の上限：台本で敵を画面の内側に置いて上限まで満たす。上限の間は新しい予告が出ない。
            _game.Flow.Pilot = new FixedPilot(0, 0, false, new Vec2i(Units.Tile, 0));
            FillToCap();
            Step(30);
            await Shoot(capture, "08-density-cap", $"敵を上限の {rules.MaxAlive} 体まで置いた場面（台本で配置）。上限の間は新しい敵が出ない");

            // 新しい試合を自動操縦で 5 分通す。
            Apply(Intent.Of(IntentKind.Restart, ClockTime));
            _game.Flow.Pilot = new Autopilot();
            StepUntil(() => R.Elapsed >= 4 * 60 * Units.TickRate && R.Enemies.Count >= 5, "終盤");
            await Shoot(capture, "09-late", "4 分過ぎ。出現の間隔が短くなった終盤");

            StepUntil(() => _game.Flow.Screen == AppScreen.Result, "勝利");
            await Shoot(capture, "10-victory", "5 分生き残った結果（得点・生き残った時間・撃破数・残機ボーナス）");

            Apply(Intent.Of(IntentKind.Restart, ClockTime));
            _game.Flow.Pilot = new ChasePilot();
            StepUntil(() => _game.Flow.Screen == AppScreen.Result, "敗北");
            await Shoot(capture, "11-defeat", "撃たずに敵へ向かい続けて残機 0。負けの結果");
        }

        private SiegeRun R => _game.Flow.Run ?? throw new InvalidOperationException("試合がありません。");

        private bool Happened(FlowEvents events) => (_game.LastEvents & events) != 0;

        /// <summary>画面の内側で、自機から離れた格子の点に敵を置いて上限まで満たす。</summary>
        private void FillToCap()
        {
            var run = R;
            var rules = _game.Settings.Rules;
            var view = run.View;
            var kinds = new[] { EnemyKind.Chaser, EnemyKind.Bouncer, EnemyKind.Lancer };
            var safe = (long)rules.SafeRadius * rules.SafeRadius;
            var placed = 0;
            for (var y = view.MinY + Units.Tile; y < view.MaxY - Units.Tile && run.Enemies.Count < rules.MaxAlive; y += 3 * Units.Tile)
            {
                for (var x = view.MinX + Units.Tile; x < view.MaxX - Units.Tile && run.Enemies.Count < rules.MaxAlive; x += 3 * Units.Tile)
                {
                    var point = new Vec2i(x, y);
                    if (Vec2i.DistanceSquared(point, run.Ship.Position) < safe) { continue; }
                    run.Place(kinds[placed++ % kinds.Length], point);
                }
            }

            if (run.Enemies.Count != rules.MaxAlive) { throw new InvalidOperationException($"上限まで置けませんでした（{run.Enemies.Count} 体）。"); }
        }

        /// <summary>決まった向きに動き、自機から決まった向きを狙い続ける操縦役。</summary>
        private sealed class FixedPilot : IPilot
        {
            private readonly int _x;
            private readonly int _y;
            private readonly bool _fire;
            private readonly Vec2i _aim;

            public FixedPilot(int x, int y, bool fire, Vec2i aim)
            {
                _x = x;
                _y = y;
                _fire = fire;
                _aim = aim;
            }

            public ShipCommand CommandFor(SiegeRun run) => new(_x, _y, _fire, run.Ship.Position + _aim);
        }

        /// <summary>撃たずに、いちばん近い敵へ向かい続ける操縦役（被弾・敗北の場面を作る）。</summary>
        private sealed class ChasePilot : IPilot
        {
            public ShipCommand CommandFor(SiegeRun run)
            {
                Enemy target = null;
                var best = long.MaxValue;
                foreach (var enemy in run.Enemies)
                {
                    if (!enemy.IsActive) { continue; }
                    var distance = Vec2i.DistanceSquared(enemy.Position, run.Ship.Position);
                    if (distance < best)
                    {
                        best = distance;
                        target = enemy;
                    }
                }

                if (target == null) { return new ShipCommand(0, 0, false, run.Ship.Position + new Vec2i(Units.Tile, 0)); }
                var dx = Math.Sign(target.Position.X - run.Ship.Position.X);
                var dy = Math.Sign(target.Position.Y - run.Ship.Position.Y);
                return new ShipCommand(dx, dy, false, target.Position);
            }
        }

        private void Apply(Intent intent)
        {
            _game.Flow.Apply(intent);
            _game.ConsumeEvents();
        }

        private void Step(int ticks)
        {
            for (var i = 0; i < ticks && _game.Flow.Screen == AppScreen.Playing; i++)
            {
                _game.Flow.StepTick();
                _game.ConsumeEvents();
            }
        }

        /// <summary><paramref name="done"/> が成り立つ Tick まで進める。</summary>
        private void StepUntil(Func<bool> done, string scene)
        {
            while (true)
            {
                if (_game.Flow.Run != null && done()) { return; }
                if (_game.Flow.Screen != AppScreen.Playing) { throw new InvalidOperationException("撮影場面を作れませんでした: " + scene); }
                _game.Flow.StepTick();
                _game.ConsumeEvents();
            }
        }

        private async Awaitable Shoot(CaptureContext capture, string name, string scene)
        {
            _game.Render();
            var run = _game.Flow.Run;
            var detail = run == null
                ? scene
                : $"{scene}。経過 {run.Elapsed} Tick、自機 ({run.Ship.Position.X}, {run.Ship.Position.Y})、残機 {run.Ship.Lives}、得点 {run.Score}、敵 {run.Enemies.Count}";
            await capture.ShootAsync(name, detail);
        }
    }
}
