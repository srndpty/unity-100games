using System.Collections.Generic;
using NUnit.Framework;
using static Game.Core.Tests.SolitaireTestKit;

namespace Game.Core.Tests
{
    public sealed class MoveRulesTests
    {
        // t1: 伏せ 2 枚の上に 9M 8F 7W（表の連続）。t2: 伏せ 1 枚の上に TF。t3: 空。t4: 9S。t5: KM。
        // 捨て札の上は 6S、組札 f1 は太陽の A〜5、f2 は空。
        private static readonly Table _sample = Parse(
            "stock: *",
            "waste: 2M 6S",
            "f1: AS 2S 3S 4S 5S",
            "t1: JW QW | 9M 8F 7W",
            "t2: QM | TF",
            "t4: 9S",
            "t5: KM");

        [Test]
        public void RunMovesOntoOppositeColorOneHigher()
        {
            // 9M 8F 7W（3 枚）を TF（赤の 10）へ。
            Assert.That(MoveRules.Check(_sample, Command.Move(T(1), T(2), 3)), Is.EqualTo(MoveError.None));
            // 7W 1 枚は 8 の赤がないので TF へは不可。
            Assert.That(MoveRules.Check(_sample, Command.Move(T(1), T(2), 1)), Is.EqualTo(MoveError.RankMismatch));
        }

        [Test]
        public void SameColorIsRejected()
        {
            // 8F 7W（2 枚、底は赤の 8）を 9S（赤の 9）へ。
            Assert.That(MoveRules.Check(_sample, Command.Move(T(1), T(4), 2)), Is.EqualTo(MoveError.SameColor));
        }

        [Test]
        public void FaceDownCardsCannotBeMoved()
        {
            Assert.That(MoveRules.Check(_sample, Command.Move(T(1), T(3), 4)), Is.EqualTo(MoveError.FaceDown));
            Assert.That(MoveRules.Check(_sample, Command.Move(T(1), T(3), 6)), Is.EqualTo(MoveError.FaceDown));
        }

        [Test]
        public void EmptyColumnTakesOnlyKing()
        {
            Assert.That(MoveRules.Check(_sample, Command.Move(T(5), T(3))), Is.EqualTo(MoveError.None));
            Assert.That(MoveRules.Check(_sample, Command.Move(T(1), T(3), 3)), Is.EqualTo(MoveError.EmptyColumnNeedsKing));
        }

        [Test]
        public void FoundationRules()
        {
            Assert.That(MoveRules.Check(_sample, Command.Move(PileId.Waste, F(1))), Is.EqualTo(MoveError.None), "6S は 5S の上");
            Assert.That(MoveRules.Check(_sample, Command.Move(T(1), F(1))), Is.EqualTo(MoveError.FoundationSuitMismatch));
            Assert.That(MoveRules.Check(_sample, Command.Move(T(4), F(1))), Is.EqualTo(MoveError.FoundationRankMismatch));
            Assert.That(MoveRules.Check(_sample, Command.Move(T(4), F(2))), Is.EqualTo(MoveError.FoundationNeedsAce));
            Assert.That(MoveRules.Check(_sample, Command.Move(T(1), F(1), 2)), Is.EqualTo(MoveError.NotSingleCard));
        }

        [Test]
        public void AceGoesToAnyEmptyFoundation()
        {
            var table = Parse("stock: *", "waste: AM", "f1: AS");
            Assert.That(MoveRules.Check(table, Command.Move(PileId.Waste, F(2))), Is.EqualTo(MoveError.None));
            Assert.That(MoveRules.Check(table, Command.Move(PileId.Waste, F(4))), Is.EqualTo(MoveError.None));
            Assert.That(MoveRules.Check(table, Command.Move(PileId.Waste, F(1))), Is.EqualTo(MoveError.FoundationSuitMismatch));
        }

        [Test]
        public void WasteAndFoundationGiveOnlyTheTopCard()
        {
            Assert.That(MoveRules.Check(_sample, Command.Move(PileId.Waste, T(1), 2)), Is.EqualTo(MoveError.NotSingleCard));
            Assert.That(MoveRules.Check(_sample, Command.Move(F(1), T(3), 2)), Is.EqualTo(MoveError.NotSingleCard));
            // 組札の上の 5S（赤）は 6 の黒の上にしか戻せない。t1 の上は 7W。
            Assert.That(MoveRules.Check(_sample, Command.Move(F(1), T(1))), Is.EqualTo(MoveError.RankMismatch));
        }

        [Test]
        public void FoundationCardCanReturnToTableau()
        {
            var table = Parse("stock: *", "f1: AS 2S 3S 4S 5S", "t1: 6M");
            Assert.That(MoveRules.Check(table, Command.Move(F(1), T(1))), Is.EqualTo(MoveError.None));
        }

        [Test]
        public void InvalidSourcesAndDestinations()
        {
            Assert.That(MoveRules.Check(_sample, Command.Move(T(1), T(1))), Is.EqualTo(MoveError.SamePile));
            Assert.That(MoveRules.Check(_sample, Command.Move(T(1), T(2), 0)), Is.EqualTo(MoveError.InvalidCount));
            Assert.That(MoveRules.Check(_sample, Command.Move(T(1), T(2), -1)), Is.EqualTo(MoveError.InvalidCount));
            Assert.That(MoveRules.Check(_sample, Command.Move(PileId.Stock, T(3))), Is.EqualTo(MoveError.FromStock));
            Assert.That(MoveRules.Check(_sample, Command.Move(T(1), PileId.Waste)), Is.EqualTo(MoveError.InvalidDestination));
            Assert.That(MoveRules.Check(_sample, Command.Move(T(1), PileId.Stock)), Is.EqualTo(MoveError.InvalidDestination));
            Assert.That(MoveRules.Check(_sample, Command.Move(F(1), F(2))), Is.EqualTo(MoveError.InvalidDestination));
            Assert.That(MoveRules.Check(_sample, Command.Move(T(3), T(1))), Is.EqualTo(MoveError.SourceEmpty));
            Assert.That(MoveRules.Check(_sample, Command.Move(F(2), T(1))), Is.EqualTo(MoveError.SourceEmpty));
        }

