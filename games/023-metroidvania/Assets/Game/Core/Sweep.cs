using System;
using TileMotion;

namespace Game.Core
{
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
    /// 動く 2 つの箱の接触判定（021 の EnemyContact を移植）。Tick の中で両者はそれぞれ始めの位置から終わりの位置へ
    /// 直線で動くとみなし、片方から見たもう片方の相対軌跡を掃引して、最初に重なり始める時刻を求める。
    /// Tick の終わりの位置だけで見ると、すれ違った弾と敵を見逃す。辺が接するだけでは触れたことにならない。
    /// </summary>
    public static class Sweep
    {
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

            // Tick の始めから重なっていれば時刻 0。
            return enter.CompareTo(Time.Zero) < 0 ? Touch.At(0, 1) : Touch.At(enter.Numerator, enter.Denominator);
        }

        /// <summary>1 軸で、動く区間 [a0, a1) が止まった区間 [b0, b1) と重なっている時刻の範囲。重ならなければ false。</summary>
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
