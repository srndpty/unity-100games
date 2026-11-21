using System;
using System.Collections.ObjectModel;

namespace Game.Core
{
    /// <summary>各マスを押したときに反転するマスの集合（自分と上下左右。端は折り返さない）。</summary>
    public static class PressMasks
    {
        private static readonly uint[] _masks = Build();

        public static ReadOnlyCollection<uint> All { get; } = Array.AsReadOnly(_masks);

        public static uint Of(int index) => _masks[LightsBoard.RequireIndex(index)];

        /// <summary>押下の集合（ビットが 1 のマス）をすべて押したときに反転するマスの集合。</summary>
        public static uint Combine(uint presses)
        {
            if ((presses & ~LightsBoard.AllCells) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(presses), presses, $"{LightsBoard.CellCount} ビットを超えています。");
            }

            uint flipped = 0;
            for (var index = 0; index < LightsBoard.CellCount; index++)
            {
                if ((presses & (1u << index)) != 0)
                {
                    flipped ^= _masks[index];
                }
            }

            return flipped;
        }

        private static uint[] Build()
        {
            const int size = LightsBoard.Size;
            var masks = new uint[LightsBoard.CellCount];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var mask = Bit(x, y);
                    if (x > 0) { mask |= Bit(x - 1, y); }
                    if (x < size - 1) { mask |= Bit(x + 1, y); }
                    if (y > 0) { mask |= Bit(x, y - 1); }
                    if (y < size - 1) { mask |= Bit(x, y + 1); }
                    masks[(y * size) + x] = mask;
                }
            }

            return masks;
        }

        private static uint Bit(int x, int y) => 1u << ((y * LightsBoard.Size) + x);
    }
}
