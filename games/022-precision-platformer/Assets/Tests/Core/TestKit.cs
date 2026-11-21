using System.Collections.Generic;

namespace Game.Core.Tests
{
    /// <summary>テスト用の部屋と操作の補助。</summary>
    internal static class TestKit
    {
        public const int T = Units.Tile;
        public const int P = Units.Pixel;

        public static readonly PlayerInput Idle = PlayerInput.None;
        public static readonly PlayerInput Right = new(1, 0, false, false);
        public static readonly PlayerInput Left = new(-1, 0, false, false);
        public static readonly PlayerInput Jump = new(0, 0, true, false);
        public static readonly PlayerInput Dash = new(0, 0, false, true);

        public static PlayerInput In(int horizontal = 0, int vertical = 0, bool jump = false, bool dash = false) => new(horizontal, vertical, jump, dash);

        /// <summary>行の数・幅から大きさを決めて部屋を解析する。</summary>
        public static Room Map(params string[] rows) => Room.Parse("test", rows, rows[0].Length, rows.Length);

        /// <summary>
        /// 幅 40・高さ 12 の部屋。床は最下段で、列 0〜<paramref name="ledge"/> だけにある（その先は下の外まで落ちる）。
        /// 自機は列 2、出口は右上の隅（届かない）。
        /// </summary>
        public static Room Ledge(int ledge = 39)
        {
            var rows = new List<string> { new string('.', 39) + "E" };
            for (var i = 0; i < 9; i++) { rows.Add(new string('.', 40)); }
            rows.Add("..S" + new string('.', 37));
            rows.Add(new string('#', ledge + 1) + new string('.', 39 - ledge));
            return Map(rows.ToArray());
        }

        public static TileMap Open => Ledge().Map;

        /// <summary>何もない空間（Ledge の上のほう）の (x, y) に浮かせた自機。ジャンプ・ダッシュのキーは離した状態にする。</summary>
        public static Player Floating(int x, int y, PrecisionConfig? config = null)
        {
            var player = new Player(config ?? new PrecisionConfig(), new Box(x, y, Player.Width, Player.Height));
            player.Step(Open, Idle);
            return player;
        }

        /// <summary>床に立った自機（2 Tick 何もしない入力で接地させ、キーを離した状態にする）。</summary>
        public static Player Standing(Room room, PrecisionConfig? config = null)
        {
            var player = new Player(config ?? new PrecisionConfig(), room.StartBox);
            player.Step(room.Map, Idle);
            player.Step(room.Map, Idle);
            return player;
        }

        public static RoomRun Run(Room room, PrecisionConfig? config = null) => new(config ?? new PrecisionConfig(), room);

        /// <summary>同じ入力のまま <paramref name="ticks"/> Tick 進め、その間の行動をまとめて返す。</summary>
        public static PlayerActions Hold(Player player, TileMap map, PlayerInput input, int ticks)
        {
            var actions = PlayerActions.None;
            for (var i = 0; i < ticks; i++)
            {
                actions |= player.Step(map, input);
            }

            return actions;
        }

        /// <summary>同じ入力のまま <paramref name="ticks"/> Tick 進める。</summary>
        public static void Hold(RoomRun run, PlayerInput input, int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                run.Tick(input);
            }
        }
    }
}
