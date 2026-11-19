using System;

namespace Game.Core
{
    public enum GamePhase
    {
        /// <summary>手番の側の着手を待っている。手番の側には合法手がある。</summary>
        AwaitingMove,

        /// <summary>石を置いて返す演出中。Position は着手後の局面。</summary>
        Placing,

        /// <summary>パスを知らせる演出中。Position の手番はまだパスする側。</summary>
        Passing,

        /// <summary>両者とも打てず終局した。</summary>
        Over,
    }

    /// <summary>着手の判定。Ok 以外では局面も手番も変わらない。</summary>
    public enum PlayVerdict
    {
        Ok,

        /// <summary>終局している。</summary>
        GameOver,

        /// <summary>演出中で、まだ着手を受け付けていない。</summary>
        Busy,

        /// <summary>手番でない側の着手。</summary>
        NotYourTurn,

        /// <summary>盤外のマス。</summary>
        OutOfBoard,

        /// <summary>石のあるマス。</summary>
        Occupied,

        /// <summary>相手の石を1つも挟めない。</summary>
        NoFlip,
    }

    [Flags]
    public enum GameEvents
    {
        None = 0,

        /// <summary>石を置いて返した。</summary>
        Placed = 1 << 0,

        /// <summary>合法手がなくパスした。</summary>
        Passed = 1 << 1,

        /// <summary>終局した。</summary>
        GameOver = 1 << 2,
    }

    /// <summary>
    /// 1試合の進行。着手の検証と適用は <see cref="Position"/> に任せ、ここはパス・終局の判定と演出の待ち時間を管理する。
    /// 乱数を使わないので、同じ開始局面と着手の列なら必ず同じ結果になる。
    /// </summary>
    public sealed class OthelloGame
    {
        private readonly OthelloConfig _config;
        private GameEvents _events;

        /// <summary>初期配置から始める。start を渡すと、その局面から始める（撮影とテスト用）。</summary>
        public OthelloGame(OthelloConfig config, Position? start = null)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            _config = config.Copy();
            Position = start ?? Position.Initial;
            ResolveTurn();
        }

        public Position Position { get; private set; }
        public GamePhase Phase { get; private set; }

        /// <summary>手番の側。パスの演出中はパスする側。</summary>
        public Side ToMove => Position.ToMove;

        public bool IsOver => Phase == GamePhase.Over;

        /// <summary>着手を受け付けている。</summary>
        public bool AcceptsMove => Phase == GamePhase.AwaitingMove;

        /// <summary>直前に置いた石のマス。まだ置いていなければ null。</summary>
        public Square? LastMove { get; private set; }

        /// <summary>直前の着手で返った石。</summary>
        public ulong LastFlips { get; private set; }

        /// <summary>置いた石の数（パスは数えない）。</summary>
        public int MovesPlayed { get; private set; }

        /// <summary>パスの回数。</summary>
        public int Passes { get; private set; }

        /// <summary>着手待ちに入った回数。CPU がまだ考えていない手番かを見分けるのに使う。</summary>
        public int Turn { get; private set; }

        /// <summary>進めた Tick 数。終局後は増えない。</summary>
        public int TickCount { get; private set; }

        /// <summary>現在のフェーズの長さ。演出の進み具合の計算に使う。</summary>
        public int PhaseDuration { get; private set; }

        /// <summary>現在のフェーズの残り Tick 数。</summary>
        public int PhaseRemaining { get; private set; }

        public int Count(Side side) => Position.Count(side);

        /// <summary>勝った側。終局前と引き分けは null。</summary>
        public Side? Winner
        {
            get
            {
                if (!IsOver) { return null; }
                var diff = Position.Count(Side.Black) - Position.Count(Side.White);
                return diff > 0 ? Side.Black : diff < 0 ? Side.White : null;
            }
        }

        public bool IsDraw => IsOver && Position.Count(Side.Black) == Position.Count(Side.White);

        /// <summary>手番の側が今打てるマス。着手待ちでなければ 0。</summary>
        public ulong LegalMoves => AcceptsMove ? Position.LegalMoves() : 0;

        /// <summary>side が square に打つ。判定が Ok のときだけ局面が進む。</summary>
        public PlayVerdict TryPlay(Side side, Square square)
        {
            var verdict = Check(side, square);
            if (verdict != PlayVerdict.Ok) { return verdict; }

            var flips = Position.Flips(square);
            Position = Position.Apply(square.Bit, flips);
            LastMove = square;
            LastFlips = flips;
            MovesPlayed++;
            _events |= GameEvents.Placed;
            EnterPhase(GamePhase.Placing, _config.FlipTicks);
            if (PhaseRemaining == 0) { ResolveTurn(); }
            return PlayVerdict.Ok;
        }

        /// <summary>side が square に打てるかを、局面を変えずに判定する。</summary>
        public PlayVerdict Check(Side side, Square square)
        {
            if (IsOver) { return PlayVerdict.GameOver; }
            if (!AcceptsMove) { return PlayVerdict.Busy; }
            if (side != ToMove) { return PlayVerdict.NotYourTurn; }
            if (!square.IsInside) { return PlayVerdict.OutOfBoard; }
            if (Position.At(square) != Disc.Empty) { return PlayVerdict.Occupied; }
            return Position.Flips(square) == 0 ? PlayVerdict.NoFlip : PlayVerdict.Ok;
        }

        /// <summary>1 Tick 進める。演出の待ち時間が終われば次の手番を決める。</summary>
        public void Tick()
        {
            if (IsOver) { return; }
            TickCount++;
            if (PhaseRemaining == 0) { return; }
            PhaseRemaining--;
            if (PhaseRemaining > 0) { return; }

            if (Phase == GamePhase.Passing) { FinishPass(); }
            else if (Phase == GamePhase.Placing) { ResolveTurn(); }
        }

        /// <summary>前回の呼び出し以降に起きた出来事を取り出す。</summary>
        public GameEvents TakeEvents()
        {
            var events = _events;
            _events = GameEvents.None;
            return events;
        }

        // 手番の側が打てれば着手待ち、打てず相手が打てればパス、両者とも打てなければ終局。
        private void ResolveTurn()
        {
            if (Position.HasLegalMove(ToMove))
            {
                Turn++;
                EnterPhase(GamePhase.AwaitingMove, 0);
                return;
            }

            if (Position.HasLegalMove(ToMove.Opponent()))
            {
                Passes++;
                _events |= GameEvents.Passed;
                EnterPhase(GamePhase.Passing, _config.PassTicks);
                if (PhaseRemaining == 0) { FinishPass(); }
                return;
            }

            _events |= GameEvents.GameOver;
            EnterPhase(GamePhase.Over, 0);
        }

        private void FinishPass()
        {
            // パスした側の相手には合法手があるので、必ず着手待ちになる。
            Position = Position.Pass();
            Turn++;
            EnterPhase(GamePhase.AwaitingMove, 0);
        }

        private void EnterPhase(GamePhase phase, int duration)
        {
            Phase = phase;
            PhaseDuration = duration;
            PhaseRemaining = duration;
        }
    }
}
