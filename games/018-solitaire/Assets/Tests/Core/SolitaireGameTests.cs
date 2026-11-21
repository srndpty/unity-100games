using System;
using System.Collections.Generic;
using NUnit.Framework;
using static Game.Core.Tests.SolitaireTestKit;

namespace Game.Core.Tests
{
    public sealed class SolitaireGameTests
    {
        /// <summary>組札に 51 枚、残りの KS が場札にある。1 手で勝つ。</summary>
        private static Table OneMoveFromWin() => Parse(
            "f1: AH 2H 3H 4H 5H 6H 7H 8H 9H TH JH QH KH",
            "f2: AD 2D 3D 4D 5D 6D 7D 8D 9D TD JD QD KD",
            "f3: AC 2C 3C 4C 5C 6C 7C 8C 9C TC JC QC KC",
            "f4: AS 2S 3S 4S 5S 6S 7S 8S 9S TS JS QS",
            "t3: KS");

        [Test]
        public void NewGameIsPlayingWithNoMoves()
        {
            var game = new SolitaireGame(Config(), 5);
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Playing));
            Assert.That(game.Moves, Is.EqualTo(0));
            Assert.That(game.CanUndo, Is.False);
            Assert.That(game.LastMove, Is.Null);
            Assert.That(game.Undo(), Is.False);
        }

        [Test]
        public void LastCardToFoundationWinsAndEndsTheGame()
        {
            var game = new SolitaireGame(Config(), OneMoveFromWin());
            Assert.That(game.SendToFoundation(T(3)), Is.EqualTo(MoveError.None));
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Won));
            Assert.That(game.Table.FoundationTotal, Is.EqualTo(52));
            Assert.That(game.CanUndo, Is.False, "勝った後は取り消せない");
            Assert.That(game.Undo(), Is.False);
            Assert.That(game.Apply(Command.Move(F(4), T(1))), Is.EqualTo(MoveError.GameOver));
            Assert.That(game.ClickStock(), Is.EqualTo(MoveError.GameOver));
            Assert.That(game.Resign(), Is.False);
        }

        [Test]
        public void FiftyOneCardsInFoundationsIsNotYetAWin()
        {
            var game = new SolitaireGame(Config(), OneMoveFromWin());
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Playing));
        }

        [Test]
        public void RejectedCommandChangesNothing()
        {
            var game = new SolitaireGame(Config(), 5);
            var before = TableText.Format(game.Table);
            Assert.That(game.Apply(Command.Move(PileId.Stock, T(1))), Is.EqualTo(MoveError.FromStock));
            Assert.That(game.Apply(Command.Move(T(1), T(1))), Is.EqualTo(MoveError.SamePile));
            Assert.That(TableText.Format(game.Table), Is.EqualTo(before));
            Assert.That(game.Moves, Is.EqualTo(0));
        }

        [Test]
        public void UndoRestoresFlippedCardAndMoveCount()
        {
            var table = Parse("stock: *", "t1: JS QS | 9C", "t2: TD");
            var game = new SolitaireGame(Config(), table);
            var before = TableText.Format(game.Table);

            Assert.That(game.Apply(Command.Move(T(1), T(2))), Is.EqualTo(MoveError.None));
            Assert.That(game.Moves, Is.EqualTo(1));
            Assert.That(game.LastMove?.Flipped, Is.True);

            Assert.That(game.Undo(), Is.True);
            Assert.That(TableText.Format(game.Table), Is.EqualTo(before));
            Assert.That(game.Table.FaceDownCountOf(T(1)), Is.EqualTo(2));
            Assert.That(game.Moves, Is.EqualTo(0));
        }

        [Test]
        public void ClickStockDrawsThenRecyclesWhenEmpty()
        {
            var game = new SolitaireGame(Config(), 9);
            for (var i = 0; i < 24; i++) { Assert.That(game.ClickStock(), Is.EqualTo(MoveError.None)); }
            Assert.That(game.Table.CountOf(PileId.Stock), Is.EqualTo(0));
            Assert.That(game.ClickStock(), Is.EqualTo(MoveError.None));
            Assert.That(game.Table.CountOf(PileId.Stock), Is.EqualTo(24));
            Assert.That(game.LastMove?.Command, Is.EqualTo(Command.Recycle));
        }

        [Test]
        public void SendToFoundationRejectsCardsThatDoNotFit()
        {
            var game = new SolitaireGame(Config(), Parse("stock: *", "t1: 9C", "waste: AD"));
            Assert.That(game.SendToFoundation(T(1)), Is.EqualTo(MoveError.InvalidDestination));
            Assert.That(game.SendToFoundation(PileId.Waste), Is.EqualTo(MoveError.None));
        }

        [Test]
        public void ResignEndsTheGame()
        {
            var game = new SolitaireGame(Config(), 5);
            Assert.That(game.Resign(), Is.True);
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Resigned));
            Assert.That(game.Resign(), Is.False, "2 回目は無視");
            Assert.That(game.ClickStock(), Is.EqualTo(MoveError.GameOver));
        }

        [Test]
        public void MoveThatLeavesNoProgressEndsAsStuck()
        {
            // QC を KH に置くと、その上に置ける J の赤はなく、残りの 3D・5D もどこにも置けない。
            var game = new SolitaireGame(Config(), StuckTable(waste: "3D 5D QC"));
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Playing));
            Assert.That(game.Apply(Command.Move(PileId.Waste, T(7))), Is.EqualTo(MoveError.None));
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Stuck));
            Assert.That(game.CanUndo, Is.False);
            Assert.That(game.Apply(Command.Recycle), Is.EqualTo(MoveError.GameOver));
        }

        [Test]
        public void StartingFromAStuckTableIsStuck()
        {
            var game = new SolitaireGame(Config(), StuckTable());
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Stuck));
        }

        [Test]
        public void GameCopiesTheTableAndConfig()
        {
            var table = Parse("stock: *", "t1: 9C", "t2: TD");
            var config = Config();
            var game = new SolitaireGame(config, table);
            config.DrawCount = 2;
            game.Apply(Command.Move(T(1), T(2)));
            Assert.That(table.CountOf(T(1)), Is.EqualTo(1), "渡した盤は変わらない");
        }

        /// <summary>
        /// 乱数で遊び、取り消しも混ぜる。毎手の後に不変条件（同じ札が 2 つの山に属さない など）を検査し、
        /// 最後に全部取り消すと配札直後の盤に戻ることを確かめる。
        /// </summary>
        [TestCase(1)]
        [TestCase(3)]
        public void RandomPlayKeepsInvariantsAndUndoesBackToTheDeal(int drawCount)
        {
            for (var seed = 1; seed <= 40; seed++)
            {
                var game = new SolitaireGame(Config(drawCount), seed);
                var dealt = TableText.Format(game.Table);
                var snapshots = new List<string> { dealt };
                PlayRandomly(game, seed, 400, undoRate: 0.2, onStep: g =>
                {
                    Table.CopyOf(g.Table).ValidateInvariants();
                    while (snapshots.Count > g.Moves + 1) { snapshots.RemoveAt(snapshots.Count - 1); }
                    if (snapshots.Count == g.Moves) { snapshots.Add(TableText.Format(g.Table)); }
                    Assert.That(TableText.Format(g.Table), Is.EqualTo(snapshots[g.Moves]), "取り消し後の盤が以前の盤と違う");
                });

                if (game.IsOver) { continue; }
                while (game.Undo()) { }
                Assert.That(TableText.Format(game.Table), Is.EqualTo(dealt), $"シード {seed}");
                Assert.That(game.Moves, Is.EqualTo(0));
            }
        }

        [Test]
        public void InvalidConfigIsRejected()
        {
            Assert.Throws<ArgumentNullException>(() => _ = new SolitaireGame(null!, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SolitaireGame(Config(drawCount: 2), 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SolitaireGame(Config(searchStates: 0), 1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _ = new SolitaireGame(Config(searchStates: SolitaireConfig.MaxDeadlockSearchStates + 1), 1));
            Assert.DoesNotThrow(() => _ = new SolitaireGame(Config(drawCount: 3, searchStates: 1), 1));
        }
    }
}
