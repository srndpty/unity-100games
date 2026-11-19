using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>1個の胞子の落下。描画はこの移動量だけを見て補間する。</summary>
    public readonly struct FallMove
    {
        public FallMove(int x, int fromY, int toY, Cell cell)
        {
            X = x;
            FromY = fromY;
            ToY = toY;
            Cell = cell;
        }

        public int X { get; }
        public int FromY { get; }
        public int ToY { get; }
        public Cell Cell { get; }
        public int Distance => FromY - ToY;
    }

    /// <summary>落下の結果。</summary>
    public sealed class FallResult
    {
        public static readonly IReadOnlyList<FallMove> NoMoves = Array.AsReadOnly(Array.Empty<FallMove>());

        public FallResult(Board board, IReadOnlyList<FallMove> moves)
        {
            Board = board ?? throw new ArgumentNullException(nameof(board));
            Moves = moves ?? throw new ArgumentNullException(nameof(moves));
            for (var i = 0; i < moves.Count; i++) { MaxDistance = Math.Max(MaxDistance, moves[i].Distance); }
        }

        public Board Board { get; }
        public IReadOnlyList<FallMove> Moves { get; }

        /// <summary>最も長く落ちた段数。</summary>
        public int MaxDistance { get; }
    }

    /// <summary>浮いている胞子を列ごとに下へ詰める。</summary>
    public static class BoardGravity
    {
        public static FallResult Settle(Board board)
        {
            if (board == null) { throw new ArgumentNullException(nameof(board)); }
            var cells = board.ToCells();
            var moves = new List<FallMove>();
            for (var x = 0; x < Board.Width; x++)
            {
                var write = 0;
                for (var y = 0; y < Board.Height; y++)
                {
                    var cell = cells[Board.Index(x, y)];
                    if (cell == Cell.Empty) { continue; }
                    if (write != y)
                    {
                        cells[Board.Index(x, write)] = cell;
                        cells[Board.Index(x, y)] = Cell.Empty;
                        moves.Add(new FallMove(x, y, write, cell));
                    }

                    write++;
                }
            }

            return moves.Count == 0
                ? new FallResult(board, FallResult.NoMoves)
                : new FallResult(Board.FromCells(cells), moves.AsReadOnly());
        }
    }
}
