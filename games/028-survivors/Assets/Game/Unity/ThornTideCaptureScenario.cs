using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using IntGeometry;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 証跡の台本。実時間と入力を使わず、意図と Tick を順に渡して場面を作って撮る。シードは撮影用の固定値（<see cref="EvidenceCapture.Seed"/>）。
    /// 意図の時刻は時計で Tick を進めないよう固定し、台本の StepTick だけで進める。
    /// </summary>
    public sealed class ThornTideCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        private const double ClockTime = 0;

        private ThornTideGame _game;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            _game = GetComponent<ThornTideGame>();
            await capture.WaitUntilAsync(() => _game.HudRoot != null, 10);
            capture.Record("settings", JsonUtility.ToJson(_game.Settings));
            capture.Record("rules", JsonUtility.ToJson(_game.Rules));

            _game.Render();
            await capture.ShootAsync("01-title", "タイトル（操作とルールの説明）");

            Apply(Intent.Of(IntentKind.Submit, ClockTime));
            _game.Flow.Pilot = new Autopilot();
            Step(150);
            await Shoot(capture, "02-start", "開始から 150 Tick。最初の武器（光の矢）が近い敵を自動で撃つ。自動操縦が動かす");

            // レベルアップの 3 択：台本で自機の上に次のレベルの分の宝石を置き、操縦役を外して 3 択の画面を出す。
            var run = R;
            run.PlaceGem(run.Player, run.ExperienceToNext - run.Experience);
            var pilot = _game.Flow.Pilot;
            _game.Flow.Pilot = null;
            _game.Flow.StepTick();
            _game.ConsumeEvents();
            if (_game.Flow.Screen != AppScreen.LevelUp) { throw new InvalidOperationException("3 択の画面になりませんでした。"); }
            await Shoot(capture, "03-level-up", "レベルアップの 3 択（台本で宝石を置いた）。試合は止まっている。1/2/3・クリック・矢印と Enter で選ぶ");
            Apply(Intent.Pick(0, _game.Flow.ChoiceShownAt + 1));
            _game.Flow.Pilot = pilot;

            StepUntil(() => AllWeapons() && Recent(R.Effects.PulseTick, ThornTideStage.EffectTicks) && Recent(R.Effects.LashTick, ThornTideStage.EffectTicks) && Recent(R.Effects.ArcTick, ThornTideStage.EffectTicks), "武器 5 種の発動");
            await Shoot(capture, "04-weapons", "武器 5 種：光の矢（弾）・舞う葉刃（回る刃）・棘の脈動（輪）・蔦の鞭（斬撃）・雷の連鎖（稲妻）");

            StepUntil(() => R.Elapsed >= 4 * 60 * TileMotion.Units.TickRate, "密な群れ");
            await Shoot(capture, "05-crowd", "4 分過ぎ。時間とともに出現の目標（生存数）と頻度が上がり、敵が密になった場面。倒した敵は宝石を落とす");

            var duration = _game.Rules.DurationTicks;
            StepUntil(() => R.BossSpawned && R.BossIndex() >= 0 && R.Elapsed >= duration + 150, "ボス");
            await Shoot(capture, "06-boss", "5 分でボス（茨の番人）が現れる。上部に体力のバー、時間はボスの猶予に変わる");

            StepUntil(() => _game.Flow.Screen == AppScreen.Result, "勝利");
            await Shoot(capture, "07-victory", "ボスを倒した結果（生き残った時間・レベル・撃破・集めた経験値）");

            // 新しい試合（次のシード）で、いちばん近い敵へ向かい続けて被弾する。
            Apply(Intent.Of(IntentKind.Restart, ClockTime));
            _game.Flow.Pilot = new ChasePilot();
            StepUntil(() => Happened(FlowEvents.Hurt), "被弾");
            Step(3);
            await Shoot(capture, "08-hurt", "敵に触れて体力が減った。被弾した Tick を含めて 30 Tick は無敵（自機が半透明に点滅）");

            Apply(Intent.Of(IntentKind.TogglePause, ClockTime));
            await Shoot(capture, "09-pause", "一時停止");
            Apply(Intent.Of(IntentKind.Submit, ClockTime));

            StepUntil(() => _game.Flow.Screen == AppScreen.Result, "敗北");
            await Shoot(capture, "10-defeat", "敵へ向かい続けて体力 0。負けの結果");
        }

        private TideRun R => _game.Flow.Run ?? throw new InvalidOperationException("試合がありません。");

        private bool Happened(FlowEvents events) => (_game.LastEvents & events) != 0;

        private bool Recent(int tick, int within) => tick >= 0 && R.Elapsed - tick < within;

        private bool AllWeapons()
        {
            for (var w = 0; w < _game.Settings.Weapons.Length; w++)
            {
                if (R.WeaponLevelOf(w) == 0) { return false; }
            }

            return true;
        }

        /// <summary>いちばん近い敵へ向かい続ける操縦役（被弾・敗北の場面を作る）。3 択は最初の候補。</summary>
        private sealed class ChasePilot : IPilot
        {
            public PlayerCommand CommandFor(TideRun run)
            {
                var target = -1;
                var best = long.MaxValue;
                for (var i = 0; i < run.Enemies.Count; i++)
                {
                    var distance = Vec2i.DistanceSquared(run.Enemies.Position(i), run.Player);
                    if (distance < best)
                    {
                        best = distance;
                        target = i;
                    }
                }

                if (target < 0) { return PlayerCommand.Idle; }
                var to = run.Enemies.Position(target) - run.Player;
                return new PlayerCommand(Math.Sign(to.X), Math.Sign(to.Y));
            }

            public int ChoiceFor(TideRun run) => 0;
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
                : $"{scene}。経過 {run.Elapsed} Tick、自機 ({run.Player.X}, {run.Player.Y})、体力 {run.Health}、レベル {run.Level}、撃破 {run.Kills}、敵 {run.NormalCount}";
            await capture.ShootAsync(name, detail);
        }
    }
}
