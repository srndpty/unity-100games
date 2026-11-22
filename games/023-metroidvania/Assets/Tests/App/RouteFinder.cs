using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;
using TileMotion;

namespace Game.App.Tests
{
    /// <summary>
    /// 全編を進む入力の列を探す道具（<see cref="Routes"/> の作り直し用）。普段のテストでは実行しない。
    /// 部屋の滞在ごとに小目標の列（玉を取る・像に触れる・扉を抜ける）を順に探し、つないで 1 回の滞在の列にする。
    /// 4 Tick を 1 手とし、12 通りの手（左右 × ジャンプ × ショット。ショットは手の最初の Tick だけ押す）から、
    /// 目標までの距離と被弾・手の切り替えの多さを費用にした重み付き A* で探す。ボスの部屋は <see cref="BossPilot"/> に任せる。
    /// 実行：<c>dotnet test games/023-metroidvania/dotnet/App.Tests --filter Name=FindRoutes --logger "console;verbosity=detailed"</c>
    /// </summary>
    public sealed class RouteFinder
    {
        private const int Macro = 4;
        private const int MaxExpanded = 400_000;
        private const int HurtCost = 2000;
        private const int SwitchCost = 2;
        private const int Grid = Units.Tile / 4;

        private static readonly Move[] _moves = BuildMoves();

        /// <summary>滞在ごとの小目標（判定と、距離を測る目標の点）。</summary>
        private static readonly Goal[][] _plan =
        {
            new[] { new Goal("回廊へ", e => e.RoomIndex == Rooms.Corridor, 39, 2) },
            new[] { new Goal("洞穴へ", e => e.RoomIndex == Rooms.Cave, 39, 7) },
            new[]
            {
                new Goal("二段ジャンプを取る", e => e.State.Has(Ability.DoubleJump), 30, 2),
                new Goal("回廊へ戻る", e => e.RoomIndex == Rooms.Corridor, 0, 8),
            },
            new[] { new Goal("拠点へ戻る", e => e.RoomIndex == Rooms.Hub, 0, 2) },
            new[] { new Goal("塔へ", e => e.RoomIndex == Rooms.Tower, 31, 21) },
            new[]
            {
                new Goal("破壊ショットを取る", e => e.State.Has(Ability.BreakShot), 11, 7),
                new Goal("破壊ショットを持ってセーブ像に触れる", e => e.Save.Room == Rooms.Tower && e.Save.State.Has(Ability.BreakShot), 20, 2),
                new Goal("ボスの部屋へ", e => e.RoomIndex == Rooms.Lair, 39, 2),
            },
        };

        [Test]
        [Explicit("ルートを作り直すときだけ実行する")]
        public void FindRoutes()
        {
            var config = new ExplorerConfig();
            var expedition = new Expedition(config, Rooms.Create());
            for (var visit = 0; visit < _plan.Length; visit++)
            {
                var inputs = new List<PlayerInput>();
                var room = expedition.RoomIndex;
                foreach (var goal in _plan[visit])
                {
                    var found = Find(expedition, goal, room, out var expanded);
                    if (found == null)
                    {
                        TestContext.Out.WriteLine($"// {visit}: {goal.Name} が見つからない（{expanded} 手を展開）");
                        Assert.Fail($"{goal.Name} が見つからない");
                        return;
                    }

                    foreach (var input in found)
                    {
                        expedition.Tick(input);
                        inputs.Add(input);
                    }

                    Assert.That(goal.Done(expedition), Is.True, $"{goal.Name}：再生しても届く");
                    TestContext.Out.WriteLine($"// {visit}: {goal.Name}（{expanded} 手を展開、体力 {expedition.Player.Health}）");
                }

                TestContext.Out.WriteLine($"\"{RouteScript.Format(inputs)}\",");
            }
        }

