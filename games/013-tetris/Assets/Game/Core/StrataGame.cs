using System;
using System.Collections.Generic;

namespace Game.Core
{
    public enum GameCommand
    {
        MoveLeft,
        MoveRight,
        RotateClockwise,
        RotateCounterClockwise,
        SoftDropOn,
        SoftDropOff,
        HardDrop,
        Hold,
    }

    public enum GamePhase
    {
        Falling,
        Clearing,
        GameOver,
    }

    public enum GameOverReason
    {
        None,

        /// <summary>次のピースの出現位置が埋まっていた。</summary>
        BlockOut,

        /// <summary>ピース全体が非表示の段で確定した。</summary>
        LockOut,
    }

    [Flags]
    public enum GameEvents
    {
        None = 0,
        Moved = 1 << 0,
        Rotated = 1 << 1,
        Locked = 1 << 2,
        HardDropped = 1 << 3,
        Held = 1 << 4,
        LinesCleared = 1 << 5,
        LevelUp = 1 << 6,
        GameOver = 1 << 7,
    }

    /// <summary>1試合の進行。確定ブロック（Board）と操作ピース（Active）を分けて持つ。</summary>
    public sealed class StrataGame
    {
        public const int PreviewCount = 5;

        private static readonly IReadOnlyList<int> _noRows = Array.AsReadOnly(Array.Empty<int>());

        private readonly StrataConfig _config;
        private readonly IPieceSource _pieces;
        private int _gravityCounter;
        private int _clearRemaining;
        private int _lowestRow;
        private bool _softDrop;
        private GameEvents _events;

        public StrataGame(StrataConfig config, int seed)
            : this(config, new SevenBag(seed), Board.Empty)
        {
        }

        public StrataGame(StrataConfig config, IPieceSource pieces, Board board)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            _config = config.Copy();
            _pieces = pieces ?? throw new ArgumentNullException(nameof(pieces));
            Board = board ?? throw new ArgumentNullException(nameof(board));
            SpawnNext();
        }

        public Board Board { get; private set; }
        public ActivePiece? Active { get; private set; }
        public PieceKind? HoldKind { get; private set; }
        public bool CanHold { get; private set; } = true;
        public GamePhase Phase { get; private set; }
        public GameOverReason OverReason { get; private set; }
        public long Score { get; private set; }
        public int Lines { get; private set; }
        public int Level { get; private set; } = 1;
        public int LockTimer { get; private set; }
        public int LockResets { get; private set; }

        /// <summary>進めた Tick 数。終了後は増えない。</summary>
        public int TickCount { get; private set; }

        /// <summary>表示に関わる状態が変わるたびに増える。</summary>
        public int Revision { get; private set; }

        /// <summary>消去演出中の段。演出が終わるまで Board には残っている。</summary>
        public IReadOnlyList<int> ClearingRows { get; private set; } = _noRows;

        /// <summary>最後に消した段数。</summary>
        public int LastClearCount { get; private set; }

        /// <summary>ハードドロップしたときの着地位置。</summary>
        public ActivePiece? Ghost => Active is ActivePiece piece ? piece.Moved(0, -DropDistance(piece)) : null;

        /// <summary>index 個先のピース。0 が次に出るピース。</summary>
        public PieceKind Preview(int index) => _pieces.Peek(index);

        /// <summary>操作を適用する。受け付けなかったときは false を返し、状態を変えない。</summary>
        public bool Apply(GameCommand command)
        {
            if (Phase == GamePhase.GameOver) { return false; }
            switch (command)
            {
                case GameCommand.SoftDropOn: return SetSoftDrop(true);
                case GameCommand.SoftDropOff: return SetSoftDrop(false);
            }

            if (Phase != GamePhase.Falling) { return false; }
            return command switch
            {
                GameCommand.MoveLeft => TryMove(-1),
                GameCommand.MoveRight => TryMove(1),
                GameCommand.RotateClockwise => TryRotate(true),
                GameCommand.RotateCounterClockwise => TryRotate(false),
                GameCommand.HardDrop => HardDrop(),
                GameCommand.Hold => Hold(),
                _ => false,
            };
        }

        /// <summary>1 Tick 進める。</summary>
        public void Tick()
        {
            if (Phase == GamePhase.GameOver) { return; }
            TickCount++;
            if (Phase == GamePhase.Clearing)
            {
                _clearRemaining--;
                if (_clearRemaining <= 0) { FinishClear(); }
                return;
            }

            var piece = Active!.Value;
            if (!Board.Fits(piece.Moved(0, -1)))
            {
                _gravityCounter = 0;
                LockTimer++;
                if (LockTimer >= _config.LockDelayTicks) { LockActive(piece); }
                return;
            }

            _gravityCounter++;
            if (_gravityCounter < FallInterval()) { return; }
            _gravityCounter = 0;
            SetActive(piece.Moved(0, -1));
            if (_softDrop) { Score += _config.SoftDropScore; }
        }

