using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class SnakeTests
    {
        [Test]
        public void Food_GrowsByOne_KeepsTail_AndRespawnsOnFreeCell()
        {
            SnakeState game = new(new SnakeConfig(), 96);
            var oldTail = game.SegmentAt(game.Length - 1);
            Assert.That(game.Food, Is.EqualTo(game.Head.Move(Direction.Right)));
            game.Step(Direction.Right);
            Assert.That(game.Score, Is.EqualTo(1));
            Assert.That(game.Length, Is.EqualTo(4));
            Assert.That(game.SegmentAt(3), Is.EqualTo(oldTail));
            AssertBody(game, 3);
        }

        [Test]
        public void SeededRandomInputs_KeepFoodAndBodyInvariants()
        {
            for (var seed = 0; seed < 64; seed++)
            {
                SnakeState game = new(new SnakeConfig(), seed);
                var random = (uint)(seed + 1);
                for (var tick = 0; tick < 2000 && game.Phase == GamePhase.Playing; tick++)
                {
                    random ^= random << 13;
                    random ^= random >> 17;
                    random ^= random << 5;
                    game.Step((Direction)(random % 4));
                    AssertBody(game, 3);
                }
            }
        }

        [Test]
        public void Reverse_IsIgnored_AndWallStopsMovement()
        {
            SnakeState game = new(new SnakeConfig(), 1);
            var start = game.Head;
            game.Step(Direction.Left);
            Assert.That(game.Head, Is.EqualTo(start.Move(Direction.Right)));
            while (game.Phase == GamePhase.Playing) { game.Step(Direction.Right); }
            var head = game.Head;
            var tick = game.TickCount;
            game.Step(Direction.Up);
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Lost));
            Assert.That(game.Head, Is.EqualTo(head));
            Assert.That(game.TickCount, Is.EqualTo(tick));
        }

        [TestCase(Direction.Up)]
        [TestCase(Direction.Down)]
        [TestCase(Direction.Right)]
        public void LegalDirection_MovesOneCell(Direction direction)
        {
            SnakeState game = new(new SnakeConfig(), 5);
            var start = game.Head;
            game.Step(direction);
            Assert.That(game.Head, Is.EqualTo(start.Move(direction)));
        }

        [Test]
        public void MovingIntoVacatingTail_IsLegal()
        {
            SnakeState game = new(new SnakeConfig { InitialLength = 4 }, 17);
            // 四角を回って、長さ4以上なら前の末尾へ入る。
            game.Step(Direction.Up);
            game.Step(Direction.Left);
            game.Step(Direction.Down);
            var tail = game.SegmentAt(game.Length - 1);
            Assert.That(game.Head.Move(Direction.Right), Is.EqualTo(tail));
            game.Step(Direction.Right);
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Playing));
            Assert.That(game.Head, Is.EqualTo(tail));
        }

        [Test]
        public void MovingIntoBody_LosesWithoutChangingSegments()
        {
            SnakeState game = new(new SnakeConfig { InitialLength = 6 }, 17);
            game.Step(Direction.Up);
            game.Step(Direction.Left);
            var head = game.Head;
            game.Step(Direction.Down);
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Lost));
            Assert.That(game.Head, Is.EqualTo(head));
        }

        [Test]
        public void InvalidDirectionAndSegmentIndex_AreRejected()
        {
            SnakeState game = new(new SnakeConfig(), 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => game.Step((Direction)99));
            Assert.Throws<ArgumentOutOfRangeException>(() => game.SegmentAt(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => game.SegmentAt(game.Length));
        }

        [TestCase(0, 16, 3, 8f)]
        [TestCase(20, 1, 3, 8f)]
        [TestCase(20, 16, 0, 8f)]
        [TestCase(20, 16, 12, 8f)]
        [TestCase(65536, 65536, 3, 8f)]
        [TestCase(33, 20, 3, 8f)]
        [TestCase(32, 21, 3, 8f)]
        [TestCase(256, 256, 3, 8f)]
        [TestCase(20, 16, 3, 0f)]
        [TestCase(20, 16, 3, -1f)]
        [TestCase(20, 16, 3, 61f)]
        [TestCase(20, 16, 3, float.NaN)]
        [TestCase(20, 16, 3, float.PositiveInfinity)]
        [TestCase(20, 16, 3, float.NegativeInfinity)]
        public void InvalidConfig_IsRejected(int width, int height, int length, float speed)
        {
            SnakeConfig config = new() { Width = width, Height = height, InitialLength = length, StepsPerSecond = speed };
            Assert.Throws<ArgumentException>(() => config.Validate());
        }

        [Test]
        public void ConfigBoundaries_AreAccepted()
        {
            new SnakeConfig { Width = 4, Height = 2, InitialLength = 3, StepsPerSecond = 60f }.Validate();
            new SnakeConfig { Width = SnakeConfig.MaxWidth, Height = SnakeConfig.MaxHeight, StepsPerSecond = float.Epsilon }.Validate();
        }

        public static void AssertBody(SnakeState game, int initialLength)
        {
            HashSet<Cell> cells = new();
            for (var i = 0; i < game.Length; i++)
            {
                var cell = game.SegmentAt(i);
                Assert.That(cells.Add(cell), Is.True, "体節の重複");
                Assert.That(game.Occupies(cell), Is.True);
                Assert.That(cell.X, Is.InRange(0, game.Width - 1));
                Assert.That(cell.Y, Is.InRange(0, game.Height - 1));
                if (i > 0)
                {
                    var previous = game.SegmentAt(i - 1);
                    Assert.That(Math.Abs(cell.X - previous.X) + Math.Abs(cell.Y - previous.Y), Is.EqualTo(1));
                }
            }
            Assert.That(game.Length, Is.EqualTo(initialLength + game.Score));
            if (game.Food.HasValue) { Assert.That(cells.Contains(game.Food.Value), Is.False); }
        }
    }
}
