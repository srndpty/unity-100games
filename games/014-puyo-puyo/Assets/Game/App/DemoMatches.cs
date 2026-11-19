using Game.Core;

namespace Game.App
{
    /// <summary>撮影とテストで使う、決まった場面から始まる試合。</summary>
    public static class DemoMatches
    {
        private static readonly FixedPairs _pairs = new(
            new Pair(Cell.Ember, Cell.Ember),
            new Pair(Cell.Tide, Cell.Moss),
            new Pair(Cell.Pollen, Cell.Tide),
            new Pair(Cell.Moss, Cell.Pollen));

        /// <summary>最初のペアを縦のまま左へ1マス動かして落とすと、プレイヤーが2連鎖する試合。</summary>
        public static VersusMatch TwoChain(SporeConfig config, int seed) => new(config, _pairs, seed, TwoChainBoard());

        /// <summary>次の1組を置くとプレイヤーが負ける試合。</summary>
        public static VersusMatch PlayerAboutToLose(SporeConfig config, int seed) => new(config, _pairs, seed, NearlyFullBoard());

        /// <summary>次の1組をそのまま落とすと CPU が負ける試合。</summary>
        public static VersusMatch CpuAboutToLose(SporeConfig config, int seed) => new(config, _pairs, seed, null, NearlyFullBoard());

        // 1連鎖目で E が4個消え、落ちた M が最下段の M とつながって2連鎖目になる。
        private static Board TwoChainBoard() => Board.Empty
            .With(0, 0, Cell.Moss)
            .With(1, 0, Cell.Tide)
            .With(0, 1, Cell.Ember)
            .With(0, 2, Cell.Ember)
            .With(0, 3, Cell.Moss)
            .With(0, 4, Cell.Moss)
            .With(0, 5, Cell.Moss);

        // 出現列だけを、連結しないよう色を交互にして高く積む。
        private static Board NearlyFullBoard()
        {
            var board = Board.Empty;
            for (var y = 0; y < Board.VisibleHeight - 2; y++) { board = board.With(Board.SpawnX, y, y % 2 == 0 ? Cell.Moss : Cell.Tide); }
            return board;
        }
    }
}
