using System;
using System.Linq;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>試合：前進・衝突・終了後・再現性。</summary>
    public sealed class RunnerRunTests
    {
        private const long M = TestKit.M;

        // 中央のレーンだけ塞ぐ壁、全レーンの Hurdle、全レーンの Beam。どれも何もしなければぶつかる。
        private static readonly string[] _middleWall = { "...", "...", "...", ".W.", "...", "..." };
        private static readonly string[] _hurdles = { "...", "...", "...", "...", "...", "HHH", "...", "..." };
        private static readonly string[] _beams = { "...", "...", "...", "...", "...", "BBB", "...", "..." };

        [Test]
        public void StartsAtRest_InTheMiddleLane_OnTheRunway()
        {
            var run = new RunnerRun(TestKit.Rules, 1);
            Assert.That(run.Body.IsAtRest(run.Config), Is.True);
            Assert.That(run.Body.Lane.To, Is.EqualTo(1));
            Assert.That(run.Z, Is.Zero);
            Assert.That(run.Chunks.Count, Is.GreaterThan(0));
            TestKit.Wait(run, 10);
            Assert.That(run.Z, Is.EqualTo(10 * run.Config.BaseSpeed));
            Assert.That(run.IsOver, Is.False);
        }

        [Test]
        public void DoingNothing_CrashesIntoTheWall_AndTheRunStops()
        {
            var run = new RunnerRun(TestKit.Only(_middleWall), 1);
            var events = RunUntilOver(run, 5000);
            Assert.That(events & RunEvents.Crashed, Is.EqualTo(RunEvents.Crashed));
            Assert.That(run.CrashKind, Is.EqualTo(ObstacleKind.Wall));
            var wall = run.Chunks.RunwayEnd + (2 * run.Config.RowLength);
            Assert.That(run.Z + (run.Config.RunnerDepth / 2), Is.GreaterThan(wall), "壁の手前の面を越えた Tick で止まる");
            Assert.That(run.PrevZ + (run.Config.RunnerDepth / 2), Is.LessThanOrEqualTo(wall));
            Assert.That(run.Meters, Is.EqualTo(run.Z / M));

            // 終わった後の Tick と操作は何も変えない。
            var hash = run.Hash();
            run.Step(TestKit.Do(Steer.Left, Leap.Jump));
            TestKit.Wait(run, 30);
            Assert.That(run.Hash(), Is.EqualTo(hash));
            Assert.That(run.TakeEvents(), Is.EqualTo(RunEvents.None));
        }

        [Test]
        public void SteeringAround_OrJumping_OrSliding_AvoidsTheObstacle()
        {
            Assert.That(Survives(_middleWall, _ => TestKit.Do(Steer.Left)), Is.True, "レーン移動で避ける");
            Assert.That(Survives(_hurdles, tick => tick == JumpTickFor(_hurdles) ? TestKit.Do(leap: Leap.Jump) : Command.None), Is.True, "Hurdle を跳ぶ");
            Assert.That(Survives(_hurdles, _ => TestKit.Do(leap: Leap.Slide)), Is.False, "Hurdle はくぐれない");
            Assert.That(Survives(_beams, _ => TestKit.Do(leap: Leap.Slide)), Is.True, "Beam をくぐる");
            Assert.That(Survives(_beams, _ => TestKit.Do(leap: Leap.Jump)), Is.False, "Beam は跳べない");
        }

        [Test]
        public void SteeringIntoTheSideOfAWall_Crashes()
        {
            // 左のレーンの長い壁の横を走っている途中で左へ動く。
            var rows = new[] { "...", "...", "W..", "W..", "W..", "W..", "...", "..." };
            var run = new RunnerRun(TestKit.Only(rows), 1);
            var beside = run.Chunks.RunwayEnd + (3 * run.Config.RowLength);
            while (run.Z < beside) { run.Step(Command.None); }
            Assert.That(run.IsOver, Is.False);
            run.Step(TestKit.Do(Steer.Left));
            TestKit.Wait(run, run.Config.LaneChangeTicks);
            Assert.That(run.IsOver, Is.True);
            Assert.That(run.CrashKind, Is.EqualTo(ObstacleKind.Wall));
            Assert.That(run.Body.Lane.IsMoving, Is.True, "横から触れた");
        }

        [Test]
        public void FastRunner_DoesNotTunnelThroughAThinHurdle()
        {
            // 1 Tick に 250 進み、自機の奥行き 128 と厚さ 2 の Hurdle（入口から 6 行目）は、どの Tick の終わりにも重ならない位置関係（助走 10 m）。
            var rows = new string[28];
            for (var i = 0; i < rows.Length; i++) { rows[i] = "..."; }
            rows[rows.Length - 1 - 6] = "HHH";
            var config = new RunConfig { BaseSpeed = 250, SpeedStep = 0, ObstacleDepth = 2, RunwayMeters = 10 };
            var run = new RunnerRun(RunRules.Build(config, new[] { TestKit.Spec("thin", 1, rows) }), 1);
            RunUntilOver(run, 1000);
            Assert.That(run.IsOver, Is.True);
            Assert.That(run.CrashKind, Is.EqualTo(ObstacleKind.Hurdle));
            var hurdleEnd = run.Chunks.RunwayEnd + (6 * config.RowLength) + ((config.RowLength - 2) / 2) + 2;
            Assert.That(run.Z - (config.RunnerDepth / 2), Is.GreaterThanOrEqualTo(hurdleEnd), "Tick の終わりにはもう通り過ぎている");
            Assert.That(run.CrashTime, Is.GreaterThan(Moment.Zero).And.LessThan(Moment.One));
        }

        [Test]
        public void SpeedRises_AtChunkEntrances_OnlyUpToTheTop()
        {
            var run = new RunnerRun(TestKit.Only("...", "...", "...", "...", "..."), 1);
            var config = run.Config;
            var sped = 0;
            var last = run.Speed;
            while (run.Z < 1500 * M)
            {
                var segment = run.Chunks.SegmentAt(run.Z);
                run.Step(Command.None);
                Assert.That(run.Speed, Is.EqualTo(segment.Speed), "区間の中では一定");
                if ((run.TakeEvents() & RunEvents.SpedUp) != 0) { sped++; }
                Assert.That(run.Speed, Is.GreaterThanOrEqualTo(last).And.LessThanOrEqualTo(config.TopSpeed));
                last = run.Speed;
            }

            Assert.That(sped, Is.EqualTo(config.MaxSpeedLevel));
            Assert.That(run.Speed, Is.EqualTo(config.TopSpeed));
        }

        [Test]
        public void SameSeedAndCommands_GiveTheSameHash_AndCloneContinuesTheSame()
        {
            static Command Pattern(long tick) => (tick % 97) switch
            {
                3 => TestKit.Do(Steer.Left),
                40 => TestKit.Do(Steer.Right, Leap.Jump),
                70 => TestKit.Do(leap: Leap.Slide),
                _ => Command.None,
            };

            var a = new RunnerRun(TestKit.Rules, 42);
            var b = new RunnerRun(TestKit.Rules, 42);
            for (var tick = 0; tick < 400; tick++)
            {
                a.Step(Pattern(tick));
                b.Step(Pattern(tick));
            }

            Assert.That(a.Hash(), Is.EqualTo(b.Hash()));
            var clone = a.Clone();
            for (var tick = 400; tick < 800; tick++)
            {
                a.Step(Pattern(tick));
                clone.Step(Pattern(tick));
            }

            Assert.That(clone.Hash(), Is.EqualTo(a.Hash()));
            Assert.That(new RunnerRun(TestKit.Rules, 43).Hash(), Is.Not.EqualTo(new RunnerRun(TestKit.Rules, 42).Hash()), "シードが違えば並びが違う");
        }

        [Test]
        public void ChunkOrder_DoesNotDependOnTheRunnersInput()
        {
            // 何もしない自機と、跳び続ける自機で、ぶつかるまでに並んだチャンクの順は同じ。
            var idle = Order(new RunnerRun(TestKit.Rules, 8), _ => Command.None);
            var busy = Order(new RunnerRun(TestKit.Rules, 8), tick => tick % 40 == 0 ? TestKit.Do(Steer.Right, Leap.Jump) : tick % 40 == 20 ? TestKit.Do(Steer.Left) : Command.None);
            var common = Math.Min(idle.Length, busy.Length);
            Assert.That(common, Is.GreaterThan(5));
            Assert.That(busy.Take(common), Is.EqualTo(idle.Take(common)));
        }

        private static RunEvents RunUntilOver(RunnerRun run, int limit)
        {
            var events = RunEvents.None;
            for (var i = 0; i < limit && !run.IsOver; i++)
            {
                run.Step(Command.None);
                events |= run.TakeEvents();
            }

            return events;
        }

        /// <summary>最初のチャンクの障害物を、<paramref name="command"/> の操作で越えて出口まで走れるか。</summary>
        private static bool Survives(string[] rows, Func<long, Command> command)
        {
            var run = new RunnerRun(TestKit.Only(rows), 1);
            var exit = run.Chunks.RunwayEnd + (rows.Length * run.Config.RowLength);
            while (run.Z < exit && !run.IsOver) { run.Step(command(run.Ticks + 1)); }
            return !run.IsOver;
        }

        /// <summary>跳ぶと最初の Hurdle を越えられる Tick（総当たり）。</summary>
        private static long JumpTickFor(string[] rows)
        {
            // 最初の Hurdle の 1500 単位手前（助走の速さで約 37 Tick 前）から探す。
            var config = TestKit.Only(rows).Config;
            var hurdle = ((long)config.RunwayMeters * TestKit.M) + (2 * config.RowLength);
            for (var tick = (hurdle - 1500) / config.BaseSpeed; tick < 1000; tick++)
            {
                if (Survives(rows, t => t == tick ? TestKit.Do(leap: Leap.Jump) : Command.None)) { return tick; }
            }

            throw new InvalidOperationException("越えられる Tick がない");
        }

        private static int[] Order(RunnerRun run, Func<long, Command> command)
        {
            var order = new System.Collections.Generic.List<int>();
            while (!run.IsOver && run.Ticks < 100_000)
            {
                for (var i = 0; i < run.Chunks.Count; i++)
                {
                    if (run.Chunks.SerialAt(i) == order.Count) { order.Add(run.Chunks.IdAt(i)); }
                }

                run.Step(command(run.Ticks + 1));
            }

            return order.ToArray();
        }
    }
}
