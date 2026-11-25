using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class AutopilotTests
    {
        /// <summary>シード 1 を操縦役で最後まで進めた最終ハッシュ。整数だけなので .NET と Mono で同じ値になる。</summary>
        private const uint SeedOneFinalHash = 4145725609u;

        [Test]
        public void AutopilotTakesOffAndWinsEverySeed()
        {
            var limit = new MissionConfig().MissionTicks;
            for (var seed = 1; seed <= 20; seed++)
            {
                var (mission, tookOffAt) = Run(seed);
                Assert.That(mission.Outcome, Is.EqualTo(Outcome.Won), $"seed {seed}");
                Assert.That(tookOffAt, Is.GreaterThan(0), "滑走路から離陸した");
                Assert.That(mission.Tick, Is.LessThan(limit));
                Assert.That(mission.Player.Ammo, Is.GreaterThan(0));
            }
        }

        [Test]
        public void SeedOneEndsWithTheFixedHash()
        {
            var (mission, _) = Run(1);
            Assert.That(mission.Hash(), Is.EqualTo(SeedOneFinalHash));
        }

        [Test]
        public void FlowWithAutopilotGivesTheSameResultAfterAnAbandonedMission()
        {
            var fresh = Run(3).Mission.Hash();

            // 途中でやめた試合の後に同じシードで始めても、操縦役の判断は持ち越さない。
            var pilot = new Autopilot();
            var abandoned = new Mission(new FlightConfig(), new MissionConfig(), 5);
            for (var tick = 0; tick < 2000; tick++) { abandoned.Step(pilot.CommandFor(abandoned)); }
            pilot.Reset();
            var mission = new Mission(new FlightConfig(), new MissionConfig(), 3);
            while (!mission.IsOver) { mission.Step(pilot.CommandFor(mission)); }
            Assert.That(mission.Hash(), Is.EqualTo(fresh));
        }

        [Test]
        public void FlowDrivenByTheAutopilotReachesTheResult()
        {
            var flow = new SortieFlow(new FlightConfig(), new MissionConfig(), 0) { Pilot = new Autopilot() };
            flow.Apply(new Intent(IntentKind.Submit, 0));
            var guard = 0;
            while (flow.Screen == AppScreen.Playing && guard++ < new MissionConfig().MissionTicks + 5) { flow.StepTick(); }
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(flow.Won, Is.True);
            var moments = flow.TakeMoments();
            Assert.That(moments & MissionMoment.TookOff, Is.EqualTo(MissionMoment.TookOff));
            Assert.That(moments & MissionMoment.PlayerFired, Is.EqualTo(MissionMoment.PlayerFired));
            Assert.That(moments & MissionMoment.EnemyDown, Is.EqualTo(MissionMoment.EnemyDown));
        }

        private static (Mission Mission, int TookOffAt) Run(int seed)
        {
            var mission = new Mission(new FlightConfig(), new MissionConfig(), seed);
            var pilot = new Autopilot();
            var tookOffAt = -1;
            while (!mission.IsOver)
            {
                var (_, moments) = mission.Step(pilot.CommandFor(mission));
                if ((moments & MissionMoment.TookOff) != 0) { tookOffAt = mission.Tick; }
            }

            return (mission, tookOffAt);
        }
    }
}
