using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>移動の純粋関数。入力の盤面を変更せず、同じ入力には常に同じ結果を返す。</summary>
    public static class MoveRules
    {
        public static bool IsDirection(Direction direction) =>
            direction == Direction.Left || direction == Direction.Right || direction == Direction.Up || direction == Direction.Down;

        public static MoveOutcome Slide(BoardState board, Direction direction)
        {
            if (board == null) { throw new ArgumentNullException(nameof(board)); }
            if (!IsDirection(direction)) { throw new ArgumentOutOfRangeException(nameof(direction)); }

            var cells = new int[BoardState.CellCount];
            var moves = new List<TileMove>(BoardState.CellCount);
            var score = 0;
            for (var line = 0; line < BoardState.Size; line++)
            {
                score += SlideLine(board, direction, line, cells, moves);
            }

            return new MoveOutcome(board, BoardState.FromTrusted(cells), direction, score, moves);
        }

        /// <summary>空きマスか、隣り合う同じ値があれば動ける。</summary>
        public static bool HasAnyMove(BoardState board)
        {
            if (board == null) { throw new ArgumentNullException(nameof(board)); }
            if (board.EmptyCount > 0) { return true; }
            for (var y = 0; y < BoardState.Size; y++)
            {
                for (var x = 0; x < BoardState.Size; x++)
                {
                    var value = board.At(x, y);
                    if (x + 1 < BoardState.Size && board.At(x + 1, y) == value) { return true; }
                    if (y + 1 < BoardState.Size && board.At(x, y + 1) == value) { return true; }
                }
            }

            return false;
        }

        // 進行方向の端に近い板から順に置く。直前に置いた板がまだ合体していないときだけ合体でき、
        // 合体後は lastMove を手放すので、できた板が同じ手でもう一度合体することはない。
        private static int SlideLine(BoardState board, Direction direction, int line, int[] cells, List<TileMove> moves)
        {
            var score = 0;
            var placed = 0;
            var lastMove = -1;
            for (var position = 0; position < BoardState.Size; position++)
            {
                var from = LineCell(direction, line, position);
                var value = board[from];
                if (value == 0) { continue; }

                if (lastMove >= 0 && moves[lastMove].Value == value)
                {
                    var to = moves[lastMove].To;
                    moves[lastMove] = new TileMove(moves[lastMove].From, to, value, true);
                    moves.Add(new TileMove(from, to, value, true));
                    cells[to] = value * 2;
                    score += value * 2;
                    lastMove = -1;
                    continue;
                }

                var destination = LineCell(direction, line, placed++);
                cells[destination] = value;
                moves.Add(new TileMove(from, destination, value, false));
                lastMove = moves.Count - 1;
            }

            return score;
        }

        // position 0 が進行方向の端。4 方向の違いはこの対応だけに閉じ込める。
        private static int LineCell(Direction direction, int line, int position)
        {
            var far = BoardState.Size - 1 - position;
            return direction switch
            {
                Direction.Left => BoardState.Index(position, line),
                Direction.Right => BoardState.Index(far, line),
                Direction.Up => BoardState.Index(line, position),
                Direction.Down => BoardState.Index(line, far),
                _ => throw new ArgumentOutOfRangeException(nameof(direction)),
            };
        }
    }
}
