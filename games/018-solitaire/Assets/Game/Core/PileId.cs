using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>山の種類。</summary>
    public enum PileKind
    {
        /// <summary>山札。すべて伏せ札。</summary>
        Stock,

        /// <summary>捨て札。すべて表。</summary>
        Waste,

        /// <summary>組札（4 つ）。すべて表。</summary>
        Foundation,

        /// <summary>場札（7 列）。下側に伏せ札、上側に表の札。</summary>
        Tableau,
    }

    /// <summary>13 の山（山札・捨て札・組札 4・場札 7）のどれか。</summary>
    public readonly struct PileId : IEquatable<PileId>
    {
        public const int FoundationCount = 4;
        public const int TableauCount = 7;
        public const int Count = 2 + FoundationCount + TableauCount;

        private const int FirstFoundation = 2;
        private const int FirstTableau = FirstFoundation + FoundationCount;

        private PileId(int index)
        {
            Index = index;
        }

        public static PileId Stock => new(0);

        public static PileId Waste => new(1);

        /// <summary>全 13 山。山札、捨て札、組札 0〜3、場札 0〜6 の順。</summary>
        public static IReadOnlyList<PileId> All { get; } = Array.AsReadOnly(CreateAll());

        /// <summary>0〜12 の通し番号。</summary>
        public int Index { get; }

        public PileKind Kind => Index switch
        {
            0 => PileKind.Stock,
            1 => PileKind.Waste,
            < FirstTableau => PileKind.Foundation,
            _ => PileKind.Tableau,
        };

        /// <summary>種類の中での番号（組札 0〜3、場札 0〜6。山札と捨て札は 0）。</summary>
        public int Slot => Kind switch
        {
            PileKind.Foundation => Index - FirstFoundation,
            PileKind.Tableau => Index - FirstTableau,
            _ => 0,
        };

        public static PileId Foundation(int slot)
        {
            if (slot < 0 || slot >= FoundationCount)
            {
                throw new ArgumentOutOfRangeException(nameof(slot), slot, $"0〜{FoundationCount - 1} で指定してください。");
            }

            return new PileId(FirstFoundation + slot);
        }

        public static PileId Tableau(int slot)
        {
            if (slot < 0 || slot >= TableauCount)
            {
                throw new ArgumentOutOfRangeException(nameof(slot), slot, $"0〜{TableauCount - 1} で指定してください。");
            }

            return new PileId(FirstTableau + slot);
        }

        public static PileId FromIndex(int index)
        {
            if (index < 0 || index >= Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, $"0〜{Count - 1} で指定してください。");
            }

            return new PileId(index);
        }

        public bool Equals(PileId other) => Index == other.Index;

        public override bool Equals(object? obj) => obj is PileId other && Equals(other);

        public override int GetHashCode() => Index;

        public override string ToString() => Kind switch
        {
            PileKind.Stock => "stock",
            PileKind.Waste => "waste",
            PileKind.Foundation => $"f{Slot + 1}",
            _ => $"t{Slot + 1}",
        };

        public static bool operator ==(PileId left, PileId right) => left.Equals(right);

        public static bool operator !=(PileId left, PileId right) => !left.Equals(right);

        private static PileId[] CreateAll()
        {
            var piles = new PileId[Count];
            for (var i = 0; i < Count; i++) { piles[i] = new PileId(i); }
            return piles;
        }
    }
}
