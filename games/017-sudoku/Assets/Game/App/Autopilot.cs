using System;
using Game.Core;

namespace Game.App
{
    /// <summary>
    /// 解き手の操作役。今の盤から次に置く正しい数字を決める（伝播で決まるマスを優先し、なければ解の数字）。
    /// ヒントと違って回数を数えず、盤も変えない。撮影台本・App テスト・PlayMode テストで共有する。
    /// </summary>
    public static class Autopilot
    {
        /// <summary>次に置くマスと数字。誤った数字があればそのマスと 0（消す）。完成していれば null。</summary>
        public static (int Cell, int Digit)? Next(SudokuGame game)
        {
            if (game == null) { throw new ArgumentNullException(nameof(game)); }
            if (game.IsSolved) { return null; }
            var solution = game.Puzzle.Solution;
            for (var cell = 0; cell < Board.CellCount; cell++)
            {
                var value = game.ValueAt(cell);
                if (value != 0 && value != solution[cell]) { return (cell, 0); }
            }

            if (Propagator.FindNext(game.Current) is { } deduction) { return (deduction.Cell, deduction.Digit); }
            for (var cell = 0; cell < Board.CellCount; cell++)
            {
                if (game.ValueAt(cell) == 0) { return (cell, solution[cell]); }
            }

            return null;
        }
    }
}
