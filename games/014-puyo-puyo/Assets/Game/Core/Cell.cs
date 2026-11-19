using System;

namespace Game.Core
{
    /// <summary>盤面の1マスの中身。</summary>
    public enum Cell : byte
    {
        Empty = 0,
        Ember = 1,
        Moss = 2,
        Tide = 3,
        Pollen = 4,

        /// <summary>枯れ胞子（おじゃま）。連結せず、隣で胞子が消えると一緒に消える。</summary>
        Ash = 5,
    }

    public static class CellRules
    {
        public const int SporeColorCount = 4;

        /// <summary>連結して消える色付きの胞子か。</summary>
        public static bool IsSpore(this Cell cell) => cell >= Cell.Ember && cell <= Cell.Pollen;
    }

    /// <summary>盤面上の位置。</summary>
    public readonly struct CellPos : IEquatable<CellPos>
    {
        public CellPos(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }
        public int Y { get; }

        public static bool operator ==(CellPos left, CellPos right) => left.Equals(right);
        public static bool operator !=(CellPos left, CellPos right) => !left.Equals(right);

        public bool Equals(CellPos other) => X == other.X && Y == other.Y;
        public override bool Equals(object? obj) => obj is CellPos other && Equals(other);
        public override int GetHashCode() => (Y * Board.Width) + X;
        public override string ToString() => $"({X}, {Y})";
    }
}
