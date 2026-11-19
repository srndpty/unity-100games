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
    }

    public enum FieldPhase
    {
        /// <summary>ペアを操作している。</summary>
        Falling,

        /// <summary>浮いた胞子が落ちている。Board は落下後の状態。</summary>
        Settling,

        /// <summary>グループが消える演出の途中。Board にはまだ残っている。</summary>
        Popping,

        /// <summary>枯れ胞子が降っている。Board は着弾後の状態。</summary>
        GarbageFalling,

        /// <summary>次のペアが出るまでの間。</summary>
        Spawning,

        /// <summary>出現位置が塞がって負けた。</summary>
        Defeated,
    }

    [Flags]
    public enum FieldEvents
    {
        None = 0,
        Moved = 1 << 0,
        Rotated = 1 << 1,
        Locked = 1 << 2,
        Popped = 1 << 3,
        GarbageLanded = 1 << 4,
        Defeated = 1 << 5,
    }

    /// <summary>片側の盤面の進行。判定は純粋関数に任せ、ここはフェーズと待ち時間だけを管理する。</summary>
    public sealed class PlayerField
    {
        private static readonly IReadOnlyList<SporeGroup> _noGroups = Array.AsReadOnly(Array.Empty<SporeGroup>());

        private readonly SporeConfig _config;
        private readonly IPairSource _pairs;
        private readonly SeededRandom _garbageRandom;
        private readonly GarbageMeter _meter;
        private int _pairIndex;
        private int _gravityCounter;
        private int _lowestY;
        private int _outgoing;
        private bool _softDrop;
        private FieldEvents _events;

        public PlayerField(SporeConfig config, IPairSource pairs, int garbageSeed, Board? board = null)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            _config = config.Copy();
            _pairs = pairs ?? throw new ArgumentNullException(nameof(pairs));
            _garbageRandom = new SeededRandom(garbageSeed);
            _meter = new GarbageMeter(_config.GarbageRate);
            Board = board ?? Board.Empty;
            Spawn();
        }

        public Board Board { get; private set; }
        public ActivePair? Active { get; private set; }
        public FieldPhase Phase { get; private set; }
        public long Score { get; private set; }

        /// <summary>今の手番で進んだ連鎖数。</summary>
        public int Chain { get; private set; }

        /// <summary>この試合の最大連鎖数。</summary>
        public int MaxChain { get; private set; }

        public int LockTimer { get; private set; }
        public int LockResets { get; private set; }

        /// <summary>進めた Tick 数。負けた後は増えない。</summary>
        public int TickCount { get; private set; }

        /// <summary>現在のフェーズの長さ。演出の進み具合の計算に使う。</summary>
        public int PhaseDuration { get; private set; }

        /// <summary>現在のフェーズの残り Tick 数。</summary>
        public int PhaseRemaining { get; private set; }

        /// <summary>消える演出中のグループ。</summary>
        public IReadOnlyList<SporeGroup> PoppingGroups { get; private set; } = _noGroups;

        /// <summary>落下中の胞子の移動。</summary>
        public IReadOnlyList<FallMove> Falls { get; private set; } = FallResult.NoMoves;

        /// <summary>着弾を待っている枯れ胞子の数。</summary>
        public int PendingGarbage => _meter.Pending;

        /// <summary>これまでに出現したペアの数。操作中のペアの識別に使う。</summary>
        public int PairsSpawned => _pairIndex;

        /// <summary>index 個先のペア。0 が次に出るペア。</summary>
        public Pair Next(int index) => _pairs.At(_pairIndex + index);

        /// <summary>操作を適用する。受け付けなかったときは false を返し、状態を変えない。</summary>
        public bool Apply(GameCommand command)
        {
            if (Phase == FieldPhase.Defeated) { return false; }
            if (command == GameCommand.SoftDropOff) { return SetSoftDrop(false); }
            if (Phase != FieldPhase.Falling) { return false; }
            return command switch
            {
                GameCommand.MoveLeft => TryMove(-1),
                GameCommand.MoveRight => TryMove(1),
                GameCommand.RotateClockwise => TryRotate(true),
                GameCommand.RotateCounterClockwise => TryRotate(false),
                GameCommand.SoftDropOn => SetSoftDrop(true),
                _ => false,
            };
        }

        /// <summary>1 Tick 進める。</summary>
        public void Tick()
        {
            if (Phase == FieldPhase.Defeated) { return; }
            TickCount++;
            if (Phase == FieldPhase.Falling)
            {
                TickFalling();
                return;
            }

            PhaseRemaining--;
            if (PhaseRemaining <= 0) { FinishPhase(); }
        }

        /// <summary>相手からの攻撃を予告に積む。</summary>
        public void ReceiveGarbage(int count) => _meter.Receive(count);

        /// <summary>相手へ送る枯れ胞子の数を取り出す。</summary>
        public int TakeOutgoing()
        {
            var outgoing = _outgoing;
            _outgoing = 0;
            return outgoing;
        }

        /// <summary>前回の呼び出し以降に起きた出来事を取り出す。</summary>
        public FieldEvents TakeEvents()
        {
            var events = _events;
            _events = FieldEvents.None;
            return events;
        }

        private int FallInterval()
        {
            var step = Math.Min(TickCount / _config.TicksPerGravityStep, _config.GravityTicks.Length - 1);
            var gravity = _config.GravityTicks[step];
            return _softDrop ? Math.Min(gravity, _config.SoftDropTicks) : gravity;
        }

        private void TickFalling()
        {
            var pair = Active!.Value;
            var lowered = pair.Moved(0, -1);
            if (!Board.Fits(lowered))
            {
                _gravityCounter = 0;
                LockTimer++;
                if (_softDrop || LockTimer >= _config.LockDelayTicks) { LockActive(pair); }
                return;
            }

            _gravityCounter++;
            if (_gravityCounter < FallInterval()) { return; }
            _gravityCounter = 0;
            SetActive(lowered);
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
            _events |= FieldEvents.Moved;
            return true;
        }

        private bool TryRotate(bool clockwise)
        {
            var turned = PairRotation.Rotate(Board, Active!.Value, clockwise);
            if (turned == null) { return false; }
            SetActive(turned.Value);
            ExtendLockDelay();
            _events |= FieldEvents.Rotated;
            return true;
        }

        // 猶予の延長は回数に上限を設け、接地したまま操作し続けても必ず確定する。
        private void ExtendLockDelay()
        {
            if (LockTimer == 0 || LockResets >= _config.MaxLockResets) { return; }
            LockResets++;
            LockTimer = 0;
        }

        // より低い段へ進んだら猶予を戻す。最低到達段は下がるだけなので、戻せる回数にも上限がある。
        private void SetActive(ActivePair pair)
        {
            Active = pair;
            if (pair.LowestY >= _lowestY) { return; }
            _lowestY = pair.LowestY;
            LockTimer = 0;
            LockResets = 0;
        }

        private void LockActive(ActivePair pair)
        {
            Board = Board.Place(pair);
            Active = null;
            Chain = 0;
            _softDrop = false;
            _events |= FieldEvents.Locked;
            BeginSettle();
        }

        private void BeginSettle()
        {
            var result = BoardGravity.Settle(Board);
            Board = result.Board;
            Falls = result.Moves;
            Enter(FieldPhase.Settling, result.MaxDistance * _config.SettleTicksPerRow);
        }

        private void Enter(FieldPhase phase, int duration)
        {
            Phase = phase;
            PhaseDuration = duration;
            PhaseRemaining = duration;
            if (duration <= 0) { FinishPhase(); }
        }

        private void FinishPhase()
        {
            switch (Phase)
            {
                case FieldPhase.Settling:
                    Falls = FallResult.NoMoves;
                    Match();
                    break;
                case FieldPhase.Popping:
                    Board = ChainResolver.Pop(Board, PoppingGroups);
                    PoppingGroups = _noGroups;
                    BeginSettle();
                    break;
                case FieldPhase.GarbageFalling:
                    Falls = FallResult.NoMoves;
                    FinishTurn();
                    break;
                case FieldPhase.Spawning:
                    Spawn();
                    break;
            }
        }

        private void Match()
        {
            var groups = GroupFinder.Find(Board);
            if (groups.Count == 0)
            {
                DropGarbage();
                return;
            }

            Chain++;
            MaxChain = Math.Max(MaxChain, Chain);
            var score = ScoreRules.ChainScore(_config, Chain, groups);
            Score += score;
            _outgoing += _meter.Attack(score);
            PoppingGroups = groups;
            _events |= FieldEvents.Popped;
            Enter(FieldPhase.Popping, _config.PopTicks);
        }

        private void DropGarbage()
        {
            var count = _meter.TakeDrop(_config.MaxGarbagePerDrop);
            if (count == 0)
            {
                FinishTurn();
                return;
            }

            var result = GarbageDropper.Drop(Board, count, _garbageRandom);
            Board = result.Board;
            Falls = result.Moves;
            _events |= FieldEvents.GarbageLanded;
            Enter(FieldPhase.GarbageFalling, result.MaxDistance * _config.SettleTicksPerRow);
        }

        private void FinishTurn()
        {
            if (!Board.Fits(ActivePair.Spawn(_pairs.At(_pairIndex))))
            {
                Defeat();
                return;
            }

            Enter(FieldPhase.Spawning, _config.SpawnDelayTicks);
        }

        private void Spawn()
        {
            var pair = ActivePair.Spawn(_pairs.At(_pairIndex));
            if (!Board.Fits(pair))
            {
                Defeat();
                return;
            }

            _pairIndex++;
            Active = pair;
            _lowestY = pair.LowestY;
            _gravityCounter = 0;
            LockTimer = 0;
            LockResets = 0;
            Phase = FieldPhase.Falling;
            PhaseDuration = 0;
            PhaseRemaining = 0;
        }

        private void Defeat()
        {
            Active = null;
            Phase = FieldPhase.Defeated;
            PhaseDuration = 0;
            PhaseRemaining = 0;
            _events |= FieldEvents.Defeated;
        }
    }
}
