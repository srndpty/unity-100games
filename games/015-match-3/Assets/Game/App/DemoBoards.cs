using Game.Core;

namespace Game.App
{
    /// <summary>撮影とテストで使う、決まった場面から始まる盤面。</summary>
    public static class DemoBoards
    {
        /// <summary>(0,2) と (1,2) を入れ替えると、0列目の C が縦に3つ消え、落ちた K が下の K 2つと並んで2連鎖目になる。</summary>
        public static readonly Swap TwoChainSwap = new(new Point(0, 2), new Point(1, 2));

        /// <summary>(2,2) と (2,1) を入れ替えると、C が T 字に5個並ぶ（縦横の重なりは1回だけ数える）。</summary>
        public static readonly Swap CrossSwap = new(new Point(2, 2), new Point(2, 1));

        /// <summary>隣り合うが並びができない入れ替え（<see cref="TwoChain"/> の盤面で）。</summary>
        public static readonly Swap NoMatchSwap = new(new Point(4, 5), new Point(5, 5));

        public static Board TwoChain() => BoardText.Parse(
            "KLPLPL",
            "CPLPLP",
            "CLPLPL",
            "PCLPLP",
            "KLPLPL",
            "KPLPLP");

        public static Board Cross() => BoardText.Parse(
            "PLPLPL",
            "LPCPLP",
            "PLCLPL",
            "LCACLP",
            "PLCLPL",
            "LPLPLP");

        /// <summary>並びも合法手もない盤面。始めるとすぐに作り直す。</summary>
        public static Board Dead() => BoardText.Parse(
            "LPCALP",
            "CALPCA",
            "LPCALP",
            "CALPCA",
            "LPCALP",
            "CALPCA");
    }
}