        private static List<PlayerInput>? Find(Expedition start, Goal goal, int room, out int expanded)
        {
            var open = new MinHeap();
            var seen = new HashSet<(int, int, int, int, uint)>();
            open.Push(new Node(start.Clone(), null, -1, 0), 0);
            expanded = 0;
            var target = ((goal.Column * Units.Tile) + (Units.Tile / 2), goal.Row * Units.Tile);
            while (open.Count > 0 && expanded < MaxExpanded)
            {
                var node = open.Pop();
                expanded++;
                for (var m = 0; m < _moves.Length; m++)
                {
                    var e = node.State.Clone();
                    var health = e.Player.Health;
                    var reached = false;
                    for (var t = 0; t < Macro; t++)
                    {
                        e.Tick(_moves[m].Input(t));
                        if (goal.Done(e)) { reached = true; break; }
                        if (e.Status != ExpeditionStatus.Playing || e.RoomIndex != room) { break; }
                    }

                    if (e.Status != ExpeditionStatus.Playing) { continue; }
                    var cost = node.Cost + Macro + ((health - e.Player.Health) * HurtCost) + (node.Move >= 0 && node.Move != m ? SwitchCost : 0);
                    var child = new Node(e, node, m, cost);
                    if (reached) { return Replay(start, child, goal); }
                    if (e.RoomIndex != room || !seen.Add(Key(e))) { continue; }
                    var dx = (double)target.Item1 - e.Player.Body.CenterX;
                    var dy = (double)target.Item2 - e.Player.Body.Bottom;
                    open.Push(child, cost + (4 * Math.Sqrt((dx * dx) + (dy * dy)) / 40));
                }
            }

            return null;
        }

        /// <summary>見つかった手を Tick ごとの入力に展開し、目標に届いたところで切る。</summary>
        private static List<PlayerInput> Replay(Expedition start, Node last, Goal goal)
        {
            var moves = new List<int>();
            for (var node = last; node.Parent != null; node = node.Parent) { moves.Add(node.Move); }
            moves.Reverse();
            var e = start.Clone();
            var inputs = new List<PlayerInput>();
            foreach (var m in moves)
            {
                for (var t = 0; t < Macro && !goal.Done(e); t++)
                {
                    var input = _moves[m].Input(t);
                    e.Tick(input);
                    inputs.Add(input);
                }
            }

            return inputs;
        }

        private static (int, int, int, int, uint) Key(Expedition e)
        {
            var p = e.Player;
            var hash = StateHash.Start();
            e.State.MixInto(ref hash);
            hash.Mix(p.Grounded);
            hash.Mix(p.AirJumpUsed);
            hash.Mix(p.JumpHeld);
            hash.Mix(p.Health);
            hash.Mix(p.Facing);
            hash.Mix(e.Save.Room);
            return (p.Body.X / Grid, p.Body.Y / Grid, p.VelocityX / 6, p.VelocityY / 8, hash.Value);
        }

        private static Move[] BuildMoves()
        {
            var moves = new List<Move>();
            for (var h = -1; h <= 1; h++)
            {
                moves.Add(new Move(h, false, false));
                moves.Add(new Move(h, true, false));
                moves.Add(new Move(h, false, true));
                moves.Add(new Move(h, true, true));
            }

            return moves.ToArray();
        }

        private sealed class Goal
        {
            public Goal(string name, Func<Expedition, bool> done, int column, int row)
            {
                Name = name;
                Done = done;
                Column = column;
                Row = row;
            }

            public string Name { get; }

            public Func<Expedition, bool> Done { get; }

            public int Column { get; }

            public int Row { get; }
        }

        /// <summary>1 手（4 Tick）。ショットは最初の Tick だけ押す。</summary>
        private readonly struct Move
        {
            public Move(int horizontal, bool jump, bool shoot)
            {
                Horizontal = horizontal;
                Jump = jump;
                Shoot = shoot;
            }

            public int Horizontal { get; }

            public bool Jump { get; }

            public bool Shoot { get; }

            public PlayerInput Input(int tick) => new(Horizontal, Jump, Shoot && tick == 0);
        }

        private sealed class Node
        {
            public Node(Expedition state, Node? parent, int move, int cost)
            {
                State = state;
                Parent = parent;
                Move = move;
                Cost = cost;
            }

            public Expedition State { get; }

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
