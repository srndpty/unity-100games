using System;
using TileMotion;

namespace Game.Core
{
    /// <summary>鍵のかかった扉を開けようとした結果。</summary>
    public enum DoorResult
    {
        /// <summary>この操作で開いた（小扉なら小鍵を 1 つ使った）。</summary>
        Opened,

        /// <summary>もう開いている（鍵は使わない）。</summary>
        AlreadyOpen,

        /// <summary>合う鍵を持っていない（何も変わらない）。</summary>
        NoKey,
    }

    /// <summary>
    /// 部屋をまたいで保持する進行フラグ：小鍵の個数・ボス鍵・開いた扉・開けた宝箱・全滅の小鍵（出た・取った）・訪れた部屋・ボス撃破。
    /// 部屋を出入りしても、体力が 0 になって入口から再開しても変わらない。敵・弾は部屋ごとの状態で、ここには持たない。
    /// 二重に起きてはいけない操作（鍵の消費・宝箱の中身・全滅の小鍵）は、2 回目に false などを返して状態を変えない。
    /// </summary>
    public sealed class DungeonState
    {
        private readonly World _world;
        private readonly bool[] _passages;
        private readonly bool[] _chests;
        private readonly bool[] _rewardShown;
        private readonly bool[] _rewardTaken;
        private readonly bool[] _visited;

        public DungeonState(World world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _passages = new bool[world.PassageCount];
            for (var room = 0; room < world.Rooms.Count; room++)
            {
                foreach (Side side in Enum.GetValues(typeof(Side)))
                {
                    if (world.Rooms[room].Door(side) == DoorKind.Open) { _passages[world.PassageId(room, side)] = true; }
                }
            }

            _chests = new bool[world.ChestCount];
            _rewardShown = new bool[world.Rooms.Count];
            _rewardTaken = new bool[world.Rooms.Count];
            _visited = new bool[world.Rooms.Count];
        }

        private DungeonState(DungeonState source)
        {
            _world = source._world;
            _passages = (bool[])source._passages.Clone();
            _chests = (bool[])source._chests.Clone();
            _rewardShown = (bool[])source._rewardShown.Clone();
            _rewardTaken = (bool[])source._rewardTaken.Clone();
            _visited = (bool[])source._visited.Clone();
            SmallKeys = source.SmallKeys;
            HasBossKey = source.HasBossKey;
            BossDefeated = source.BossDefeated;
        }

        public World World => _world;

        /// <summary>持っている小鍵の数。小扉を 1 枚開けるごとに 1 減る。</summary>
        public int SmallKeys { get; private set; }

        /// <summary>ボス鍵。ボス扉を開けても無くならない。</summary>
        public bool HasBossKey { get; private set; }

        public bool BossDefeated { get; private set; }

        public DungeonState Clone() => new(this);

        /// <summary>部屋 <paramref name="room"/> の <paramref name="side"/> の出入口が通れるか（出入口がなければ false）。</summary>
        public bool IsOpen(int room, Side side)
        {
            var id = _world.PassageId(room, side);
            return id >= 0 && _passages[id];
        }

        /// <summary>部屋の辺ごとの開閉（<see cref="Terrain"/> に渡す）。</summary>
        public bool[] OpenSides(int room)
        {
            var open = new bool[4];
            foreach (Side side in Enum.GetValues(typeof(Side))) { open[(int)side] = IsOpen(room, side); }
            return open;
        }

        /// <summary>
        /// 部屋 <paramref name="room"/> の <paramref name="side"/> の扉を開ける。小扉は小鍵を 1 つ使い、ボス扉はボス鍵を持っていれば開く
        /// （ボス鍵は無くならない）。小鍵でボス扉は開かず、ボス鍵で小扉は開かない。
        /// </summary>
        public DoorResult TryOpen(int room, Side side)
        {
            var id = _world.PassageId(room, side);
            if (id < 0) { throw new ArgumentException($"{_world.Rooms[room].Name} の {side} に出入口はありません。", nameof(side)); }
            var kind = _world.Rooms[room].Door(side);
            if (kind != DoorKind.Small && kind != DoorKind.Boss) { throw new InvalidOperationException($"{side} の出入口は鍵の扉ではありません。"); }
            if (_passages[id]) { return DoorResult.AlreadyOpen; }
            if (kind == DoorKind.Small)
            {
                if (SmallKeys <= 0) { return DoorResult.NoKey; }
                SmallKeys--;
            }
            else if (!HasBossKey)
            {
                return DoorResult.NoKey;
            }

            _passages[id] = true;
            return DoorResult.Opened;
        }

        public bool IsChestOpen(int room, int index) => _chests[_world.ChestId(room, index)];

        /// <summary>宝箱を開けて中身を得る。既に開いていれば false（中身は二度と出ない）。</summary>
        public bool TryOpenChest(int room, int index)
        {
            var id = _world.ChestId(room, index);
            if (_chests[id]) { return false; }
            _chests[id] = true;
            switch (_world.Rooms[room].Chests[index].Content)
            {
                case ChestContent.SmallKey:
                    SmallKeys++;
                    break;
                case ChestContent.BossKey:
                    HasBossKey = true;
                    break;
                default:
                    throw new InvalidOperationException("未定義の中身です。");
            }

            return true;
        }

        /// <summary>全滅の小鍵が出ているか（取った後も true のまま）。</summary>
        public bool IsRewardShown(int room) => _rewardShown[room];

        public bool IsRewardTaken(int room) => _rewardTaken[room];

        /// <summary>全滅の小鍵を出す。出す場所がない部屋か、既に出ていれば false。</summary>
        public bool ShowReward(int room)
        {
            if (!_world.Rooms[room].Reward.HasValue || _rewardShown[room]) { return false; }
            _rewardShown[room] = true;
            return true;
        }

        /// <summary>出ている全滅の小鍵を取る。出ていないか既に取っていれば false。</summary>
        public bool TryTakeReward(int room)
        {
            if (!_rewardShown[room] || _rewardTaken[room]) { return false; }
            _rewardTaken[room] = true;
            SmallKeys++;
            return true;
        }

        /// <summary>ボスを倒した。既に倒していれば false。</summary>
        public bool DefeatBoss()
        {
            if (BossDefeated) { return false; }
            BossDefeated = true;
            return true;
        }

        public bool IsVisited(int room) => _visited[room];

        public void Visit(int room) => _visited[room] = true;

        public void MixInto(ref StateHash hash)
        {
            hash.Mix(SmallKeys);
            hash.Mix(HasBossKey);
            hash.Mix(BossDefeated);
            foreach (var value in _passages) { hash.Mix(value); }
            foreach (var value in _chests) { hash.Mix(value); }
            foreach (var value in _rewardShown) { hash.Mix(value); }
            foreach (var value in _rewardTaken) { hash.Mix(value); }
            foreach (var value in _visited) { hash.Mix(value); }
        }
    }
}
