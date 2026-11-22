using System;
using TileMotion;

namespace Game.Core
{
    public enum DungeonStatus
    {
        Playing,

        /// <summary>部屋の切り替え中（スクロール）。入力・被弾・敵の動きはない。</summary>
        Transition,

        /// <summary>体力が 0 になった。<see cref="Dungeon.Continue"/> で入口から再開できる。</summary>
        Down,

        /// <summary>秘宝を取った。以後の Tick と入力は受け付けない。</summary>
        Won,
    }

    /// <summary>
    /// 1 回の試合。ダンジョンの状態（鍵・扉・宝箱・全滅の小鍵・ボス撃破・訪れた部屋）と自機の体力は部屋をまたいで持ち越し、
    /// 敵・弾は部屋に入るたびに初めから作る。出入口に触れた Tick に隣の部屋の到着位置へ移り、<see cref="VaultConfig.TransitionTicks"/> Tick の
    /// 切り替えの後、次の Tick からそこで進む。体力が 0 になると止まり、<see cref="Continue"/> で入口から体力満タンで再開する
    /// （ダンジョンの状態はそのまま）。秘宝に触れたら勝ち。
    /// 乱数は 1 試合のシードから、部屋に入るたびに「シード・部屋・入った回数」で系列を作る（ある部屋の乱数の使い方が他の部屋に響かない）。
    /// </summary>
    public sealed class Dungeon
    {
        private readonly VaultConfig _config;
        private DungeonEvents _events;

        public Dungeon(VaultConfig config, World world, int seed)
            : this(config, world, seed, null, -1, default)
        {
        }

        private Dungeon(VaultConfig config, World world, int seed, DungeonState? state, int room, Cell cell)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            _config = config.Copy();
            World = world ?? throw new ArgumentNullException(nameof(world));
            Seed = seed;
            if (state == null)
            {
                State = new DungeonState(world);
                room = world.StartRoom;
                cell = world.Rooms[room].Start!.Value;
            }
            else
            {
                if (room < 0 || room >= world.Rooms.Count) { throw new ArgumentOutOfRangeException(nameof(room), room, "ダンジョンにない部屋です。"); }
                if (!ReferenceEquals(state.World, world)) { throw new ArgumentException("別のダンジョンの状態です。", nameof(state)); }
                State = state.Clone();
            }

            Hero = new Hero(_config, cell.Centered(Hero.Size));
            Run = Enter(room);
        }

        private Dungeon(Dungeon source)
        {
            _config = source._config;
            World = source.World;
            Seed = source.Seed;
            State = source.State.Clone();
            Hero = source.Hero.Clone();
            Run = source.Run.Clone(State, Hero);
            Status = source.Status;
            Deaths = source.Deaths;
            ElapsedTicks = source.ElapsedTicks;
            RoomEntries = source.RoomEntries;
            RoomTicks = source.RoomTicks;
            TransitionRemaining = source.TransitionRemaining;
            PreviousRoom = source.PreviousRoom;
            EnteredFrom = source.EnteredFrom;
        }

        /// <summary>
        /// 状態と位置を指定して試合を始める（テスト・撮影用）。状態は複製して使うので、渡したものは変わらない。
        /// 自機はマス <paramref name="cell"/> の中央に置く。
        /// </summary>
        public static Dungeon StartAt(VaultConfig config, DungeonState state, int room, Cell cell, int seed)
        {
            if (state == null) { throw new ArgumentNullException(nameof(state)); }
            return new Dungeon(config, state.World, seed, state, room, cell);
        }

        public World World { get; }

        public int Seed { get; }

        public DungeonState State { get; private set; }

        public Hero Hero { get; private set; }

        public RoomRun Run { get; private set; }

        public int RoomIndex => Run.RoomIndex;

        public DungeonStatus Status { get; private set; }

        /// <summary>体力が 0 になった回数。</summary>
        public int Deaths { get; private set; }

        /// <summary>試合を始めてから進めた Tick 数（切り替えを含む）。</summary>
        public long ElapsedTicks { get; private set; }

        /// <summary>部屋に入った回数（開始・出入口・再開を含む）。部屋の乱数の系列に使う。</summary>
        public int RoomEntries { get; private set; }

