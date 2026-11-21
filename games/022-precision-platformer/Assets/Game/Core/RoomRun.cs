using System;

namespace Game.Core
{
    public enum RunStatus
    {
        Playing,

        /// <summary>死亡して、開始位置に戻るのを待っている。入力は受け付けない。</summary>
        Dying,
        Cleared,
    }

    public enum DeathCause
    {
        None,
        Spike,
        Fall,
    }

    [Flags]
    public enum RunEventKind
    {
        None = 0,
        Jumped = 1 << 0,
        WallJumped = 1 << 1,
        Dashed = 1 << 2,
        Died = 1 << 3,
        Respawned = 1 << 4,
        Cleared = 1 << 5,
    }

    /// <summary>
    /// 1 部屋の挑戦。死亡すると <see cref="PrecisionConfig.DeathTicks"/> Tick 後に部屋の開始位置から
    /// 新しい自機でやり直す（何度でも）。出口に触れたら終わり、以後の Tick と入力は受け付けない。
    /// 1 Tick の順序は「自機の移動 → トゲ → 落下 → 出口」（同じ Tick なら死亡が優先）。
    /// 乱数は使わないので、部屋と入力の列だけで結果が決まる。
    /// </summary>
    public sealed class RoomRun
    {
        private readonly PrecisionConfig _config;
        private RunEventKind _events;
        private int _respawnLeft;

        public RoomRun(PrecisionConfig config, Room room)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            _config = config.Copy();
            Room = room ?? throw new ArgumentNullException(nameof(room));
            Player = new Player(_config, room.StartBox);
        }

        private RoomRun(RoomRun source)
        {
            _config = source._config;
            Room = source.Room;
            Player = source.Player.Clone();
            Status = source.Status;
            LastDeath = source.LastDeath;
            Deaths = source.Deaths;
            Ticks = source.Ticks;
            _respawnLeft = source._respawnLeft;
        }

        public Room Room { get; }

        public TileMap Map => Room.Map;

        /// <summary>今の自機。死亡からの再開で新しく作り直す。</summary>
        public Player Player { get; private set; }

        public RunStatus Status { get; private set; }

        public DeathCause LastDeath { get; private set; }

        /// <summary>この部屋での死亡回数。</summary>
        public int Deaths { get; private set; }

        /// <summary>この部屋で進めた Tick 数（死亡の演出中を含む）。</summary>
        public long Ticks { get; private set; }

        public bool IsCleared => Status == RunStatus.Cleared;

        /// <summary>同じ状態の複製（取り出していない出来事は複製しない）。</summary>
        public RoomRun Clone() => new(this);

        /// <summary>1 Tick 進める。クリアの後は何もせず false。</summary>
        public bool Tick(PlayerInput input)
        {
            if (IsCleared)
            {
                return false;
            }

            Ticks++;
            if (Status == RunStatus.Dying)
            {
                if (--_respawnLeft == 0)
                {
                    Player = new Player(_config, Room.StartBox);
                    Status = RunStatus.Playing;
                    _events |= RunEventKind.Respawned;
                }

                return true;
            }

            var actions = Player.Step(Map, input);
            if ((actions & PlayerActions.Jumped) != 0) { _events |= RunEventKind.Jumped; }
            if ((actions & PlayerActions.WallJumped) != 0) { _events |= RunEventKind.WallJumped; }
            if ((actions & PlayerActions.Dashed) != 0) { _events |= RunEventKind.Dashed; }

            if (Map.TouchesSpike(Player.Body))
            {
                Die(DeathCause.Spike);
            }
            else if (Player.Body.Top <= 0)
            {
                Die(DeathCause.Fall);
            }
            else if (Map.Touches(Player.Body, TileKind.Exit))
            {
                Status = RunStatus.Cleared;
                _events |= RunEventKind.Cleared;
            }

            return true;
        }

        public RunEventKind TakeEvents()
        {
            var events = _events;
            _events = RunEventKind.None;
            return events;
        }

        /// <summary>状態のハッシュ（FNV-1a）。Mono と .NET で同じ結果になることの確認用。</summary>
        public uint Hash(uint seed = 2166136261u)
        {
            var hash = seed;
            void Mix(long value)
            {
                unchecked
                {
                    hash = (hash ^ (uint)value) * 16777619u;
                    hash = (hash ^ (uint)(value >> 32)) * 16777619u;
                }
            }

            Mix(Ticks);
            Mix((int)Status);
            Mix(Deaths);
            Mix(Player.Body.X);
            Mix(Player.Body.Y);
            Mix(Player.VelocityX);
            Mix(Player.VelocityY);
            Mix(Player.DashesLeft);
            Mix(Player.Facing);
            return hash;
        }

        private void Die(DeathCause cause)
        {
            Status = RunStatus.Dying;
            LastDeath = cause;
            Deaths++;
            _respawnLeft = _config.DeathTicks;
            _events |= RunEventKind.Died;
        }
    }
}
