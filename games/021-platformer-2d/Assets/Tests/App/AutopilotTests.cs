using System.Diagnostics;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class AutopilotTests
    {
        /// <summary>自動操縦で 1 面を最初から挑戦し、終わった状態を返す。</summary>
        internal static LevelRun Play(Level level, PlatformerConfig? config = null)
        {
            var run = new LevelRun(config ?? new PlatformerConfig(), level);
            var pilot = new Autopilot();
            while (!run.IsOver)
            {
                run.Tick(pilot.Next(run));
            }

            return run;
        }

        [Test]
        public void ClearsEveryLevelWithoutMissWithDefaults()
        {
            foreach (var level in Levels.All())
            {
                var watch = Stopwatch.StartNew();
                var run = Play(level);
                TestContext.Progress.WriteLine($"{level.Name}: {run.Status} {run.MissReason} {run.Ticks} Tick, 踏み {run.Stomps}, x={run.Player.Body.X / Units.Tile}, {watch.ElapsedMilliseconds} ms");
                Assert.That(run.Status, Is.EqualTo(RunStatus.Cleared), $"{level.Name}：{run.MissReason}（列 {run.Player.Body.X / Units.Tile}、{run.Ticks} Tick）");
            }
        }

        [Test]
        public void SameLevel_SameResult()
        {
            var a = Play(Levels.Hills);
            var b = Play(Levels.Hills);
            Assert.That(a.Hash(), Is.EqualTo(b.Hash()));
        }

        [Test]
        public void FinishedRun_ReturnsNoInput()
        {
            var run = Play(Levels.Meadow);
            Assert.That(new Autopilot().Next(run), Is.EqualTo(PlayerInput.None));
        }
    }
}
