using System;
using Game.Core;

namespace Game.App
{
    /// <summary>
    /// 撮影とテストで使う盤。生成した問題は解が 1 つなので、誤りを含んだまま全マスを埋めることはできない。
    /// そこで、解から「2 つの数字を入れ替えても重複しない長方形」の 4 マスだけを空けた盤を作る。
    /// </summary>
    public static class DemoPuzzles
    {
        private const int MaxSeedsToTry = 1000;

        /// <summary>4 マスだけが空いた盤と、そのマス、正解と逆の数字。</summary>
        public static (Puzzle Puzzle, int[] Cells, int[] Swapped) SwappableRectangle(int seed, SudokuConfig config)
        {
            for (var i = 0; i < MaxSeedsToTry; i++)
            {
                var solution = PuzzleGenerator.Generate(unchecked(seed + i), config, Difficulty.Easy).Solution;
                if (FindRectangle(solution) is not { } found) { continue; }
                var givens = solution;
                foreach (var cell in found.Cells) { givens = givens.With(cell, 0); }
                return (new Puzzle(givens, solution), found.Cells, found.Swapped);
            }

            throw new InvalidOperationException("入れ替え可能な長方形を持つ解が見つかりません。");
        }

        /// <summary>
        /// 2 行 × 2 列（行は同じブロックの帯、列は別のブロック）の 4 マスで、数字 a・b が対角に並ぶ長方形。
        /// 見つかれば左上・右上・左下・右下の順のマスと、入れ替えた数字。
        /// </summary>
        public static (int[] Cells, int[] Swapped)? FindRectangle(Grid solution)
        {
            if (solution == null) { throw new ArgumentNullException(nameof(solution)); }
            for (var r1 = 0; r1 < Board.Size; r1++)
            {
                for (var r2 = r1 + 1; r2 < Board.Size && r2 / Board.BoxSize == r1 / Board.BoxSize; r2++)
                {
                    for (var c1 = 0; c1 < Board.Size; c1++)
                    {
                        for (var c2 = c1 + 1; c2 < Board.Size; c2++)
                        {
                            if (c1 / Board.BoxSize == c2 / Board.BoxSize) { continue; }
                            var cells = new[] { Board.Cell(r1, c1), Board.Cell(r1, c2), Board.Cell(r2, c1), Board.Cell(r2, c2) };
                            var a = solution[cells[0]];
                            var b = solution[cells[1]];
                            if (solution[cells[2]] != b || solution[cells[3]] != a) { continue; }
                            return (cells, new[] { b, a, a, b });
                        }
                    }
                }
            }

            return null;
        }
    }
}
