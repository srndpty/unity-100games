using System.Collections.Generic;
using Game.App;
using Game.Core;

namespace Game.Unity.Tests
{
    /// <summary>
    /// 1 面を、手順どおりに実際の Physics2D の世界で最初から再生する（画面遷移を通さない）。手順が空なら <paramref name="limit"/> Tick まで何も撃たずに進める。
    /// 撃ち方は撮影・ゲームと同じ操縦役（<see cref="PlannedPilot"/>）。
    /// </summary>
    internal static class Replay
    {
        /// <summary>
        /// 面 <paramref name="index"/> を読み込み、<paramref name="plan"/> を撃ち終えて静止するか、勝敗が決まるか、
        /// <paramref name="limit"/> Tick まで進める。
        /// </summary>
        public static SiegeRun Play(PebbleSiegeWorld world, SiegeConfig config, Stage stage, int index, IReadOnlyList<ShotStep> plan, int limit = 20000)
        {
            var run = new SiegeRun(config, stage);
            world.Load(index, stage);
            var pilot = new PlannedPilot(new[] { plan });
            pilot.Reset(0);
            var report = new TickReport();
            for (var t = 0; t < limit && !run.IsOver; t++)
            {
                if (plan.Count > 0 && run.Phase == RunPhase.Aiming && run.ShotsUsed >= plan.Count && run.QuietTicks >= PlannedPilot.AimDelay) { break; }
                if (pilot.PullFor(run) is { } pull) { run.Launch(pull); }
                if (pilot.DetonateFor(run)) { run.Detonate(); }
                var commands = run.BeginTick();
                report.Clear();
                world.Simulate(commands, report);
                run.EndTick(report);
                world.Remove(run.BrokenThisTick, run.JointsToRemove, run.ShotRemovedThisTick);
            }

            return run;
        }
    }
}
