using System;

namespace Game.Core
{
    /// <summary>
    /// 9×9 の盤の形。マスは 0〜80 の通し番号（行 × 9 + 列）で表す。
    /// 単位（行・列・ブロック）と、各マスと同じ単位に入る他のマス（ピア）の表を持つ。
    /// </summary>
    public static class Board
    {
        public const int Size = 9;
        public const int BoxSize = 3;
        public const int CellCount = Size * Size;
        public const int UnitCount = Size * 3;
        public const int PeerCount = 20;

        private static readonly int[][] _units = BuildUnits();
        private static readonly int[][] _peers = BuildPeers();

        public static int Row(int cell) => cell / Size;

        public static int Column(int cell) => cell % Size;

        public static int Box(int cell) => Row(cell) / BoxSize * BoxSize + Column(cell) / BoxSize;

        public static int Cell(int row, int column) => row * Size + column;

        public static bool IsCell(int cell) => cell >= 0 && cell < CellCount;

        public static bool IsDigit(int digit) => digit >= 1 && digit <= Size;

        /// <summary>単位の種類。0〜8 が行、9〜17 が列、18〜26 がブロック。</summary>
        public static UnitKind KindOf(int unit) => (UnitKind)(unit / Size);

        /// <summary>単位の番号。行・列・ブロックの中での 0〜8。</summary>
        public static int NumberOf(int unit) => unit % Size;

        /// <summary>単位に入る 9 マス。</summary>
        public static int UnitCell(int unit, int position) => _units[unit][position];

        /// <summary>同じ行・列・ブロックに入る他の 20 マス。</summary>
        public static int Peer(int cell, int position) => _peers[cell][position];

        /// <summary>2 マスが同じ単位に入る（同じマスは含まない）。</summary>
        public static bool AreAligned(int a, int b) => a != b && (Row(a) == Row(b) || Column(a) == Column(b) || Box(a) == Box(b));

        public static void RequireCell(int cell, string name)
        {
            if (!IsCell(cell)) { throw new ArgumentOutOfRangeException(name, cell, "0〜80 のマスを指定してください。"); }
        }

        private static int[][] BuildUnits()
        {
            var units = new int[UnitCount][];
            for (var i = 0; i < Size; i++)
            {
                units[i] = new int[Size];
                units[Size + i] = new int[Size];
                units[Size * 2 + i] = new int[Size];
                for (var j = 0; j < Size; j++)
                {
                    units[i][j] = Cell(i, j);
                    units[Size + i][j] = Cell(j, i);
                    units[Size * 2 + i][j] = Cell(i / BoxSize * BoxSize + j / BoxSize, i % BoxSize * BoxSize + j % BoxSize);
                }
            }

            return units;
        }

        private static int[][] BuildPeers()
        {
            var peers = new int[CellCount][];
            for (var cell = 0; cell < CellCount; cell++)
            {
                peers[cell] = new int[PeerCount];
                var count = 0;
                for (var other = 0; other < CellCount; other++)
                {
                    if (AreAligned(cell, other)) { peers[cell][count++] = other; }
                }
            }

            return peers;
        }
    }

    public enum UnitKind
    {
        Row,
        Column,
        Box,
    }

    /// <summary>数字 1〜9 の集合を 9 ビットで表す。数字 d はビット d-1。</summary>
    public static class Digits
    {
        public const int All = (1 << Board.Size) - 1;

        public static int Bit(int digit) => 1 << (digit - 1);

        public static bool Has(int mask, int digit) => (mask & Bit(digit)) != 0;

        public static int Count(int mask)
        {
            var count = 0;
            while (mask != 0)
            {
                mask &= mask - 1;
                count++;
            }

            return count;
        }

        /// <summary>集合に入る最小の数字。空なら 0。</summary>
        public static int Lowest(int mask)
        {
            if (mask == 0) { return 0; }
            var digit = 1;
            while ((mask & 1) == 0)
            {
                mask >>= 1;
                digit++;
            }

            return digit;
        }
    }
}
