using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>証跡の台本。実時間と入力を使わず、手順どおりに場面を作って撮る。</summary>
    public sealed class SafeDigCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        private const int FlagsToPlace = 6;
        private SafeDigGame _game;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            _game = GetComponent<SafeDigGame>();
            await capture.WaitUntilAsync(() => _game.HudRoot != null, 10);
            capture.Record("settings", JsonUtility.ToJson(_game.Settings));

            _game.Render();
            await capture.ShootAsync("01-title", "タイトル");

            _game.Flow.Submit();
            await Shoot(capture, "02-ready");

            RevealNextSafe();
            await Shoot(capture, "03-cascade");

            for (var i = 0; i < FlagsToPlace; i++)
            {
                Require(AnswerKeyPilot.TryNextMine(Game, out var x, out var y), "旗を立てる爆弾");
                _game.Flow.ToggleFlag(x, y);
                RevealNextSafe();
            }

            await Shoot(capture, "04-flags");

            Require(AnswerKeyPilot.TryNextSafe(Game, out var wrongX, out var wrongY), "誤った旗の場所");
            _game.Flow.ToggleFlag(wrongX, wrongY);
            Require(AnswerKeyPilot.TryNextMine(Game, out var mineX, out var mineY), "踏む爆弾");
            _game.Flow.Reveal(mineX, mineY);
            await Shoot(capture, "05-lost");

            _game.Flow.Submit();
            while (_game.Flow.Screen == AppScreen.Playing) { RevealNextSafe(); }
            await Shoot(capture, "06-won");

            _game.Flow.Submit();
            RevealNextSafe();
            await Shoot(capture, "07-replay");
        }

        private MinesweeperGame Game => _game.Flow.Game;

        private void RevealNextSafe()
        {
            Require(AnswerKeyPilot.TryNextSafe(Game, out var x, out var y), "開く安全なセル");
            _game.Flow.Reveal(x, y);
        }

        private async Awaitable Shoot(CaptureContext capture, string name)
        {
            _game.Render();
            await capture.ShootAsync(name, $"シード {Game.Seed}、未発掘 {Game.SafeCellsLeft}、旗 {Game.FlagCount}");
        }

        private static void Require(bool found, string scene)
        {
            if (!found) { throw new InvalidOperationException("撮影場面を作れませんでした: " + scene); }
        }
    }
}
