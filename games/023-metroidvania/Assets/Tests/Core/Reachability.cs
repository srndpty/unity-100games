using System;
using System.Collections.Generic;
using TileMotion;

namespace Game.Core.Tests
{
    /// <summary>
    /// 到達できる範囲の探索。<see cref="Macro"/> Tick を 1 手とし、手ごとに左右（3 通り）× ジャンプ（押す・離す）の 6 通りを試す。
    /// 状態はそのまま比べると位置と速さの組で数が爆発するので、位置を横 1/2・縦 1/4 タイル、速さを横 6・縦 8 単位にまとめ、
    /// 接地・二段ジャンプの使用・押下・能力・取ったアイテムはそのまま比べた鍵で 1 度だけ展開する
    /// （敵の位置、体力・無敵、接地猶予・入力バッファの残り Tick は鍵に含めない）。
    /// まとめた分だけ近似なので、「届かない」は届く高さに十分な差がある場面でだけ使う（段差 5 タイルに対し、1 回のジャンプは 3.3 タイル）。
    /// 目標の点が与えられれば近い順に（最良優先）、なければ全部を広げる。展開の上限に達したら <see cref="Capped"/> で、何も言わない。
    /// </summary>
    internal sealed class Reachability
    {
        public const int Macro = 4;
        public const int Grid = Units.Tile / 4;

        private static readonly PlayerInput[] _moves =
        {
            new(-1, false, false), new(0, false, false), new(1, false, false),
            new(-1, true, false), new(0, true, false), new(1, true, false),
        };

        private Reachability()
        {
        }

        public bool Found { get; private set; }

        public bool Capped { get; private set; }

        public int Expanded { get; private set; }

        /// <summary>探索の間に、開始した部屋で自機の足が届いた最も高い位置。</summary>
        public int HighestFeet { get; private set; } = int.MinValue;

        public static Reachability Run(Expedition start, Func<Expedition, bool> goal, (int X, int Y)? target = null, int cap = 200_000)
        {
            var result = new Reachability();
            var room = start.RoomIndex;
            var open = new PriorityQueue();
            var seen = new HashSet<(int, int, int, int, uint)>();
            open.Push(start.Clone(), 0);
            seen.Add(Key(start));
            while (open.Count > 0)
            {
                if (result.Expanded >= cap)
                {
                    result.Capped = true;
                    return result;
                }

                var node = open.Pop();
                result.Expanded++;
                foreach (var move in _moves)
                {
                    var next = node.Clone();
                    for (var t = 0; t < Macro && next.Status == ExpeditionStatus.Playing && next.RoomIndex == room; t++)
                    {
                        next.Tick(move);
                        if (next.RoomIndex == room) { result.HighestFeet = Math.Max(result.HighestFeet, next.Player.Body.Bottom); }
                    }

                    if (goal(next))
                    {
                        result.Found = true;
                        return result;
                    }

                    if (next.Status != ExpeditionStatus.Playing || next.RoomIndex != room || !seen.Add(Key(next))) { continue; }
                    var priority = 0.0;
                    if (target is (int x, int y))
                    {
                        var dx = (double)x - next.Player.Body.CenterX;
                        var dy = (double)y - next.Player.Body.Bottom;
                        priority = Math.Sqrt((dx * dx) + (dy * dy));
                    }

                    open.Push(next, priority);
                }
            }

            return result;
        }

        /// <summary>位置をまとめた自機と、世界の状態の鍵。</summary>
        private static (int, int, int, int, uint) Key(Expedition expedition)
        {
            var p = expedition.Player;
            var hash = StateHash.Start();
            expedition.State.MixInto(ref hash);
            hash.Mix(p.Grounded);
            hash.Mix(p.JumpHeld);
            hash.Mix(p.AirJumpUsed);
            return (p.Body.X / (2 * Grid), p.Body.Y / Grid, p.VelocityX / 6, p.VelocityY / 8, hash.Value);
        }

        /// <summary>優先度の小さい順（同じなら入れた順）に取り出す二分ヒープ。</summary>
        private sealed class PriorityQueue
        {
            private readonly List<(Expedition Item, double Priority, long Order)> _items = new();
            private long _order;

            public int Count => _items.Count;

            public void Push(Expedition item, double priority)
            {
                _items.Add((item, priority, _order++));
                var i = _items.Count - 1;
                while (i > 0)
                {
                    var parent = (i - 1) / 2;
                    if (!Less(_items[i], _items[parent])) { break; }
                    (_items[parent], _items[i]) = (_items[i], _items[parent]);
                    i = parent;
                }
            }

            public Expedition Pop()
            {
                var top = _items[0].Item;
                var last = _items.Count - 1;
                _items[0] = _items[last];
                _items.RemoveAt(last);
                var i = 0;
                while (true)
                {
                    var left = (2 * i) + 1;
                    var right = left + 1;
                    var smallest = i;
                    if (left < _items.Count && Less(_items[left], _items[smallest])) { smallest = left; }
                    if (right < _items.Count && Less(_items[right], _items[smallest])) { smallest = right; }
                    if (smallest == i) { break; }
                    (_items[smallest], _items[i]) = (_items[i], _items[smallest]);
                    i = smallest;
                }

                return top;
            }

            private static bool Less((Expedition Item, double Priority, long Order) a, (Expedition Item, double Priority, long Order) b)
            {
                return a.Priority < b.Priority || (a.Priority == b.Priority && a.Order < b.Order);
            }
        }
    }
}
