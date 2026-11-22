using System;
using TileMotion;

namespace Game.Core
{
    /// <summary>向き（4 方向）。剣はこの向きの前に出る。</summary>
    public enum Facing
    {
        Right,
        Up,
        Left,
        Down,
    }

    /// <summary>向きと移動量の変換。</summary>
    public static class Facings
    {
        public static int Dx(Facing facing) => facing switch
        {
            Facing.Right => 1,
            Facing.Left => -1,
            Facing.Up => 0,
            Facing.Down => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(facing), facing, "未定義の向きです。"),
        };

        public static int Dy(Facing facing) => facing switch
        {
            Facing.Up => 1,
            Facing.Down => -1,
            Facing.Right => 0,
            Facing.Left => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(facing), facing, "未定義の向きです。"),
        };

        /// <summary>差 (dx, dy) の大きいほうの軸の向き（同じなら横）。両方 0 なら <paramref name="fallback"/>。</summary>
        public static Facing Toward(long dx, long dy, Facing fallback)
        {
            if (dx == 0 && dy == 0) { return fallback; }
            if (Math.Abs(dx) >= Math.Abs(dy)) { return dx > 0 ? Facing.Right : Facing.Left; }
            return dy > 0 ? Facing.Up : Facing.Down;
        }
    }

    /// <summary>マスの位置（列・行。行 0 が最下段）。</summary>
    public readonly struct Cell : IEquatable<Cell>
    {
        public Cell(int column, int row)
        {
            Column = column;
            Row = row;
        }

        public int Column { get; }

        public int Row { get; }

        /// <summary>マスの中央に置いた、幅・高さ <paramref name="size"/> の箱。</summary>
        public Box Centered(int size) => new((Column * Units.Tile) + ((Units.Tile - size) / 2), (Row * Units.Tile) + ((Units.Tile - size) / 2), size, size);

        /// <summary>マス全体の箱。</summary>
        public Box Whole => new(Column * Units.Tile, Row * Units.Tile, Units.Tile, Units.Tile);

        public bool Equals(Cell other) => Column == other.Column && Row == other.Row;

        public override bool Equals(object? obj) => obj is Cell other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Column, Row);

        public override string ToString() => $"({Column}, {Row})";

        public static bool operator ==(Cell left, Cell right) => left.Equals(right);

        public static bool operator !=(Cell left, Cell right) => !left.Equals(right);
    }

    /// <summary>箱の補助。</summary>
    public static class Boxes
    {
        public static int CenterY(Box box) => box.Y + (box.Height / 2);

        /// <summary>箱の中心があるマス。</summary>
        public static Cell CellOf(Box box) => new(Units.FloorDiv(box.CenterX, Units.Tile), Units.FloorDiv(CenterY(box), Units.Tile));
    }

    /// <summary>
    /// 1 Tick の移動の経路。地形の解決と同じく、横に動いてから（<see cref="Start"/> → <see cref="Corner"/>）
    /// 縦に動く（<see cref="Corner"/> → <see cref="End"/>）。各区間は 1 軸だけの移動で、地形で止まった位置まで（022・023 と同じ）。
    /// </summary>
    public readonly struct Motion
    {
        public Motion(Box start, Box corner, Box end)
        {
            Start = start;
            Corner = corner;
            End = end;
        }

        public static Motion Still(Box box) => new(box, box, box);

        public Box Start { get; }

        public Box Corner { get; }

        public Box End { get; }

        public Box HorizontalSweep => Box.Span(Start, Corner);

        public Box VerticalSweep => Box.Span(Corner, End);

        /// <summary>経路のどこかで止まっている <paramref name="target"/> に重なるか。</summary>
        public bool Touches(Box target) => HorizontalSweep.Overlaps(target) || VerticalSweep.Overlaps(target);
    }

    /// <summary>Tick の中で最初に触れた時刻（0〜1 の分数）。触れていなければ <see cref="Hit"/> が false。</summary>
    public readonly struct Touch
    {
        private Touch(bool hit, long numerator, long denominator)
        {
            Hit = hit;
            Numerator = numerator;
            Denominator = denominator;
        }

        public static Touch None => new(false, 1, 1);

        public bool Hit { get; }

        public long Numerator { get; }

        public long Denominator { get; }

        public static Touch At(long numerator, long denominator)
        {
            if (denominator <= 0) { throw new ArgumentOutOfRangeException(nameof(denominator), denominator, "分母は正の値にしてください。"); }
            return new Touch(true, numerator, denominator);
        }

        /// <summary>この接触が <paramref name="other"/> より早ければ true（触れていない接触はいつも遅い）。</summary>
        public bool IsEarlierThan(Touch other)
        {
            if (!Hit) { return false; }
            if (!other.Hit) { return true; }
            return Numerator * other.Denominator < other.Numerator * Denominator;
        }
    }

    /// <summary>
    /// 動く 2 つの箱の接触判定（021 の EnemyContact・023 の Sweep を移植）。Tick の中で両者はそれぞれ始めの位置から終わりの位置へ
    /// 直線で動くとみなし、相対軌跡を掃引して最初に重なり始める時刻を求める。辺が接するだけでは触れたことにならない。
    /// </summary>
    public static class Sweep
    {
        /// <summary>
        /// 自機の経路 <paramref name="a"/>（横 → 縦）と、直線で動く <paramref name="bStart"/> → <paramref name="bEnd"/> の最初の接触。
        /// 自機は経路を一定の速さで進むとみなし、Tick を横の区間（横の移動量の割合）と縦の区間に分ける。相手はその分け目の時刻の位置へ補間し、
        /// 区間ごとに同じ時間の範囲で比べる（地形の解決と被弾の判定が同じ経路を使う）。片方の軸が 0 なら 1 本の直線と同じ。
        /// </summary>
        public static Touch Along(Motion a, Box bStart, Box bEnd)
        {
            long first = Math.Abs(a.Corner.X - a.Start.X);
            long second = Math.Abs(a.End.Y - a.Corner.Y);
            if (first == 0 || second == 0) { return First(a.Start, a.End, bStart, bEnd); }

            // 分け目の時刻は first / total。座標を total 倍すると相手の分け目の位置も整数になる。
            // 速さは Tile 未満（VaultConfig.MaxSpeed）なので、total 倍しても int に収まる。
            var total = (int)(first + second);
            var split = new Box(
                (bStart.X * total) + ((bEnd.X - bStart.X) * (int)first),
                (bStart.Y * total) + ((bEnd.Y - bStart.Y) * (int)first),
                bStart.Width * total,
                bStart.Height * total);
            var horizontal = First(Scale(a.Start, total), Scale(a.Corner, total), Scale(bStart, total), split);
            if (horizontal.Hit) { return Touch.At(first * horizontal.Numerator, total * horizontal.Denominator); }

            var vertical = First(Scale(a.Corner, total), Scale(a.End, total), split, Scale(bEnd, total));
            if (!vertical.Hit) { return Touch.None; }
            return Touch.At((first * vertical.Denominator) + (second * vertical.Numerator), total * vertical.Denominator);
        }

        private static Box Scale(Box box, int factor) => new(box.X * factor, box.Y * factor, box.Width * factor, box.Height * factor);

        public static Touch First(Box aStart, Box aEnd, Box bStart, Box bEnd)
        {
            var rx = (long)(aEnd.X - aStart.X) - (bEnd.X - bStart.X);
            var ry = (long)(aEnd.Y - aStart.Y) - (bEnd.Y - bStart.Y);
            if (!Axis(aStart.Left, aStart.Right, bStart.Left, bStart.Right, rx, out var enterX, out var exitX)
                || !Axis(aStart.Bottom, aStart.Top, bStart.Bottom, bStart.Top, ry, out var enterY, out var exitY))
            {
                return Touch.None;
            }

            var enter = Max(enterX, enterY);
            var exit = Min(exitX, exitY);
            if (enter.CompareTo(exit) >= 0 || enter.CompareTo(Time.One) >= 0 || exit.CompareTo(Time.Zero) <= 0)
            {
                return Touch.None;
            }

            return enter.CompareTo(Time.Zero) < 0 ? Touch.At(0, 1) : Touch.At(enter.Numerator, enter.Denominator);
        }

        private static bool Axis(int a0, int a1, int b0, int b1, long r, out Time enter, out Time exit)
        {
            if (r == 0)
            {
                enter = Time.NegativeInfinity;
                exit = Time.PositiveInfinity;
                return a0 < b1 && b0 < a1;
            }

            if (r > 0)
            {
                enter = new Time((long)b0 - a1, r);
                exit = new Time((long)b1 - a0, r);
            }
            else
            {
                enter = new Time((long)a0 - b1, -r);
                exit = new Time((long)a1 - b0, -r);
            }

            return true;
        }

        private static Time Max(Time a, Time b) => a.CompareTo(b) >= 0 ? a : b;

        private static Time Min(Time a, Time b) => a.CompareTo(b) <= 0 ? a : b;

        /// <summary>分数の時刻。分母 0 は無限大（分子の符号が向き）。</summary>
        private readonly struct Time
        {
            public Time(long numerator, long denominator)
            {
                Numerator = numerator;
                Denominator = denominator;
            }

            public static Time Zero => new(0, 1);

            public static Time One => new(1, 1);

            public static Time NegativeInfinity => new(-1, 0);

            public static Time PositiveInfinity => new(1, 0);

            public long Numerator { get; }

            public long Denominator { get; }

            public int CompareTo(Time other)
            {
                if (Denominator == 0 || other.Denominator == 0)
                {
                    var self = Denominator == 0 ? Math.Sign(Numerator) * 2 : 0;
                    var that = other.Denominator == 0 ? Math.Sign(other.Numerator) * 2 : 0;
                    if (self != 0 || that != 0) { return self.CompareTo(that); }
                }

                return (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);
            }
        }
    }
}
