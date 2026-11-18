using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class ForgeGameTests
    {
        private const int MoveLimit = 100000;
        private static readonly Direction[] _allDirections = { Direction.Left, Direction.Right, Direction.Up, Direction.Down };

        private static ForgeRules OnlyTwos => new() { FourChancePercent = 0 };

        private static ForgeGame From(ForgeRules rules, params int[] values) =>
            ForgeGame.FromBoard(BoardState.FromValues(values), rules, 7);

        [Test]
        public void NewGame_StartsWithTwoTiles()
        {
            var game = new ForgeGame(new ForgeRules(), 1);
            Assert.That(game.Board.TileCount, Is.EqualTo(ForgeRules.InitialTiles));
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Playing));
            Assert.That(game.Score, Is.Zero);
            Assert.That(game.MoveCount, Is.Zero);
            Assert.That(game.LastOutcome, Is.Null);
            Assert.That(game.LastSpawn, Is.Null);
        }

        [Test]
        public void SameSeedAndMoves_ReproduceTheGame()
        {
            var first = new ForgeGame(new ForgeRules(), 42);
            var second = new ForgeGame(new ForgeRules(), 42);
            for (var i = 0; i < 200; i++)
            {
                var direction = _allDirections[i % _allDirections.Length];
                Assert.That(first.Move(direction), Is.EqualTo(second.Move(direction)));
                Assert.That(first.Board, Is.EqualTo(second.Board));
            }
        }

        [Test]
        public void ValidMove_AddsScoreAndSpawnsOneTile()
        {
            var game = From(OnlyTwos, 2, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            Assert.That(game.Move(Direction.Left), Is.EqualTo(MoveResult.Moved));
            Assert.That(game.Score, Is.EqualTo(4));
            Assert.That(game.MoveCount, Is.EqualTo(1));
            Assert.That(game.Board.TileCount, Is.EqualTo(2));
            Assert.That(game.LastSpawn, Is.Not.Null);
            Assert.That(game.Board[game.LastSpawn!.Cell], Is.EqualTo(2));
            Assert.That(game.LastOutcome!.After[0], Is.EqualTo(4));
        }

        [Test]
        public void UnchangedMove_IsRejectedWithoutSpawnScoreOrRandomUse()
        {
            int[] values = { 2, 4, 8, 16, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
            var game = From(new ForgeRules(), values);
            var twin = From(new ForgeRules(), values);

            Assert.That(game.Move(Direction.Left), Is.EqualTo(MoveResult.Rejected));
            Assert.That(game.Move(Direction.Up), Is.EqualTo(MoveResult.Rejected));
            Assert.That(game.Board, Is.EqualTo(BoardState.FromValues(values)));
            Assert.That(game.Score, Is.Zero);
            Assert.That(game.MoveCount, Is.Zero);
            Assert.That(game.LastOutcome, Is.Null);

            game.Move(Direction.Down);
            twin.Move(Direction.Down);
            Assert.That(game.Board, Is.EqualTo(twin.Board), "拒否された手は乱数を消費しない");
        }

        [Test]
        public void InvalidDirection_IsRejected()
        {
            var game = new ForgeGame(new ForgeRules(), 1);
            var board = game.Board;
            Assert.That(game.Move((Direction)99), Is.EqualTo(MoveResult.Rejected));
            Assert.That(game.Board, Is.EqualTo(board));
        }

        [Test]
        public void FullBoardWithEqualNeighbors_IsNotLost()
        {
            var game = From(
                OnlyTwos,
                2, 2, 8, 16,
                8, 16, 32, 2,
                4, 8, 16, 32,
                8, 16, 32, 64);

            Assert.That(game.Move(Direction.Left), Is.EqualTo(MoveResult.Moved));
            Assert.That(game.Board.EmptyCount, Is.Zero);
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Playing));
            Assert.That(game.Move(Direction.Up), Is.EqualTo(MoveResult.Moved), "満杯でも合体できる手が残っている");
        }

        [Test]
        public void FullBoardWithoutMoves_IsLost()
        {
            var game = From(
                OnlyTwos,
                2, 2, 8, 16,
                8, 16, 32, 64,
                4, 8, 16, 32,
                8, 16, 32, 64);

            Assert.That(game.Move(Direction.Left), Is.EqualTo(MoveResult.Lost));
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Lost));
            Assert.That(game.IsOver, Is.True);
        }

        [Test]
        public void ReachingTarget_WinsWithoutSpawning()
        {
            var game = From(new ForgeRules(), 1024, 1024, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            Assert.That(game.Move(Direction.Left), Is.EqualTo(MoveResult.Won));
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Won));
            Assert.That(game.Score, Is.EqualTo(ForgeRules.TargetValue));
            Assert.That(game.Board.TileCount, Is.EqualTo(1));
            Assert.That(game.LastSpawn, Is.Null);
        }

        [Test]
        public void ReachingTarget_OnCrowdedBoard_WinsInsteadOfLosing()
        {
            var game = From(
                OnlyTwos,
                1024, 1024, 8, 16,
                8, 16, 32, 64,
                4, 8, 16, 32,
                8, 16, 32, 64);

            Assert.That(game.Move(Direction.Left), Is.EqualTo(MoveResult.Won));
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Won));
            Assert.That(game.Board.EmptyCount, Is.EqualTo(1));
        }

        [Test]
        public void AfterGameOver_MovesAreRejected()
        {
            var won = From(new ForgeRules(), 1024, 1024, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            won.Move(Direction.Left);
            var board = won.Board;
            foreach (var direction in _allDirections) { Assert.That(won.Move(direction), Is.EqualTo(MoveResult.Rejected)); }
            Assert.That(won.Board, Is.EqualTo(board));
            Assert.That(won.Score, Is.EqualTo(ForgeRules.TargetValue));
            Assert.That(won.MoveCount, Is.EqualTo(1));
        }

        [Test]
        public void FromBoard_RejectsBoardsThatCannotBePlayed()
        {
            var rules = new ForgeRules();
            var stuck = BoardState.FromValues(2, 4, 2, 4, 4, 2, 4, 2, 2, 4, 2, 4, 4, 2, 4, 2);
            Assert.Throws<ArgumentNullException>(() => ForgeGame.FromBoard(null!, rules, 1));
            Assert.Throws<ArgumentException>(() => ForgeGame.FromBoard(BoardState.Empty, rules, 1));
            Assert.Throws<ArgumentException>(() => ForgeGame.FromBoard(BoardState.Empty.With(0, ForgeRules.TargetValue), rules, 1));
            Assert.Throws<ArgumentException>(() => ForgeGame.FromBoard(stuck, rules, 1));
        }

        [TestCase(-1)]
        [TestCase(101)]
        public void Rules_RejectOutOfRangeChance(int percent)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ForgeGame(new ForgeRules { FourChancePercent = percent }, 1));
        }

        [Test]
        public void Constructor_RejectsNullRules()
        {
            Assert.Throws<ArgumentNullException>(() => new ForgeGame(null!, 1));
        }

        [Test]
        public void Rules_AreCopiedAtStart()
        {
            var rules = new ForgeRules { FourChancePercent = 0 };
            var game = ForgeGame.FromBoard(BoardState.Empty.With(0, 2), rules, 3);
            rules.FourChancePercent = 100;
            game.Move(Direction.Right);
            Assert.That(game.LastSpawn!.Value, Is.EqualTo(2));
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(8)]
        public void RandomPlay_FinishesAndKeepsScoreAndSumConsistent(int seed)
        {
            var game = new ForgeGame(new ForgeRules(), seed);
            var pilot = new SeededRandom(seed + 1000);
            var score = 0;
            for (var i = 0; i < MoveLimit && !game.IsOver; i++)
            {
                var before = game.Board.Sum;
                var result = game.Move(_allDirections[pilot.Next(_allDirections.Length)]);
                if (result == MoveResult.Rejected)
                {
                    Assert.That(game.Board.Sum, Is.EqualTo(before));
                    continue;
                }

                score += game.LastOutcome!.ScoreGained;
                var spawned = game.LastSpawn?.Value ?? 0;
                Assert.That(game.Board.Sum, Is.EqualTo(before + spawned));
                Assert.That(game.Score, Is.EqualTo(score));
            }

            Assert.That(game.IsOver, Is.True);
        }
    }
}
