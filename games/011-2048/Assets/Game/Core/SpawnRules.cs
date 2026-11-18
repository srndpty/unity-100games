using System;

namespace Game.Core
{
    /// <summary>新しく出現する板。</summary>
    public sealed class SpawnedTile
    {
        public SpawnedTile(int cell, int value)
        {
            Cell = cell;
            Value = value;
        }

        public int Cell { get; }
        public int Value { get; }
    }

    /// <summary>新しい板の抽選。乱数は常に「出現マス→値」の順に 1 回ずつ消費する。</summary>
    public static class SpawnRules
    {
        private const int PercentRange = 100;

        public static SpawnedTile Choose(BoardState board, SeededRandom random, int fourChancePercent)
        {
            if (board == null) { throw new ArgumentNullException(nameof(board)); }
            if (random == null) { throw new ArgumentNullException(nameof(random)); }
            RequirePercent(fourChancePercent);
            if (board.EmptyCount == 0) { throw new InvalidOperationException("空きマスがありません。"); }

            var cell = NthEmptyCell(board, random.Next(board.EmptyCount));
            var value = random.Next(PercentRange) < fourChancePercent ? 4 : 2;
            return new SpawnedTile(cell, value);
        }

        internal static void RequirePercent(int percent)
        {
            if (percent < 0 || percent > PercentRange)
            {
                throw new ArgumentOutOfRangeException(nameof(percent), percent, $"0〜{PercentRange} の範囲で指定してください。");
            }
        }

        private static int NthEmptyCell(BoardState board, int index)
        {
            for (var cell = 0; cell < BoardState.CellCount; cell++)
            {
                if (board[cell] == 0 && index-- == 0) { return cell; }
            }

            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }
}
