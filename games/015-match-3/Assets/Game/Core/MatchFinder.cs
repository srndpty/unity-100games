using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>消える並びの検出結果。</summary>
    public sealed class MatchResult
    {
        public static readonly MatchResult None = new(Array.AsReadOnly(Array.Empty<Point>()), 0);

        public MatchResult(IReadOnlyList<Point> cells, int lines)
        {
            Cells = cells ?? throw new ArgumentNullException(nameof(cells));
            Lines = lines;
        }

        /// <summary>消えるマス。縦横の並びが重なるマスも1回だけ含む。盤面の添字順。</summary>
        public IReadOnlyList<Point> Cells { get; }

        /// <summary>3個以上の並びの本数。交差する縦と横は別々に数える。</summary>
        public int Lines { get; }

        public bool IsEmpty => Cells.Count == 0;
    }

    /// <summary>並びの検出だけを行う。補充や落下は知らない。</summary>
    public static class MatchFinder
    {
        public const int MinLength = 3;

        public static MatchResult Find(Board board)
        {
            if (board == null) { throw new ArgumentNullException(nameof(board)); }
            var marked = new bool[Board.CellCount];
            var lines = 0;
            for (var y = 0; y < Board.Height; y++) { lines += Scan(board, marked, 0, y, 1, 0, Board.Width); }
            for (var x = 0; x < Board.Width; x++) { lines += Scan(board, marked, x, 0, 0, 1, Board.Height); }
            if (lines == 0) { return MatchResult.None; }

            var cells = new List<Point>();
            for (var y = 0; y < Board.Height; y++)
            {
                for (var x = 0; x < Board.Width; x++)
                {
                    if (marked[Board.Index(x, y)]) { cells.Add(new Point(x, y)); }
                }
            }

            return new MatchResult(cells.AsReadOnly(), lines);
        }

        /// <summary>そのマスを含む並びがあるか。合法手の判定で、入れ替えた2マスだけを見るのに使う。</summary>
        public static bool HasMatchAt(Board board, Point point)
        {
            if (board == null) { throw new ArgumentNullException(nameof(board)); }
            var piece = board.At(point);
            if (piece == Piece.Empty) { return false; }
            return RunLength(board, point, piece, 1, 0) + RunLength(board, point, piece, -1, 0) + 1 >= MinLength
                || RunLength(board, point, piece, 0, 1) + RunLength(board, point, piece, 0, -1) + 1 >= MinLength;
        }

        // 1本の行または列を走査し、3個以上の並びに印を付ける。見つけた本数を返す。
        private static int Scan(Board board, bool[] marked, int x0, int y0, int dx, int dy, int length)
        {
            var lines = 0;
            var start = 0;
            for (var i = 1; i <= length; i++)
            {
                var startPiece = board.At(x0 + (dx * start), y0 + (dy * start));
                if (i < length && board.At(x0 + (dx * i), y0 + (dy * i)) == startPiece) { continue; }
                if (startPiece != Piece.Empty && i - start >= MinLength)
                {
                    lines++;
                    for (var k = start; k < i; k++) { marked[Board.Index(x0 + (dx * k), y0 + (dy * k))] = true; }
                }

                start = i;
            }

            return lines;
        }

        private static int RunLength(Board board, Point from, Piece piece, int dx, int dy)
        {
            var count = 0;
            var x = from.X + dx;
            var y = from.Y + dy;
            while (Board.IsInside(x, y) && board.At(x, y) == piece)
            {
                count++;
                x += dx;
                y += dy;
            }

            return count;
        }
    }
}
