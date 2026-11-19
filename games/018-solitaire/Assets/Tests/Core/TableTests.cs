using System;
using NUnit.Framework;
using static Game.Core.Tests.SolitaireTestKit;

namespace Game.Core.Tests
{
    public sealed class TableTests
    {
        [Test]
        public void MovingTheLastFaceUpCardFlipsTheNextAndUndoHidesItAgain()
        {
            var table = Parse("stock: *", "t1: JW QW | 9M", "t2: TF");
            var before = TableText.Format(table);

            var record = table.Execute(Command.Move(T(1), T(2)));
            Assert.That(record.Flipped, Is.True);
            Assert.That(record.Cards, Is.EqualTo(1));
            Assert.That(table.FaceDownCountOf(T(1)), Is.EqualTo(1), "QW が表になった");
            Assert.That(table.CardAt(T(2), 1), Is.EqualTo(Card.Parse("9M")));
            table.ValidateInvariants();

            table.Undo(record);
            Assert.That(TableText.Format(table), Is.EqualTo(before));
            Assert.That(table.FaceDownCountOf(T(1)), Is.EqualTo(2), "QW は伏せ直る");
        }

        [Test]
        public void MovingFromAColumnWithFaceUpCardsLeftDoesNotFlip()
        {
            var table = Parse("stock: *", "t1: QW | 9M 8F", "t2: 9W");
            var record = table.Execute(Command.Move(T(1), T(2)));
            Assert.That(record.Flipped, Is.False);
            Assert.That(table.FaceDownCountOf(T(1)), Is.EqualTo(1));
        }

        [Test]
        public void EmptyingAColumnDoesNotFlip()
        {
            var table = Parse("stock: *", "t1: KM");
            var record = table.Execute(Command.Move(T(1), T(2)));
            Assert.That(record.Flipped, Is.False);
            Assert.That(table.CountOf(T(1)), Is.EqualTo(0));
            Assert.That(table.FaceDownCountOf(T(1)), Is.EqualTo(0));
        }

        [Test]
        public void DrawOneMovesTheTopStockCardFaceUp()
        {
            var table = Parse("stock: 2M 3M 4M", "waste: AS", "t2: " + Rest("2M 3M 4M AS"));
            var record = table.Execute(Command.Draw);
            Assert.That(record.Cards, Is.EqualTo(1));
            Assert.That(table.CountOf(PileId.Stock), Is.EqualTo(2));
            Assert.That(table.CardAt(PileId.Waste, 1), Is.EqualTo(Card.Parse("4M")));
            Assert.That(table.FaceDownCountOf(PileId.Stock), Is.EqualTo(2));
            table.ValidateInvariants();
        }

        [Test]
        public void DrawThreeTakesThreeOrWhatIsLeft()
        {
            var table = Parse("draw: 3", "stock: 2M 3M 4M 5M", "t2: " + Rest("2M 3M 4M 5M"));
            var before = TableText.Format(table);

            var first = table.Execute(Command.Draw);
            Assert.That(first.Cards, Is.EqualTo(3));
            // 1 枚ずつ表にして捨て札へ積むので、山札の一番上（5M）が捨て札の一番下になる。
            Assert.That(TableText.Format(table), Does.Contain("waste: 5M 4M 3M\n"));

            var second = table.Execute(Command.Draw);
            Assert.That(second.Cards, Is.EqualTo(1));
            Assert.That(table.CountOf(PileId.Stock), Is.EqualTo(0));

            table.Undo(second);
            table.Undo(first);
            Assert.That(TableText.Format(table), Is.EqualTo(before));
        }

        [Test]
        public void RecycleTurnsTheWasteBackIntoTheSameStockOrder()
        {
            var table = Parse("stock: 2M 3M 4M", "t2: " + Rest("2M 3M 4M"));
            var start = TableText.Format(table);
            for (var i = 0; i < 3; i++) { table.Execute(Command.Draw); }
            var drawn = TableText.Format(table);

            var record = table.Execute(Command.Recycle);
            Assert.That(record.Cards, Is.EqualTo(3));
            Assert.That(TableText.Format(table), Is.EqualTo(start), "戻すと最初の山札と同じ並び");

            table.Undo(record);
            Assert.That(TableText.Format(table), Is.EqualTo(drawn));
        }

        [Test]
        public void IllegalCommandThrowsAndLeavesTheTableUnchanged()
        {
            var table = Parse("stock: *", "t1: 9M", "t2: 9S");
            var before = TableText.Format(table);
            Assert.Throws<InvalidOperationException>(() => table.Execute(Command.Move(T(1), T(2))));
            Assert.Throws<InvalidOperationException>(() => table.Execute(Command.Recycle));
            Assert.That(TableText.Format(table), Is.EqualTo(before));
        }

        [Test]
        public void UndoWithAMismatchedRecordThrows()
        {
            var table = Parse("stock: *", "t1: QW | 9M");
            Assert.Throws<InvalidOperationException>(() => table.Undo(new MoveRecord(Command.Move(T(1), T(2), 3), 3, false)));
            Assert.Throws<InvalidOperationException>(() => table.Undo(new MoveRecord(Command.Draw, 1, false)));
        }

        [Test]
        public void InvariantsRejectDuplicatesMissingCardsAndHiddenTops()
        {
            Assert.That(() => Parse("stock: *", "t1: 9M 9M"), Throws.InstanceOf<FormatException>(), "表記の重複");
            Assert.That(() => Parse("t1: 9M"), Throws.InvalidOperationException, "52 枚に足りない");
            Assert.That(() => Parse("stock: *", "t1: 9M 8F |"), Throws.InvalidOperationException, "一番上が伏せ札");
            Assert.That(() => Parse("stock: *", "f1: 2S"), Throws.InvalidOperationException, "組札が A から始まらない");
            Assert.That(() => Parse("stock: *", "f1: AS 2F"), Throws.InvalidOperationException, "組札のスート違い");
            Assert.That(() => Parse("stock: *", "waste: 9M | 8F"), Throws.InstanceOf<FormatException>(), "捨て札に伏せ札");
            Assert.That(() => Parse("draw: 2", "stock: *"), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => Parse("x1: 9M"), Throws.InstanceOf<FormatException>());
        }

        [Test]
        public void FormatAndParseRoundTrip()
        {
            var table = Deal.Create(7, 3);
            var text = TableText.Format(table);
            Assert.That(TableText.Format(TableText.Parse(text)), Is.EqualTo(text));
            Assert.That(TableText.Format(Table.CopyOf(table)), Is.EqualTo(text));
        }

        /// <summary>excluded 以外の 52 枚を 1 列にした表記（一番上だけ表）。山札や捨て札だけの盤を書くために使う。</summary>
        private static string Rest(string excluded)
        {
            var skip = new System.Collections.Generic.HashSet<string>(excluded.Split(' '));
            var cards = new System.Collections.Generic.List<string>();
            for (var id = 0; id < Card.DeckSize; id++)
            {
                var text = Card.FromId(id).ToString();
                if (!skip.Contains(text)) { cards.Add(text); }
            }

            cards.Insert(cards.Count - 1, "|");
            return string.Join(" ", cards);
        }
    }
}
