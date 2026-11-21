using System;

namespace Game.Core
{
    public enum ContactKind
    {
        None,

        /// <summary>上から踏んだ。</summary>
        Stomp,

        /// <summary>横・下から触れた、踏めない敵に触れた、Tick の始めから重なっていた。自機のミス。</summary>
        Hurt,
    }

    /// <summary>接触の種類と、Tick の中で最初に触れた時刻（0〜1 の分数）。</summary>
    public readonly struct Contact
    {
        public Contact(ContactKind kind, long timeNumerator, long timeDenominator)
        {
            if (timeDenominator <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(timeDenominator), timeDenominator, "分母は正の値にしてください。");
            }

            Kind = kind;
            TimeNumerator = timeNumerator;
            TimeDenominator = timeDenominator;
        }

        public static Contact None => new(ContactKind.None, 0, 1);

        public ContactKind Kind { get; }

        public long TimeNumerator { get; }

        public long TimeDenominator { get; }

        /// <summary>この接触が <paramref name="other"/> より早いと -1、同時で 0、遅いと 1。</summary>
        public int CompareTime(Contact other)
        {
            return (TimeNumerator * other.TimeDenominator).CompareTo(other.TimeNumerator * TimeDenominator);
        }
    }

    /// <summary>
    /// 自機と敵の接触判定。地形との衝突・接地検出（<see cref="GroundProbe"/>）とは切り離した純粋関数。
    /// Tick の中で両者はそれぞれ始めの位置から終わりの位置へ直線で動くとみなし、敵から見た自機の相対軌跡を掃引して、
    /// 最初に重なり始める時刻と面を求める。最初に触れた面が敵の上面で、自機が相対的に下へ動いていれば踏みつけ。
    /// 縦と横が同時に触れた（角にちょうど当たった）ときは縦を優先する。辺が接するだけでは触れたことにならない。
    /// </summary>
    public static class EnemyContact
    {
        public static Contact Classify(Box playerStart, Box playerEnd, Box enemyStart, Box enemyEnd, bool stompable)
        {
            var rx = (long)(playerEnd.X - playerStart.X) - (enemyEnd.X - enemyStart.X);
            var ry = (long)(playerEnd.Y - playerStart.Y) - (enemyEnd.Y - enemyStart.Y);

            if (!Axis(playerStart.Left, playerStart.Right, enemyStart.Left, enemyStart.Right, rx, out var enterX, out var exitX)
                || !Axis(playerStart.Bottom, playerStart.Top, enemyStart.Bottom, enemyStart.Top, ry, out var enterY, out var exitY))
            {
                return Contact.None;
            }

            var enter = Max(enterX, enterY);
            var exit = Min(exitX, exitY);

            // 重なる時間帯 (enter, exit) が Tick の [0, 1] と交わらなければ触れていない。
            if (enter.CompareTo(exit) >= 0 || enter.CompareTo(Time.One) >= 0 || exit.CompareTo(Time.Zero) <= 0)
            {
                return Contact.None;
            }

            if (enter.CompareTo(Time.Zero) < 0)
            {
                // Tick の始めから重なっている。どちら向きにも決められないので自機のミスとする。
                return new Contact(ContactKind.Hurt, 0, 1);
            }

            var vertical = enterY.CompareTo(enterX) >= 0;
            var stomp = stompable && vertical && ry < 0;
            return new Contact(stomp ? ContactKind.Stomp : ContactKind.Hurt, enter.Numerator, enter.Denominator);
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
                    if (self != 0 || that != 0)
                    {
                        return self.CompareTo(that);
                    }
                }

                return (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);
            }
        }
    }
}
