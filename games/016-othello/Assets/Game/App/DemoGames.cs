using Game.Core;

namespace Game.App
{
    /// <summary>撮影とテストで使う決まった局面。どれも黒の手番。</summary>
    public static class DemoGames
    {
        /// <summary>黒が <see cref="PassMove"/> に打つと白は打てずパスし、黒の手番に戻る局面。</summary>
        public static Position PassSoon() => BoardText.Parse(Side.Black,
            "WBBBBBBB",
            "BBBWWWW.",
            "WBWWWWWW",
            ".WBBW.W.",
            "BWWBW...",
            "..WWW.W.",
            "..WWWW..",
            "....W...");

        public static readonly Square PassMove = new(7, 1);

        /// <summary>空きが a6・b8・g8 の3マスだけの終盤。どの順に打っても黒が勝つ。</summary>
        public static Position Endgame() => BoardText.Parse(Side.Black,
            "BBBBBBBB",
            "BBBWBWBB",
            "BBBBBBBW",
            "WWWWBBWW",
            "WWBBBWWW",
            ".WBBWWWW",
            "WWWWWWWW",
            "B.BBBB.W");
    }
}
