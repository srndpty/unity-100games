using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using TileMotion;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 証跡の台本。実時間と入力を使わず、意図と Tick を順に渡して場面を作って撮る。乱数は使わない。
    /// 意図の時刻はすべて 0 にする（時計で Tick を進めず、台本の StepTick だけで進める）。
    /// </summary>
    public sealed class MossDepthsCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        private const int TickLimit = 60 * 60 * 5;
        private const double ClockTime = 0;

        private MossDepthsGame _game;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            _game = GetComponent<MossDepthsGame>();
            await capture.WaitUntilAsync(() => _game.HudRoot != null, 10);
            capture.Record("settings", JsonUtility.ToJson(_game.Settings));

            _game.Render();
            await capture.ShootAsync("01-title", "タイトル（拠点の配置を Stage Prefab のまま描く。試合はまだない）");

            Apply(Intent.Of(IntentKind.Submit, ClockTime));
            await Shoot(capture, "02-start", "拠点、試合の開始直後");

            // 入力の列（Routes）とボスの操縦役で進め、各場面を撮る。
            _game.Flow.Pilot = Routes.Pilot();
            StepUntil(() => E.RoomIndex == Rooms.Corridor && E.Player.Body.X > 8 * Units.Tile, "回廊");
            await Shoot(capture, "03-corridor", "回廊。歩く敵の間を抜ける");

            StepUntil(() => E.State.Has(Ability.DoubleJump), "二段ジャンプの取得");
            Step(4);
            await Shoot(capture, "04-double-jump-get", "洞穴の穴の底で二段ジャンプを手に入れた（案内が出る）");

            StepUntil(() => E.RoomIndex == Rooms.Cave && E.Player.AirJumpUsed && E.Player.VelocityY > 20, "二段ジャンプ");
            await Shoot(capture, "05-double-jump", "穴の壁（5 タイル）を二段ジャンプで越える途中");

            StepUntil(() => E.RoomIndex == Rooms.Hub && E.RoomEntries >= 5 && E.Player.Body.Y > 7 * Units.Tile, "拠点の高い段");
            await Shoot(capture, "06-ledge", "拠点に戻り、二段ジャンプで高い段から塔へ登る");

            StepUntil(() => E.State.Has(Ability.BreakShot), "破壊ショットの取得");
            Step(4);
            await Shoot(capture, "07-break-shot-get", "塔の台の上で破壊ショットを手に入れた");

            StepUntil(() => E.State.BrokenCount > 0, "ひびの壁");
            Step(6);
            await Shoot(capture, "08-wall-broken", "塔の右の通路、ひびの壁を撃って壊した直後");

            var pilot = _game.Flow.Pilot;
            _game.Flow.Pilot = null;
            Apply(Intent.Of(IntentKind.TogglePause, ClockTime));
            await Shoot(capture, "09-map", "一時停止の地図（訪れた 4 部屋と今いる塔）");
            Apply(Intent.Of(IntentKind.Submit, ClockTime));
            _game.Flow.Pilot = pilot;

            StepUntil(() => E.RoomIndex == Rooms.Lair && E.Run.Bullets.Count >= 3 && E.Run.Boss!.Health < E.Run.Boss.MaxHealth, "ボス戦");
            Step(8);
            await Shoot(capture, "10-boss", "ボスの間。気絶の後の 3 方向の弾を避けながら撃つ");

            StepUntil(() => _game.Flow.Screen == AppScreen.Victory, "勝利");
            await Shoot(capture, "11-victory", "ボスを倒した結果（タイム・力尽きた回数・見つけたもの）");

            // 同じ道順でボスの間まで進み、ボスへ向かって歩き続けて倒れる。最後のセーブ（塔の像）から再開する。
            Apply(Intent.Of(IntentKind.Restart, ClockTime));
            _game.Flow.Pilot = new RoutePilot(Routes.All(), new HoldPilot(new PlayerInput(1, false, false)));
            StepUntil(() => _game.Flow.Screen == AppScreen.GameOver, "ゲームオーバー");
            await Shoot(capture, "12-game-over", "ボスの間で力尽きた。最後に触れたセーブ像から再開できる");

            Apply(Intent.Of(IntentKind.Submit, ClockTime));
            Step(2);
            await Shoot(capture, "13-continue", "塔のセーブ像の前から体力満タンで再開（破壊ショットは残り、セーブの後に壊した壁は元に戻る）");
        }

        private Expedition E => _game.Flow.Expedition ?? throw new InvalidOperationException("試合がありません。");

        /// <summary>同じ入力を押し続ける操縦役。</summary>
        private sealed class HoldPilot : IPilot
        {
            private readonly PlayerInput _input;

            public HoldPilot(PlayerInput input) => _input = input;

            public PlayerInput InputFor(Expedition expedition) => _input;
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
                if (_game.Flow.Expedition != null && done()) { return; }
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
            var e = _game.Flow.Expedition;
            var detail = e == null
                ? scene
                : $"{scene}。{e.World.Rooms[e.RoomIndex].Name}、経過 {e.ElapsedTicks} Tick、自機 ({e.Player.Body.X}, {e.Player.Body.Y})、体力 {e.Player.Health}、力尽きた {e.Deaths}";
            await capture.ShootAsync(name, detail);
        }
    }
}
