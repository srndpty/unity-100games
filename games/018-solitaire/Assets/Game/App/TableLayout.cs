using System;
using Game.Core;

namespace Game.App
{
    /// <summary>卓の上の点（ワールド座標。1 が札 1 枚の幅、上が正）。</summary>
    public readonly struct TablePoint : IEquatable<TablePoint>
    {
        public TablePoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }

        public double Y { get; }

        public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);

        public static TablePoint operator +(TablePoint a, TablePoint b) => new(a.X + b.X, a.Y + b.Y);

        public static TablePoint operator -(TablePoint a, TablePoint b) => new(a.X - b.X, a.Y - b.Y);

        public static bool operator ==(TablePoint left, TablePoint right) => left.Equals(right);

        public static bool operator !=(TablePoint left, TablePoint right) => !left.Equals(right);

        public double DistanceTo(TablePoint other) => Math.Sqrt(((X - other.X) * (X - other.X)) + ((Y - other.Y) * (Y - other.Y)));

        public bool Equals(TablePoint other) => X.Equals(other.X) && Y.Equals(other.Y);

        public override bool Equals(object? obj) => obj is TablePoint other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Y);

        public override string ToString() => $"({X:0.###}, {Y:0.###})";
    }

    /// <summary>クリックした札。Index は山の下から数えた位置。空の山の枠なら -1。</summary>
    public readonly struct TableHit : IEquatable<TableHit>
    {
        public TableHit(PileId pile, int index)
        {
            Pile = pile;
            Index = index;
        }

        public PileId Pile { get; }

        public int Index { get; }

        public static bool operator ==(TableHit left, TableHit right) => left.Equals(right);

        public static bool operator !=(TableHit left, TableHit right) => !left.Equals(right);

        public bool Equals(TableHit other) => Pile == other.Pile && Index == other.Index;

        public override bool Equals(object? obj) => obj is TableHit other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Pile, Index);

        public override string ToString() => $"{Pile}[{Index}]";
    }

    /// <summary>
    /// 札の置き場所。描画とクリックの判定が同じ計算を使う。
    /// 上の段に左から山札・捨て札・（空き）・組札 4 つ、下の段に場札 7 列。場札は伏せ札を狭く、表の札を広くずらして重ねる。
    /// </summary>
    public static class TableLayout
    {
        public const double CardWidth = 1.0;
        public const double CardHeight = 1.4;
        public const double ColumnPitch = 1.22;
        public const double LeftColumnX = -7.9;
        public const double TopRowY = 3.75;
        public const double TableauY = 1.95;
        public const double FaceDownStep = .2;
        public const double FaceUpStep = .36;

        /// <summary>3 枚めくりで、捨て札の上の 3 枚をずらす幅。</summary>
        public const double WasteFanStep = .3;

        public const int WasteFanCount = 3;

        /// <summary>場札の 1 列に残りうる伏せ札の最大（7 列目の配札）。</summary>
        public const int MaxFaceDownInColumn = PileId.TableauCount - 1;

        /// <summary>
        /// 卓の中身が届きうる範囲の端（卓の座標）。下端は、伏せ札 6 枚の上で開いた K から A まで 13 枚が並んだ列の最後の札の下端。
        /// 右端は 3 枚めくりで広げた捨て札ではなく、最も右の列（組札・場札）。
        /// </summary>
        public static double ContentTop => TopRowY + (CardHeight / 2);

        public static double ContentBottom =>
            TableauY - (MaxFaceDownInColumn * FaceDownStep) - ((Card.MaxRank - 1) * FaceUpStep) - (CardHeight / 2);

        public static double ContentLeft => ColumnX(0) - (CardWidth / 2);

        public static double ContentRight => ColumnX(PileId.TableauCount - 1) + (CardWidth / 2);

        /// <summary>空の山の枠の中心。</summary>
        public static TablePoint SlotCenter(PileId pile) => pile.Kind switch
        {
            PileKind.Stock => new TablePoint(ColumnX(0), TopRowY),
            PileKind.Waste => new TablePoint(ColumnX(1), TopRowY),
            PileKind.Foundation => new TablePoint(ColumnX(3 + pile.Slot), TopRowY),
            _ => new TablePoint(ColumnX(pile.Slot), TableauY),
        };

        /// <summary>山の index 番目の札の中心。</summary>
        public static TablePoint CardCenter(IReadOnlyTable table, PileId pile, int index)
        {
            if (table == null) { throw new ArgumentNullException(nameof(table)); }
            var slot = SlotCenter(pile);
            switch (pile.Kind)
            {
                case PileKind.Tableau:
                    var down = Math.Min(index, table.FaceDownCountOf(pile));
                    var up = index - down;
                    return new TablePoint(slot.X, slot.Y - (down * FaceDownStep) - (up * FaceUpStep));
                case PileKind.Waste when table.DrawCount > 1:
                    var fanned = Math.Max(0, index - (table.CountOf(pile) - WasteFanCount));
                    return new TablePoint(slot.X + (fanned * WasteFanStep), slot.Y);
                default:
                    return slot;
            }
        }

        /// <summary>点にある一番手前の札（なければ空の山の枠）。どちらもなければ null。山札は札があっても枠として扱う。</summary>
        public static TableHit? HitTest(IReadOnlyTable table, TablePoint point)
        {
            if (table == null) { throw new ArgumentNullException(nameof(table)); }
            if (!point.IsFinite) { return null; }
            foreach (var pile in PileId.All)
            {
                var count = table.CountOf(pile);
                // 山札・捨て札・組札は一番上の札しか触れない。場札は手前（上）の札から順に調べる。
                var lowest = pile.Kind == PileKind.Tableau ? 0 : count - 1;
                for (var index = count - 1; index >= Math.Max(lowest, 0); index--)
                {
                    if (Contains(CardCenter(table, pile, index), point))
                    {
                        return new TableHit(pile, pile.Kind == PileKind.Stock ? -1 : index);
                    }
                }

                if (count == 0 && Contains(SlotCenter(pile), point)) { return new TableHit(pile, -1); }
            }

            return null;
        }

        /// <summary>
        /// ドラッグした札（底の札の中心が baseCenter）を落とす先。底の札と重なる面積が最も大きい組札・場札（元の山を除く）。重ならなければ null。
        /// </summary>
        public static PileId? DropTarget(IReadOnlyTable table, PileId from, TablePoint baseCenter)
        {
            if (table == null) { throw new ArgumentNullException(nameof(table)); }
            if (!baseCenter.IsFinite) { return null; }
            PileId? best = null;
            var bestArea = 0.0;
            foreach (var pile in PileId.All)
            {
                if (pile == from || (pile.Kind != PileKind.Foundation && pile.Kind != PileKind.Tableau)) { continue; }
                var count = table.CountOf(pile);
                var target = count == 0 ? SlotCenter(pile) : CardCenter(table, pile, count - 1);
                var area = Overlap(target, baseCenter);
                if (area > bestArea)
                {
                    bestArea = area;
                    best = pile;
                }
            }

            return best;
        }

        private static double ColumnX(int column) => LeftColumnX + (column * ColumnPitch);

        private static bool Contains(TablePoint center, TablePoint point) =>
            Math.Abs(point.X - center.X) <= CardWidth / 2 && Math.Abs(point.Y - center.Y) <= CardHeight / 2;

        private static double Overlap(TablePoint a, TablePoint b)
        {
            var width = CardWidth - Math.Abs(a.X - b.X);
            var height = CardHeight - Math.Abs(a.Y - b.Y);
            return width > 0 && height > 0 ? width * height : 0;
        }
    }
}
