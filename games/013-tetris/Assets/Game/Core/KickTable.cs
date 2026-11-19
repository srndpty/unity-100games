using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Game.Core
{
    /// <summary>回転のキック候補のデータ。先頭から順に試し、最初に収まった位置を採用する。</summary>
    public static class KickTable
    {
        public const int CandidateCount = 5;

        private static readonly IReadOnlyList<Cell> _none = Array.AsReadOnly(Array.Empty<Cell>());

        // 添字は「回転前の向き × 2 + (右回転なら 0、左回転なら 1)」。
        private static readonly IReadOnlyList<Cell>[] _common =
        {
            Kicks((0, 0), (-1, 0), (-1, 1), (0, -2), (-1, -2)), // 北→東
            Kicks((0, 0), (1, 0), (1, 1), (0, -2), (1, -2)), // 北→西
            Kicks((0, 0), (1, 0), (1, -1), (0, 2), (1, 2)), // 東→南
            Kicks((0, 0), (1, 0), (1, -1), (0, 2), (1, 2)), // 東→北
            Kicks((0, 0), (1, 0), (1, 1), (0, -2), (1, -2)), // 南→西
            Kicks((0, 0), (-1, 0), (-1, 1), (0, -2), (-1, -2)), // 南→東
            Kicks((0, 0), (-1, 0), (-1, -1), (0, 2), (-1, 2)), // 西→北
            Kicks((0, 0), (-1, 0), (-1, -1), (0, 2), (-1, 2)), // 西→南
        };

        private static readonly IReadOnlyList<Cell>[] _long =
        {
            Kicks((0, 0), (-2, 0), (1, 0), (-2, -1), (1, 2)), // 北→東
            Kicks((0, 0), (-1, 0), (2, 0), (-1, 2), (2, -1)), // 北→西
            Kicks((0, 0), (-1, 0), (2, 0), (-1, 2), (2, -1)), // 東→南
            Kicks((0, 0), (2, 0), (-1, 0), (2, 1), (-1, -2)), // 東→北
            Kicks((0, 0), (2, 0), (-1, 0), (2, 1), (-1, -2)), // 南→西
            Kicks((0, 0), (1, 0), (-2, 0), (1, -2), (-2, 1)), // 南→東
            Kicks((0, 0), (1, 0), (-2, 0), (1, -2), (-2, 1)), // 西→北
            Kicks((0, 0), (-2, 0), (1, 0), (-2, -1), (1, 2)), // 西→南
        };

        /// <summary>回転で試す位置の差分。回転しない種類は空を返す。</summary>
        public static IReadOnlyList<Cell> Offsets(PieceKind kind, Rotation from, bool clockwise)
        {
            if (from < Rotation.North || from > Rotation.West) { throw new ArgumentOutOfRangeException(nameof(from), from, "未定義の向きです。"); }
            var index = ((int)from * 2) + (clockwise ? 0 : 1);
            return kind switch
            {
                PieceKind.O => _none,
                PieceKind.I => _long[index],
                PieceKind.T or PieceKind.S or PieceKind.Z or PieceKind.J or PieceKind.L => _common[index],
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未定義の種類です。"),
            };
        }

        private static ReadOnlyCollection<Cell> Kicks(params (int X, int Y)[] offsets)
        {
            var cells = new Cell[offsets.Length];
            for (var i = 0; i < offsets.Length; i++) { cells[i] = new Cell(offsets[i].X, offsets[i].Y); }
            return Array.AsReadOnly(cells);
        }
    }
}
