using System;
using System.Linq;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class MoveRulesTests
    {
        private static readonly Direction[] _allDirections = { Direction.Left, Direction.Right, Direction.Up, Direction.Down };
        private static readonly int[] _mergedSources = { 1, 2 };

        private static BoardState TopRow(int a, int b, int c, int d) =>
            BoardState.FromValues(a, b, c, d, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        private static BoardState RandomBoard(SeededRandom random)
        {
            var values = new int[BoardState.CellCount];
            for (var i = 0; i < values.Length; i++)
            {
                var exponent = random.Next(6);
                values[i] = exponent == 0 ? 0 : 1 << exponent;
            }

            return BoardState.FromValues(values);
        }

        private static BoardState Mirror(BoardState board)
        {
            var values = new int[BoardState.CellCount];
            for (var y = 0; y < BoardState.Size; y++)
            {
                for (var x = 0; x < BoardState.Size; x++) { values[BoardState.Index(x, y)] = board.At(BoardState.Size - 1 - x, y); }
            }

            return BoardState.FromValues(values);
        }

        private static BoardState Transpose(BoardState board)
        {
            var values = new int[BoardState.CellCount];
            for (var y = 0; y < BoardState.Size; y++)
            {
                for (var x = 0; x < BoardState.Size; x++) { values[BoardState.Index(x, y)] = board.At(y, x); }
            }

            return BoardState.FromValues(values);
        }

        [Test]
        public void FourEqualTiles_BecomeTwoPairsInOneMove()
        {
            var outcome = MoveRules.Slide(TopRow(2, 2, 2, 2), Direction.Left);
            Assert.That(outcome.After, Is.EqualTo(TopRow(4, 4, 0, 0)));
            Assert.That(outcome.ScoreGained, Is.EqualTo(8));
        }

        [TestCase(2, 2, 4, 0, 4, 4, 0, 0, 4)]
        [TestCase(4, 2, 2, 0, 4, 4, 0, 0, 4)]
        [TestCase(2, 2, 2, 0, 4, 2, 0, 0, 4)]
        [TestCase(2, 0, 0, 2, 4, 0, 0, 0, 4)]
        [TestCase(0, 0, 0, 2, 2, 0, 0, 0, 0)]
        [TestCase(2, 4, 2, 4, 2, 4, 2, 4, 0)]
        [TestCase(4, 4, 8, 8, 8, 16, 0, 0, 24)]
        [TestCase(2, 2, 4, 8, 4, 4, 8, 0, 4)]
        [TestCase(1024, 1024, 0, 0, 2048, 0, 0, 0, 2048)]
        public void SlideLeft_FollowsLineRules(int a, int b, int c, int d, int e, int f, int g, int h, int score)
        {
            var outcome = MoveRules.Slide(TopRow(a, b, c, d), Direction.Left);
            Assert.That(outcome.After, Is.EqualTo(TopRow(e, f, g, h)));
            Assert.That(outcome.ScoreGained, Is.EqualTo(score));
        }

        [Test]
        public void EachDirection_PacksTowardItsEdge()
        {
            var board = BoardState.FromValues(
                2, 0, 0, 2,
                0, 0, 0, 0,
                0, 0, 0, 0,
                2, 0, 0, 4);

            Assert.That(MoveRules.Slide(board, Direction.Left).After, Is.EqualTo(BoardState.FromValues(
                4, 0, 0, 0,
                0, 0, 0, 0,
                0, 0, 0, 0,
                2, 4, 0, 0)));
            Assert.That(MoveRules.Slide(board, Direction.Right).After, Is.EqualTo(BoardState.FromValues(
                0, 0, 0, 4,
                0, 0, 0, 0,
                0, 0, 0, 0,
                0, 0, 2, 4)));
            Assert.That(MoveRules.Slide(board, Direction.Up).After, Is.EqualTo(BoardState.FromValues(
                4, 0, 0, 2,
                0, 0, 0, 4,
                0, 0, 0, 0,
                0, 0, 0, 0)));
            Assert.That(MoveRules.Slide(board, Direction.Down).After, Is.EqualTo(BoardState.FromValues(
                0, 0, 0, 0,
                0, 0, 0, 0,
                0, 0, 0, 2,
                4, 0, 0, 4)));
        }

        [Test]
        public void MergePriority_StartsFromTheDestinationEdge()
        {
            Assert.That(MoveRules.Slide(TopRow(2, 2, 2, 0), Direction.Right).After, Is.EqualTo(TopRow(0, 0, 2, 4)));
        }

        [Test]
        public void Moves_DescribeSourcesAndMergedPair()
        {
            var moves = MoveRules.Slide(TopRow(0, 2, 2, 4), Direction.Left).Moves;
            Assert.That(moves.Count, Is.EqualTo(3));
            Assert.That(moves.Where(move => move.Merges).Select(move => move.From), Is.EquivalentTo(_mergedSources));
            Assert.That(moves.Where(move => move.Merges).Select(move => move.To), Is.All.EqualTo(0));
            var single = moves.Single(move => !move.Merges);
            Assert.That((single.From, single.To, single.Value), Is.EqualTo((3, 1, 4)));
        }

        [Test]
        public void Slide_DoesNotChangeInputBoard()
        {
            var board = TopRow(2, 2, 4, 4);
            var before = board.ToArray();
            foreach (var direction in _allDirections) { MoveRules.Slide(board, direction); }
            Assert.That(board.ToArray(), Is.EqualTo(before));
        }

        [Test]
        public void Slide_RejectsInvalidArguments()
        {
            Assert.Throws<ArgumentNullException>(() => MoveRules.Slide(null!, Direction.Left));
            Assert.Throws<ArgumentOutOfRangeException>(() => MoveRules.Slide(BoardState.Empty, (Direction)99));
            Assert.Throws<ArgumentNullException>(() => MoveRules.HasAnyMove(null!));
        }

        [Test]
        public void HasAnyMove_IsFalseOnlyWhenFullWithoutEqualNeighbors()
        {
            var stuck = BoardState.FromValues(
                2, 4, 2, 4,
                4, 2, 4, 2,
                2, 4, 2, 4,
                4, 2, 4, 2);
            var horizontal = BoardState.FromValues(
                2, 2, 8, 4,
                4, 8, 4, 2,
                2, 4, 2, 4,
                4, 2, 4, 2);
            var vertical = BoardState.FromValues(
                2, 4, 2, 4,
                4, 2, 4, 2,
                2, 4, 2, 8,
                4, 2, 4, 8);

            Assert.That(MoveRules.HasAnyMove(stuck), Is.False);
            Assert.That(MoveRules.HasAnyMove(horizontal), Is.True);
            Assert.That(MoveRules.HasAnyMove(vertical), Is.True);
        }

        [Test]
        public void HasAnyMove_IsTrueWithOneEmptyCell()
        {
            var board = BoardState.FromValues(
                2, 4, 2, 4,
                4, 2, 4, 2,
                2, 4, 2, 4,
                4, 2, 4, 0);

            Assert.That(MoveRules.HasAnyMove(board), Is.True);
        }

        [Test]
        public void RandomBoards_KeepSlideInvariants()
        {
            var random = new SeededRandom(11);
            for (var i = 0; i < 2000; i++)
            {
                var board = RandomBoard(random);
                foreach (var direction in _allDirections)
                {
                    var outcome = MoveRules.Slide(board, direction);
                    Assert.That(outcome.After.Sum, Is.EqualTo(board.Sum), "合計値は移動で変わらない");
                    Assert.That(outcome.ScoreGained, Is.EqualTo(outcome.Moves.Where(move => move.Merges).Sum(move => move.Value)), "獲得点は合体でできた値の合計");
                    Assert.That(outcome.Moves.Select(move => move.From), Is.Unique, "1枚は1回だけ動く");
                    Assert.That(outcome.Moves.Count, Is.EqualTo(board.TileCount));
                    Assert.That(outcome.Moves.GroupBy(move => move.To).All(group => group.Count() <= 2), Is.True, "1マスに集まるのは最大2枚");
                    Assert.That(outcome.Changed, Is.EqualTo(outcome.Moves.Any(move => move.From != move.To)));
                }
            }
        }

        [Test]
        public void RandomBoards_HasAnyMoveMatchesSomeDirectionChanging()
        {
            var random = new SeededRandom(12);
            for (var i = 0; i < 2000; i++)
            {
                var board = RandomBoard(random);
                if (board.TileCount == 0) { continue; }
                var changes = _allDirections.Any(direction => MoveRules.Slide(board, direction).Changed);
                Assert.That(MoveRules.HasAnyMove(board), Is.EqualTo(changes), board.ToString());
            }
        }

        [Test]
        public void RandomBoards_DirectionsAreSymmetric()
        {
            var random = new SeededRandom(13);
            for (var i = 0; i < 2000; i++)
            {
                var board = RandomBoard(random);
                Assert.That(Mirror(MoveRules.Slide(Mirror(board), Direction.Left).After), Is.EqualTo(MoveRules.Slide(board, Direction.Right).After));
                Assert.That(Transpose(MoveRules.Slide(Transpose(board), Direction.Left).After), Is.EqualTo(MoveRules.Slide(board, Direction.Up).After));
                Assert.That(Transpose(MoveRules.Slide(Transpose(board), Direction.Right).After), Is.EqualTo(MoveRules.Slide(board, Direction.Down).After));
            }
        }
    }
}