        /// <summary>今の部屋に入ってから進めた Tick 数（切り替えを含む）。</summary>
        public long RoomTicks { get; private set; }

        /// <summary>切り替えの残り Tick 数（切り替え中でなければ 0）。</summary>
        public int TransitionRemaining { get; private set; }

        /// <summary>直前にいた部屋（出入口で移ったとき。開始・再開では -1）。切り替えの描画に使う。</summary>
        public int PreviousRoom { get; private set; } = -1;

        /// <summary>今の部屋に入った辺（出入口で移ったとき）。</summary>
        public Side EnteredFrom { get; private set; }

        public bool IsWon => Status == DungeonStatus.Won;

        public Dungeon Clone() => new(this);

        /// <summary>1 Tick 進める。進行中・切り替え中でなければ何もせず false。</summary>
        public bool Tick(HeroInput input)
        {
            if (Status == DungeonStatus.Transition)
            {
                ElapsedTicks++;
                RoomTicks++;
                if (--TransitionRemaining <= 0)
                {
                    Status = DungeonStatus.Playing;
                    _events |= DungeonEvents.Arrived;
                }

                return true;
            }

            if (Status != DungeonStatus.Playing) { return false; }
            ElapsedTicks++;
            RoomTicks++;
            var outcome = Run.Tick(input);
            _events |= Run.TakeEvents();
            switch (outcome)
            {
                case RoomOutcome.Exit:
                    var side = Run.ExitSide;
                    var next = World.Neighbor(RoomIndex, side);
                    var from = World.Opposite(side);
                    Hero.Arrive(World.Rooms[next].Arrival(from));
                    PreviousRoom = RoomIndex;
                    EnteredFrom = from;
                    Run = Enter(next);
                    Status = DungeonStatus.Transition;
                    TransitionRemaining = _config.TransitionTicks;
                    _events |= DungeonEvents.RoomChanged;
                    break;
                case RoomOutcome.Down:
                    Status = DungeonStatus.Down;
                    Deaths++;
                    break;
                case RoomOutcome.Won:
                    Status = DungeonStatus.Won;
                    break;
            }

            return true;
        }

        /// <summary>
        /// 体力が 0 になった後、入口の開始位置から体力満タンで再開する。ダンジョンの状態（鍵・扉・宝箱・ボス撃破）はそのまま。
        /// 倒れていなければ何もせず false。
        /// </summary>
        public bool Continue()
        {
            if (Status != DungeonStatus.Down) { return false; }
            var room = World.StartRoom;
            Hero = new Hero(_config, World.Rooms[room].Start!.Value.Centered(Hero.Size));
            PreviousRoom = -1;
            Run = Enter(room);
            Status = DungeonStatus.Playing;
            _events |= DungeonEvents.Continued;
            return true;
        }

        /// <summary>前回取り出してから起きた出来事。</summary>
        public DungeonEvents TakeEvents()
        {
            var events = _events;
            _events = DungeonEvents.None;
            return events;
        }

        /// <summary>以後の動きを決める状態をすべて含むハッシュ。</summary>
        public uint Hash()
        {
            var hash = StateHash.Start();
            hash.Mix(Seed);
            hash.Mix((int)Status);
            hash.Mix(Deaths);
            hash.Mix(ElapsedTicks);
            hash.Mix(RoomEntries);
            hash.Mix(RoomTicks);
            hash.Mix(TransitionRemaining);
            State.MixInto(ref hash);
            Hero.MixInto(ref hash);
            Run.MixInto(ref hash);
            return hash.Value;
        }

        /// <summary>部屋の乱数のシード。試合のシード・部屋・入った回数を混ぜる。</summary>
        public static int RoomSeed(int seed, int room, int entry)
        {
            var hash = StateHash.Start();
            hash.Mix(seed);
            hash.Mix(room);
            hash.Mix(entry);
            return unchecked((int)hash.Value);
        }

        private RoomRun Enter(int room)
        {
            State.Visit(room);
            RoomEntries++;
            RoomTicks = 0;
            return new RoomRun(_config, State, room, Hero, RoomSeed(Seed, room, RoomEntries));
        }
    }
}
