using System.Collections.Generic;
using TileMotion;

namespace Game.Core.Tests
{
    /// <summary>テスト用の世界と操作の補助。</summary>
    internal static class TestKit
    {
        public const int T = Units.Tile;
        public const int P = Units.Pixel;

        public static readonly PlayerInput Idle = PlayerInput.None;
        public static readonly PlayerInput Right = new(1, false, false);
        public static readonly PlayerInput Left = new(-1, false, false);
        public static readonly PlayerInput Jump = new(0, true, false);
        public static readonly PlayerInput Shoot = new(0, false, true);

        public static PlayerInput In(int horizontal = 0, bool jump = false, bool shoot = false) => new(horizontal, jump, shoot);

        public static ExplorerConfig Config() => new();

        /// <summary>本番の 5 部屋。</summary>
        public static World Real() => Rooms.Create();

        /// <summary>行の数・幅から大きさを決めて部屋を解析する。</summary>
        public static Room Map(int gridX, int gridY, params string[] rows) => Room.Parse($"test{gridX}{gridY}", gridX, gridY, rows, rows[0].Length, rows.Length);

        public static World Worlds(params Room[] rooms) => new(rooms);

        /// <summary>試合を世界の状態と位置を指定して始める。</summary>
        public static Expedition At(World world, int room, Cell cell, Ability abilities = Ability.None, ExplorerConfig? config = null)
        {
            var state = new WorldState(world);
            if ((abilities & Ability.DoubleJump) != 0) { state.Grant(Ability.DoubleJump); }
            if ((abilities & Ability.BreakShot) != 0) { state.Grant(Ability.BreakShot); }
            return Expedition.StartAt(config ?? Config(), world, state, room, cell);
        }

        /// <summary>同じ入力のまま進め、その間の出来事をまとめて返す。</summary>
        public static ExpeditionEvents Hold(Expedition expedition, PlayerInput input, int ticks)
        {
            var events = ExpeditionEvents.None;
            for (var i = 0; i < ticks; i++)
            {
                expedition.Tick(input);
                events |= expedition.TakeEvents();
            }

            return events;
        }

        /// <summary>条件が満たされるまで同じ入力で進める（上限まで）。満たされたら true。</summary>
        public static bool Until(Expedition expedition, PlayerInput input, System.Func<Expedition, bool> done, int limit = 2000)
        {
            for (var i = 0; i < limit; i++)
            {
                if (done(expedition)) { return true; }
                expedition.Tick(input);
            }

            return done(expedition);
        }

        /// <summary>キーを一度離してから、ジャンプを押し続けて <paramref name="ticks"/> Tick 進める。</summary>
        public static void JumpFor(Expedition expedition, int horizontal, int ticks)
        {
            expedition.Tick(In(horizontal));
            for (var i = 0; i < ticks; i++) { expedition.Tick(In(horizontal, jump: true)); }
        }

        /// <summary>本番の世界で、ある種類の最初のアイテムの (部屋, 部屋の中の番号)。</summary>
        public static (int Room, int Index) FindPickup(World world, PickupKind kind)
        {
            for (var room = 0; room < world.Rooms.Count; room++)
            {
                var pickups = world.Rooms[room].Pickups;
                for (var i = 0; i < pickups.Count; i++)
                {
                    if (pickups[i].Kind == kind) { return (room, i); }
                }
            }

            throw new KeyNotFoundException(kind.ToString());
        }
    }
}
