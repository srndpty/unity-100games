using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>64 ビットの盤面集合の操作。netstandard2.1 には BitOperations がないので自前で持つ。</summary>
    public static class Bits
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

        public static int PopCount(ulong value)
        {
            unchecked
            {
                value -= (value >> 1) & 0x5555555555555555UL;
                value = (value & 0x3333333333333333UL) + ((value >> 2) & 0x3333333333333333UL);
                value = (value + (value >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
                return (int)((value * 0x0101010101010101UL) >> 56);
            }
        }

        /// <summary>最下位の立ったビットの番号。value は 0 以外。</summary>
        public static int LowestIndex(ulong value)
        {
            unchecked
            {
                return _deBruijnIndex[((value ^ (value - 1)) * DeBruijn) >> 58];
            }
        }

        /// <summary>立ったビットのマスを番号の小さい順に返す。</summary>
        public static IReadOnlyList<Square> ToSquares(ulong value)
        {
            var squares = new List<Square>(PopCount(value));
            while (value != 0)
            {
                squares.Add(Square.FromIndex(LowestIndex(value)));
                value &= value - 1;
            }

            return squares.AsReadOnly();
        }
    }
}
