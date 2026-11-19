using System;
using System.Collections.Generic;

namespace Game.Core
{
    public enum GamePhase
    {
        /// <summary>入れ替えを待っている。</summary>
        Idle,

        /// <summary>成立する入れ替えの演出中。Board は入れ替える前の状態。</summary>
        Swapping,

        /// <summary>不成立の入れ替えを行って戻す演出中。Board は入れ替える前の状態のまま。</summary>
        Reverting,

        /// <summary>並びが消える演出中。Board にはまだ残っている。</summary>
        Clearing,

        /// <summary>落下と補充の演出中。Board は落下・補充を終えた状態。</summary>
        Falling,

        /// <summary>手詰まりで盤面を作り直した直後の演出中。Board は作り直した状態。</summary>
        Reshuffling,

        /// <summary>目標スコアに届いた。</summary>
        Won,

        /// <summary>目標に届かないまま手数を使い切った。</summary>
        Lost,
    }

    /// <summary>試合のシードから分ける乱数の系列。</summary>
    public enum RandomSeries
    {
        Generation = 1,
        Refill = 2,
        Reshuffle = 3,
    }

    [Flags]
    public enum GameEvents
    {
        None = 0,

        /// <summary>成立する入れ替えを始めた。</summary>
        Swapped = 1 << 0,

        /// <summary>不成立の入れ替えを始めた（戻る）。</summary>
        Rejected = 1 << 1,

        /// <summary>並びが消え始めた。</summary>
        Cleared = 1 << 2,

        /// <summary>2段目以降の連鎖で消え始めた。</summary>
        Chained = 1 << 3,

        /// <summary>手詰まりで盤面を作り直した。</summary>
        Reshuffled = 1 << 4,

        Won = 1 << 5,
        Lost = 1 << 6,
    }

    /// <summary>
    /// 1試合の進行。検出・落下・補充・生成は純粋関数に任せ、ここはフェーズと待ち時間だけを管理する。
    /// 乱数は「初期生成」「補充」「作り直し」の3系列を試合のシードから分けて持ち、混ぜない。
    /// </summary>
    public sealed class Match3Game
    {
        private static readonly IReadOnlyList<Point> _noCells = Array.AsReadOnly(Array.Empty<Point>());

        private readonly Match3Config _config;
        private readonly SeededRandom _refillRandom;
        private readonly SeededRandom _reshuffleRandom;
        private CascadeStep? _step;
        private GameEvents _events;

        /// <summary>シードから初期盤面を作って始める。board を渡すと、その盤面から始める（撮影とテスト用）。</summary>
        public Match3Game(Match3Config config, int seed, Board? board = null)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            _config = config.Copy();
            Seed = seed;
            _refillRandom = new SeededRandom(SeriesSeed(seed, RandomSeries.Refill));
            _reshuffleRandom = new SeededRandom(SeriesSeed(seed, RandomSeries.Reshuffle));
            if (board == null)
            {
                Board = BoardGenerator.Generate(new SeededRandom(SeriesSeed(seed, RandomSeries.Generation)), _config.Colors, _config.MaxGenerationAttempts);
            }
            else
            {
                if (board.HasEmpty()) { throw new ArgumentException("空きのある盤面からは始められません。", nameof(board)); }
                if (!MatchFinder.Find(board).IsEmpty) { throw new ArgumentException("並びのある盤面からは始められません。", nameof(board)); }
                Board = board;
            }

            MovesLeft = _config.Moves;
            Phase = GamePhase.Idle;
            if (!SwapRules.HasLegalMove(Board)) { Reshuffle(); }
        }

        public int Seed { get; }
        public Board Board { get; private set; }
        public GamePhase Phase { get; private set; }
        public long Score { get; private set; }
        public int MovesLeft { get; private set; }

        /// <summary>成立した入れ替えの回数。</summary>
        public int MovesUsed => _config.Moves - MovesLeft;

        public int TargetScore => _config.TargetScore;
        public int Colors => _config.Colors;

        /// <summary>今の手で進んだ連鎖の段数。待機中は直前の手の最終段数。</summary>
        public int Chain { get; private set; }

        /// <summary>この試合の最大連鎖数。</summary>
        public int MaxChain { get; private set; }

        /// <summary>手詰まりで作り直した回数。</summary>
        public int Reshuffles { get; private set; }

        /// <summary>進めた Tick 数。終わった後は増えない。</summary>
        public int TickCount { get; private set; }

        /// <summary>現在のフェーズの長さ。演出の進み具合の計算に使う。</summary>
        public int PhaseDuration { get; private set; }

        /// <summary>現在のフェーズの残り Tick 数。</summary>
        public int PhaseRemaining { get; private set; }

        /// <summary>入れ替えと戻しの演出中の2マス。</summary>
        public Swap? ActiveSwap { get; private set; }

        /// <summary>消える演出中のマス。</summary>
        public IReadOnlyList<Point> ClearingCells => Phase == GamePhase.Clearing ? _step!.Match.Cells : _noCells;

