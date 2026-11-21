using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>証跡の台本。実時間と入力を使わず、意図と Tick を順に渡して場面を作って撮る。</summary>
    public sealed class TideClaimCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        private const long TickLimit = 60L * 60 * 20;

        private TideClaimGame _game;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            _game = GetComponent<TideClaimGame>();
            await capture.WaitUntilAsync(() => _game.HudRoot != null, 10);
            capture.Record("settings", JsonUtility.ToJson(_game.Settings));

            _game.Render();
            await capture.ShootAsync("01-title", "タイトル（試合の前の盤。試合はまだない）");

            Apply(Intent.Of(IntentKind.Submit));
            await Shoot(capture, "02-start", "開始直後。自機は下の縁の中央");

            // 自動操縦で 1 本目の線を描かせ、半ばで撮る。
            _game.Flow.Pilot = new Autopilot();
            StepUntil(() => Game.Trail.Count >= 12, "線を描いている途中");
            await Shoot(capture, "03-drawing", "Space を押しながら海へ線を引いている途中");

            StepUntil(() => Game.ClaimedCells > 0, "1 本目を閉じる");
            await Shoot(capture, "04-claimed", "線を閉じ、渦のいない側が陸になった直後");

            Apply(Intent.Of(IntentKind.TogglePause));
            await Shoot(capture, "05-paused", "一時停止のメニュー");

            Apply(Intent.Of(IntentKind.Submit));
            StepUntil(() => _game.Flow.Screen == AppScreen.Result, "勝利");
            Require(Game.Status == GameStatus.Won, "勝利");
            await Shoot(capture, "06-won", "自動操縦で占有率 75% 以上に達した結果");

            // 手で操作する：海へ線を引いて止まり、渦が触れるのを待つ。
            _game.Flow.Pilot = null;
            Apply(Intent.Of(IntentKind.Submit));
            LeaveLineInTheSea();
            StepUntil(() => Game.Lives < _game.Settings.Rules.Lives, "ミス");
            Release();
            await Shoot(capture, "07-miss", "止めた線に渦が触れた直後。線は消え、自機は始点に戻る");

            while (_game.Flow.Screen == AppScreen.Playing)
            {
                LeaveLineInTheSea();
                StepUntil(() => Game.IsOver || !Game.IsDrawing, "ミスを重ねる");
                Release();
            }

            Require(Game.Status == GameStatus.Lost, "敗北");
            await Shoot(capture, "08-lost", "残機がなくなった結果");

            Apply(Intent.Of(IntentKind.Submit));
            await Shoot(capture, "09-next", "結果から次の試合（新しいシード）");
        }

        private QixGame Game => _game.Flow.Game ?? throw new InvalidOperationException("試合がありません。");

        private void Apply(Intent intent) => _game.Flow.Apply(intent);

        // 縁の真上へ 10 セル線を引き、向きのキーを離して描画キーだけを押したままにする。
        private void LeaveLineInTheSea()
        {
            var lives = Game.Lives;
            Apply(Intent.Down(Control.Draw));
            Apply(Intent.Down(Control.Up));
            StepUntil(() => Game.Trail.Count >= 10 || Game.Lives < lives, "線を引く");
            Apply(Intent.Up(Control.Up));
        }

        private void Release()
        {
            Apply(Intent.Up(Control.Up));
            Apply(Intent.Up(Control.Draw));
        }

        private void StepUntil(Func<bool> done, string scene)
        {
            var start = Game.Ticks;
            while (!done())
            {
                if (_game.Flow.Screen != AppScreen.Playing || Game.Ticks - start > TickLimit)
                {
                    throw new InvalidOperationException("撮影場面を作れませんでした: " + scene);
                }

                _game.Flow.StepTick();
            }
        }

        private async Awaitable Shoot(CaptureContext capture, string name, string scene)
        {
            _game.Render();
            var game = _game.Flow.Game;
            var detail = game == null
                ? scene
                : $"{scene}。シード {game.Seed}、Tick {game.Ticks}、陸 {TideClaimGame.Percent(game)}（{game.ClaimedCells}/{game.InitialOpen}）、残機 {game.Lives}、スコア {game.Score}";
            await capture.ShootAsync(name, detail);
        }

        private static void Require(bool condition, string scene)
        {
            if (!condition) { throw new InvalidOperationException("撮影場面を作れませんでした: " + scene); }
        }
    }
}
