using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>証跡の台本。実時間と入力を使わず、Tick 単位で場面を作って撮る。</summary>
    public sealed class MazeCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        private const int MaxTicks = 40000;
        private MazeGame _game;
        private Autopilot _pilot;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            _game = GetComponent<MazeGame>();
            _pilot = new Autopilot(EvidenceCapture.Seed);
            await capture.WaitUntilAsync(() => _game.HudRoot != null, 10);
            capture.Record("settings", JsonUtility.ToJson(_game.Settings));

            _game.Render();
            await capture.ShootAsync("01-title", "Tick 0");

            _game.Flow.Submit();
            _game.Step();
            await Shoot(capture, "02-ready");

            _game.ShowTargets = true;
            Play(game => game.Phase == GamePhase.Active && game.IsChase && AllRoaming(game), "追跡");
            await Shoot(capture, "03-chase");
            _game.ShowTargets = false;

            Play(game => game.PowerTicksLeft > 0 && Any(game, CatMode.Frightened), "逃走");
            await Shoot(capture, "04-frightened");

            Play(game => Any(game, CatMode.Returning), "帰還");
            await Shoot(capture, "05-returning");

            Idle(game => game.Phase == GamePhase.Dying, "ミス");
            await Shoot(capture, "06-miss");

            Idle(game => game.Phase == GamePhase.GameOver, "ゲームオーバー");
            await Shoot(capture, "07-game-over");

            // クリア場面は猫を止めた設定で作る。通常設定の走破は App テストの対象。
            var calm = _game.Settings.Game.Copy();
            calm.CatSpeed = 1;
            calm.ReleaseTicks = new[] { 0, int.MaxValue, int.MaxValue, int.MaxValue };
            capture.Record("08-clear", "猫を止めた設定で撮影");
            _game.UseConfig(calm);
            _game.Flow.Submit();
            Play(game => game.Phase == GamePhase.Won, "クリア");
            await Shoot(capture, "08-clear");

            _game.UseConfig(_game.Settings.Game);
            _game.Flow.Submit();
            _game.Step();
            await Shoot(capture, "09-replay");
        }

        private async Awaitable Shoot(CaptureContext capture, string name)
        {
            _game.Render();
            await capture.ShootAsync(name, "Tick " + _game.Flow.Game.TickCount);
        }

        private void Play(Func<MazeGameState, bool> until, string scene) =>
            Run(until, scene, () => _pilot.Next(_game.Flow.Game));

        private void Idle(Func<MazeGameState, bool> until, string scene) =>
            Run(until, scene, () => Direction.None);

        private void Run(Func<MazeGameState, bool> until, string scene, Func<Direction> next)
        {
            for (var i = 0; i < MaxTicks; i++)
            {
                if (until(_game.Flow.Game)) { return; }
                if (_game.Flow.Game.IsOver) { break; }
                _game.Step(next());
            }

            if (!until(_game.Flow.Game)) { throw new InvalidOperationException("撮影場面を作れませんでした: " + scene); }
        }

        private static bool AllRoaming(MazeGameState game)
        {
            for (var i = 0; i < MazeGameConfig.CatCount; i++)
            {
                if (game.Cat(i).Mode != CatMode.Roaming) { return false; }
            }

            return true;
        }

        private static bool Any(MazeGameState game, CatMode mode)
        {
            for (var i = 0; i < MazeGameConfig.CatCount; i++)
            {
                if (game.Cat(i).Mode == mode) { return true; }
            }

            return false;
        }
    }
}
