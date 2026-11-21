using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>
    /// 各部屋を死亡せずに抜ける入力の列を探す道具（<see cref="Routes"/> の作り直し用）。普段のテストでは実行しない。
    /// 4 Tick を 1 手とし、22 通りの手（左右 × ジャンプ、8 方向のダッシュ × ジャンプ）から、
    /// 出口までの距離とダッシュ・手の切り替えの多さを費用にした重み付き A* で探す。
    /// 実行：<c>dotnet test games/022-precision-platformer/dotnet/App.Tests --filter Name=FindRoutes --logger "console;verbosity=detailed"</c>
    /// </summary>
    public sealed class RouteFinder
    {
        private const int Macro = 4;
        private const int MaxExpanded = 3_000_000;
        private const int DashCost = 24;
        private const int SwitchCost = 3;

        private static readonly Move[] _moves = BuildMoves();

        [Test]
        [Explicit("ルートを作り直すときだけ実行する")]
        public void FindRoutes()
        {
            var rooms = Rooms.All();
            var config = new PrecisionConfig();
            for (var i = 0; i < rooms.Count; i++)
            {
                var route = Find(config, rooms[i], out var expanded);
                TestContext.Out.WriteLine(route == null
                    ? $"// {i + 1} {rooms[i].Name}：見つからない（{expanded} 手を展開）"
                    : $"\"{route}\", // {i + 1} {rooms[i].Name}（{expanded} 手を展開）");
            }
        }

        private static string? Find(PrecisionConfig config, Room room, out int expanded)
        {
            var exit = ExitCenter(room);
            var open = new MinHeap();
            var seen = new HashSet<(int, int, int, int, int, bool, bool, bool)>();
            open.Push(new Node(new RoomRun(config, room), null, -1, 0), 0);
            expanded = 0;
            while (open.Count > 0 && expanded < MaxExpanded)
            {
                var node = open.Pop();
                expanded++;
                for (var m = 0; m < _moves.Length; m++)
                {
                    var move = _moves[m];
                    var run = node.Run.Clone();
                    for (var t = 0; t < Macro && run.Status == RunStatus.Playing; t++) { run.Tick(move.Input(t)); }
                    if (run.Status == RunStatus.Dying) { continue; }
                    var cost = node.Cost + Macro + (move.Dash ? DashCost : 0) + (node.Move >= 0 && node.Move != m ? SwitchCost : 0);
                    var child = new Node(run, node, m, cost);
                    if (run.IsCleared) { return Replay(config, room, child); }
                    var p = run.Player;
                    var key = (p.Body.X >> 5, p.Body.Y >> 5, p.VelocityX / 6, p.VelocityY / 8, p.DashesLeft, p.IsDashing, move.Jump, p.Grounded);
                    if (!seen.Add(key)) { continue; }
                    var dx = exit.X - p.Body.CenterX;
                    var dy = exit.Y - (p.Body.Y + (Player.Height / 2));
                    open.Push(child, cost + (4 * Math.Sqrt(((double)dx * dx) + ((double)dy * dy)) / 40));
                }
            }

            return null;
        }

        /// <summary>見つかった手を Tick ごとの入力に展開し、出口に着いたところまでで切る。</summary>
        private static string Replay(PrecisionConfig config, Room room, Node last)
        {
            var moves = new List<int>();
            for (var node = last; node.Parent != null; node = node.Parent) { moves.Add(node.Move); }
            moves.Reverse();
            var run = new RoomRun(config, room);
            var inputs = new List<PlayerInput>();
            foreach (var m in moves)
            {
                for (var t = 0; t < Macro && !run.IsCleared; t++)
                {
                    var input = _moves[m].Input(t);
                    run.Tick(input);
                    inputs.Add(input);
                }
            }

            Assert.That(run.IsCleared && run.Deaths == 0, Is.True, "再生しても死亡せずに抜ける");
            return RouteScript.Format(inputs);
        }

        private static (int X, int Y) ExitCenter(Room room)
        {
            for (var column = 0; column < room.Map.Width; column++)
            {
                for (var row = 0; row < room.Map.Height; row++)
                {
                    if (room.Map[column, row] == TileKind.Exit) { return ((column * Units.Tile) + (Units.Tile / 2), (row * Units.Tile) + (Units.Tile / 2)); }
                }
            }

            throw new InvalidOperationException("出口がありません。");
        }

        private static Move[] BuildMoves()
        {
            var moves = new List<Move>();
            for (var h = -1; h <= 1; h++)
            {
                moves.Add(new Move(h, 0, false, false));
                moves.Add(new Move(h, 0, true, false));
            }

            for (var h = -1; h <= 1; h++)
            {
                for (var v = -1; v <= 1; v++)
                {
                    if (h == 0 && v == 0) { continue; }
                    moves.Add(new Move(h, v, false, true));
                    moves.Add(new Move(h, v, true, true));
                }
            }

            return moves.ToArray();
        }

        /// <summary>1 手（4 Tick）。ダッシュは最初の Tick だけ押す。</summary>
        private readonly struct Move
        {
            public Move(int horizontal, int vertical, bool jump, bool dash)
            {
                Horizontal = horizontal;
                Vertical = vertical;
                Jump = jump;
                Dash = dash;
            }

            public int Horizontal { get; }

            public int Vertical { get; }

            public bool Jump { get; }

            public bool Dash { get; }

            public PlayerInput Input(int tick) => new(Horizontal, Vertical, Jump, Dash && tick == 0);
        }

        private sealed class Node
        {
            public Node(RoomRun run, Node? parent, int move, int cost)
            {
                Run = run;
                Parent = parent;
                Move = move;
                Cost = cost;
            }

            public RoomRun Run { get; }

            public Node? Parent { get; }

            public int Move { get; }

            public int Cost { get; }
        }

        /// <summary>優先度の小さい順に取り出す二分ヒープ（Unity の .NET でも動くよう自前で持つ）。</summary>
        private sealed class MinHeap
        {
            private readonly List<(Node Node, double Priority)> _items = new();

            public int Count => _items.Count;

            public void Push(Node node, double priority)
            {
                _items.Add((node, priority));
                var i = _items.Count - 1;
                while (i > 0)
                {
                    var parent = (i - 1) / 2;
                    if (_items[parent].Priority <= _items[i].Priority) { break; }
                    (_items[parent], _items[i]) = (_items[i], _items[parent]);
                    i = parent;
                }
            }

            public Node Pop()
            {
                var top = _items[0].Node;
                var last = _items.Count - 1;
                _items[0] = _items[last];
                _items.RemoveAt(last);
                var i = 0;
                while (true)
                {
                    var left = (2 * i) + 1;
                    var right = left + 1;
                    var smallest = i;
                    if (left < _items.Count && _items[left].Priority < _items[smallest].Priority) { smallest = left; }
                    if (right < _items.Count && _items[right].Priority < _items[smallest].Priority) { smallest = right; }
                    if (smallest == i) { break; }
                    (_items[smallest], _items[i]) = (_items[i], _items[smallest]);
                    i = smallest;
                }

                return top;
            }
        }
    }
}
