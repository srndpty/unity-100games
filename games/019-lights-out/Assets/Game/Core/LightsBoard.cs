using System;

namespace Game.Core
{
    /// <summary>
    /// 5×5 の点灯状態。マス (列 x, 行 y) の index は y * 5 + x で、その位置のビットが 1 なら点灯。
    /// 押下は GF(2) 上の加算（XOR）なので、押す順序によらず結果が決まり、同じマスを 2 回押すと元に戻る。
    /// </summary>
    public readonly struct LightsBoard : IEquatable<LightsBoard>
    {
        public const int Size = 5;
        public const int CellCount = Size * Size;
        public const uint AllCells = (1u << CellCount) - 1;

        public static readonly LightsBoard Dark;

        public LightsBoard(uint bits)
        {
            if ((bits & ~AllCells) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bits), bits, $"{CellCount} ビットを超えています。");
            }

            Bits = bits;
        }

        public uint Bits { get; }

        public bool IsDark => Bits == 0;

        public int LitCount => BitCount.Of(Bits);

        public bool IsLit(int index) => (Bits & (1u << RequireIndex(index))) != 0;

        /// <summary>押したマスと上下左右を反転した盤。</summary>
        public LightsBoard Press(int index) => new(Bits ^ PressMasks.Of(index));

        /// <summary>押下の集合（ビットが 1 のマスを 1 回ずつ押す）を適用した盤。</summary>
        public LightsBoard PressAll(uint presses) => new(Bits ^ PressMasks.Combine(presses));

        public static int IndexOf(int x, int y)
        {
            if (x < 0 || x >= Size) { throw new ArgumentOutOfRangeException(nameof(x), x, "盤の外です。"); }
            if (y < 0 || y >= Size) { throw new ArgumentOutOfRangeException(nameof(y), y, "盤の外です。"); }
            return (y * Size) + x;
        }

        public static int RequireIndex(int index)
        {
            if (index < 0 || index >= CellCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, $"0〜{CellCount - 1} で指定してください。");
            }

            return index;
        }

        public bool Equals(LightsBoard other) => Bits == other.Bits;

        public override bool Equals(object? obj) => obj is LightsBoard other && Equals(other);

        public override int GetHashCode() => (int)Bits;

        public static bool operator ==(LightsBoard left, LightsBoard right) => left.Equals(right);

        public static bool operator !=(LightsBoard left, LightsBoard right) => !left.Equals(right);

        public override string ToString() => $"LightsBoard(0x{Bits:X7})";
    }
}
