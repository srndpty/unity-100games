using System;
using Game.Core;
using IntGeometry;

namespace Game.App
{
    public enum CpuAction
    {
        None,
        Played,
        Passed,
        Resigned,
    }

    /// <summary>
    /// CPU の打ち手。手番が来た最初の Tick に <see cref="Mcts"/> を作り、Tick ごとに決まった数だけ回す（テスト・撮影）。
    /// Unity の対局では Tick は考える間だけを数え、探索はフレームの時間の予算で <see cref="Think"/> から回す（<c>runSearch: false</c>）。
    /// 総数を回し終え、考える間（最小の Tick）も過ぎたら、プレイヤーと同じ <see cref="GoGame.TryPlay"/> で打つ（パス・投了も同じ口）。
    /// 乱数は（試合シード × 手数 × 探索）の系列で、手ごとに作り直す。相手がパスした後は、終局後と同じ系列で死石を推定してからパスを評価する。
    /// </summary>
    public sealed class CpuPlayer
    {
        /// <summary>乱数の系列の用途。</summary>
        public const int RandomPurpose = 1;

        private readonly MctsConfig _config;
        private readonly int _playouts;
        private readonly int _perTick;
        private readonly int _thinkTicks;
        private readonly int _matchSeed;
        private GoGame? _searchGame;
        private int _searchMove = -1;
        private int _ticks;

        public CpuPlayer(Stone side, MctsConfig config, int playouts, int playoutsPerTick, int thinkTicks, int matchSeed)
        {
            if (!side.IsColor()) { throw new ArgumentOutOfRangeException(nameof(side), side, "黒か白です。"); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            if (playouts < 1) { throw new ArgumentOutOfRangeException(nameof(playouts)); }
            if (playoutsPerTick < 1) { throw new ArgumentOutOfRangeException(nameof(playoutsPerTick), playoutsPerTick, "1 以上を指定してください（Tick だけでも必ず打てる）。"); }
            if (thinkTicks < 0) { throw new ArgumentOutOfRangeException(nameof(thinkTicks)); }
            Side = side;
            _config = config.Copy();
            _playouts = playouts;
            _perTick = playoutsPerTick;
            _thinkTicks = thinkTicks;
            _matchSeed = matchSeed;
        }

        public Stone Side { get; }

        /// <summary>考えている探索（考えていなければ null）。</summary>
        public Mcts? Search { get; private set; }

        /// <summary>直前に決めた手の勝率（‰、CPU から見て）。まだ打っていなければ 500。</summary>
        public int LastWinPermille { get; private set; } = 500;

        /// <summary>
        /// 1 Tick 進める。自分の手番なら考え（<paramref name="runSearch"/> が true のときだけ回す）、回し終えて考える間も過ぎたら打つ。
        /// false なら探索は <see cref="Think"/> だけで進む。
        /// </summary>
        public CpuAction Tick(GoGame game, bool runSearch = true)
        {
            if (game == null) { throw new ArgumentNullException(nameof(game)); }
            if (game.IsOver || game.ToMove != Side)
            {
                Search = null;
                return CpuAction.None;
            }

            Ensure(game);
            if (runSearch) { Search!.Run(_perTick); }
            _ticks++;
            if (!Search!.IsDone || _ticks < _thinkTicks) { return CpuAction.None; }

            var search = Search;
            Search = null;
            LastWinPermille = search.BestWinPermille;
            if (_config.ResignPermille > 0 && game.MoveCount >= _config.ResignMinMove && LastWinPermille < _config.ResignPermille)
            {
                Expect(game.TryResign(Side), "投了");
                return CpuAction.Resigned;
            }

            var move = search.BestMove;
            if (move == Points.Pass)
            {
                Expect(game.TryPass(Side), "パス");
                return CpuAction.Passed;
            }

            Expect(game.TryPlay(Side, move), Points.Name(move));
            return CpuAction.Played;
        }

        /// <summary>
        /// Tick と別に探索を進める（Unity がフレームの残り時間で呼ぶ）。手番でなければ何もしない。戻り値は回した数。
        /// 総数が同じなら手は変わらないので、呼び方（機械の速さ）は考える時間だけを変える。
        /// </summary>
        public int Think(GoGame game, int maxPlayouts)
        {
            if (game == null) { throw new ArgumentNullException(nameof(game)); }
            if (game.IsOver || game.ToMove != Side) { return 0; }
            Ensure(game);
            return Search!.Run(maxPlayouts);
        }

        private void Ensure(GoGame game)
        {
            if (Search != null && ReferenceEquals(_searchGame, game) && _searchMove == game.MoveCount) { return; }
            Search = new Mcts(game, _config, _playouts, RandomSeries.Derive(_matchSeed, game.MoveCount, RandomPurpose), Ownership.Series(_matchSeed, game.MoveCount + 1));
            _searchGame = game;
            _searchMove = game.MoveCount;
            _ticks = 0;
        }

        private static void Expect(MoveVerdict verdict, string what)
        {
            if (verdict != MoveVerdict.Ok) { throw new InvalidOperationException($"CPU の {what} が拒否されました: {verdict}"); }
        }
    }
}
