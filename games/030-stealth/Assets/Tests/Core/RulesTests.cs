using System;
using IntGeometry;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>自機・設定・施設の規則と負テスト。</summary>
    public sealed class RulesTests
    {
        [Test]
        public void FootstepsComeOnTheFirstMovingTickAndThenEveryStepTicks()
        {
            var config = Steady();
            var intruder = new Intruder(config, new Cell(5, 4));
            var map = FacilityMap.Parse(OpenRoom());
            Assert.That(intruder.Move(map, Move(1, 0)), Is.Not.Null);
            for (var t = 1; t < config.StepTicks; t++) { Assert.That(intruder.Move(map, Move(1, 0)), Is.Null, $"{t}"); }
            Assert.That(intruder.Move(map, Move(1, 0)), Is.Not.Null);

            // 止まると数え直し、次に動いた Tick にすぐ足音が出る。止まっている間は出ない。
            Assert.That(intruder.Move(map, Move(1, 0)), Is.Null);
            Assert.That(intruder.Move(map, IntruderInput.None), Is.Null);
            Assert.That(intruder.Move(map, Move(0, 1)), Is.Not.Null);
        }

        [Test]
        public void GaitSetsSpeedAndNoise()
        {
            var config = Steady();
            var map = FacilityMap.Parse(OpenRoom());
            foreach (var (input, speed, radius) in new[] { (Sneak(1, 0), config.SneakSpeed, config.SneakNoise), (Move(1, 0), config.WalkSpeed, config.WalkNoise), (Run(1, 0), config.RunSpeed, config.RunNoise) })
            {
                var intruder = new Intruder(config, new Cell(5, 4));
                var x = intruder.Body.X;
                var noise = intruder.Move(map, input);
                Assert.That(intruder.Body.X - x, Is.EqualTo(speed));
                Assert.That(noise!.Value.Radius, Is.EqualTo(radius));
                Assert.That(noise.Value.Gait, Is.EqualTo(input.Gait));
            }

            Assert.That(new IntruderInput(1, 0, true, true).Gait, Is.EqualTo(Gait.Sneak), "両方押したら忍び足");
        }

        [Test]
        public void DiagonalIsScaledButNeverZero()
        {
            Assert.That(WardConfig.DiagonalOf(18), Is.EqualTo(12));
            Assert.That(WardConfig.DiagonalOf(1), Is.EqualTo(1));
            Assert.That(WardConfig.DiagonalOf(0), Is.Zero);
        }

        [Test]
        public void WallsAndCratesStopTheIntruder()
        {
            var config = Steady();
            var map = FacilityMap.Parse("#####", "#E.X#", "#..O#", "#####");
            var intruder = new Intruder(config, new Cell(1, 2));
            for (var t = 0; t < 100; t++) { intruder.Move(map, Run(1, 0)); }
            Assert.That(intruder.Body.Right, Is.EqualTo(3 * T), "木箱の手前で止まる");
            for (var t = 0; t < 100; t++) { intruder.Move(map, Run(0, 1)); }
            Assert.That(intruder.Body.Top, Is.EqualTo(3 * T), "壁の手前で止まる");
        }

        [Test]
        public void RejectsUndefinedInput()
        {
            Assert.That(() => new IntruderInput(2, 0, false, false), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => new IntruderInput(0, -2, false, false), Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void DefaultConfigIsValid()
        {
            Assert.That(() => new WardConfig().Validate(), Throws.Nothing);
        }

        [TestCase(nameof(WardConfig.SneakSpeed), 0)]
        [TestCase(nameof(WardConfig.WalkSpeed), 8)]
        [TestCase(nameof(WardConfig.RunSpeed), WardConfig.MaxSpeed + 1)]
        [TestCase(nameof(WardConfig.ChaseSpeed), 32)]
        [TestCase(nameof(WardConfig.WalkNoise), 1)]
        [TestCase(nameof(WardConfig.RunNoise), 3)]
        [TestCase(nameof(WardConfig.VisionRange), 0)]
        [TestCase(nameof(WardConfig.AlertVisionRange), 1000)]
        [TestCase(nameof(WardConfig.VisionHalfAngle), 40000)]
        [TestCase(nameof(WardConfig.AwarenessNearGain), 5)]
        [TestCase(nameof(WardConfig.AwarenessDecay), 0)]
        [TestCase(nameof(WardConfig.LookPeriodTicks), 162)]
        [TestCase(nameof(WardConfig.AlertTicks), 0)]
        [TestCase(nameof(WardConfig.BodySize), 300)]
        public void RejectsOutOfRangeSettings(string field, int value)
        {
            var config = new WardConfig();
            typeof(WardConfig).GetField(field)!.SetValue(config, value);
            Assert.That(() => config.Validate(), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => new Infiltration(config, Facility.Ward(), 1), Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void RunningMustStayUnderHalfTheBody()
        {
            var config = new WardConfig { BodySize = 64, SneakSpeed = 9, WalkSpeed = 18, RunSpeed = 32 };
            Assert.That(() => config.Validate(), Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void MatchKeepsItsOwnCopyOfTheSettings()
        {
            var config = new WardConfig();
            var match = new Infiltration(config, Facility.Ward(), 1);
            config.RunSpeed = 1;
            Assert.That(match.Config.RunSpeed, Is.EqualTo(new WardConfig().RunSpeed));
        }

        [Test]
        public void ChangingTheReturnedSettingsDoesNotAffectTheMatch()
        {
            // 返した設定を書き換えても（検証を通らない 0 でも）、試合は始めの検証済みの設定で進む。複製した試合も同じ。
            var reference = new Infiltration(new WardConfig(), Facility.Ward(), 1);
            var match = new Infiltration(new WardConfig(), Facility.Ward(), 1);
            var returned = match.Config;
            returned.StepTicks = 0;
            returned.RunSpeed = 1;
            returned.VisionRange = 0;
            match.Config.StepTicks = 0;
            var clone = match.Clone();
            clone.Config.ChaseSpeed = 0;
            for (var t = 0; t < 300; t++)
            {
                var input = t < 150 ? Run(1, 1) : Move(0, 1);
                reference.Tick(input);
                match.Tick(input);
                clone.Tick(input);
            }

            Assert.That(match.Hash(), Is.EqualTo(reference.Hash()));
            Assert.That(clone.Hash(), Is.EqualTo(reference.Hash()));
            Assert.That(match.Config.StepTicks, Is.EqualTo(new WardConfig().StepTicks), "返すのは毎回新しい複製");
            Assert.That(match.SpeedOf(Gait.Run), Is.EqualTo(new WardConfig().RunSpeed));
            Assert.That(match.CurrentVision.Range, Is.EqualTo(new WardConfig().VisionAt(match.Level).Range));
        }

        [Test]
        public void WardFacilityIsWellFormed()
        {
            var ward = Facility.Ward();
            Assert.That(ward.Map.Width, Is.EqualTo(32));
            Assert.That(ward.Map.Height, Is.EqualTo(18));
            Assert.That(ward.Guards, Has.Count.EqualTo(3));
            Assert.That(ward.Exit, Is.EqualTo(new Cell(1, 1)));
            Assert.That(ward.Objective, Is.EqualTo(new Cell(28, 15)));
        }

        [Test]
        public void RejectsBrokenFacilities()
        {
            Assert.That(() => FacilityMap.Parse("#E#", "#O"), Throws.ArgumentException, "幅がそろわない");
            Assert.That(() => FacilityMap.Parse("#E?O#"), Throws.ArgumentException, "未知の文字");
            Assert.That(() => new Facility(FacilityMap.Parse("#E..#")), Throws.InstanceOf<InvalidOperationException>(), "目的物がない");
            Assert.That(() => new Facility(FacilityMap.Parse("#EOO#")), Throws.InstanceOf<InvalidOperationException>(), "目的物が 2 つ");
            Assert.That(() => new Facility(FacilityMap.Parse("#E#O#")), Throws.ArgumentException, "目的物へ歩いて行けない");
            Assert.That(() => WithSentry(OpenRoom(), new Cell(0, 0), 0), Throws.ArgumentException, "巡回点が壁");
        }

        [Test]
        public void LookAroundSweepsBothWays()
        {
            var config = new WardConfig { LookSweep = 8000, LookPeriodTicks = 160 };
            var body = new GuardBody(config, FacilityMap.Parse(OpenRoom()), new Cell(3, 3), 0);
            Assert.That(body.SweepOffset(0), Is.Zero);
            Assert.That(body.SweepOffset(40), Is.EqualTo(8000));
            Assert.That(body.SweepOffset(80), Is.Zero);
            Assert.That(body.SweepOffset(120), Is.EqualTo(-8000));
            Assert.That(body.SweepOffset(160), Is.Zero);
        }

        [Test]
        public void GuardWalksAlongThePathAndTurnsGradually()
        {
            var config = Steady();
            var map = FacilityMap.Parse(_wallInTheWay);
            var body = new GuardBody(config, map, new Cell(1, 2), Trig.Left);
            var goal = new Cell(5, 2);
            for (var t = 0; t < 400 && body.StandingOn != goal; t++)
            {
                var before = body.Facing;
                body.Execute(Order.Walk(goal, Pace.Patrol, null), AlertLevel.Normal);
                var turn = Trig.Normalize(body.Facing - before);
                if (turn > Trig.FullTurn / 2) { turn = Trig.FullTurn - turn; }
                Assert.That(turn, Is.LessThanOrEqualTo(config.TurnSpeed));
                Assert.That(map.IsWalkable(Cell.Of(body.Position)), Is.True);
            }

            Assert.That(body.StandingOn, Is.EqualTo(goal));
        }

        private static readonly string[] _wallInTheWay =
        {
            "#######",
            "#.....#",
            "#E.#.O#",
            "#.....#",
            "#######",
        };
    }
}
