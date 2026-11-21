using System.Collections.Generic;

namespace Game.Core.Tests
{
    /// <summary>テスト用の面と操作の補助。</summary>
    internal static class TestKit
    {
        public const int T = Units.Tile;
        public const int P = Units.Pixel;

        public static readonly PlayerInput Idle = PlayerInput.None;
        public static readonly PlayerInput Right = new(1, false, false);
        public static readonly PlayerInput Left = new(-1, false, false);
        public static readonly PlayerInput RunRight = new(1, true, false);
        public static readonly PlayerInput Jump = new(0, false, true);
        public static readonly PlayerInput RightJump = new(1, false, true);

        public static Level Map(params string[] rows) => Level.Parse("test", rows);

        /// <summary>幅 40 の平らな面。床は最下段、自機は列 2、ゴールは右上の隅（届かない）。</summary>
        public static Level Flat(int width = 40)
        {
            var rows = new List<string>();
            rows.Add(new string('.', width - 1) + "G");
            for (var i = 0; i < 6; i++)
            {
                rows.Add(new string('.', width));
            }

            rows.Add("..S" + new string('.', width - 3));
            rows.Add(new string('#', width));
            return Map(rows.ToArray());
        }

        public static LevelRun Run(Level level, PlatformerConfig? config = null) => new(config ?? new PlatformerConfig(), level);

        /// <summary>同じ入力のまま <paramref name="ticks"/> Tick 進める。</summary>
        public static void Hold(LevelRun run, PlayerInput input, int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                run.Tick(input);
            }
        }

        /// <summary>同じ入力のまま <paramref name="ticks"/> Tick 進める。</summary>
        public static void Hold(Player player, TileMap map, PlayerInput input, int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                player.Step(map, input);
            }
        }

        /// <summary>床に立った自機（1 Tick 何もしない入力で接地させ、ジャンプキーを離した状態にする）。</summary>
        public static Player StandingPlayer(Level level, PlatformerConfig? config = null)
        {
            var player = new Player(config ?? new PlatformerConfig(), Level.PlaceOnTile(level.StartColumn, level.StartRow, Player.Width, Player.Height));
            player.Step(level.Map, PlayerInput.None);
            player.Step(level.Map, PlayerInput.None);
            return player;
        }

        /// <summary>ジャンプキーを押し続け、到達した最高の高さ（足元）を返す。<paramref name="holdTicks"/> Tick で離す。</summary>
        public static int Apex(Level level, int holdTicks, PlatformerConfig? config = null)
        {
            var player = StandingPlayer(level, config);
            var start = player.Body.Bottom;
            var best = start;
            for (var i = 0; i < 200; i++)
            {
                player.Step(level.Map, new PlayerInput(0, false, i < holdTicks));
                best = System.Math.Max(best, player.Body.Bottom);
            }

            return best - start;
        }
    }
}
