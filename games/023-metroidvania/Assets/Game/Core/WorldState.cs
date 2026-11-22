using System;
using TileMotion;

namespace Game.Core
{
    /// <summary>能力。取ると以後ずっと使える。</summary>
    [Flags]
    public enum Ability
    {
        None = 0,
        DoubleJump = 1 << 0,
        BreakShot = 1 << 1,
    }

    /// <summary>
    /// 部屋をまたいで保持する状態：取得した能力・壊したひびの壁・取ったアイテム・訪れた部屋。
    /// 部屋を出入りしても変わらず、セーブ像に触れるとこの複製が記録され、体力が 0 になるとその複製に戻る。
    /// 敵・弾・ボスの体力は部屋ごとの状態で、ここには持たない（部屋に入るたびに初めから）。
    /// </summary>
    public sealed class WorldState
    {
        private readonly bool[][] _broken;
        private readonly bool[] _taken;
        private readonly bool[] _visited;

        public WorldState(World world)
        {
            if (world == null) { throw new ArgumentNullException(nameof(world)); }
            _broken = new bool[world.Rooms.Count][];
            for (var i = 0; i < _broken.Length; i++)
            {
                var map = world.Rooms[i].Map;
                _broken[i] = new bool[map.Width * map.Height];
            }

            _taken = new bool[world.PickupCount];
            _visited = new bool[world.Rooms.Count];
        }

        private WorldState(WorldState source)
        {
            Abilities = source.Abilities;
            _broken = new bool[source._broken.Length][];
            for (var i = 0; i < _broken.Length; i++) { _broken[i] = (bool[])source._broken[i].Clone(); }
            _taken = (bool[])source._taken.Clone();
            _visited = (bool[])source._visited.Clone();
        }

        public Ability Abilities { get; private set; }

        public bool Has(Ability ability) => ability != Ability.None && (Abilities & ability) == ability;

        public void Grant(Ability ability)
        {
            if (ability == Ability.None || (ability & ~(Ability.DoubleJump | Ability.BreakShot)) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ability), ability, "未定義の能力です。");
            }

            Abilities |= ability;
        }

        public bool IsTaken(int pickupId) => _taken[pickupId];

        /// <summary>アイテムを取ったことにする。既に取っていれば false（二重には取れない）。</summary>
        public bool Take(int pickupId)
        {
            if (_taken[pickupId]) { return false; }
            _taken[pickupId] = true;
            return true;
        }

        /// <summary>取ったアイテムの数。</summary>
        public int TakenCount
        {
            get
            {
                var count = 0;
                foreach (var taken in _taken) { if (taken) { count++; } }
                return count;
            }
        }

        public bool IsVisited(int room) => _visited[room];

        public void Visit(int room) => _visited[room] = true;

        /// <summary>部屋 <paramref name="room"/> の壊れた壁の配列（地形と共有し、壊すとここが変わる）。</summary>
        internal bool[] BrokenIn(int room) => _broken[room];

        public bool IsBroken(int room, int column, int row, TileMap map)
        {
            if (map == null) { throw new ArgumentNullException(nameof(map)); }
            return map.Contains(column, row) && _broken[room][map.Index(column, row)];
        }

        /// <summary>ひびの壁を壊れたことにする（テスト・撮影で場面を作る用）。ひびの壁でなければ例外。</summary>
        public void Break(int room, int column, int row, TileMap map)
        {
            if (map == null) { throw new ArgumentNullException(nameof(map)); }
            if (map[column, row] != TileKind.Cracked || !map.Contains(column, row))
            {
                throw new ArgumentException($"({column}, {row}) はひびの壁ではありません。");
            }

            _broken[room][map.Index(column, row)] = true;
        }

        /// <summary>壊れたひびの壁の数（全部屋）。</summary>
        public int BrokenCount
        {
            get
            {
                var count = 0;
                foreach (var room in _broken)
                {
                    foreach (var broken in room) { if (broken) { count++; } }
                }

                return count;
            }
        }

        public WorldState Clone() => new(this);

        public void MixInto(ref StateHash hash)
        {
            hash.Mix((int)Abilities);
            foreach (var room in _broken)
            {
                for (var i = 0; i < room.Length; i++)
                {
                    if (room[i]) { hash.Mix(i); }
                }

                hash.Mix(-1);
            }

            foreach (var taken in _taken) { hash.Mix(taken); }
            foreach (var visited in _visited) { hash.Mix(visited); }
        }
    }
}
