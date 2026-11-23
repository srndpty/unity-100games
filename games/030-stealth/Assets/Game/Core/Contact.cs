using System;
using TileMotion;

namespace Game.Core
{
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
    public readonly struct BoxTouch
    {
        private BoxTouch(bool hit, long numerator, long denominator)
        {
            Hit = hit;
            Numerator = numerator;
            Denominator = denominator;
        }

        public static BoxTouch None => new(false, 1, 1);

        public bool Hit { get; }

        public long Numerator { get; }

        public long Denominator { get; }

        public static BoxTouch At(long numerator, long denominator)
        {
            if (denominator <= 0) { throw new ArgumentOutOfRangeException(nameof(denominator), denominator, "分母は正の値にしてください。"); }
            return new BoxTouch(true, numerator, denominator);
        }

        /// <summary>この接触が <paramref name="other"/> より早ければ true（触れていない接触はいつも遅い）。</summary>
        public bool IsEarlierThan(BoxTouch other)
        {
            if (!Hit) { return false; }
            if (!other.Hit) { return true; }
            return Numerator * other.Denominator < other.Numerator * Denominator;
        }
    }

    /// <summary>
    /// 動く 2 つの箱の接触判定（025 の Sweep を移植）。Tick の中で両者はそれぞれ始めの位置から終わりの位置へ
    /// 直線で動くとみなし、相対軌跡を掃引して最初に重なり始める時刻を求める。辺が接するだけでは触れたことにならない。
    /// </summary>
    public static class BoxSweep
    {
        /// <summary>
        /// 自機の経路 <paramref name="a"/>（横 → 縦）と、直線で動く <paramref name="bStart"/> → <paramref name="bEnd"/> の最初の接触。
        /// 自機は経路を一定の速さで進むとみなし、Tick を横の区間（横の移動量の割合）と縦の区間に分ける。相手はその分け目の時刻の位置へ補間し、
        /// 区間ごとに同じ時間の範囲で比べる（地形の解決と被弾の判定が同じ経路を使う）。片方の軸が 0 なら 1 本の直線と同じ。
        /// </summary>
        public static BoxTouch Along(Motion a, Box bStart, Box bEnd)
        {
            long first = Math.Abs(a.Corner.X - a.Start.X);
            long second = Math.Abs(a.End.Y - a.Corner.Y);
            if (first == 0 || second == 0) { return First(a.Start, a.End, bStart, bEnd); }

            // 分け目の時刻は first / total。座標を total 倍すると相手の分け目の位置も整数になる。
            // 速さは Tile/4 以下（WardConfig.MaxSpeed）なので、total 倍しても int に収まる。
            var total = (int)(first + second);
            var split = new Box(
                (bStart.X * total) + ((bEnd.X - bStart.X) * (int)first),
                (bStart.Y * total) + ((bEnd.Y - bStart.Y) * (int)first),
                bStart.Width * total,
                bStart.Height * total);
            var horizontal = First(Scale(a.Start, total), Scale(a.Corner, total), Scale(bStart, total), split);
            if (horizontal.Hit) { return BoxTouch.At(first * horizontal.Numerator, total * horizontal.Denominator); }

            var vertical = First(Scale(a.Corner, total), Scale(a.End, total), split, Scale(bEnd, total));
            if (!vertical.Hit) { return BoxTouch.None; }
            return BoxTouch.At((first * vertical.Denominator) + (second * vertical.Numerator), total * vertical.Denominator);
        }

        private static Box Scale(Box box, int factor) => new(box.X * factor, box.Y * factor, box.Width * factor, box.Height * factor);

        public static BoxTouch First(Box aStart, Box aEnd, Box bStart, Box bEnd)
        {
            var rx = (long)(aEnd.X - aStart.X) - (bEnd.X - bStart.X);
            var ry = (long)(aEnd.Y - aStart.Y) - (bEnd.Y - bStart.Y);
            if (!Axis(aStart.Left, aStart.Right, bStart.Left, bStart.Right, rx, out var enterX, out var exitX)
                || !Axis(aStart.Bottom, aStart.Top, bStart.Bottom, bStart.Top, ry, out var enterY, out var exitY))
            {
                return BoxTouch.None;
            }

            var enter = Max(enterX, enterY);
            var exit = Min(exitX, exitY);
            if (enter.CompareTo(exit) >= 0 || enter.CompareTo(Time.One) >= 0 || exit.CompareTo(Time.Zero) <= 0)
            {
                return BoxTouch.None;
            }

            return enter.CompareTo(Time.Zero) < 0 ? BoxTouch.At(0, 1) : BoxTouch.At(enter.Numerator, enter.Denominator);
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
