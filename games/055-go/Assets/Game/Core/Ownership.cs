using System;
using IntGeometry;

namespace Game.Core
{
    /// <summary>
    /// 終局後の死石の推定。今の盤からプレイアウトを決まった回数回し、各交点が最後にどちらの持ち物になったか（石か、その色だけに接する空点か）を数える。
    /// 連の石が相手の持ち物になった割合が閾値を超えたら、その連を死石とする。回すのは <see cref="OwnershipSearch"/> で、少しずつ回せる。
    /// </summary>
    public static class Ownership
    {
        /// <summary>乱数の系列の用途（試合シード × 手数 × 用途）。</summary>
        public const int RandomPurpose = 2;

        /// <summary>終局後の推定の系列（試合シード × 終局の手数 × 用途）。</summary>
        public static RandomSeries Series(int matchSeed, int moveCount) => RandomSeries.Derive(matchSeed, moveCount, RandomPurpose);

        /// <summary>終局した対局の推定を始める（回すのは呼び出し側）。</summary>
        public static OwnershipSearch Start(GoGame game, MctsConfig config, int matchSeed)
        {
            if (game == null) { throw new ArgumentNullException(nameof(game)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            return new OwnershipSearch(game, config.OwnershipPlayouts, config.PlayoutMoveLimit, Series(matchSeed, game.MoveCount));
        }

        /// <summary>各交点が黒・白の持ち物になった回数（一度に回す）。</summary>
        public static (int[] Black, int[] White) Count(GoGame game, int playouts, int moveLimit, RandomSeries random)
        {
            var search = new OwnershipSearch(game, playouts, moveLimit, random);
            search.Run(playouts);
            return (search.BlackCounts(), search.WhiteCounts());
        }

        /// <summary>死石と推定した石の集合（一度に回す）。</summary>
        public static PointSet EstimateDead(GoGame game, MctsConfig config, int matchSeed)
        {
            var search = Start(game, config, matchSeed);
            search.Run(search.Target);
            return search.EstimateDead(config.DeadPercent);
        }
    }

    /// <summary>死石の推定のプレイアウトを少しずつ回す。何回に分けて回しても、総数が同じなら同じ数え（同じ推定）になる。</summary>
    public sealed class OwnershipSearch
    {
        private readonly Board _start;
        private readonly Stone _toMove;
        private readonly int _moveLimit;
        private readonly RandomSeries _random;
        private readonly int[] _black = new int[Points.Count];
        private readonly int[] _white = new int[Points.Count];
        private readonly Stone[] _owners = new Stone[Points.Count];

        public OwnershipSearch(GoGame game, int playouts, int moveLimit, RandomSeries random)
        {
            if (game == null) { throw new ArgumentNullException(nameof(game)); }
            if (playouts < 1) { throw new ArgumentOutOfRangeException(nameof(playouts)); }
            if (moveLimit < 1) { throw new ArgumentOutOfRangeException(nameof(moveLimit)); }
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _start = game.CopyBoard();
            _toMove = game.ToMove;
            _moveLimit = moveLimit;
            Target = playouts;
        }

        public int Target { get; }

        public int Done { get; private set; }

        public bool IsDone => Done >= Target;

        /// <summary>最大 <paramref name="maxPlayouts"/> 回（残りがそれより少なければ残りだけ）回す。戻り値は回した数。</summary>
        public int Run(int maxPlayouts)
        {
            if (maxPlayouts < 0) { throw new ArgumentOutOfRangeException(nameof(maxPlayouts)); }
            var count = Math.Min(maxPlayouts, Target - Done);
            for (var i = 0; i < count; i++)
            {
                var board = _start.Clone();
                Playout.Run(board, _toMove, 0, Points.Pass, Points.Pass, _random, _moveLimit);
                AreaScore.Owners(board.CopyCells(), _owners);
                for (var p = 0; p < Points.Count; p++)
                {
                    if (_owners[p] == Stone.Black) { _black[p]++; }
                    else if (_owners[p] == Stone.White) { _white[p]++; }
                }
            }

            Done += count;
            return count;
        }

        public int[] BlackCounts() => (int[])_black.Clone();

        public int[] WhiteCounts() => (int[])_white.Clone();

        /// <summary>連の石が相手の持ち物になった割合（%）が <paramref name="deadPercent"/> より大きい連の石。回し終えてから呼ぶ。</summary>
        public PointSet EstimateDead(int deadPercent)
        {
            if (!IsDone) { throw new InvalidOperationException("推定を回し終えていません。"); }
            var dead = PointSet.Empty;
            var seen = PointSet.Empty;
            for (var p = 0; p < Points.Count; p++)
            {
                var color = _start[p];
                if (color == Stone.Empty || seen.Contains(p)) { continue; }
                var chain = _start.ChainAt(p);
                seen = seen.Union(chain);
                long lost = 0;
                foreach (var q in chain) { lost += color == Stone.Black ? _white[q] : _black[q]; }
                if (lost * 100 > (long)deadPercent * Target * chain.Count) { dead = dead.Union(chain); }
            }

            return dead;
        }
    }
}
