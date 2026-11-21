using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class TableLayoutTests
    {
        private static Table Parse(params string[] lines) => TableText.Parse(string.Join("\n", lines));

        [Test]
        public void TableauCardsStepDownNarrowlyWhileHiddenAndWiderWhenFaceUp()
        {
            var table = Parse("stock: *", "t2: JS QS | 9C 8D");
            var pile = PileId.Tableau(1);
            var slot = TableLayout.SlotCenter(pile);
            Assert.That(TableLayout.CardCenter(table, pile, 0), Is.EqualTo(slot));
            Assert.That(TableLayout.CardCenter(table, pile, 1).Y, Is.EqualTo(slot.Y - TableLayout.FaceDownStep).Within(1e-9));
            Assert.That(TableLayout.CardCenter(table, pile, 2).Y, Is.EqualTo(slot.Y - (2 * TableLayout.FaceDownStep)).Within(1e-9));
            Assert.That(TableLayout.CardCenter(table, pile, 3).Y, Is.EqualTo(slot.Y - (2 * TableLayout.FaceDownStep) - TableLayout.FaceUpStep).Within(1e-9));
        }

        [Test]
        public void LongestColumnEndsAtTheContentBottom()
        {
            // 伏せ札 6 枚の上で開いた K から A までの 13 枚。画面に収まるかは PlayMode で実際の表示範囲を確かめる。
            var table = Parse(
                "stock: *",
                "t7: 2H 3H 4H 5H 7H 8H | KC QD JS TH 9C 8D 7S 6H 5C 4D 3S 2D AC");
            var bottom = TableLayout.CardCenter(table, PileId.Tableau(6), 18).Y - (TableLayout.CardHeight / 2);
            Assert.That(bottom, Is.EqualTo(TableLayout.ContentBottom).Within(1e-9));
            Assert.That(TableLayout.SlotCenter(PileId.Stock).Y + (TableLayout.CardHeight / 2), Is.EqualTo(TableLayout.ContentTop).Within(1e-9));
            Assert.That(TableLayout.SlotCenter(PileId.Tableau(0)).X - (TableLayout.CardWidth / 2), Is.EqualTo(TableLayout.ContentLeft).Within(1e-9));
            Assert.That(TableLayout.SlotCenter(PileId.Foundation(3)).X + (TableLayout.CardWidth / 2), Is.EqualTo(TableLayout.ContentRight).Within(1e-9));
        }

        [Test]
        public void WasteFansOnlyTheTopThreeWithDrawThree()
        {
            var one = Parse("waste: 2H 3H 4H 5H", "stock: *");
            var three = Parse("draw: 3", "waste: 2H 3H 4H 5H", "stock: *");
            var slot = TableLayout.SlotCenter(PileId.Waste);
            for (var i = 0; i < 4; i++) { Assert.That(TableLayout.CardCenter(one, PileId.Waste, i), Is.EqualTo(slot)); }
            Assert.That(TableLayout.CardCenter(three, PileId.Waste, 0), Is.EqualTo(slot));
            Assert.That(TableLayout.CardCenter(three, PileId.Waste, 1), Is.EqualTo(slot));
            Assert.That(TableLayout.CardCenter(three, PileId.Waste, 3).X, Is.EqualTo(slot.X + (2 * TableLayout.WasteFanStep)).Within(1e-9));
        }

        [Test]
        public void HitTestFindsTheFrontmostCard()
        {
            var table = Parse("stock: *", "t2: JS QS | 9C 8D");
            var pile = PileId.Tableau(1);
            var top = TableLayout.CardCenter(table, pile, 3);
            Assert.That(TableLayout.HitTest(table, top), Is.EqualTo(new TableHit(pile, 3)));

            // 一番上の札より上で、9C だけが見えている帯。
            var strip = TableLayout.CardCenter(table, pile, 2) + new TablePoint(0, (TableLayout.CardHeight / 2) - .05);
            Assert.That(TableLayout.HitTest(table, strip), Is.EqualTo(new TableHit(pile, 2)));

            var hidden = TableLayout.CardCenter(table, pile, 0) + new TablePoint(0, (TableLayout.CardHeight / 2) - .05);
            Assert.That(TableLayout.HitTest(table, hidden), Is.EqualTo(new TableHit(pile, 0)));
        }

        [Test]
        public void HitTestReportsEmptySlotsAndStock()
        {
            var table = Parse("stock: *", "t2: 9C");
            Assert.That(TableLayout.HitTest(table, TableLayout.SlotCenter(PileId.Tableau(0))), Is.EqualTo(new TableHit(PileId.Tableau(0), -1)));
            Assert.That(TableLayout.HitTest(table, TableLayout.SlotCenter(PileId.Foundation(2))), Is.EqualTo(new TableHit(PileId.Foundation(2), -1)));
            Assert.That(TableLayout.HitTest(table, TableLayout.SlotCenter(PileId.Stock)), Is.EqualTo(new TableHit(PileId.Stock, -1)));
            Assert.That(TableLayout.HitTest(table, new TablePoint(8, 0)), Is.Null, "卓の外");
            Assert.That(TableLayout.HitTest(table, new TablePoint(double.NaN, 0)), Is.Null);
        }

        [Test]
        public void DropTargetIsThePileWithTheLargestOverlap()
        {
            var table = Parse("stock: *", "t1: 9C", "t2: TD");
            var t2 = TableLayout.CardCenter(table, PileId.Tableau(1), 0);
            Assert.That(TableLayout.DropTarget(table, PileId.Tableau(0), t2 + new TablePoint(.1, -.2)), Is.EqualTo(PileId.Tableau(1)));

            // t2 と t3（空）の間で、t3 に寄っている。
            var between = t2 + new TablePoint(TableLayout.ColumnPitch * .7, 0);
            Assert.That(TableLayout.DropTarget(table, PileId.Tableau(0), between), Is.EqualTo(PileId.Tableau(2)));

            // 元の山と捨て札・山札には落とさない。
            Assert.That(TableLayout.DropTarget(table, PileId.Tableau(1), t2), Is.Null);
            Assert.That(TableLayout.DropTarget(table, PileId.Tableau(0), TableLayout.SlotCenter(PileId.Waste)), Is.Null);
            Assert.That(TableLayout.DropTarget(table, PileId.Tableau(0), new TablePoint(20, 20)), Is.Null);
        }
    }
}
