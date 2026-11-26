using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>操縦役：決着・再現性・勝率。</summary>
    public sealed class AutopilotTests
    {
        /// <summary>シード 1 を操縦役で通したときの最終ハッシュ（.NET と Mono で一致させる）。</summary>
        private const uint SeedOneFinalHash = 0x48E3B9C7u;

        [Test]
        public void EverySeedEndsInVictoryOrDeathWithinTheLimit()
        {
            var wins = 0;
            for (int seed = 1; seed <= 20; seed++)
            {
                var run = AppLab.Play(seed, limit: 20_000);
                Assert.That(run.State, Is.Not.EqualTo(RunState.Playing), $"シード {seed} が決着しない");
                if (run.State == RunState.Won) { wins++; }
            }

            // 操縦役は画面の情報だけで遊ぶ。5 階まで届くことが多い難しさに調整した（README に記録）。
            Assert.That(wins, Is.GreaterThanOrEqualTo(15));
        }

        [Test]
        public void SameSeedReplaysToTheSameHash()
        {
            var a = AppLab.Play(1);
            var b = AppLab.Play(1);
            Assert.That(b.Hash(), Is.EqualTo(a.Hash()));
            Assert.That(b.Player.Actions, Is.EqualTo(a.Player.Actions));
            TestContext.Progress.WriteLine($"seed 1: {a.State} turns {a.Player.Actions} hash 0x{a.Hash():X8}");
            Assert.That(a.Hash(), Is.EqualTo(SeedOneFinalHash), "固定値（.NET と Mono）");
        }

        [Test]
        public void RecordedCommandsReplayExactly()
        {
            var config = new CrawlConfig();
            var run = new CrawlRun(config, 3);
            var pilot = new Autopilot(config);
            var log = new List<Command>();
            for (int i = 0; i < 3000 && run.State == RunState.Playing; i++)
            {
                var c = pilot.Decide(run.View);
                log.Add(c);
                run.Submit(c);
            }

            var replay = new CrawlRun(config, 3);
            foreach (var c in log) { replay.Submit(c); }
            Assert.That(replay.Hash(), Is.EqualTo(run.Hash()));
            Assert.That(replay.State, Is.EqualTo(run.State));
            Assert.That(replay.Floor, Is.EqualTo(run.Floor));
        }

        [Test]
        public void PilotRunsThroughTheFlowAndResetsBetweenRuns()
        {
            var config = new CrawlConfig();
            var pilot = new Autopilot(config);

            // 別の試合を途中でやめた後でも、同じシードなら同じ結果（操縦役の状態を持ち越さない）。
            var interrupted = new CrawlFlow(config, 5) { Pilot = pilot };
            interrupted.Apply(Intent.Of(IntentKind.Submit, 0));
            interrupted.Apply(Intent.Of(IntentKind.Submit, 0));
            for (int i = 0; i < 300; i++) { interrupted.StepPilot(); }

            uint Finish(IPilot p)
            {
                var flow = new CrawlFlow(config, 1) { Pilot = p };
                flow.Apply(Intent.Of(IntentKind.Submit, 0));
                flow.Apply(Intent.Of(IntentKind.Submit, 0));
                for (int i = 0; i < 20_000 && flow.Screen == AppScreen.Playing; i++) { flow.StepPilot(); }
                Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
                Assert.That(flow.StepPilot().Accepted, Is.False, "結果の後は進まない");
                return flow.Run!.Hash();
            }

            Assert.That(Finish(pilot), Is.EqualTo(Finish(new Autopilot(config))));
        }

        [Test]
        public void PilotOnlyReadsThePlayerView()
        {
            // 操縦役は PlayerView しか受け取らない（型で保証）。視界の外に敵を足しても判断が変わらない。
            var config = new CrawlConfig();
            var a = new CrawlRun(config, 4);
            var b = new CrawlRun(config, 4);
            foreach (var room in b.Grid.Rooms)
            {
                var c = room.Center;
                if (!b.IsVisible(c) && b.MonsterAt(c) == null)
                {
                    b.AddMonster(MonsterKind.Golem, c);
                    break;
                }
            }

            b.Refresh();
            Assert.That(new Autopilot(config).Decide(b.View).ToString(), Is.EqualTo(new Autopilot(config).Decide(a.View).ToString()));
        }
    }
}