        [Test]
        public void DrawAndRecycleDependOnStockAndWaste()
        {
            Assert.That(MoveRules.Check(_sample, Command.Draw), Is.EqualTo(MoveError.None));
            Assert.That(MoveRules.Check(_sample, Command.Recycle), Is.EqualTo(MoveError.StockNotEmpty));
            Assert.That(MoveRules.StockCommand(_sample), Is.EqualTo(Command.Draw));

            var emptyStock = Parse("waste: *");
            Assert.That(MoveRules.Check(emptyStock, Command.Draw), Is.EqualTo(MoveError.StockEmpty));
            Assert.That(MoveRules.Check(emptyStock, Command.Recycle), Is.EqualTo(MoveError.None));
            Assert.That(MoveRules.StockCommand(emptyStock), Is.EqualTo(Command.Recycle));

            var nothing = Parse("t1: " + AllCardsAsColumnText());
            Assert.That(MoveRules.Check(nothing, Command.Recycle), Is.EqualTo(MoveError.WasteEmpty));
            Assert.That(MoveRules.Check(nothing, Command.Draw), Is.EqualTo(MoveError.StockEmpty));
        }

        [Test]
        public void BrokenRunIsRejectedEvenIfBuiltByHand()
        {
            // 手で作った盤でも、表の札が列になっていなければ Table が拒否する。
            Assert.That(() => Parse("stock: *", "t1: 9M 9F"), Throws.InvalidOperationException);
        }

        [Test]
        public void FoundationMoveFromPrefersTheSuitAlreadyStarted()
        {
            var table = Parse("stock: *", "waste: 2M", "f3: AM", "t1: AF");
            Assert.That(MoveRules.FoundationMoveFrom(table, PileId.Waste), Is.EqualTo(Command.Move(PileId.Waste, F(3))));
            Assert.That(MoveRules.FoundationMoveFrom(table, T(1)), Is.EqualTo(Command.Move(T(1), F(1))));
            Assert.That(MoveRules.FoundationMoveFrom(table, T(2)), Is.Null, "空の列");
            Assert.That(MoveRules.FoundationMoveFrom(table, PileId.Stock), Is.Null);
            Assert.That(MoveRules.FoundationMoveFrom(table, F(3)), Is.Null);
        }

        [Test]
        public void LegalMovesOfTheSampleTable()
        {
            var moves = new List<Command>();
            MoveRules.LegalMoves(_sample, moves);
            Assert.That(moves, Is.EquivalentTo(BruteForceLegalMoves(_sample)));
            Assert.That(moves[0], Is.EqualTo(Command.Draw));
            Assert.That(moves, Does.Contain(Command.Move(T(1), T(2), 3)));
            Assert.That(moves, Does.Contain(Command.Move(PileId.Waste, F(1))));
            Assert.That(moves, Does.Contain(Command.Move(PileId.Waste, T(1))));
            Assert.That(moves, Does.Contain(Command.Move(T(5), T(3))));
        }

        /// <summary>置き先から逆引きする列挙を、全コマンドを Check にかける素朴な列挙と、乱数で遊んだ全局面で照合する。</summary>
        [TestCase(1)]
        [TestCase(3)]
        public void LegalMovesMatchBruteForceThroughoutRandomPlay(int drawCount)
        {
            var moves = new List<Command>();
            var checkedStates = 0;
            for (var seed = 1; seed <= 15; seed++)
            {
                var game = new SolitaireGame(Config(drawCount), seed);
                PlayRandomly(game, seed, 300, undoRate: 0.1, onStep: g =>
                {
                    MoveRules.LegalMoves(g.Table, moves);
                    Assert.That(moves, Is.EquivalentTo(BruteForceLegalMoves(g.Table)));
                    checkedStates++;
                });
            }

            Assert.That(checkedStates, Is.GreaterThan(1000));
        }

        private static List<Command> BruteForceLegalMoves(IReadOnlyTable table)
        {
            var expected = new List<Command>();
            foreach (var command in new[] { Command.Draw, Command.Recycle })
            {
                if (MoveRules.Check(table, command) == MoveError.None) { expected.Add(command); }
            }

            foreach (var from in PileId.All)
            {
                foreach (var to in PileId.All)
                {
                    for (var count = 1; count <= Card.DeckSize; count++)
                    {
                        var command = Command.Move(from, to, count);
                        if (MoveRules.Check(table, command) == MoveError.None) { expected.Add(command); }
                    }
                }
            }

            return expected;
        }

        private static string AllCardsAsColumnText()
        {
            // 山札も捨て札も空の盤：52 枚を 1 列に積み、一番上だけ表にする。
            var text = new System.Text.StringBuilder();
            for (var id = 0; id < Card.DeckSize - 1; id++) { text.Append(Card.FromId(id)).Append(' '); }
            text.Append("| ").Append(Card.FromId(Card.DeckSize - 1));
            return text.ToString();
        }
    }
}
