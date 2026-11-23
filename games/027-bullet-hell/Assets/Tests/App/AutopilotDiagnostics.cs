using System;
using System.Diagnostics;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>調整用の計測（明示したときだけ走る）。</summary>
    public sealed class AutopilotDiagnostics
    {
        [Test]
        [Explicit("調整用の計測")]
        public void Survey()
        {
            var bosses = TestScripts.Load();
            for (var seed = 1; seed <= 8; seed++)
            {
                var watch = Stopwatch.StartNew();
                var run = new VeilRun(new VeilConfig(), bosses, seed);
                var pilot = new Autopilot();
                var peak = new int[6];
                var hitsAt = new int[6];
                var ticks = new int[6];
                var timeouts = 0;
                while (!run.IsOver && run.Elapsed < 60 * 60 * 10)
                {
                    var stage = run.StageIndex;
                    var hits = run.Hits;
                    var hpBefore = run.BossHp;
                    run.Step(pilot.CommandFor(run));
                    peak[stage] = Math.Max(peak[stage], run.Bullets.Count);
                    if (run.Phase == RunPhase.Pattern) { ticks[stage]++; }
                    if (run.Hits > hits) { hitsAt[stage]++; }
                    if ((run.TakeEvents() & RunEvents.PatternTimedOut) != 0)
                    {
                        timeouts++;
                        TestContext.Progress.WriteLine($"  timeout stage {stage} hp left {hpBefore}");
                    }
                }

                TestContext.Progress.WriteLine($"seed {seed}: {run.Status} stage {run.StageIndex} ticks {run.Elapsed} lives {run.Lives} timeouts {timeouts} hits [{string.Join(",", hitsAt)}] peak [{string.Join(",", peak)}] ticks [{string.Join(",", ticks)}] {watch.ElapsedMilliseconds} ms");
            }
        }
    }
}
