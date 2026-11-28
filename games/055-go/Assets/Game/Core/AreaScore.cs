using System;

namespace Game.Core
{
    /// <summary>面積計点の結果。差（Margin）= 黒の面積 − 白の面積 − コミ。</summary>
    public readonly struct ScoreResult
    {
        public ScoreResult(int blackStones, int blackTerritory, int whiteStones, int whiteTerritory, int neutral, int komi)
        {
            BlackStones = blackStones;
            BlackTerritory = blackTerritory;
            WhiteStones = whiteStones;
            WhiteTerritory = whiteTerritory;
            Neutral = neutral;
            Komi = komi;
        }

        public int BlackStones { get; }

        /// <summary>黒の石だけに接する空点（取り除いた死石の跡を含む）。</summary>
        public int BlackTerritory { get; }

        public int WhiteStones { get; }

        public int WhiteTerritory { get; }

        /// <summary>両方の色に接する空点（セキの中の空点など）。どちらにも数えない。</summary>
        public int Neutral { get; }

        public int Komi { get; }

        public int BlackArea => BlackStones + BlackTerritory;

        public int WhiteArea => WhiteStones + WhiteTerritory;

        /// <summary>正なら黒の勝ち、負なら白の勝ち、0 は引き分け。</summary>
        public int Margin => BlackArea - WhiteArea - Komi;

        /// <summary>勝った色。引き分けは Empty。</summary>
        public Stone Winner => Margin > 0 ? Stone.Black : Margin < 0 ? Stone.White : Stone.Empty;
    }

    /// <summary>
    /// 中国式の面積計点（Tromp-Taylor 型）。死石を取り除いた盤で、各色の石の数とその色の石だけに接する空点の領域を数える。
    /// </summary>
    public static class AreaScore
    {
        /// <summary>盤をそのまま数える（死石なし）。</summary>
        public static ScoreResult Compute(Board board, int komi) => Compute(board, PointSet.Empty, komi);

        /// <summary>死石（<paramref name="dead"/> の石）を取り除いてから数える。死石の集合に空点を含めたら拒否する。</summary>
        public static ScoreResult Compute(Board board, PointSet dead, int komi)
        {
            if (board == null) { throw new ArgumentNullException(nameof(board)); }
            var cells = new Stone[Points.Count];
            for (var p = 0; p < Points.Count; p++) { cells[p] = board[p]; }
            foreach (var p in dead)
            {
                if (cells[p] == Stone.Empty) { throw new ArgumentException($"{Points.Name(p)} に石がないのに死石に含まれています。", nameof(dead)); }
                cells[p] = Stone.Empty;
            }

            return Compute(cells, komi);
        }

        /// <summary>配置から数える（探索のプレイアウトの終わり）。</summary>
        internal static ScoreResult Compute(Stone[] cells, int komi)
        {
            int blackStones = 0, whiteStones = 0, blackTerritory = 0, whiteTerritory = 0, neutral = 0;
            var visited = new bool[Points.Count];
            var stack = new int[Points.Count];
            for (var p = 0; p < Points.Count; p++)
            {
                var s = cells[p];
                if (s == Stone.Black) { blackStones++; continue; }
                if (s == Stone.White) { whiteStones++; continue; }
                if (visited[p]) { continue; }

                // 空点の領域を塗りつぶし、接する色を集める。
                var size = 0;
                var touchesBlack = false;
                var touchesWhite = false;
                var top = 0;
                stack[top++] = p;
                visited[p] = true;
                while (top > 0)
                {
                    var q = stack[--top];
                    size++;
                    foreach (var n in Points.Neighbors(q))
                    {
                        var c = cells[n];
                        if (c == Stone.Black) { touchesBlack = true; }
                        else if (c == Stone.White) { touchesWhite = true; }
                        else if (!visited[n])
                        {
                            visited[n] = true;
                            stack[top++] = n;
                        }
                    }
                }

                if (touchesBlack && !touchesWhite) { blackTerritory += size; }
                else if (touchesWhite && !touchesBlack) { whiteTerritory += size; }
                else { neutral += size; }
            }

            return new ScoreResult(blackStones, blackTerritory, whiteStones, whiteTerritory, neutral, komi);
        }

        /// <summary>各交点の持ち主（石の色か、その色だけに接する空点の色。どちらでもなければ Empty）。死石の推定で数える。</summary>
        internal static void Owners(Stone[] cells, Stone[] owners)
        {
            var visited = new bool[Points.Count];
            var stack = new int[Points.Count];
            var region = new int[Points.Count];
            for (var p = 0; p < Points.Count; p++)
            {
                if (cells[p] != Stone.Empty) { owners[p] = cells[p]; continue; }
                if (visited[p]) { continue; }
                var size = 0;
                var touchesBlack = false;
                var touchesWhite = false;
                var top = 0;
                stack[top++] = p;
                visited[p] = true;
                while (top > 0)
                {
                    var q = stack[--top];
                    region[size++] = q;
                    foreach (var n in Points.Neighbors(q))
                    {
                        var c = cells[n];
                        if (c == Stone.Black) { touchesBlack = true; }
                        else if (c == Stone.White) { touchesWhite = true; }
                        else if (!visited[n])
                        {
                            visited[n] = true;
                            stack[top++] = n;
                        }
                    }
                }

                var owner = touchesBlack == touchesWhite ? Stone.Empty : touchesBlack ? Stone.Black : Stone.White;
                for (var i = 0; i < size; i++) { owners[region[i]] = owner; }
            }
        }
    }
}
