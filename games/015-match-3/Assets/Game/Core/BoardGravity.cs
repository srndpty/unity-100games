using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>1個の結晶の落下。補充された結晶は盤面の上（FromY ≥ Height）から落ちてくる。描画はこの移動量だけを見て補間する。</summary>
    public readonly struct FallMove
    {
        public FallMove(int x, int fromY, int toY, Piece piece)
        {
            X = x;
            FromY = fromY;
            ToY = toY;
            Piece = piece;
        }

        public int X { get; }
        public int FromY { get; }
        public int ToY { get; }
        public Piece Piece { get; }
        public int Distance => FromY - ToY;
    }

    /// <summary>落下または補充の結果。</summary>
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

    /// <summary>空きマスへ上の結晶を列ごとに詰める。乱数も並びも使わない。</summary>
    public static class BoardGravity
    {
        public static FallResult Collapse(Board board)
        {
            if (board == null) { throw new ArgumentNullException(nameof(board)); }
            var cells = board.ToCells();
            var moves = new List<FallMove>();
            for (var x = 0; x < Board.Width; x++)
            {
                var write = 0;
                for (var y = 0; y < Board.Height; y++)
                {
                    var piece = cells[Board.Index(x, y)];
                    if (piece == Piece.Empty) { continue; }
                    if (write != y)
                    {
                        cells[Board.Index(x, write)] = piece;
                        cells[Board.Index(x, y)] = Piece.Empty;
                        moves.Add(new FallMove(x, y, write, piece));
                    }

                    write++;
                }
            }

            return moves.Count == 0
                ? new FallResult(board, FallResult.NoMoves)
                : new FallResult(Board.FromCells(cells), moves.AsReadOnly());
        }
    }

    /// <summary>空きマスを乱数で埋める。並びは避けない（連鎖が起きうる）。検出のことは知らない。</summary>
    public static class Refiller
    {
        /// <summary>
        /// 各列の空きを下から順に埋める。乱数は列の左から、列の中では下から引く。
        /// 空きの上に結晶が残っていてはならない（先に <see cref="BoardGravity.Collapse"/> で詰める）。
        /// </summary>
        public static FallResult Fill(Board board, SeededRandom random, int colors)
        {
            if (board == null) { throw new ArgumentNullException(nameof(board)); }
            if (random == null) { throw new ArgumentNullException(nameof(random)); }
            if (colors < 1 || colors > Pieces.MaxColors) { throw new ArgumentOutOfRangeException(nameof(colors), colors, $"1〜{Pieces.MaxColors} 色で指定してください。"); }
            var cells = board.ToCells();
            var moves = new List<FallMove>();
            for (var x = 0; x < Board.Width; x++)
            {
                var first = Board.Height;
                while (first > 0 && cells[Board.Index(x, first - 1)] == Piece.Empty) { first--; }
                for (var y = 0; y < first; y++)
                {
                    if (cells[Board.Index(x, y)] == Piece.Empty) { throw new ArgumentException("落下前の盤面は補充できません。", nameof(board)); }
                }

                var gap = Board.Height - first;
                for (var y = first; y < Board.Height; y++)
                {
                    var piece = Pieces.Color(random.Next(colors));
                    cells[Board.Index(x, y)] = piece;
                    moves.Add(new FallMove(x, y + gap, y, piece));
                }
            }

            return moves.Count == 0
                ? new FallResult(board, FallResult.NoMoves)
                : new FallResult(Board.FromCells(cells), moves.AsReadOnly());
        }
    }
}
