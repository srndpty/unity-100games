using System.Diagnostics;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>調整用：操縦役のラウンドの打数と時間を書き出す（通常のテストでは走らない）。</summary>
    public sealed class PlayProbe
    {
        [Test]
        [Explicit("調整用")]
        public void PrintRounds()
        {
            var course = AppKit.Production();
            for (var seed = 1; seed <= 20; seed++)
            {
                var watch = Stopwatch.StartNew();
                var round = AppKit.PlayOut(seed, course: course);
                var line = $"seed {seed}: {round.ScoreOf(0)}/{round.ScoreOf(1)}/{round.ScoreOf(2)} total {round.Total} ticks {round.Ticks} {watch.ElapsedMilliseconds} ms wind";
                for (var i = 0; i < 3; i++) { line += $" ({round.WindOf(i).X / 16667},{round.WindOf(i).Y / 16667})"; }
                TestContext.Out.WriteLine(line);
                foreach (var shot in round.Shots)
                {
                    TestContext.Out.WriteLine($"   h{shot.Hole} s{shot.Stroke} club{shot.Setup.Club} p{shot.Setup.Power} e{shot.Setup.Error} {shot.Outcome} end ({shot.Actual.Position.X / 1e6:F1},{shot.Actual.Position.Y / 1e6:F1}) diff {shot.Difference / 1e6:F2}");
                }
            }
        }
    }
}
