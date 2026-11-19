using System;
using NUnit.Framework;
using static Game.Core.Tests.Match3TestKit;

namespace Game.Core.Tests
{
    public sealed class Match3GameTests
    {
        [Test]
        public void NoMatchSwapIsRevertedWithoutSpendingAMove()
        {
            var config = new Match3Config();
            var game = new Match3Game(config, 1, TwoChainBoard());
            var before = game.Board;

            var verdict = game.TrySwap(P(4, 5), P(5, 5));

            Assert.That(verdict, Is.EqualTo(SwapVerdict.NoMatch));
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Reverting));
            Assert.That(game.ActiveSwap, Is.EqualTo(new Swap(P(4, 5), P(5, 5))));
            Assert.That(game.TakeEvents(), Is.EqualTo(GameEvents.Rejected));
            for (var i = 0; i < config.SwapTicks * 2; i++) { game.Tick(); }

            Assert.That(game.Phase, Is.EqualTo(GamePhase.Idle));
            Assert.That(game.ActiveSwap, Is.Null);
            Assert.That(game.Board, Is.EqualTo(before));
            Assert.That(game.MovesLeft, Is.EqualTo(config.Moves));
            Assert.That(game.Score, Is.Zero);
        }

        [TestCase(0, 2, 0, 2, SwapVerdict.SameCell)]
        [TestCase(0, 2, 1, 3, SwapVerdict.NotAdjacent)]
        [TestCase(0, 0, -1, 0, SwapVerdict.OutOfBoard)]
        public void InvalidSwapChangesNothing(int ax, int ay, int bx, int by, SwapVerdict expected)
        {
            var game = new Match3Game(new Match3Config(), 1, TwoChainBoard());

            Assert.That(game.TrySwap(P(ax, ay), P(bx, by)), Is.EqualTo(expected));
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Idle));
            Assert.That(game.ActiveSwap, Is.Null);
            Assert.That(game.TakeEvents(), Is.EqualTo(GameEvents.None));
        }

        [Test]
        public void LegalSwapSpendsAMoveAndGoesThroughThePhases()
        {
            var config = new Match3Config();
            var game = new Match3Game(config, 1, TwoChainBoard());

            Assert.That(game.TrySwap(TwoChainSwap.From, TwoChainSwap.To), Is.EqualTo(SwapVerdict.Legal));
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Swapping));
            Assert.That(game.Board, Is.EqualTo(TwoChainBoard()), "入れ替えの演出中は入れ替える前の盤面");
            for (var i = 0; i < config.SwapTicks; i++) { game.Tick(); }

            Assert.That(game.Phase, Is.EqualTo(GamePhase.Clearing));
            Assert.That(game.MovesLeft, Is.EqualTo(config.Moves - 1));
            Assert.That(game.Chain, Is.EqualTo(1));
            Assert.That(game.ClearingCells, Is.EqualTo(new[] { P(0, 2), P(0, 3), P(0, 4) }));
            Assert.That(game.Score, Is.EqualTo(30));
            for (var i = 0; i < config.ClearTicks; i++) { game.Tick(); }

            Assert.That(game.Phase, Is.EqualTo(GamePhase.Falling));
            Assert.That(game.PhaseDuration, Is.EqualTo(config.FallTicksPerRow * 3));
            Assert.That(game.Falls, Is.Not.Empty);
            RunUntilIdle(game);

            Assert.That(game.Phase, Is.EqualTo(GamePhase.Idle));
            Assert.That(game.MaxChain, Is.GreaterThanOrEqualTo(2));
            Assert.That(game.MovesLeft, Is.EqualTo(config.Moves - 1), "連鎖しても手数は1つだけ減る");
            Assert.That(game.MovesUsed, Is.EqualTo(1));
            Assert.That(MatchFinder.Find(game.Board).IsEmpty, Is.True);
            Assert.That(game.TakeEvents() & (GameEvents.Swapped | GameEvents.Cleared | GameEvents.Chained), Is.EqualTo(GameEvents.Swapped | GameEvents.Cleared | GameEvents.Chained));
        }

        [Test]
        public void SwapDuringResolutionIsIgnored()
        {
            var game = new Match3Game(new Match3Config(), 1, TwoChainBoard());
            game.TrySwap(TwoChainSwap.From, TwoChainSwap.To);
            game.TakeEvents();

            for (var i = 0; i < MaxSteps && !game.AcceptsSwap; i++)
            {
                var phase = game.Phase;
                var board = game.Board;
                var moves = game.MovesLeft;
                Assert.That(game.TrySwap(P(3, 3), P(4, 3)), Is.EqualTo(SwapVerdict.Busy), phase.ToString());
                Assert.That(game.Phase, Is.EqualTo(phase));
                Assert.That(game.Board, Is.EqualTo(board));
                Assert.That(game.MovesLeft, Is.EqualTo(moves));
                game.Tick();
            }

            Assert.That(game.TakeEvents() & GameEvents.Rejected, Is.EqualTo(GameEvents.None));
        }

        [Test]
        public void RepeatedSwapRequestIsIgnoredWhileTheFirstIsAnimating()
        {
            var game = new Match3Game(new Match3Config(), 1, TwoChainBoard());

            Assert.That(game.TrySwap(TwoChainSwap.From, TwoChainSwap.To), Is.EqualTo(SwapVerdict.Legal));
            Assert.That(game.TrySwap(TwoChainSwap.From, TwoChainSwap.To), Is.EqualTo(SwapVerdict.Busy));
            RunUntilIdle(game);

            Assert.That(game.MovesLeft, Is.EqualTo(new Match3Config().Moves - 1));
        }

        [Test]
        public void RunningOutOfMovesBelowTargetLoses()
        {
            var config = InstantConfig();
            config.Moves = 1;
            config.TargetScore = 10_000_000;
            var game = new Match3Game(config, 1, TwoChainBoard());
            Play(game, TwoChainSwap);

            Assert.That(game.Phase, Is.EqualTo(GamePhase.Lost));
            Assert.That(game.MovesLeft, Is.Zero);
            Assert.That(game.MovesUsed, Is.EqualTo(1));
            Assert.That(game.TakeEvents() & GameEvents.Lost, Is.EqualTo(GameEvents.Lost));
        }

        [Test]
        public void ReachingTargetWinsEvenOnTheLastMove()
        {
            var config = InstantConfig();
            config.Moves = 1;
            config.TargetScore = 30;
            var game = new Match3Game(config, 1, TwoChainBoard());
            Play(game, TwoChainSwap);

            Assert.That(game.Phase, Is.EqualTo(GamePhase.Won));
            Assert.That(game.TakeEvents() & (GameEvents.Won | GameEvents.Lost), Is.EqualTo(GameEvents.Won));
        }

        [Test]
        public void TargetIsJudgedAfterTheWholeCascade()
        {
            var config = InstantConfig();
            config.TargetScore = 30;
            var game = new Match3Game(config, 1, TwoChainBoard());
            game.TrySwap(TwoChainSwap.From, TwoChainSwap.To);
            game.Tick();

            Assert.That(game.Phase, Is.EqualTo(GamePhase.Won));
            Assert.That(game.MaxChain, Is.GreaterThanOrEqualTo(2), "1段目で目標に届いても、連鎖を最後まで解決してから判定する");
        }

        [Test]
        public void FinishedGameIgnoresSwapsAndTicks()
        {
            var config = InstantConfig();
            config.Moves = 1;
            var game = new Match3Game(config, 1, TwoChainBoard());
            Play(game, TwoChainSwap);
            var ticks = game.TickCount;
            var board = game.Board;
            game.TakeEvents();

            Assert.That(game.IsOver, Is.True);
            Assert.That(game.TrySwap(P(3, 3), P(4, 3)), Is.EqualTo(SwapVerdict.Busy));
            game.Tick();
            Assert.That(game.TickCount, Is.EqualTo(ticks));
            Assert.That(game.Board, Is.EqualTo(board));
            Assert.That(game.TakeEvents(), Is.EqualTo(GameEvents.None));
        }

        [Test]
        public void DeadStartingBoardIsReshuffledFromItsOwnStream()
        {
            var config = new Match3Config();
            var game = new Match3Game(config, 77, DeadBoard());
            var expected = BoardGenerator.Generate(new SeededRandom(Match3Game.SeriesSeed(77, RandomSeries.Reshuffle)), config.Colors, config.MaxGenerationAttempts);

            Assert.That(game.Phase, Is.EqualTo(GamePhase.Reshuffling));
            Assert.That(game.Board, Is.EqualTo(expected));
            Assert.That(game.Reshuffles, Is.EqualTo(1));
            Assert.That(game.MovesLeft, Is.EqualTo(config.Moves), "作り直しで手数は減らない");
            Assert.That(game.TakeEvents(), Is.EqualTo(GameEvents.Reshuffled));
            Assert.That(game.TrySwap(P(0, 0), P(1, 0)), Is.EqualTo(SwapVerdict.Busy));
            for (var i = 0; i < config.ReshuffleTicks; i++) { game.Tick(); }

            Assert.That(game.Phase, Is.EqualTo(GamePhase.Idle));
            Assert.That(SwapRules.HasLegalMove(game.Board), Is.True);
        }

        [Test]
        public void GeneratedStartUsesTheGenerationStream()
        {
            var config = new Match3Config();
            var expected = BoardGenerator.Generate(new SeededRandom(Match3Game.SeriesSeed(5, RandomSeries.Generation)), config.Colors, config.MaxGenerationAttempts);

            Assert.That(new Match3Game(config, 5).Board, Is.EqualTo(expected));
            Assert.That(new Match3Game(config, 6).Board, Is.Not.EqualTo(expected));
        }

        [Test]
        public void SeriesDoNotShareState()
        {
            // 初期盤面を作るのに何回作り直したかによらず、補充の系列は同じ値から始まる。
            Assert.That(Match3Game.SeriesSeed(5, RandomSeries.Generation), Is.Not.EqualTo(Match3Game.SeriesSeed(5, RandomSeries.Refill)));
            Assert.That(Match3Game.SeriesSeed(5, RandomSeries.Refill), Is.Not.EqualTo(Match3Game.SeriesSeed(5, RandomSeries.Reshuffle)));
            var a = new Match3Game(InstantConfig(), 5, TwoChainBoard());
            var b = new Match3Game(InstantConfig(), 5, TwoChainBoard());
            Play(a, TwoChainSwap);
            Play(b, TwoChainSwap);

            Assert.That(a.Board, Is.EqualTo(b.Board));
            Assert.That(a.Score, Is.EqualTo(b.Score));
        }

        [Test]
        public void StartingBoardWithRunOrHoleIsRejected()
        {
            var withRun = Parse(
                "LPLPLP",
                "PLPLPL",
                "LPLPLP",
                "PLPLPL",
                "LPLPLP",
                "CCCLPL");
            var withHole = Parse(
                ".PLPLP",
                "PLPLPL",
                "LPLPLP",
                "PLPLPL",
                "LPLPLP",
                "CALPCA");

            Assert.Throws<ArgumentException>(() => _ = new Match3Game(new Match3Config(), 1, withRun));
            Assert.Throws<ArgumentException>(() => _ = new Match3Game(new Match3Config(), 1, withHole));
        }

        [Test]
        public void InstantAndTimedResolutionAgree()
        {
            for (var seed = 0; seed < 30; seed++)
            {
                var instant = new Match3Game(InstantConfig(), seed);
                var timed = new Match3Game(new Match3Config(), seed);
                PlayFirstMoves(instant);
                PlayFirstMoves(timed);

                Assert.That(instant.Board, Is.EqualTo(timed.Board), $"seed {seed}");
                Assert.That(instant.Score, Is.EqualTo(timed.Score), $"seed {seed}");
                Assert.That(instant.Phase, Is.EqualTo(timed.Phase), $"seed {seed}");
                Assert.That(instant.Reshuffles, Is.EqualTo(timed.Reshuffles), $"seed {seed}");
            }
        }

        [Test]
        public void GameAlwaysEndsWithinTheMoveLimit()
        {
            for (var seed = 0; seed < 100; seed++)
            {
                var game = new Match3Game(InstantConfig(), seed);
                PlayFirstMoves(game);

                Assert.That(game.IsOver, Is.True, $"seed {seed}");
                Assert.That(game.MovesLeft, Is.GreaterThanOrEqualTo(0));
                Assert.That(game.Phase == GamePhase.Won || game.MovesLeft == 0, Is.True, $"seed {seed}");
            }
        }

        [Test]
        public void SameSeedAndMovesReproduceTheFinalState()
        {
            // 整数だけで動くので、Mono と .NET で同じ値になる。目標を届かない値にして、手数を使い切るまで遊ぶ。
            var config = new Match3Config { TargetScore = 10_000_000 };
            var fingerprint = 0u;
            var score = 0L;
            for (var seed = 0; seed < 10; seed++)
            {
                var game = new Match3Game(config, seed * 7919);
                PlayFirstMoves(game);
                fingerprint = unchecked((fingerprint * 31u) + game.Board.Fingerprint());
                score += game.Score;
            }

            Assert.That(fingerprint, Is.EqualTo(3106782398u));
            Assert.That(score, Is.EqualTo(19200L));
        }

        [Test]
        public void ConfigIsCopiedAtStart()
        {
            var config = new Match3Config { Moves = 3 };
            var game = new Match3Game(config, 1);
            config.Moves = 50;

            Assert.That(game.MovesLeft, Is.EqualTo(3));
        }
    }
}
