using System;
using System.Linq;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    public sealed class PuzzleBookTests
    {
        private const string Valid = "puzzle 例\nhint 案内\npar 1\nbody 128 100\nrope 128 136 40 10\ngoal 128 24 10\nend\n";

        [Test]
        public void ParsesValidPuzzle()
        {
            var puzzle = PuzzleBook.Parse("# 注釈\n" + Valid).Puzzles.Single();
            Assert.That(puzzle.Name, Is.EqualTo("例"));
            Assert.That(puzzle.Hint, Is.EqualTo("案内"));
            Assert.That(puzzle.Par, Is.EqualTo(1));
            Assert.That(puzzle.Body, Is.EqualTo(P(128, 100)));
            Assert.That(puzzle.Ropes.Single().Anchor, Is.EqualTo(P(128, 136)));
            Assert.That(puzzle.Ropes.Single().Length, Is.EqualTo(40 * U));
            Assert.That(puzzle.Ropes.Single().Links, Is.EqualTo(10));
            Assert.That(puzzle.Goal.Radius, Is.EqualTo(10 * U));
            Assert.DoesNotThrow(() => puzzle.Validate(new RopeConfig()));
        }

        [TestCase("body 128 100\nrope 128 136 40 10\ngoal 128 24 10\n", TestName = "Unclosed")]
        [TestCase("par 1\nbody 128 100\nrope 128 136 40 10\nend\n", TestName = "NoGoal")]
        [TestCase("par 1\nrope 128 136 40 10\ngoal 128 24 10\nend\n", TestName = "NoBody")]
        [TestCase("par 1\nbody 128 100\nbody 128 100\nrope 128 136 40 10\ngoal 128 24 10\nend\n", TestName = "TwoBodies")]
        [TestCase("par 1\nbody 128 100\nrope 128 136 40\ngoal 128 24 10\nend\n", TestName = "MissingNumber")]
        [TestCase("par 1\nbody 128 1x0\nrope 128 136 40 10\ngoal 128 24 10\nend\n", TestName = "NotInteger")]
        [TestCase("par 1\nbody 128 99999\nrope 128 136 40 10\ngoal 128 24 10\nend\n", TestName = "HugeNumber")]
        [TestCase("par 1\nbody 128 100\nrope 128 136 40 10\nwall 10 10 5 20\ngoal 128 24 10\nend\n", TestName = "InvertedWall")]
        [TestCase("par 1\nbody 128 100\nrope 128 136 40 10\nlaser 1 2\ngoal 128 24 10\nend\n", TestName = "UnknownKey")]
        [TestCase("par 1\nbody 128 100\nrope 128 136 40 10\ngoal 128 24 0\nend\n", TestName = "ZeroRadius")]
        public void RejectsMalformedText(string body)
        {
            Assert.Throws<FormatException>(() => PuzzleBook.Parse("puzzle 例\n" + body));
        }

        [Test]
        public void RejectsKeyOutsidePuzzle()
        {
            Assert.Throws<FormatException>(() => PuzzleBook.Parse("body 1 2\n" + Valid));
        }

        [TestCase("par 1\nbody 128 100\nrope 128 136 30 10\ngoal 128 24 10", TestName = "RopeTooShort")]
        [TestCase("par 2\nbody 128 100\nrope 128 136 40 10\ngoal 128 24 10", TestName = "ParAboveRopes")]
        [TestCase("par 0\nbody 128 100\nrope 128 136 40 10\ngoal 128 24 10", TestName = "ParZero")]
        [TestCase("par 1\nbody 128 100\ngoal 128 24 10", TestName = "NoRope")]
        [TestCase("par 1\nbody 128 100\nrope 128 136 40 1\ngoal 128 24 10", TestName = "OneLink")]
        [TestCase("par 1\nbody 128 100\nrope 128 136 40 99\ngoal 128 24 10", TestName = "TooManyLinks")]
        [TestCase("par 1\nbody 128 100\nrope 128 200 120 10\ngoal 128 24 10", TestName = "AnchorOutside")]
        [TestCase("par 1\nbody 2 100\nrope 10 136 60 10\ngoal 128 24 10", TestName = "BodyOutside")]
        [TestCase("par 1\nbody 128 100\nrope 128 136 40 10\nspike 128 90 6\ngoal 128 24 10", TestName = "BodyOnSpike")]
        [TestCase("par 1\nbody 128 100\nrope 128 136 40 10\ngoal 128 90 6", TestName = "BodyOnGoal")]
        [TestCase("par 1\nbody 128 100\nrope 128 136 40 10\nwall 100 90 160 96\ngoal 128 24 10", TestName = "BodyInWall")]
        [TestCase("par 1\nbody 128 100\nrope 128 136 40 10\ngoal 128 24 40", TestName = "GoalTooLarge")]
        [TestCase("par 1\nbody 128 100\nrope 1 136 40 10\nrope 2 136 40 10\nrope 3 136 40 10\nrope 4 136 40 10\nrope 5 136 40 10\ngoal 128 24 10", TestName = "FiveRopes")]
        public void RejectsInvalidPuzzle(string body)
        {
            var puzzle = One(body);
            Assert.Throws<FormatException>(() => puzzle.Validate(new RopeConfig()));
        }

        /// <summary>壁の厚さの境界：1 Tick の移動量の上限が「半径 + 厚さの半分」未満なら受け付ける。</summary>
        [Test]
        public void WallThicknessBoundary()
        {
            var config = new RopeConfig();
            var puzzle = One("par 1\nbody 128 100\nrope 128 136 40 10\nwall 60 40 200 41\ngoal 128 24 10");
            var half = U / 2;
            config.MaxStep = config.BodyRadius + half - 1;
            Assert.DoesNotThrow(() => puzzle.Validate(config));
            config.MaxStep = config.BodyRadius + half;
            Assert.Throws<FormatException>(() => puzzle.Validate(config));
        }

        [Test]
        public void ProductionHasTenValidPuzzles()
        {
            var book = Production();
            Assert.That(book.Puzzles.Count, Is.EqualTo(PuzzleBook.ProductionCount));
            Assert.That(book.Puzzles.Select(p => p.Name).Distinct().Count(), Is.EqualTo(PuzzleBook.ProductionCount));
            Assert.That(book.Puzzles.All(p => p.Hint.Length > 0), Is.True);
        }

        [Test]
        public void ProductionRejectsWrongCount()
        {
            Assert.Throws<FormatException>(() => PuzzleBook.Production(Valid, new RopeConfig()));
        }

        [Test]
        public void PuzzleListsAreReadOnly()
        {
            var puzzle = PuzzleBook.Parse(Valid).Puzzles[0];
            Assert.Throws<NotSupportedException>(() => ((System.Collections.Generic.IList<RopeSpec>)puzzle.Ropes).Add(default));
        }
    }
}
