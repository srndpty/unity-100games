using System;
using System.Collections.Generic;
using System.Linq;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>
    /// 題材固有テスト「到達不能な障害物配置を拒否」。探索（<see cref="Planner"/>）を、自機の本物の 1 Tick と
    /// 箱の掃引だけで書いた素朴な全探索と照合し、通れない配置を設定の検証で拒否することを確かめる。
    /// </summary>
    public sealed class ReachabilityTests
    {
        private static readonly RunConfig _config = new();
        private static readonly Planner _planner = new(_config, new MotionGraph(_config));
        private static readonly string[] _corridorTail = { "WHW", "W.W", "...", "...", "..." };
        private static readonly Steer[] _steers = { Steer.None, Steer.Left, Steer.Right };
        private static readonly Leap[] _leaps = { Leap.None, Leap.Jump, Leap.Slide };

        [Test]
        public void RowOfWallsInEveryLane_IsRejected()
        {
            var specs = new[] { TestKit.Spec("blocked", 1, "...", "WWW", "...") };
            Assert.That(() => RunRules.Build(new RunConfig(), specs), Throws.ArgumentException.With.Message.Contains("到達不能"));
        }

        [Test]
        public void HurdleRow_NeedsAJump_AndBeamRow_NeedsASlide()
        {
            var hurdles = TestKit.Chunk("...", "...", "...", "...", "...", "HHH", "...", "...");
            var beams = TestKit.Chunk("...", "...", "...", "...", "...", "BBB", "...", "...");
            Assert.That(_planner.Passable(hurdles, _config.TopSpeed, 1), Is.True);
            Assert.That(_planner.Passable(beams, _config.TopSpeed, 1), Is.True);

            // 跳べない・くぐれない自機では通れない。
            var low = new RunConfig { JumpVelocity = 8, Gravity = 2 };
            Assert.That(new Planner(low, new MotionGraph(low)).Passable(hurdles, low.TopSpeed, 1), Is.False);
            var tall = new RunConfig { SlideHeight = 300 };
            Assert.That(new Planner(tall, new MotionGraph(tall)).Passable(beams, tall.TopSpeed, 1), Is.False);
        }

        [Test]
        public void ShiftingTwoLanes_PassesSlowly_ButNotAtTopSpeed()
        {
            // 左のレーンから右端だけ空いた壁まで：2 レーン動く距離が足りるかは速さで決まる。
            var chunk = TestKit.Chunk("...", "WW.", "...", "...");
            Assert.That(_planner.Passable(chunk, _config.BaseSpeed, 0), Is.True);
            Assert.That(_planner.Passable(chunk, _config.TopSpeed, 0), Is.False);
            Assert.That(_planner.Passable(chunk, _config.TopSpeed, 2), Is.True, "右のレーンからなら動かなくてよい");

            // 難度 1 のチャンクは最も速い段階でも出るので拒否する。速さが上がる前に出ない難度でも同じ（最後は上限の速さで出る）。
            foreach (var tier in new[] { 1, 3 })
            {
                var specs = new[] { TestKit.Spec("other", 1, "...", "...", "..."), TestKit.Spec("shift", tier, "...", "WW.", "...", "...") };
                Assert.That(() => RunRules.Build(new RunConfig(), specs), Throws.ArgumentException.With.Message.Contains("shift"));
            }

            // 速さの段階が上がらない設定なら受け付ける。
            var slow = new RunConfig { MaxSpeedLevel = 0 };
            Assert.That(() => RunRules.Build(slow, new[] { TestKit.Spec("shift", 1, "...", "WW.", "...", "...") }), Throws.Nothing);
        }

        [Test]
        public void HurdleThenBeam_NeedsRoomToLand_Boundary()
        {
            // 両側を壁で囲んだ通路で、Hurdle の何行後に Beam を置けば通れるか。
            int? first = null;
            for (var gap = 0; gap <= 10; gap++)
            {
                var passable = _planner.Passable(TestKit.Chunk(Corridor(gap)), _config.TopSpeed, 1);
                if (passable && first == null) { first = gap; }
                if (first != null) { Assert.That(passable, Is.True, $"{first} 行で通れるなら {gap} 行でも通れる"); }
            }

            Assert.That(first, Is.Not.Null.And.GreaterThan(0), "すぐ後の Beam は通れない");
            Assert.That(_planner.Passable(TestKit.Chunk(Corridor(first!.Value - 1)), _config.TopSpeed, 1), Is.False);
            var near = new[] { TestKit.Spec("near", 1, Corridor(first.Value - 1)) };
            Assert.That(() => RunRules.Build(new RunConfig(), near), Throws.ArgumentException.With.Message.Contains("到達不能"));
            var far = new[] { TestKit.Spec("far", 1, Corridor(first.Value)) };
            Assert.That(() => RunRules.Build(new RunConfig(), far), Throws.Nothing);
        }

        [Test]
        public void ChunkWithoutEmptyEnds_OrBadRows_IsRejected()
        {
            Assert.That(() => TestKit.Chunk("H..", "...", "..."), Throws.ArgumentException.With.Message.Contains("入口と出口"));
            Assert.That(() => TestKit.Chunk("...", "...", "..B"), Throws.ArgumentException.With.Message.Contains("入口と出口"));
            Assert.That(() => TestKit.Chunk("...", "X..", "..."), Throws.ArgumentException.With.Message.Contains("未定義の文字"));
            Assert.That(() => TestKit.Chunk("...", "....", "..."), Throws.ArgumentException.With.Message.Contains("3 文字"));
            Assert.That(() => TestKit.Chunk("...", "..."), Throws.ArgumentException.With.Message.Contains("行数"));
            Assert.That(() => ChunkDef.Parse(0, TestKit.Spec("t", 4, "...", "...", "..."), 3), Throws.ArgumentException.With.Message.Contains("難度"));
        }

        [Test]
        public void Planner_MatchesNaiveSearch_OnRandomChunks()
        {
            var random = new SeededRandom(2029);
            var passed = 0;
            var failed = 0;
            for (var i = 0; i < 10; i++)
            {
                var chunk = TestKit.Chunk(RandomRows(random));
                foreach (var speed in new[] { _config.BaseSpeed, _config.TopSpeed })
                {
                    for (var lane = 0; lane < RunConfig.Lanes; lane++)
                    {
                        var expected = NaivePassable(chunk, speed, lane);
                        Assert.That(_planner.Passable(chunk, speed, lane), Is.EqualTo(expected), $"#{i} 速さ {speed} レーン {lane}");
                        if (expected) { passed++; } else { failed++; }
                    }
                }
            }

            Assert.That(passed, Is.GreaterThan(10), "通れる配置も試す");
            Assert.That(failed, Is.GreaterThan(10), "通れない配置も試す");
        }

        [Test]
        public void PassableChunk_HasAPlanFromEveryPhase_ThatReplaysWithoutTouching()
        {
            // 障害物を手前へ伸ばした検査を通れば、実際にどの位相で入っても通れる（十分条件）ことを、伸ばさない探索の経路で確かめる。
            var random = new SeededRandom(7);
            var checkedChunks = 0;
            for (var i = 0; i < 40 && checkedChunks < 12; i++)
            {
                var chunk = TestKit.Chunk(RandomRows(random));
                const int Speed = 72;
                if (Enumerable.Range(0, RunConfig.Lanes).Any(lane => !_planner.Passable(chunk, Speed, lane))) { continue; }
                checkedChunks++;
                for (var lane = 0; lane < RunConfig.Lanes; lane++)
                {
                    for (var phase = 0; phase < Speed; phase += 7)
                    {
                        var obstacles = chunk.Obstacles.Select(o => new PlanObstacle(o.Kind, o.Lane, o.Box(_config).Z, o.Box(_config).Depth)).ToList();
                        var plan = _planner.Plan(LaneState.Rest(_config, lane), VerticalState.Rest, phase, chunk.Length(_config), Speed, obstacles);
                        Assert.That(plan, Is.Not.Null, $"#{i} レーン {lane} 位相 {phase}");
                        Replay(chunk, lane, phase, Speed, plan!);
                    }
                }
            }

            Assert.That(checkedChunks, Is.GreaterThanOrEqualTo(12));
        }

        private static string[] Corridor(int gap)
        {
            var rows = new List<string> { "...", "WBW" };
            for (var i = 0; i < gap; i++) { rows.Add("W.W"); }
            rows.AddRange(_corridorTail);
            return rows.ToArray();
        }

        private static string[] RandomRows(SeededRandom random)
        {
            var count = random.Range(4, 8);
            var rows = new string[count];
            rows[0] = "...";
            rows[count - 1] = "...";
            for (var r = 1; r < count - 1; r++)
            {
                var chars = new char[RunConfig.Lanes];
                for (var lane = 0; lane < RunConfig.Lanes; lane++)
                {
                    var roll = random.Next(100);
                    chars[lane] = roll < 18 ? 'W' : roll < 28 ? 'H' : roll < 38 ? 'B' : '.';
                }

                rows[r] = new string(chars);
            }

            return rows;
        }

        /// <summary>素朴な全探索：自機の組の集合を Tick ごとに 9 通りの操作で進め、伸ばした障害物との掃引で間引く。</summary>
        private static bool NaivePassable(ChunkDef chunk, int speed, int lane)
        {
            var boxes = chunk.Obstacles.Select(o => o.Box(_config)).Select(b => new Box3(b.X, b.Y, b.Z - (speed - 1), b.Width, b.Height, b.Depth + (speed - 1))).ToList();
            var length = chunk.Length(_config);
            var last = ((length - 1) / speed) + 1;
            var firstGoal = Math.Max(1, length - speed + 1 <= 0 ? 0 : ((length - speed) / speed) + 1);
            var states = new HashSet<(LaneState, VerticalState)> { (LaneState.Rest(_config, lane), VerticalState.Rest) };
            for (var k = 1; k <= last; k++)
            {
                var next = new HashSet<(LaneState, VerticalState)>();
                foreach (var (laneState, vertical) in states)
                {
                    foreach (var steer in _steers)
                    {
                        foreach (var leap in _leaps)
                        {
                            var l = laneState.Next(steer, _config, out _);
                            var v = vertical.Next(leap, _config, out _);
                            var from = RunnerShape.From(_config, l, v, (k - 1) * speed);
                            var to = RunnerShape.To(_config, l, v, k * speed);
                            if (boxes.Any(b => Sweep3.Boxes(from, to, b).Hits)) { continue; }
                            if (k >= firstGoal && (l != LaneState.Rest(_config, l.To) || v != VerticalState.Rest)) { continue; }
                            next.Add((l, v));
                        }
                    }
                }

                if (next.Count == 0) { return false; }
                states = next;
            }

            return true;
        }

        private static void Replay(ChunkDef chunk, int lane, int phase, int speed, Command[] plan)
        {
            var body = RunnerBody.Rest(_config, lane);
            var z = phase;
            foreach (var command in plan)
            {
                body.Step(command, _config);
                var from = RunnerShape.From(_config, body.Lane, body.Vertical, z);
                var to = RunnerShape.To(_config, body.Lane, body.Vertical, z + speed);
                z += speed;
                foreach (var obstacle in chunk.Obstacles)
                {
                    Assert.That(Sweep3.Boxes(from, to, obstacle.Box(_config)).Hits, Is.False, $"z {z} で {obstacle} に触れた");
                }
            }

            Assert.That(z, Is.GreaterThanOrEqualTo(chunk.Length(_config)));
            Assert.That(body.IsAtRest(_config), Is.True, "出口を越えた Tick に止まっている");
        }
    }
}