        /// <summary>前回の呼び出し以降に起きた出来事を取り出す。</summary>
        public GameEvents TakeEvents()
        {
            var events = _events;
            _events = GameEvents.None;
            return events;
        }

        private int FallInterval()
        {
            var gravity = _config.GravityTicks[Level - 1];
            return _softDrop ? Math.Min(gravity, _config.SoftDropTicks) : gravity;
        }

        private bool SetSoftDrop(bool on)
        {
            if (_softDrop == on) { return false; }
            _softDrop = on;
            return true;
        }

        private bool TryMove(int dx)
        {
            var moved = Active!.Value.Moved(dx, 0);
            if (!Board.Fits(moved)) { return false; }
            SetActive(moved);
            ExtendLockDelay();
            _events |= GameEvents.Moved;
            return true;
        }

        private bool TryRotate(bool clockwise)
        {
            var piece = Active!.Value;
            var target = PieceShapes.Turn(piece.Rotation, clockwise);
            var offsets = KickTable.Offsets(piece.Kind, piece.Rotation, clockwise);
            for (var i = 0; i < offsets.Count; i++)
            {
                var turned = piece.Turned(target, offsets[i]);
                if (!Board.Fits(turned)) { continue; }
                SetActive(turned);
                ExtendLockDelay();
                _events |= GameEvents.Rotated;
                return true;
            }

            return false;
        }

        private bool HardDrop()
        {
            var piece = Active!.Value;
            var distance = DropDistance(piece);
            Score += (long)distance * _config.HardDropScore;
            _events |= GameEvents.HardDropped;
            LockActive(piece.Moved(0, -distance));
            return true;
        }

        private bool Hold()
        {
            if (!CanHold) { return false; }
            var kind = Active!.Value.Kind;
            var swapped = HoldKind;
            HoldKind = kind;
            CanHold = false;
            _events |= GameEvents.Held;
            Spawn(swapped ?? _pieces.Take());
            return true;
        }

        private int DropDistance(ActivePiece piece)
        {
            var distance = 0;
            while (Board.Fits(piece.Moved(0, -(distance + 1)))) { distance++; }
            return distance;
        }

        // 操作の猶予は回数に上限を設け、接地したまま操作し続けても必ず確定する。
        private void ExtendLockDelay()
        {
            if (LockTimer == 0 || LockResets >= _config.MaxLockResets) { return; }
            LockResets++;
            LockTimer = 0;
        }

        // より低い段へ進んだら猶予を戻す。最低到達段は下がるだけなので、戻せる回数にも上限がある。
        private void SetActive(ActivePiece piece)
        {
            Active = piece;
            Revision++;
            var lowest = piece.LowestRow;
            if (lowest >= _lowestRow) { return; }
            _lowestRow = lowest;
            LockTimer = 0;
            LockResets = 0;
        }

        private void LockActive(ActivePiece piece)
        {
            Board = Board.Lock(piece);
            Active = null;
            CanHold = true;
            Revision++;
            _events |= GameEvents.Locked;
            if (piece.IsAboveVisibleArea)
            {
                EndGame(GameOverReason.LockOut);
                return;
            }

            var rows = Board.FullRows();
            if (rows.Count == 0)
            {
                SpawnNext();
                return;
            }

            AwardLines(rows.Count);
            ClearingRows = rows;
            Phase = GamePhase.Clearing;
            _clearRemaining = _config.LineClearTicks;
            if (_clearRemaining == 0) { FinishClear(); }
        }

        // 得点は消す前のレベルで計算する。
        private void AwardLines(int count)
        {
            Score += (long)_config.LineScores[count - 1] * Level;
            Lines += count;
            LastClearCount = count;
            _events |= GameEvents.LinesCleared;
            var level = Math.Min(1 + (Lines / _config.LinesPerLevel), _config.MaxLevel);
            if (level > Level) { _events |= GameEvents.LevelUp; }
            Level = level;
        }

        private void FinishClear()
        {
            Board = Board.ClearRows(ClearingRows);
            ClearingRows = _noRows;
            Phase = GamePhase.Falling;
            Revision++;
            SpawnNext();
        }

        private void SpawnNext() => Spawn(_pieces.Take());

        private void Spawn(PieceKind kind)
        {
            var piece = ActivePiece.Spawn(kind);
            Active = null;
            Revision++;
            if (!Board.Fits(piece))
            {
                EndGame(GameOverReason.BlockOut);
                return;
            }

            var lowered = piece.Moved(0, -1);
            var placed = Board.Fits(lowered) ? lowered : piece;
            Active = placed;
            _lowestRow = placed.LowestRow;
            _gravityCounter = 0;
            LockTimer = 0;
            LockResets = 0;
        }

        private void EndGame(GameOverReason reason)
        {
            Phase = GamePhase.GameOver;
            OverReason = reason;
            _events |= GameEvents.GameOver;
        }
    }
}
