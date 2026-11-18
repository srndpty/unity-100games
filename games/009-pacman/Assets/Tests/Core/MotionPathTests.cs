using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class MotionPathTests
    {
        private const int Span = 29 * MazeActor.UnitsPerCell;
        private const int Radius = MazeGameConfig.MinContactRadius;

        private static MotionPath Straight(int x, int y, int dx, int dy, int units)
        {
            var path = new MotionPath();
            path.Begin(x, y, units);
            path.Add(dx, dy, units);
            return path;
        }

        private static MotionPath Still(int x, int y)
        {
            var path = new MotionPath();
            path.Begin(x, y, 0);
            return path;
        }

        [Test]
        public void Graze_TouchesBetweenEndpointsThatAreApart()
        {
            var graph = new MazeGraph(MazeLayout.CreateDefault());
            var player = new MazeActor(graph.CellId(1, 7), Direction.Right);
            var cat = new MazeActor(graph.CellId(1, 8), Direction.Up);
            player.Advance(graph, 58, out _);
            cat.Advance(graph, 492, out _);
            Assert.That(player.Touches(cat, graph, Radius), Is.False, "開始時は離れている");

            player.BeginMotion(graph, 50);
            cat.BeginMotion(graph, 50);
            player.AdvanceInMotion(graph, 50, out _);
            cat.AdvanceInMotion(graph, 50, out _);

            Assert.That(player.Touches(cat, graph, Radius), Is.False, "終了時も離れている");
            Assert.That(player.TouchedDuringMotion(cat, graph, Radius), Is.True);
        }

        [Test]
        public void ClosestApproach_IsInclusiveAtExactRadius()
        {
            var moving = Straight(1000, 1000, 1, 0, 100);
            var still = Still(1050, 1000 + Radius);
            Assert.That(moving.Touches(still, Span, Radius), Is.True);
            Assert.That(moving.Touches(still, Span, Radius - 1), Is.False);
        }

        [Test]
        public void HeadOn_TouchesOnlyWhenTheGapCloses()
        {
            var left = Straight(1000, 1000, 1, 0, 50);
            Assert.That(left.Touches(Straight(1000 + 100 + Radius, 1000, -1, 0, 50), Span, Radius), Is.True);
            Assert.That(left.Touches(Straight(1000 + 101 + Radius, 1000, -1, 0, 50), Span, Radius), Is.False);
        }

        [Test]
        public void OpenEnd_IgnoresContactOnlyAtTheFinalInstant()
        {
            var moving = Straight(1000, 1000, 1, 0, 50);
            var atEnd = Still(1050 + Radius, 1000);
            Assert.That(moving.Touches(atEnd, Span, Radius), Is.True);
            Assert.That(moving.Touches(atEnd, Span, Radius, includeEnd: false), Is.False);
        }

        [Test]
        public void OpenEnd_KeepsContactThatStartsEarlier()
        {
            var moving = Straight(1000, 1000, 1, 0, 50);
            var inside = Still(1049 + Radius, 1000);
            Assert.That(moving.Touches(inside, Span, Radius, includeEnd: false), Is.True);

            var passing = Still(1025, 1000 + Radius);
            Assert.That(moving.Touches(passing, Span, Radius, includeEnd: false), Is.True, "途中で最接近する");
        }

        [Test]
        public void Parallel_NeverTouches()
        {
            var upper = Straight(1000, 1000, 1, 0, 50);
            var lower = Straight(1000, 1000 + Radius + 1, 1, 0, 50);
            Assert.That(upper.Touches(lower, Span, Radius), Is.False);
        }

        [Test]
        public void TunnelSeam_TouchesAcrossTheWrap()
        {
            var crossing = Straight(Span - 50, 1000, 1, 0, 100);
            var still = Still(10, 1000 + Radius);
            Assert.That(crossing.Touches(still, Span, Radius), Is.True);
            Assert.That(crossing.Touches(still, Span, Radius - 1), Is.False);
        }

        [Test]
        public void Corner_TouchesAtTheTurnEvenWhenTheChordStaysApart()
        {
            var turning = new MotionPath();
            turning.Begin(0, 0, 50);
            turning.Add(1, 0, 25);
            turning.Add(0, -1, 25);
            Assert.That(turning.Touches(Still(25 + 84, 84), Span, Radius), Is.True);
        }

        [Test]
        public void StoppedPath_StaysAtItsLastPosition()
        {
            var stopping = new MotionPath();
            stopping.Begin(1000, 1000, 50);
            stopping.Add(1, 0, 20);
            var incoming = Straight(1020 + Radius + 50, 1000, -1, 0, 50);
            Assert.That(stopping.Touches(incoming, Span, Radius), Is.True);
            Assert.That(stopping.Touches(Straight(1021 + Radius + 50, 1000, -1, 0, 50), Span, Radius), Is.False);
        }

        [Test]
        public void MatchesDenseSampling_ForRandomPaths()
        {
            var random = new SeededRandom(12345);
            for (var round = 0; round < 3000; round++)
            {
                var a = RandomTrace(random, 0, 0);
                var b = RandomTrace(random, random.Next(500) - 250, random.Next(500) - 250);
                var nearest = long.MaxValue;
                const int Samples = 400;
                for (var i = 0; i <= Samples; i++)
                {
                    var (ax, ay) = a.At(i, Samples);
                    var (bx, by) = b.At(i, Samples);
                    nearest = Math.Min(nearest, ((ax - bx) * (ax - bx)) + ((ay - by) * (ay - by)));
                }

                var sampled = Math.Sqrt(nearest);
                var touches = a.Path.Touches(b.Path, Span, Radius);
                if (sampled <= Radius) { Assert.That(touches, Is.True, "round " + round); }
                if (sampled > Radius + 1.5) { Assert.That(touches, Is.False, "round " + round); }
            }
        }

        [Test]
        public void GameMisses_IncludeGrazesThatEndpointsWouldLose()
        {
            var moves = new[] { Direction.Up, Direction.Left, Direction.Down, Direction.Right };
            var grazes = 0;
            for (var seed = 1; seed <= 400 && grazes == 0; seed++)
            {
                var config = new MazeGameConfig
                {
                    ContactRadius = Radius,
                    PlayerSpeed = MazeGameConfig.MaxSpeed - 3, // 位置が同じ刻みに揃わない速さにする
                    CatSpeed = MazeGameConfig.MaxSpeed - 17,
                    ReadyTicks = 1,
                    DyingTicks = 1,
                    Lives = 9,
                    ReleaseTicks = new[] { 0, 0, 0, 0 },
                };
                var game = new MazeGameState(config, seed);
                var input = new SeededRandom(seed);
                var move = Direction.Left;
                for (var tick = 0; tick < 6000 && !game.IsOver && grazes == 0; tick++)
                {
                    if (tick % 20 == 0) { move = moves[input.Next(moves.Length)]; }
                    game.Step(move);
                    if ((game.Events & GameEvents.PlayerCaught) != 0 && !AnyDangerousCatOverlaps(game, Radius)) { grazes++; }
                }
            }

            Assert.That(grazes, Is.GreaterThan(0));
        }

        private static bool AnyDangerousCatOverlaps(MazeGameState game, int radius)
        {
            for (var i = 0; i < MazeGameConfig.CatCount; i++)
            {
                var cat = game.Cat(i);
                if (cat.IsDangerous && game.Player.Touches(cat.Actor, game.Graph, radius)) { return true; }
            }

            return false;
        }

        private static Trace RandomTrace(SeededRandom random, int x, int y)
        {
            var budget = 1 + random.Next(50);
            var first = random.Next(budget + 1);
            var second = random.Next(budget - first + 1);
            return new Trace(x, y, budget, RandomAxis(random), first, RandomAxis(random), second);
        }

        private static (int Dx, int Dy) RandomAxis(SeededRandom random) => random.Next(4) switch
        {
            0 => (1, 0),
            1 => (-1, 0),
            2 => (0, 1),
            _ => (0, -1),
        };

        private sealed class Trace
        {
            private readonly int _x;
            private readonly int _y;
            private readonly int _budget;
            private readonly (int Dx, int Dy) _firstAxis;
            private readonly int _first;
            private readonly (int Dx, int Dy) _secondAxis;
            private readonly int _second;

            public Trace(int x, int y, int budget, (int Dx, int Dy) firstAxis, int first, (int Dx, int Dy) secondAxis, int second)
            {
                _x = x;
                _y = y;
                _budget = budget;
                _firstAxis = firstAxis;
                _first = first;
                _secondAxis = secondAxis;
                _second = second;
                Path = new MotionPath();
                Path.Begin(x, y, budget);
                Path.Add(firstAxis.Dx, firstAxis.Dy, first);
                Path.Add(secondAxis.Dx, secondAxis.Dy, second);
            }

            public MotionPath Path { get; }

            // 整数で比べられるよう、1単位未満の端数は切り捨てる。
            public (long X, long Y) At(int sample, int samples)
            {
                var traveled = Math.Min(_budget * sample / samples, _first + _second);
                var onFirst = Math.Min(traveled, _first);
                var onSecond = traveled - onFirst;
                return (_x + (_firstAxis.Dx * onFirst) + (_secondAxis.Dx * onSecond), _y + (_firstAxis.Dy * onFirst) + (_secondAxis.Dy * onSecond));
            }
        }
    }
}
