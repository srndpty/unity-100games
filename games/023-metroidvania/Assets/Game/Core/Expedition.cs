using System;
using TileMotion;

namespace Game.Core
{
    public enum ExpeditionStatus
    {
        Playing,

        /// <summary>体力が 0 になった。<see cref="Expedition.Continue"/> で最後のセーブから再開できる。</summary>
        Down,

        /// <summary>ボスを倒した。以後の Tick と入力は受け付けない。</summary>
        Won,
    }

    /// <summary>セーブ像に触れた時点の記録。世界の状態の複製と、再開する部屋・像の位置。</summary>
    public sealed class SavePoint
    {
        public SavePoint(WorldState state, int room, Cell cell)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            Room = room;
            Cell = cell;
        }

        public WorldState State { get; }

        public int Room { get; }

        public Cell Cell { get; }
    }

    /// <summary>
    /// 1 回の試合。世界の状態（能力・壊した壁・取ったアイテム・訪れた部屋）と自機の体力は部屋をまたいで持ち越し、
    /// 敵・弾・ボスは部屋に入るたびに初めから作る。扉に触れた Tick に隣の部屋の到着位置へ移り、次の Tick からそこで進む。
    /// 体力が 0 になると止まり、<see cref="Continue"/> で最後に触れたセーブ像の時点の世界の状態に戻して、その像の前から再開する
    /// （試合の開始位置は最初のセーブとして扱う）。ボスを倒したら勝ち。乱数は使わず、入力の列だけで結果が決まる。
    /// </summary>
    public sealed class Expedition
    {
        private readonly ExplorerConfig _config;
        private ExpeditionEvents _events;

        public Expedition(ExplorerConfig config, World world)
            : this(config, world, null, -1, default)
        {
        }

        private Expedition(ExplorerConfig config, World world, WorldState? state, int room, Cell cell)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            _config = config.Copy();
            World = world ?? throw new ArgumentNullException(nameof(world));
            if (state == null)
            {
                State = new WorldState(world);
                room = world.StartRoom;
                cell = world.Rooms[room].Start!.Value;
            }
            else
            {
                if (room < 0 || room >= world.Rooms.Count) { throw new ArgumentOutOfRangeException(nameof(room), room, "世界にない部屋です。"); }
                State = state.Clone();
            }

            State.Visit(room);
            Player = new Player(_config, Room.PlaceOnTile(cell, Player.Width, Player.Height));
            Run = new RoomRun(_config, world, State, room, Player);
            Save = new SavePoint(State.Clone(), room, cell);
            RoomEntries = 1;
        }

        /// <summary>
        /// 世界の状態と位置を指定して試合を始める（テスト・撮影用）。指定した位置が最初のセーブになる。
        /// 世界の状態は複製して使うので、渡したものは変わらない。
        /// </summary>
        public static Expedition StartAt(ExplorerConfig config, World world, WorldState state, int room, Cell cell)
        {
            if (state == null) { throw new ArgumentNullException(nameof(state)); }
            return new Expedition(config, world, state, room, cell);
        }

        private Expedition(Expedition source)
        {
            _config = source._config;
            World = source.World;
            State = source.State.Clone();
            Player = source.Player.Clone();
            Run = source.Run.Clone(State, Player);
            Save = source.Save;
            Status = source.Status;
            Deaths = source.Deaths;
            ElapsedTicks = source.ElapsedTicks;
            RoomEntries = source.RoomEntries;
            RoomTicks = source.RoomTicks;
        }

        public World World { get; }

        public WorldState State { get; private set; }

        public Player Player { get; private set; }

        public RoomRun Run { get; private set; }

        public int RoomIndex => Run.RoomIndex;

        /// <summary>最後に触れたセーブ像の記録（世界の状態は複製で、以後の進行で変わらない）。</summary>
        public SavePoint Save { get; private set; }

        public ExpeditionStatus Status { get; private set; }

        /// <summary>体力が 0 になった回数。</summary>
        public int Deaths { get; private set; }

        /// <summary>試合を始めてから進めた Tick 数。</summary>
        public long ElapsedTicks { get; private set; }

        /// <summary>部屋に入った回数（開始・扉・再開を含む）。部屋ごとの入力の列の番号に使う。</summary>
        public int RoomEntries { get; private set; }

        /// <summary>今の部屋に入ってから進めた Tick 数。</summary>
        public long RoomTicks { get; private set; }

        public bool IsWon => Status == ExpeditionStatus.Won;

        public Expedition Clone() => new(this);

        /// <summary>1 Tick 進める。進行中でなければ何もせず false。</summary>
        public bool Tick(PlayerInput input)
        {
            if (Status != ExpeditionStatus.Playing) { return false; }
            ElapsedTicks++;
            RoomTicks++;
            var outcome = Run.Tick(input);
            var events = Run.TakeEvents();
            if ((events & ExpeditionEvents.Saved) != 0)
            {
                Save = new SavePoint(State.Clone(), RoomIndex, Run.SavedAt);
            }

            switch (outcome)
            {
                case RoomOutcome.Door:
                    var side = Run.ExitSide;
                    var next = World.Neighbor(RoomIndex, side);
                    var arrival = World.Rooms[next].Arrival(World.Opposite(side));
                    Player.Teleport(Room.PlaceOnTile(arrival, Player.Width, Player.Height));
                    Enter(next);
                    events |= ExpeditionEvents.RoomChanged;
                    break;
                case RoomOutcome.Down:
                    Status = ExpeditionStatus.Down;
                    Deaths++;
                    break;
                case RoomOutcome.Won:
                    Status = ExpeditionStatus.Won;
                    break;
            }

            _events |= events;
            return true;
        }

        /// <summary>
        /// 体力が 0 になった後、最後のセーブの時点の世界の状態に戻し、その像の前から体力満タンで再開する。
        /// セーブの後に取った能力・アイテム・壊した壁は失われる。倒れていなければ何もせず false。
        /// </summary>
        public bool Continue()
        {
            if (Status != ExpeditionStatus.Down) { return false; }
            State = Save.State.Clone();
            Player = new Player(_config, Room.PlaceOnTile(Save.Cell, Player.Width, Player.Height));
            Status = ExpeditionStatus.Playing;
            Enter(Save.Room);
            _events |= ExpeditionEvents.Continued;
            return true;
        }

        /// <summary>前回取り出してから起きた出来事。</summary>
        public ExpeditionEvents TakeEvents()
        {
            var events = _events;
            _events = ExpeditionEvents.None;
            return events;
        }

        /// <summary>以後の動きを決める状態をすべて含むハッシュ。</summary>
        public uint Hash()
        {
            var hash = StateHash.Start();
            hash.Mix((int)Status);
            hash.Mix(Deaths);
            hash.Mix(ElapsedTicks);
            hash.Mix(RoomEntries);
            hash.Mix(RoomTicks);
            hash.Mix(Save.Room);
            hash.Mix(Save.Cell.Column);
            hash.Mix(Save.Cell.Row);
            Save.State.MixInto(ref hash);
            State.MixInto(ref hash);
            Player.MixInto(ref hash);
            Run.MixInto(ref hash);
            return hash.Value;
        }

        private void Enter(int room)
        {
            State.Visit(room);
            Run = new RoomRun(_config, World, State, room, Player);
            RoomEntries++;
            RoomTicks = 0;
        }
    }
}
