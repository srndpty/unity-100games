using System;

namespace Game.Core
{
    /// <summary>
    /// 交点の集合（81 点を ulong 2 枚の 128 ビットで持つ値型）。連の石と呼吸点の集合に使う。
    /// 書き換えは新しい値を返す（配列の要素のまま書き換える誤りを防ぐ）。netstandard2.1 には BitOperations がないので数え方は自前。
    /// </summary>
    public readonly struct PointSet : IEquatable<PointSet>
    {
        private const ulong DeBruijn = 0x03F79D71B4CB0A89UL;

        private static readonly int[] _deBruijnIndex =
        {
            0, 47, 1, 56, 48, 27, 2, 60,
            57, 49, 41, 37, 28, 16, 3, 61,
            54, 58, 35, 52, 50, 42, 21, 44,
            38, 32, 29, 23, 17, 11, 4, 62,
            46, 55, 26, 59, 40, 36, 15, 53,
            34, 51, 20, 43, 31, 22, 10, 45,
            25, 39, 14, 33, 19, 30, 9, 24,
            13, 18, 8, 12, 7, 6, 5, 63,
        };

        public PointSet(ulong low, ulong high)
        {
            Low = low;
            High = high;
        }

        public static PointSet Empty => default;

        /// <summary>交点 0〜63。</summary>
        public ulong Low { get; }

        /// <summary>交点 64〜80（下位 17 ビット）。</summary>
        public ulong High { get; }

        public bool IsEmpty => Low == 0 && High == 0;

        public int Count => PopCount(Low) + PopCount(High);

        public static PointSet Of(int point) => Empty.With(point);

        public bool Contains(int point) => point < 64 ? ((Low >> point) & 1) != 0 : ((High >> (point - 64)) & 1) != 0;

        public PointSet With(int point)
        {
            Check(point);
            return point < 64 ? new PointSet(Low | (1UL << point), High) : new PointSet(Low, High | (1UL << (point - 64)));
        }

        public PointSet Without(int point)
        {
            Check(point);
            return point < 64 ? new PointSet(Low & ~(1UL << point), High) : new PointSet(Low, High & ~(1UL << (point - 64)));
        }

        public PointSet Union(PointSet other) => new(Low | other.Low, High | other.High);

        public PointSet Except(PointSet other) => new(Low & ~other.Low, High & ~other.High);

        public PointSet Intersect(PointSet other) => new(Low & other.Low, High & other.High);

        /// <summary>番号の最も小さい交点。空なら <see cref="Points.Pass"/>。</summary>
        public int First()
        {
            if (Low != 0) { return LowestIndex(Low); }
            if (High != 0) { return 64 + LowestIndex(High); }
            return Points.Pass;
        }

        /// <summary>番号の小さい順に回す（割り当てなし）。</summary>
        public Enumerator GetEnumerator() => new(this);

        public bool Equals(PointSet other) => Low == other.Low && High == other.High;

        public override bool Equals(object? obj) => obj is PointSet other && Equals(other);

        public override int GetHashCode() => unchecked((Low.GetHashCode() * 397) ^ High.GetHashCode());

        public static bool operator ==(PointSet left, PointSet right) => left.Equals(right);

        public static bool operator !=(PointSet left, PointSet right) => !left.Equals(right);

        public override string ToString()
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (var p in this) { names.Add(Points.Name(p)); }
            return "{" + string.Join(" ", names) + "}";
        }

        private static void Check(int point)
        {
            if (!Points.IsOnBoard(point)) { throw new ArgumentOutOfRangeException(nameof(point), point, "盤の外です。"); }
        }

        private static int PopCount(ulong value)
        {
            unchecked
            {
                value -= (value >> 1) & 0x5555555555555555UL;
                value = (value & 0x3333333333333333UL) + ((value >> 2) & 0x3333333333333333UL);
                value = (value + (value >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
                return (int)((value * 0x0101010101010101UL) >> 56);
            }
        }

        private static int LowestIndex(ulong value)
        {
            unchecked
            {
                return _deBruijnIndex[((value ^ (value - 1)) * DeBruijn) >> 58];
            }
        }

        public struct Enumerator
        {
            private ulong _low;
            private ulong _high;

            internal Enumerator(PointSet set)
            {
                _low = set.Low;
                _high = set.High;
                Current = Points.Pass;
            }

            public int Current { get; private set; }

            public bool MoveNext()
            {
                if (_low != 0)
                {
                    Current = LowestIndex(_low);
                    _low &= _low - 1;
                    return true;
                }

                if (_high != 0)
                {
                    Current = 64 + LowestIndex(_high);
                    _high &= _high - 1;
                    return true;
                }

                return false;
            }
        }
    }
}
