using System;

namespace Game.Core.Tests
{
    /// <summary>テストで使う盤。</summary>
    public static class SudokuTestKit
    {
        /// <summary>Naked Single と Hidden Single だけで解ける問題。</summary>
        public const string EasyText =
            "53..7...." +
            "6..195..." +
            ".98....6." +
            "8...6...3" +
            "4..8.3..1" +
            "7...2...6" +
            ".6....28." +
            "...419..5" +
            "....8..79";

        public const string EasySolutionText =
            "534678912" +
            "672195348" +
            "198342567" +
            "859761423" +
            "426853791" +
            "713924856" +
            "961537284" +
            "287419635" +
            "345286179";

        /// <summary>伝播だけでは進まず、分岐が要る一意解の問題。</summary>
        public const string HardText =
            "8........" +
            "..36....." +
            ".7..9.2.." +
            ".5...7..." +
            "....457.." +
            "...1...3." +
            "..1....68" +
            "..85...1." +
            ".9....4..";

        public static Grid Easy => Grid.Parse(EasyText);

        public static Grid EasySolution => Grid.Parse(EasySolutionText);

        public static Puzzle EasyPuzzle => new(Easy, EasySolution);

        /// <summary>解に、行・列・ブロックの規則をすべて守っているかを確かめる。</summary>
        public static bool IsValidSolution(Grid grid) => grid.IsComplete && !grid.HasConflict;

        /// <summary>
        /// 完成盤から、2 行 × 2 列の 4 マスで数字 a・b が入れ替え可能な長方形（2 つのブロックにまたがる）を探して空ける。
        /// その盤は解が 2 つあり、伝播では 1 マスも決まらない。
        /// </summary>
        public static (Grid Grid, int[] Cells) WithDeadlyRectangle(Grid solution)
        {
            for (var r1 = 0; r1 < Board.Size; r1++)
            {
                for (var r2 = r1 + 1; r2 < Board.Size && r2 / Board.BoxSize == r1 / Board.BoxSize; r2++)
                {
                    for (var c1 = 0; c1 < Board.Size; c1++)
                    {
                        for (var c2 = c1 + 1; c2 < Board.Size; c2++)
                        {
                            if (c1 / Board.BoxSize == c2 / Board.BoxSize) { continue; }
                            var a = solution[Board.Cell(r1, c1)];
                            var b = solution[Board.Cell(r1, c2)];
                            if (solution[Board.Cell(r2, c1)] != b || solution[Board.Cell(r2, c2)] != a) { continue; }
                            var cells = new[] { Board.Cell(r1, c1), Board.Cell(r1, c2), Board.Cell(r2, c1), Board.Cell(r2, c2) };
                            var grid = solution;
                            foreach (var cell in cells) { grid = grid.With(cell, 0); }
                            return (grid, cells);
                        }
                    }
                }
            }

            throw new InvalidOperationException("入れ替え可能な長方形がありません。");
        }
    }
}
