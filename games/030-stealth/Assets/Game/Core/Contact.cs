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

    /// <summary>同じ時刻の箱どうしを掃引する。辺が接するだけでは重なったことにならない。</summary>
    public static class BoxSweep
    {
        public static bool Touches(Motion intruder, GuardBody guard)
        {
            var start = guard.PreviousBox;
            var end = guard.Box;
            if (!intruder.Touches(Box.Span(start, end))) { return false; }

            var guardDistance = Math.Abs(end.X - start.X) + Math.Abs(end.Y - start.Y);
            if (guardDistance == 0) { return true; }

            // 自機が縦へ折れる時刻と、警備員が止まる時刻で区切る。
            // 両方の分母を掛けた単位で時刻と座標を表すと、区切りの位置を整数で計算できる。
            var horizontal = Math.Abs(intruder.Corner.X - intruder.Start.X);
            var vertical = Math.Abs(intruder.End.Y - intruder.Corner.Y);
            var intruderSpeed = Math.Max(1, horizontal + vertical);
            var guardSpeed = guard.LastWalkSpeed;
            var scale = intruderSpeed * guardSpeed;
            var turn = horizontal * guardSpeed;
            var stop = guardDistance * intruderSpeed;
            var from = 0;
            while (from < scale)
            {
                var to = scale;
                if (turn > from) { to = Math.Min(to, turn); }
                if (stop > from) { to = Math.Min(to, stop); }

                if (OverlapsDuring(IntruderAt(from), IntruderAt(to), GuardAt(from), GuardAt(to))) { return true; }
                from = to;
            }

            return false;

            Box IntruderAt(int time) => At(
                intruder.Start,
                Math.Sign(intruder.Corner.X - intruder.Start.X) * intruderSpeed * Math.Min(time, turn),
                Math.Sign(intruder.End.Y - intruder.Corner.Y) * intruderSpeed * Math.Max(0, time - turn));

            Box GuardAt(int time) => At(
                start,
                Math.Sign(end.X - start.X) * guardSpeed * Math.Min(time, stop),
                Math.Sign(end.Y - start.Y) * guardSpeed * Math.Min(time, stop));

            Box At(Box box, int dx, int dy) => new(
                ((box.X - start.X) * scale) + dx,
                ((box.Y - start.Y) * scale) + dy,
                box.Width * scale,
                box.Height * scale);
        }

        private static bool OverlapsDuring(Box aStart, Box aEnd, Box bStart, Box bEnd)
        {
            var rx = (long)(aEnd.X - aStart.X) - (bEnd.X - bStart.X);
            var ry = (long)(aEnd.Y - aStart.Y) - (bEnd.Y - bStart.Y);
            if (!Axis(aStart.Left, aStart.Right, bStart.Left, bStart.Right, rx, out var enterX, out var exitX)
                || !Axis(aStart.Bottom, aStart.Top, bStart.Bottom, bStart.Top, ry, out var enterY, out var exitY))
            {
                return false;
            }

            var enter = Max(enterX, enterY);
            var exit = Min(exitX, exitY);
            if (enter.CompareTo(exit) >= 0 || enter.CompareTo(Time.One) >= 0 || exit.CompareTo(Time.Zero) <= 0)
            {
                return false;
            }

            return true;
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
