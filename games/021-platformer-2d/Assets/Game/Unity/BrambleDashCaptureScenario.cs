using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>証跡の台本。実時間と入力を使わず、意図と Tick を順に渡して場面を作って撮る。乱数は使わない。</summary>
    public sealed class BrambleDashCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        private const int TickLimit = 60 * 60 * 10;

        private BrambleDashGame _game;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            _game = GetComponent<BrambleDashGame>();
            await capture.WaitUntilAsync(() => _game.HudRoot != null, 10);
            capture.Record("settings", JsonUtility.ToJson(_game.Settings));

            _game.Render();
            await capture.ShootAsync("01-title", "タイトル（1 面の配置を Stage Prefab のまま描く。試合はまだない）");

            Apply(Intent.Of(IntentKind.Submit));
            await Shoot(capture, "02-start", "1 面の開始直後");

            // 自動操縦で進め、跳んでいる途中・踏みつけた直後を撮る。
            _game.Flow.Pilot = new Autopilot();
            StepUntil(() => !Run.Player.Grounded && Run.Player.VelocityY > 0 && Run.Player.Body.X > 8 * Units.Tile, "跳ぶ");
            await Shoot(capture, "03-jump", "走りながら跳んでいる途中");

            StepUntil(() => Run.Stomps > 0, "踏みつけ");
            await Shoot(capture, "04-stomp", "敵を上から踏んだ直後（敵はつぶれ、自機は跳ね返る）");

            StepUntil(() => _game.Flow.Screen == AppScreen.Interlude, "1 面クリア");
            Require(Campaign.Status == CampaignStatus.LevelCleared, "1 面クリア");
            await Shoot(capture, "05-clear", "旗の柱に触れて 1 面クリア。残り時間ボーナスと次の面の案内");

            StepUntil(() => Campaign.LevelIndex == 1 && _game.Flow.Screen == AppScreen.Playing && Run.Player.Body.X > 40 * Units.Tile, "2 面");
            await Shoot(capture, "06-level2", "2 面の途中（カメラは自機を追って横に進む）");

            StepUntil(() => Campaign.LevelIndex == 2 && _game.Flow.Screen == AppScreen.Playing && Run.Player.Body.X > 60 * Units.Tile, "3 面");
            await Shoot(capture, "07-level3", "3 面の途中");

            StepUntil(() => _game.Flow.Screen == AppScreen.Result, "勝利");
            Require(Campaign.Status == CampaignStatus.Won, "勝利");
            await Shoot(capture, "08-won", "自動操縦で 3 面をミスなしで抜けた結果");

            // 手で操作する：右へ歩き続けて、最初の歩き虫に横からぶつかる。
            _game.Flow.Pilot = null;
            Apply(Intent.Of(IntentKind.Submit));
            Apply(Intent.Down(Control.Right));
            StepUntil(() => _game.Flow.Screen == AppScreen.Interlude, "ミス");
            Require(Campaign.Status == CampaignStatus.Missed && Run.MissReason == MissReason.Enemy, "横から触れてミス");
            await Shoot(capture, "09-miss", "歩き虫に横から触れてミス。残機が 1 減り、この面の最初からやり直す");

            StepUntil(() => _game.Flow.Screen == AppScreen.Playing, "やり直し");
            Apply(Intent.Of(IntentKind.TogglePause));
            await Shoot(capture, "10-paused", "一時停止のメニュー");

            Apply(Intent.Of(IntentKind.Submit));
            while (_game.Flow.Screen != AppScreen.Result)
            {
                // 一時停止で押していたキーは消えるので、押し直す。面の合間の間は押したキーを持ち越す。
                Apply(Intent.Down(Control.Right));
                StepUntil(() => _game.Flow.Screen != AppScreen.Playing, "ミスを重ねる");
                StepUntil(() => _game.Flow.Screen != AppScreen.Interlude, "面の合間");
            }

            Require(Campaign.Status == CampaignStatus.Lost, "敗北");
            await Shoot(capture, "11-lost", "残機がなくなった結果");
        }

        private Campaign Campaign => _game.Flow.Campaign ?? throw new InvalidOperationException("試合がありません。");

        private LevelRun Run => Campaign.Run;

        private void Apply(Intent intent) => _game.Flow.Apply(intent);

        private void StepUntil(Func<bool> done, string scene)
        {
            for (var i = 0; !done(); i++)
            {
                var screen = _game.Flow.Screen;
                if (i > TickLimit || (screen != AppScreen.Playing && screen != AppScreen.Interlude))
                {
                    throw new InvalidOperationException("撮影場面を作れませんでした: " + scene);
                }

                _game.Flow.StepTick();

                // カメラの追従は描画のたびに進むので、撮影中も Tick ごとに描く。
                _game.Render();
            }
        }

        private async Awaitable Shoot(CaptureContext capture, string name, string scene)
        {
            _game.Render();
            var campaign = _game.Flow.Campaign;
            var detail = campaign == null
                ? scene
                : $"{scene}。{campaign.LevelIndex + 1} 面、Tick {campaign.Run.Ticks}、自機 ({campaign.Run.Player.Body.X}, {campaign.Run.Player.Body.Y})、残機 {campaign.Lives}、スコア {campaign.Score}";
            await capture.ShootAsync(name, detail);
        }

        private static void Require(bool condition, string scene)
        {
            if (!condition) { throw new InvalidOperationException("撮影場面を作れませんでした: " + scene); }
        }
    }
}
