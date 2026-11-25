using System;

namespace Game.Core
{
    public enum ActorState
    {
        /// <summary>置き場が空いている。</summary>
        None,
        Idle,
        Walk,
        Attack,
        Jump,
        Land,
        Grab,
        Grabbed,
        HitStun,
        Knockdown,
        Wakeup,
        Dead,
    }

    /// <summary>敵の考え（攻撃権の有無とは別）。</summary>
    public enum EnemyMind
    {
        Approach,
        Wait,
        Engage,
    }

    /// <summary>プレイヤーと敵に共通の実体。状態は Brawl だけが書き換える。</summary>
    public sealed class Actor
    {
        public Actor(int slot) => Slot = slot;

        public int Slot { get; }

        public ActorDef? Def { get; internal set; }

        public ActorKind Kind => Def?.Kind ?? ActorKind.Player;

        /// <summary>出現の番号（1 から増える）。描画順と攻撃権の同順位の決め手。</summary>
        public long Serial { get; internal set; }

        public ActorState State { get; internal set; }

        /// <summary>今の状態に入ってからの Tick。</summary>
        public int StateTick { get; internal set; }

        public int X { get; internal set; }

        public int Y { get; internal set; }

        public int Z { get; internal set; }

        public int VX { get; internal set; }

        public int VY { get; internal set; }

        /// <summary>向き。右が 1、左が -1。</summary>
        public int Facing { get; internal set; } = 1;

        public int Health { get; internal set; }

        public MoveDef? Move { get; internal set; }

        /// <summary>技を出してからの Tick（0 が出した Tick）。</summary>
        public int MoveTick { get; internal set; }

        /// <summary>技の発動の番号。発動ごとに増える。</summary>
        public long Activation { get; internal set; }

        /// <summary>今の発動で当てた相手の置き場（ビット）。同じ発動は 1 体に 1 ヒット。</summary>
        public ulong HitMask { get; internal set; }

        public int HitStop { get; internal set; }

        public int Invuln { get; internal set; }

        /// <summary>空中技をこの跳躍で使ったか。</summary>
        public bool AirUsed { get; internal set; }

        /// <summary>掴んでいる相手か、掴まれている相手の置き場（無ければ -1）。</summary>
        public int Partner { get; internal set; } = -1;

        public int Knees { get; internal set; }

        /// <summary>今の状態が終わるまでの Tick（怯み・着地・起き上がり・掴み）。</summary>
        public int Timer { get; internal set; }

        // 敵の考え
        public EnemyMind Mind { get; internal set; }

        public bool HasToken { get; internal set; }

        public int TokenHeld { get; internal set; }

        public int Waited { get; internal set; }

        public int Cooldown { get; internal set; }

        public int Tokens { get; internal set; }

        /// <summary>待つときの奥行きのずれ。</summary>
        public int WaitOffset { get; internal set; }

        public uint Rng { get; internal set; }

        /// <summary>倒れて消えるときに回復アイテムを落とすか。</summary>
        public bool Food { get; internal set; }

        public bool Active => State != ActorState.None;

        /// <summary>画面に描くか（置き場が使われているか）。</summary>
        public bool InWorld => State != ActorState.None;

        public bool Grounded => Y == 0 && State != ActorState.Jump && !(State == ActorState.Knockdown && Timer < 0);

        public MovePhase Phase => Move == null ? MovePhase.None : Move.PhaseAt(MoveTick);

        public bool Enraged => Def != null && Def.Rage > 100 && Health * 2 <= Def.Health;

        /// <summary>倒れた（体力 0）。以後は攻撃も攻撃権の要求もしない。</summary>
        public bool Defeated => Health <= 0;

        public DepthBox Body
        {
            get
            {
                var def = Def ?? throw new InvalidOperationException("置き場が空です。");
                return DepthBox.Place(X, Y, Z, Facing, def.Body);
            }
        }

        public DepthBox AttackBox
        {
            get
            {
                var move = Move ?? throw new InvalidOperationException("技を出していません。");
                return DepthBox.Place(X, Y, Z, Facing, move.Box);
            }
        }

        internal void CopyFrom(Actor other)
        {
            Def = other.Def;
            Serial = other.Serial;
            State = other.State;
            StateTick = other.StateTick;
            X = other.X;
            Y = other.Y;
            Z = other.Z;
            VX = other.VX;
            VY = other.VY;
            Facing = other.Facing;
            Health = other.Health;
            Move = other.Move;
            MoveTick = other.MoveTick;
            Activation = other.Activation;
            HitMask = other.HitMask;
            HitStop = other.HitStop;
            Invuln = other.Invuln;
            AirUsed = other.AirUsed;
            Partner = other.Partner;
            Knees = other.Knees;
            Timer = other.Timer;
            Mind = other.Mind;
            HasToken = other.HasToken;
            TokenHeld = other.TokenHeld;
            Waited = other.Waited;
            Cooldown = other.Cooldown;
            Tokens = other.Tokens;
            WaitOffset = other.WaitOffset;
            Rng = other.Rng;
            Food = other.Food;
        }

        internal void Clear()
        {
            Def = null;
            Serial = 0;
            State = ActorState.None;
            StateTick = 0;
            X = 0;
            Y = 0;
            Z = 0;
            VX = 0;
            VY = 0;
            Facing = 1;
            Health = 0;
            Move = null;
            MoveTick = 0;
            Activation = 0;
            HitMask = 0;
            HitStop = 0;
            Invuln = 0;
            AirUsed = false;
            Partner = -1;
            Knees = 0;
            Timer = 0;
            Mind = EnemyMind.Approach;
            HasToken = false;
            TokenHeld = 0;
            Waited = 0;
            Cooldown = 0;
            Tokens = 0;
            WaitOffset = 0;
            Rng = 0;
            Food = false;
        }
    }
}
