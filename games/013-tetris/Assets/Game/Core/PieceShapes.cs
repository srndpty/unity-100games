using System;
using System.Collections.Generic;

namespace Game.Core
{
    public enum PieceKind
    {
        I,
        O,
        T,
        S,
        Z,
        J,
        L,
    }

    public enum Rotation
    {
        North,
        East,
        South,
        West,
    }

    /// <summary>7種の形のデータ。北向きの形だけを定義し、他の向きは枠の中で回して求める。</summary>
    public static class PieceShapes
    {
        public const int KindCount = 7;
        public const int RotationCount = 4;
        public const int CellCount = 4;

        private static readonly int[] _boxSizes = { 4, 2, 3, 3, 3, 3, 3 };

        // 枠の左下を原点とする北向きのマス。並びは PieceKind と同じ。
        private static readonly Cell[][] _north =
        {
            new[] { new Cell(0, 2), new Cell(1, 2), new Cell(2, 2), new Cell(3, 2) },
            new[] { new Cell(0, 0), new Cell(1, 0), new Cell(0, 1), new Cell(1, 1) },
            new[] { new Cell(1, 2), new Cell(0, 1), new Cell(1, 1), new Cell(2, 1) },
            new[] { new Cell(1, 2), new Cell(2, 2), new Cell(0, 1), new Cell(1, 1) },
            new[] { new Cell(0, 2), new Cell(1, 2), new Cell(1, 1), new Cell(2, 1) },
            new[] { new Cell(0, 2), new Cell(0, 1), new Cell(1, 1), new Cell(2, 1) },
            new[] { new Cell(2, 2), new Cell(0, 1), new Cell(1, 1), new Cell(2, 1) },
        };

        private static readonly IReadOnlyList<Cell>[][] _cells = BuildCells();

        public static int BoxSize(PieceKind kind) => _boxSizes[Index(kind)];

        /// <summary>枠の左下を原点とする、その向きのマス。</summary>
        public static IReadOnlyList<Cell> Cells(PieceKind kind, Rotation rotation) => _cells[Index(kind)][RotationIndex(rotation)];

        public static Rotation Turn(Rotation rotation, bool clockwise) =>
            (Rotation)((RotationIndex(rotation) + (clockwise ? 1 : RotationCount - 1)) % RotationCount);

        private static IReadOnlyList<Cell>[][] BuildCells()
        {
            var result = new IReadOnlyList<Cell>[KindCount][];
            for (var kind = 0; kind < KindCount; kind++)
            {
                result[kind] = new IReadOnlyList<Cell>[RotationCount];
                var current = _north[kind];
                for (var rotation = 0; rotation < RotationCount; rotation++)
                {
                    result[kind][rotation] = Array.AsReadOnly(current);
                    current = TurnClockwise(current, _boxSizes[kind]);
                }
            }

            return result;
        }

        private static Cell[] TurnClockwise(Cell[] cells, int boxSize)
        {
            var turned = new Cell[cells.Length];
            for (var i = 0; i < cells.Length; i++) { turned[i] = new Cell(cells[i].Y, boxSize - 1 - cells[i].X); }
            return turned;
        }

        private static int Index(PieceKind kind)
        {
            var index = (int)kind;
            if (index < 0 || index >= KindCount) { throw new ArgumentOutOfRangeException(nameof(kind), kind, "未定義の種類です。"); }
            return index;
        }

        private static int RotationIndex(Rotation rotation)
        {
            var index = (int)rotation;
            if (index < 0 || index >= RotationCount) { throw new ArgumentOutOfRangeException(nameof(rotation), rotation, "未定義の向きです。"); }
            return index;
        }
    }
}
