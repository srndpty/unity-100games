using System;
using TileMotion;

namespace Game.Core.Tests
{
    /// <summary>テスト用のダンジョンと操作の補助。</summary>
    internal static class TestKit
    {
        public const int T = Units.Tile;
        public const int P = Units.Pixel;

        public static readonly HeroInput Idle = HeroInput.None;
        public static readonly HeroInput Right = new(1, 0, false, false);
        public static readonly HeroInput Left = new(-1, 0, false, false);
        public static readonly HeroInput Up = new(0, 1, false, false);
        public static readonly HeroInput Down = new(0, -1, false, false);
        public static readonly HeroInput Attack = new(0, 0, true, false);
        public static readonly HeroInput Roll = new(0, 0, false, true);

        public static HeroInput In(int x = 0, int y = 0, bool attack = false, bool roll = false) => new(x, y, attack, roll);

        public static VaultConfig Config() => new();

        /// <summary>行の数・幅から大きさを決めて部屋を解析する。</summary>
        public static Room Map(int gridX, int gridY, params string[] rows) => Room.Parse($"test{gridX}{gridY}", gridX, gridY, rows, rows[0].Length, rows.Length);

        public static World Worlds(params Room[] rooms) => new(rooms);

        /// <summary>本番の 6 部屋。</summary>
        public static World Real() => Rooms.Create();

        /// <summary>状態を指定して部屋 <paramref name="room"/> のマス <paramref name="cell"/> から始める。</summary>
        public static Dungeon At(World world, int room, Cell cell, VaultConfig? config = null, Action<DungeonState>? setup = null, int seed = 1)
        {
            var state = new DungeonState(world);
            setup?.Invoke(state);
            return Dungeon.StartAt(config ?? Config(), state, room, cell, seed);
        }

        /// <summary>同じ入力のまま進め、その間の出来事をまとめて返す。</summary>
        public static DungeonEvents Hold(Dungeon dungeon, HeroInput input, int ticks)
        {
            var events = DungeonEvents.None;
            for (var i = 0; i < ticks; i++)
            {
                dungeon.Tick(input);
                events |= dungeon.TakeEvents();
            }

            return events;
        }

        /// <summary>条件が満たされるまで同じ入力で進める（上限まで）。満たされたら true。</summary>
        public static bool Until(Dungeon dungeon, HeroInput input, Func<Dungeon, bool> done, int limit = 2000)
        {
            for (var i = 0; i < limit; i++)
            {
                if (done(dungeon)) { return true; }
                dungeon.Tick(input);
            }

            return done(dungeon);
        }

        /// <summary>キーを一度離してから押し始め、<paramref name="ticks"/> Tick 押し続ける。</summary>
        public static DungeonEvents Press(Dungeon dungeon, HeroInput input, int ticks = 1)
        {
            var events = Hold(dungeon, new HeroInput(input.X, input.Y, false, false), 1);
            return events | Hold(dungeon, input, ticks);
        }

        /// <summary>床だけの地形（自機の単体テスト用）。</summary>
        public static Terrain Open(int width = 12, int height = 8)
        {
            var tiles = new TileKind[width * height];
            return new Terrain(new TileMap(width, height, tiles), new bool[4]);
        }

        /// <summary>開いた 2 部屋（左 A に開始位置、右 B）。B の中身は <paramref name="b"/> の行で指定する（左端は出入口）。</summary>
        public static World Pair(string[] a, string[] b) => Worlds(Map(0, 0, a), Map(1, 0, b));
    }
}