        /// <summary>落下中の結晶の移動。</summary>
        public IReadOnlyList<FallMove> Falls => Phase == GamePhase.Falling ? _step!.Falls : FallResult.NoMoves;

        /// <summary>直前に消えた段の得点。</summary>
        public long LastStepScore { get; private set; }

        public bool IsOver => Phase == GamePhase.Won || Phase == GamePhase.Lost;

        /// <summary>入れ替えを受け付けるか。</summary>
        public bool AcceptsSwap => Phase == GamePhase.Idle;

        /// <summary>
        /// 入れ替えを試みる。Legal と NoMatch のときは演出を始め、それ以外は状態を変えない。
        /// 不成立（NoMatch）は戻すだけで手数を減らさない。
        /// </summary>
        public SwapVerdict TrySwap(Point a, Point b)
        {
            if (!AcceptsSwap) { return SwapVerdict.Busy; }
            var verdict = SwapRules.Check(Board, a, b);
            if (verdict == SwapVerdict.Legal)
            {
                ActiveSwap = new Swap(a, b);
                Enter(GamePhase.Swapping, _config.SwapTicks);
                _events |= GameEvents.Swapped;
            }
            else if (verdict == SwapVerdict.NoMatch)
            {
                ActiveSwap = new Swap(a, b);
                Enter(GamePhase.Reverting, _config.SwapTicks * 2);
                _events |= GameEvents.Rejected;
            }

            return verdict;
        }

        /// <summary>1 Tick 進める。待ち時間が 0 のフェーズは同じ Tick の中で続けて終える。</summary>
        public void Tick()
        {
            if (IsOver) { return; }
            TickCount++;
            if (!IsTimed(Phase)) { return; }
            PhaseRemaining--;
            while (IsTimed(Phase) && PhaseRemaining <= 0) { FinishPhase(); }
        }

        /// <summary>前回の呼び出し以降に起きた出来事を取り出す。</summary>
        public GameEvents TakeEvents()
        {
            var events = _events;
            _events = GameEvents.None;
            return events;
        }

        /// <summary>試合のシードから系列ごとのシードを作る。SeededRandom 側で撹拌するので、近い値でも系列は似ない。</summary>
        public static int SeriesSeed(int seed, RandomSeries stream) => unchecked((seed * 31) + ((int)stream * -1640531527));

        private static bool IsTimed(GamePhase phase) =>
            phase == GamePhase.Swapping || phase == GamePhase.Reverting || phase == GamePhase.Clearing
            || phase == GamePhase.Falling || phase == GamePhase.Reshuffling;

        private void Enter(GamePhase phase, int duration)
        {
            Phase = phase;
            PhaseDuration = duration;
            PhaseRemaining = duration;
        }

        private void FinishPhase()
        {
            switch (Phase)
            {
                case GamePhase.Swapping:
                    var swap = ActiveSwap!.Value;
                    ActiveSwap = null;
                    Board = Board.Swapped(swap.From, swap.To);
                    MovesLeft--;
                    Chain = 0;
                    BeginClear();
                    break;
                case GamePhase.Reverting:
                    ActiveSwap = null;
                    Enter(GamePhase.Idle, 0);
                    break;
                case GamePhase.Clearing:
                    Board = _step!.After;
                    Enter(GamePhase.Falling, _config.FallTicksPerRow * _step.FallRows);
                    break;
                case GamePhase.Falling:
                    BeginClear();
                    break;
                case GamePhase.Reshuffling:
                    Enter(GamePhase.Idle, 0);
                    break;
            }
        }

        // 次の段を解決する。並びがなければ手番を終える。
        private void BeginClear()
        {
            _step = CascadeResolver.Step(Board, Chain + 1, _refillRandom, _config.Colors, _config.ScorePerPiece);
            if (_step == null)
            {
                EndTurn();
                return;
            }

            Chain = _step.Chain;
            MaxChain = Math.Max(MaxChain, Chain);
            LastStepScore = _step.Score;
            Score = CascadeResolver.AddScore(Score, _step.Score);
            _events |= Chain >= 2 ? GameEvents.Cleared | GameEvents.Chained : GameEvents.Cleared;
            Enter(GamePhase.Clearing, _config.ClearTicks);
        }

        private void EndTurn()
        {
            _step = null;
            if (Score >= _config.TargetScore)
            {
                Enter(GamePhase.Won, 0);
                _events |= GameEvents.Won;
            }
            else if (MovesLeft == 0)
            {
                Enter(GamePhase.Lost, 0);
                _events |= GameEvents.Lost;
            }
            else if (!SwapRules.HasLegalMove(Board))
            {
                Reshuffle();
            }
            else
            {
                Enter(GamePhase.Idle, 0);
            }
        }

        private void Reshuffle()
        {
            Board = BoardGenerator.Generate(_reshuffleRandom, _config.Colors, _config.MaxGenerationAttempts);
            Reshuffles++;
            _events |= GameEvents.Reshuffled;
            Enter(GamePhase.Reshuffling, _config.ReshuffleTicks);
        }
    }
}
