namespace Game.Core.Tests
{
    internal static class Match3TestKit
    {
        public const int MaxSteps = 5000;

        /// <summary>演出の待ち時間がない設定。</summary>
        public static Match3Config InstantConfig() => new()
        {
            SwapTicks = 0,
            ClearTicks = 0,
            FallTicksPerRow = 0,
            ReshuffleTicks = 0,
        };

        /// <summary>
        /// 並びも合法手もない盤面。値は (x + 2y) mod 4 の4色で、列は2色の交互、行は4色の巡回になる。
        /// どの入れ替えでも、動いた結晶の行と列に同じ色が並ばない。
        /// </summary>
        public static Board DeadBoard() => Parse(
            "LPCALP",
            "CALPCA",
            "LPCALP",
            "CALPCA",
            "LPCALP",
            "CALPCA");

        /// <summary>
        /// <see cref="TwoChainSwap"/> で入れ替えると、0列目の C が縦に3つ消え、落ちた K が下の K 2つと並んで2連鎖目になる盤面。
        /// 他の列は L と P の市松模様で、並びに関わらない。
        /// </summary>
        public static Board TwoChainBoard() => Parse(
            "KLPLPL",
            "CPLPLP",
            "CLPLPL",
            "PCLPLP",
            "KLPLPL",
            "KPLPLP");

        public static readonly Swap TwoChainSwap = new(new Point(0, 2), new Point(1, 2));

        public static Board Parse(params string[] rows) => BoardText.Parse(rows);

        /// <summary>入れ替えを受け付けるか終わるまで進める。</summary>
        public static void RunUntilIdle(Match3Game game)
        {
            for (var i = 0; i < MaxSteps && !game.AcceptsSwap && !game.IsOver; i++) { game.Tick(); }
        }

        /// <summary>入れ替えて、次の入れ替えを受け付けるか終わるまで進める。</summary>
        public static SwapVerdict Play(Match3Game game, Swap swap)
        {
            var verdict = game.TrySwap(swap.From, swap.To);
            RunUntilIdle(game);
            return verdict;
        }

        /// <summary>毎回、最初の合法手を選んで終わるまで遊ぶ。</summary>
        public static void PlayFirstMoves(Match3Game game)
        {
            for (var i = 0; i < MaxSteps && !game.IsOver; i++) { Play(game, SwapRules.LegalMoves(game.Board)[0]); }
        }

        public static Point P(int x, int y) => new(x, y);
    }
}
