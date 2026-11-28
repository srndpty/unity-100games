using System;
using IntGeometry;

namespace Game.Core
{
    /// <summary>
    /// 終局後の死石の推定。今の盤からプレイアウトを決まった回数回し、各交点が最後にどちらの持ち物になったか（石か、その色だけに接する空点か）を数える。
    /// 連の石が相手の持ち物になった割合が閾値を超えたら、その連を死石とする。
    /// </summary>
    public static class Ownership
    {
        /// <summary>乱数の系列の用途（試合シード × 手数 × 用途）。</summary>
        public const int RandomPurpose = 2;

        /// <summary>各交点が黒・白の持ち物になった回数。</summary>
        public static (int[] Black, int[] White) Count(GoGame game, int playouts, int moveLimit, RandomSeries random)
        {
            if (game == null) { throw new ArgumentNullException(nameof(game)); }
            if (random == null) { throw new ArgumentNullException(nameof(random)); }
            if (playouts < 1) { throw new ArgumentOutOfRangeException(nameof(playouts)); }
            var black = new int[Points.Count];
            var white = new int[Points.Count];
            var owners = new Stone[Points.Count];
            var start = game.CopyBoard();
            for (var i = 0; i < playouts; i++)
            {
                var board = start.Clone();
                Playout.Run(board, game.ToMove, 0, Points.Pass, Points.Pass, random, moveLimit);
                AreaScore.Owners(board.CopyCells(), owners);
                for (var p = 0; p < Points.Count; p++)
                {
                    if (owners[p] == Stone.Black) { black[p]++; }
                    else if (owners[p] == Stone.White) { white[p]++; }
                }
            }

            return (black, white);
        }

        /// <summary>死石と推定した石の集合。</summary>
        public static PointSet EstimateDead(GoGame game, MctsConfig config, int matchSeed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            var playouts = config.OwnershipPlayouts;
            var (black, white) = Count(game, playouts, config.PlayoutMoveLimit, RandomSeries.Derive(matchSeed, game.MoveCount, RandomPurpose));
            var dead = PointSet.Empty;
            var seen = PointSet.Empty;
            for (var p = 0; p < Points.Count; p++)
            {
                var color = game[p];
                if (color == Stone.Empty || seen.Contains(p)) { continue; }
                var chain = game.ChainAt(p);
                seen = seen.Union(chain);
                long lost = 0;
                foreach (var q in chain) { lost += color == Stone.Black ? white[q] : black[q]; }
                if (lost * 100 > (long)config.DeadPercent * playouts * chain.Count) { dead = dead.Union(chain); }
            }

            return dead;
        }
    }
}
