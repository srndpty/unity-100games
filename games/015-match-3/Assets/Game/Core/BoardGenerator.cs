using System;

namespace Game.Core
{
    /// <summary>並びがなく、合法手が1つ以上ある盤面を作る。初期盤面と手詰まりの作り直しで共用する。</summary>
    public static class BoardGenerator
    {
        /// <summary>
        /// 左から2つ・下から2つと同じ色を避けて1マスずつ選ぶ。3色以上なら必ず候補が残るので、並びは生まれない。
        /// 合法手がなければ作り直し、maxAttempts 回で作れなければ例外にする。
        /// </summary>
        public static Board Generate(SeededRandom random, int colors, int maxAttempts) => Generate(random, colors, maxAttempts, out _);

        /// <summary>作り直した回数も返す版。試行回数の上限が十分かのテストに使う。</summary>
        public static Board Generate(SeededRandom random, int colors, int maxAttempts, out int attempts)
        {
            if (random == null) { throw new ArgumentNullException(nameof(random)); }
            if (colors < 3 || colors > Pieces.MaxColors) { throw new ArgumentOutOfRangeException(nameof(colors), colors, $"3〜{Pieces.MaxColors} 色で指定してください。"); }
            if (maxAttempts < 1) { throw new ArgumentOutOfRangeException(nameof(maxAttempts), maxAttempts, "1以上で指定してください。"); }
            var candidates = new Piece[Pieces.MaxColors];
            for (attempts = 1; attempts <= maxAttempts; attempts++)
            {
                var board = Fill(random, colors, candidates);
                if (SwapRules.HasLegalMove(board)) { return board; }
            }

            throw new InvalidOperationException($"{maxAttempts} 回作り直しても合法手のある盤面を作れませんでした。");
        }

        private static Board Fill(SeededRandom random, int colors, Piece[] candidates)
        {
            var cells = new Piece[Board.CellCount];
            for (var y = 0; y < Board.Height; y++)
            {
                for (var x = 0; x < Board.Width; x++)
                {
                    var left = x >= 2 && cells[Board.Index(x - 1, y)] == cells[Board.Index(x - 2, y)] ? cells[Board.Index(x - 1, y)] : Piece.Empty;
                    var below = y >= 2 && cells[Board.Index(x, y - 1)] == cells[Board.Index(x, y - 2)] ? cells[Board.Index(x, y - 1)] : Piece.Empty;
                    var count = 0;
                    for (var c = 0; c < colors; c++)
                    {
                        var piece = Pieces.Color(c);
                        if (piece != left && piece != below) { candidates[count++] = piece; }
                    }

                    cells[Board.Index(x, y)] = candidates[random.Next(count)];
                }
            }

            return Board.FromCells(cells);
        }
    }
}
