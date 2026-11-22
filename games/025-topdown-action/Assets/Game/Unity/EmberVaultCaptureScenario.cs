using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 証跡の台本。実時間と入力を使わず、意図と Tick を順に渡して場面を作って撮る。シードは撮影用の固定値（<see cref="EvidenceCapture.Seed"/>）。
    /// 意図の時刻はすべて 0 にする（時計で Tick を進めず、台本の StepTick だけで進める）。
    /// </summary>
    public sealed class EmberVaultCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        private const int TickLimit = 60 * 60 * 5;
        private const double ClockTime = 0;

        private EmberVaultGame _game;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            _game = GetComponent<EmberVaultGame>();
            await capture.WaitUntilAsync(() => _game.HudRoot != null, 10);
            capture.Record("settings", JsonUtility.ToJson(_game.Settings));

            _game.Render();
            await capture.ShootAsync("01-title", "タイトル（入口の配置を Stage Prefab のまま描く。試合はまだない）");

            Apply(Intent.Of(IntentKind.Submit, ClockTime));
            await Shoot(capture, "02-start", "入口、試合の開始直後");

            _game.Flow.Pilot = new Autopilot();
            StepUntil(() => D.RoomIndex == Rooms.East && Happened(FlowEvents.Rolled), "回避ロール");
            Step(3);
            await Shoot(capture, "03-roll", "東の間。射手の弾を回避ロールでかわす（ロールの始めの 12 Tick は無敵）");

            StepUntil(() => Happened(FlowEvents.ChestOpened), "宝箱");
            Step(2);
            await Shoot(capture, "04-chest", "東の間の宝箱を開けて小鍵を手に入れた");

            StepUntil(() => D.Status == DungeonStatus.Transition && D.TransitionRemaining == _game.Settings.Rules.TransitionTicks / 2, "部屋の切り替え");
            await Shoot(capture, "05-transition", "部屋の切り替え（スクロール）の途中。入力・被弾・敵の動きは止まる");

            StepUntil(() => D.RoomIndex == Rooms.West && D.Hero.IsSwordActive && AnyFlash(), "剣の命中");
            await Shoot(capture, "06-sword", "西の回廊。剣の当たる Tick（不透明の弧）でスライムに命中");

            StepUntil(() => Happened(FlowEvents.RewardShown), "全滅の小鍵");
            Step(2);
            await Shoot(capture, "07-reward", "西の回廊の敵を全滅させ、中央に小鍵が現れた");

            StepUntil(() => Happened(FlowEvents.DoorOpened), "小扉");
            Step(4);
            await Shoot(capture, "08-door", "入口の上の小扉を小鍵 1 本で開けた");

            StepUntil(() => D.RoomIndex == Rooms.Antechamber && D.Status == DungeonStatus.Playing, "前室");
            var pilot = _game.Flow.Pilot;
            _game.Flow.Pilot = null;
            Apply(Intent.Of(IntentKind.TogglePause, ClockTime));
            await Shoot(capture, "09-map", "前室に着いて一時停止した地図（訪れた部屋・開いた扉・閉じた小扉（黄）とボス扉（赤））");
            Apply(Intent.Of(IntentKind.Submit, ClockTime));
            _game.Flow.Pilot = pilot;

            StepUntil(() => D.State.HasBossKey, "ボス鍵");
            Step(2);
            await Shoot(capture, "10-boss-key", "宝物庫の宝箱からボス鍵を手に入れた");

            StepUntil(() => D.RoomIndex == Rooms.Lair && D.Status == DungeonStatus.Playing && D.Run.Projectiles.Count >= 6, "ボス戦");
            Step(6);
            await Shoot(capture, "11-boss", "ボスの間。番人の 8 方向の弾");

            StepUntil(() => Happened(FlowEvents.BossDefeated), "ボス撃破");
            Step(6);
            await Shoot(capture, "12-boss-defeated", "番人を倒した。いた場所に秘宝が現れた");

            StepUntil(() => _game.Flow.Screen == AppScreen.Victory, "勝利");
            await Shoot(capture, "13-victory", "秘宝を取った結果（タイム・倒れた回数）");

            // 新しい試合で、敵に向かって剣を振らずに歩き続けて倒れる。入口から再開する。
            Apply(Intent.Of(IntentKind.Restart, ClockTime));
            _game.Flow.Pilot = new ChasePilot(new Autopilot());
            StepUntil(() => _game.Flow.Screen == AppScreen.GameOver, "ゲームオーバー");
            await Shoot(capture, "14-game-over", "敵に当たり続けて倒れた。入口から再開できる");

            Apply(Intent.Of(IntentKind.Submit, ClockTime));
            Step(2);
            await Shoot(capture, "15-continue", "入口から体力満タンで再開（手に入れた鍵・開けた宝箱はそのまま）");
        }

        private Dungeon D => _game.Flow.Dungeon ?? throw new InvalidOperationException("試合がありません。");

        private bool Happened(FlowEvents events) => (_game.LastEvents & events) != 0;

        private bool AnyFlash()
        {
            foreach (var enemy in D.Run.Enemies)
            {
                if (enemy.IsAlive && enemy.FlashRemaining > 0) { return true; }
            }

            return false;
        }

        /// <summary>入口の外では、いちばん近い敵へ剣を振らずに歩き続ける操縦役（ゲームオーバーの場面を作る）。</summary>
        private sealed class ChasePilot : IPilot
        {
            private readonly IPilot _travel;

            public ChasePilot(IPilot travel) => _travel = travel;

            public HeroInput InputFor(Dungeon dungeon)
            {
                if (dungeon.RoomIndex == Rooms.Entrance || dungeon.Status != DungeonStatus.Playing) { return _travel.InputFor(dungeon); }
                Enemy target = null;
                var best = long.MaxValue;
                var hero = dungeon.Hero.Body;
                foreach (var enemy in dungeon.Run.Enemies)
                {
                    if (!enemy.IsAlive) { continue; }
                    var distance = Math.Abs((long)enemy.Body.CenterX - hero.CenterX) + Math.Abs((long)Boxes.CenterY(enemy.Body) - Boxes.CenterY(hero));
                    if (distance < best)
                    {
                        best = distance;
                        target = enemy;
                    }
                }

                if (target == null) { return _travel.InputFor(dungeon); }
                return new HeroInput(Math.Sign(target.Body.CenterX - hero.CenterX), Math.Sign(Boxes.CenterY(target.Body) - Boxes.CenterY(hero)), false, false);
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
            for (var i = 0; ; i++)
            {
                if (_game.Flow.Dungeon != null && done()) { return; }
                if (i > TickLimit || (_game.Flow.Screen != AppScreen.Playing && !done()))
                {
                    throw new InvalidOperationException("撮影場面を作れませんでした: " + scene);
                }

                _game.Flow.StepTick();
                _game.ConsumeEvents();
            }
        }

        private async Awaitable Shoot(CaptureContext capture, string name, string scene)
        {
            _game.Render();
            var d = _game.Flow.Dungeon;
            var detail = d == null
                ? scene
                : $"{scene}。{d.World.Rooms[d.RoomIndex].Name}、経過 {d.ElapsedTicks} Tick、自機 ({d.Hero.Body.X}, {d.Hero.Body.Y})、体力 {d.Hero.Health}、小鍵 {d.State.SmallKeys}、倒れた {d.Deaths}";
            await capture.ShootAsync(name, detail);
        }
    }
}
