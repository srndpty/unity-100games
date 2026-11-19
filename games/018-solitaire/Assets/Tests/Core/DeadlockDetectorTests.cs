using System;
using System.Collections.Generic;
using NUnit.Framework;
using static Game.Core.Tests.SolitaireTestKit;

namespace Game.Core.Tests
{
    public sealed class DeadlockDetectorTests
    {
        [Test]
        public void NoMovesAtAllIsStuck()
        {
            var table = StuckTable();
            var moves = new List<Command>();
            MoveRules.LegalMoves(table, moves);
            Assert.That(moves, Is.Empty);
            Assert.That(DeadlockDetector.Check(table, 20_000), Is.EqualTo(DeadlockVerdict.Stuck));
        }

        [Test]
        public void StockCardsThatFitNowhereAreStuckEvenThoughDrawingIsLegal()
        {
            // 3F・5F（赤）と 2M（黒の 2、赤の 3 がない）は、何周めくってもどこにも置けない。
            var table = StuckTable(stock: "3F 5F 2M");
            Assert.That(DeadlockDetector.Check(table, 20_000), Is.EqualTo(DeadlockVerdict.Stuck));
            Assert.That(NaiveReachesProgress(table, 100_000), Is.False);
        }

        [Test]
        public void DrawThreeCanHideACardForever()
        {
            // 3 枚めくりでは 3M（4S の上に置ける）が常に 2 枚目にあり、一番上に来ない。
            var table = StuckTable(stock: "5F 3M 3F", drawCount: 3);
            Assert.That(DeadlockDetector.Check(table, 20_000), Is.EqualTo(DeadlockVerdict.Stuck));
            Assert.That(NaiveReachesProgress(table, 100_000), Is.False);

            var drawOne = StuckTable(stock: "5F 3M 3F", drawCount: 1);
            Assert.That(DeadlockDetector.Check(drawOne, 20_000), Is.EqualTo(DeadlockVerdict.Progress));
        }

        [Test]
        public void ProgressFoundAfterDrawingSeveralTimes()
        {
            // 3M は山札の一番下。2 回めくると捨て札の上に来て、4S の上に置ける。
            var table = StuckTable(stock: "3M 5F");
            Assert.That(DeadlockDetector.Check(table, 20_000), Is.EqualTo(DeadlockVerdict.Progress));
        }

        [Test]
        public void ProgressFoundAfterRecycling()
        {
            // 捨て札の下にある 3M は、戻してめくり直すと使える。
            var table = StuckTable(waste: "3M 5F");
            Assert.That(DeadlockDetector.Check(table, 20_000), Is.EqualTo(DeadlockVerdict.Progress));
        }

        [Test]
        public void ImmediateFoundationMoveIsProgress()
        {
            var table = StuckTable(waste: "AF");
            Assert.That(DeadlockDetector.Check(table, 1), Is.EqualTo(DeadlockVerdict.Progress));
        }

        [Test]
        public void SearchBeyondTheLimitIsUndetermined()
        {
            // 手詰まりの盤でも、めくりの局面を調べ切る前に上限に達すると決めない。
            var table = StuckTable(stock: "3F 5F 2M");
            Assert.That(DeadlockDetector.Check(table, 1), Is.EqualTo(DeadlockVerdict.Undetermined));
        }

        [Test]
        public void CheckDoesNotChangeTheTable()
        {
            var table = Deal.Create(3, 1);
            var before = TableText.Format(table);
            DeadlockDetector.Check(table, 20_000);
            Assert.That(TableText.Format(table), Is.EqualTo(before));
        }

        [Test]
        public void InvalidLimitThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => DeadlockDetector.Check(StuckTable(), 0));
        }

        /// <summary>
        /// 乱数で遊んで手詰まりと判定された局面を、並べ替えを同一視しない素朴な幅優先探索で照合する。
        /// 手詰まりと判定した局面からは、どの手順でも前進できないこと（偽陽性がないこと）を確かめる。
        /// 素朴な探索は同一視をしないので局面が多く、上限を超えたもの（null）は照合できなかったものとして数えない。
        /// </summary>
        [TestCase(1)]
        [TestCase(3)]
        public void StuckVerdictsFromRandomPlayAreConfirmedByNaiveSearch(int drawCount)
        {
            var stuck = 0;
            var confirmed = 0;
            for (var seed = 1; seed <= 20; seed++)
            {
                var game = new SolitaireGame(Config(drawCount), seed);
                PlayRandomly(game, seed * 7919, 1000);
                if (game.Phase != GamePhase.Stuck) { continue; }
                stuck++;
                var naive = NaiveReachesProgress(game.Table, 50_000);
                Assert.That(naive, Is.Not.EqualTo(true), $"シード {seed} の手詰まりから前進できる\n{TableText.Format(game.Table)}");
                if (naive == false) { confirmed++; }
            }

            Assert.That(stuck, Is.GreaterThan(5), "手詰まりの局面が少なすぎる");
            Assert.That(confirmed, Is.GreaterThan(5), "素朴な探索で照合できた局面が少なすぎる");
        }
    }
}
