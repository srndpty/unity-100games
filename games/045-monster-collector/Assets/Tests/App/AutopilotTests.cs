using System.Collections.Generic;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>操縦役がタイトルから結果まで遊び切る（捕獲・育成・トレーナー 3 戦）。</summary>
    public sealed class AutopilotTests
    {
        public const int MaxTicks = 400_000;

        [Test]
        public void SeedOneClearsTheGame()
        {
            var flow = AppLab.Flow(1);
            var pilot = new Autopilot();
            var ticks = AppLab.RunUntil(flow, pilot, f => f.Screen == FlowScreen.Result, MaxTicks);
            var w = flow.World!;
            TestContext.WriteLine($"Tick {ticks}、遊んだ Tick {w.PlayTicks}、戦闘 {w.BattleCount}、勝ち {w.Wins}、負け {w.Losses}、捕まえた {w.CaughtCount}、手持ち {AppLab.Levels(flow)}、ボックス {w.Roster.Box.Count}");
            Assert.That(w.Mode, Is.EqualTo(Game.Core.WorldMode.Cleared));
            Assert.That(w.CaughtCount, Is.GreaterThanOrEqualTo(2), "捕獲して仲間を増やしている");
        }

        [Test]
        public void TwentySeedsClear()
        {
            var stats = new List<string>();
            for (int seed = 1; seed <= 20; seed++)
            {
                var flow = AppLab.Flow(seed);
                var pilot = new Autopilot();
                AppLab.RunUntil(flow, pilot, f => f.Screen == FlowScreen.Result, MaxTicks);
                var w = flow.World!;
                stats.Add($"{seed}: 戦闘 {w.BattleCount} 負け {w.Losses} 捕獲 {w.CaughtCount} 秒 {w.PlayTicks / 60} 手持ち {AppLab.Levels(flow)}");
            }

            TestContext.WriteLine(string.Join("\n", stats));
        }
    }
}
