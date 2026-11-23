using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// 自機が取りうる横の状態と縦の状態を、本物の 1 Tick（<see cref="LaneState.Next"/>・<see cref="VerticalState.Next"/>）で
    /// 止まった状態から列挙した表。横と縦は独立に動くので、自機の状態はこの 2 つの組で表せる（先行入力は除く）。
    /// </summary>
    public sealed class MotionGraph
    {
        /// <summary>列挙する状態の数の上限（超えたら設定を拒否する）。</summary>
        public const int MaxStates = 2048;

        private static readonly Steer[] _steers = { Steer.None, Steer.Left, Steer.Right };
        private static readonly Leap[] _leaps = { Leap.None, Leap.Jump, Leap.Slide };

        private readonly Dictionary<LaneState, int> _laneIndex = new();
        private readonly Dictionary<VerticalState, int> _verticalIndex = new();
        private readonly List<LaneState> _lanes = new();
        private readonly List<VerticalState> _verticals = new();
        private readonly int[] _laneNext;
        private readonly int[] _verticalNext;

        public MotionGraph(RunConfig config)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            for (var lane = 0; lane < RunConfig.Lanes; lane++) { Intern(_laneIndex, _lanes, LaneState.Rest(config, lane)); }
            var laneNext = new List<int>();
            for (var i = 0; i < _lanes.Count; i++)
            {
                foreach (var steer in _steers) { laneNext.Add(Intern(_laneIndex, _lanes, _lanes[i].Next(steer, config, out _))); }
            }

            Intern(_verticalIndex, _verticals, VerticalState.Rest);
            var verticalNext = new List<int>();
            for (var i = 0; i < _verticals.Count; i++)
            {
                foreach (var leap in _leaps) { verticalNext.Add(Intern(_verticalIndex, _verticals, _verticals[i].Next(leap, config, out _))); }
            }

            _laneNext = laneNext.ToArray();
            _verticalNext = verticalNext.ToArray();
        }

        public int LaneCount => _lanes.Count;

        public int VerticalCount => _verticals.Count;

        public LaneState Lane(int index) => _lanes[index];

        public VerticalState Vertical(int index) => _verticals[index];

        /// <summary>レーン <paramref name="lane"/> に止まった状態の番号（列挙の順で 0〜2）。</summary>
        public static int RestLane(int lane) => lane;

        /// <summary>接地して止まった縦の状態の番号。</summary>
        public static int RestVertical => 0;

        public int LaneNext(int index, Steer steer) => _laneNext[(index * _steers.Length) + (int)steer];

        public int VerticalNext(int index, Leap leap) => _verticalNext[(index * _leaps.Length) + (int)leap];

        public bool TryIndexOf(LaneState lane, VerticalState vertical, out int laneIndex, out int verticalIndex)
        {
            verticalIndex = -1;
            return _laneIndex.TryGetValue(lane, out laneIndex) && _verticalIndex.TryGetValue(vertical, out verticalIndex);
        }

        private static int Intern<T>(Dictionary<T, int> index, List<T> states, T state)
            where T : notnull
        {
            if (index.TryGetValue(state, out var found)) { return found; }
            if (states.Count >= MaxStates) { throw new ArgumentException($"自機の状態が {MaxStates} を超えます。動きの設定（Tick 数・速さ）を小さくしてください。"); }
            index.Add(state, states.Count);
            states.Add(state);
            return states.Count - 1;
        }
    }

    /// <summary>探索で使う障害物（奥行きの範囲だけを持つ。横と高さは種類とレーンで決まる）。</summary>
    public readonly struct PlanObstacle
    {
        public PlanObstacle(ObstacleKind kind, int lane, int z, int depth)
        {
            Kind = kind;
            Lane = lane;
            Z = z;
            Depth = depth;
        }

        public ObstacleKind Kind { get; }

        public int Lane { get; }

        public int Z { get; }

        public int Depth { get; }
    }

    /// <summary>
    /// 1 区間（チャンク 1 つ・助走）を通り抜けられるかの探索。状態は「横の状態 × 縦の状態」の組で、Tick ごとに
    /// 到達できる組の集合をビット集合で持って進める（横と縦の操作は独立なので、像は横の後続 × 縦の後続の積になる）。
    /// 各組の Tick の軌跡はその組だけで決まる（<see cref="LaneState.PrevX"/>・<see cref="VerticalState.PrevY"/>）ので、
    /// 障害物との掃引は本物の試合（<see cref="RunnerRun"/>）と同じ <see cref="Sweep3.Axis"/> で厳密に判定する。
    /// <para>
    /// 先行入力は探索に入れない：先行入力が後の Tick で効く道は、その Tick に押す道と同じ動きになる。
    /// </para>
    /// </summary>
    public sealed class Planner
    {
        private static readonly Steer[] _steers = { Steer.None, Steer.Left, Steer.Right };
        private static readonly Leap[] _leaps = { Leap.None, Leap.Jump, Leap.Slide };

        private readonly RunConfig _config;
        private readonly MotionGraph _graph;
        private readonly int _words;

        // 縦の状態ごとの、3 つの操作の後続の集合。
        private readonly ulong[] _verticalImage;

        // 横の状態ごとの、異なる後続の番号。
        private readonly int[][] _laneSuccessors;

        // [横の状態, 障害物のレーン] の横の軸の重なり、[縦の状態, 障害物の種類] の高さの軸の重なり（Tick に依らない）。
        private readonly Span[,] _xSpans;
        private readonly Span[,] _ySpans;

        public Planner(RunConfig config, MotionGraph graph)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _words = (graph.VerticalCount + 63) / 64;
            _verticalImage = new ulong[graph.VerticalCount * _words];
            for (var v = 0; v < graph.VerticalCount; v++)
            {
                foreach (var leap in _leaps) { Set(_verticalImage, v * _words, graph.VerticalNext(v, leap)); }
            }

            _laneSuccessors = new int[graph.LaneCount][];
            for (var l = 0; l < graph.LaneCount; l++)
            {
                var successors = new List<int>();
                foreach (var steer in _steers)
                {
                    var next = graph.LaneNext(l, steer);
                    if (!successors.Contains(next)) { successors.Add(next); }
                }

                _laneSuccessors[l] = successors.ToArray();
            }

            _xSpans = new Span[graph.LaneCount, RunConfig.Lanes];
            for (var l = 0; l < graph.LaneCount; l++)
            {
                var from = RunnerShape.From(config, graph.Lane(l), VerticalState.Rest, 0);
                var to = RunnerShape.To(config, graph.Lane(l), VerticalState.Rest, 0);
                for (var lane = 0; lane < RunConfig.Lanes; lane++)
                {
                    var box = new Obstacle(ObstacleKind.Wall, lane, 0).Box(config);
                    _xSpans[l, lane] = Sweep3.Axis(from.X, to.X, from.Width, box.X, box.Width);
                }
            }

            var kinds = (ObstacleKind[])Enum.GetValues(typeof(ObstacleKind));
            _ySpans = new Span[graph.VerticalCount, kinds.Length];
            for (var v = 0; v < graph.VerticalCount; v++)
            {
                var vertical = graph.Vertical(v);
                foreach (var kind in kinds)
                {
                    var box = new Obstacle(kind, 1, 0).Box(config);
                    _ySpans[v, (int)kind] = Sweep3.Axis(vertical.PrevY, vertical.Y, vertical.Height(config), box.Y, box.Height);
                }
            }
        }

        public MotionGraph Graph => _graph;

        /// <summary>
        /// チャンクが、速さ <paramref name="speed"/> で入口のレーン <paramref name="startLane"/> に止まって入ったとき、
        /// 出口まで止まった状態で抜けられるか。どの位相（入口を越えた量 0〜speed-1）で入っても通れることを確かめるため、
        /// 障害物を手前へ speed-1 伸ばし、出口を越えうる Tick ではどれも止まった状態を求める（十分条件）。
        /// </summary>
        public bool Passable(ChunkDef chunk, int speed, int startLane)
        {
            if (chunk == null) { throw new ArgumentNullException(nameof(chunk)); }
            var obstacles = Expanded(chunk, speed);
            var length = chunk.Length(_config);
            var last = Ceil(length, speed);
            var firstGoal = Math.Max(1, Ceil(length - speed + 1, speed));
            return Explore(MotionGraph.RestLane(startLane), MotionGraph.RestVertical, 0, speed, obstacles, firstGoal, last, null);
        }

        /// <summary>
        /// 区間の中の z0 にいる自機の状態から、区間の終わり <paramref name="length"/> を越える Tick に止まった状態になる操作の列を求める。
        /// 道がなければ null。
        /// </summary>
        public Command[]? Plan(LaneState lane, VerticalState vertical, int z0, int length, int speed, IReadOnlyList<PlanObstacle> obstacles)
        {
            if (obstacles == null) { throw new ArgumentNullException(nameof(obstacles)); }
            if (speed <= 0) { throw new ArgumentOutOfRangeException(nameof(speed)); }
            if (!_graph.TryIndexOf(lane, vertical, out var l, out var v)) { throw new ArgumentException("探索の表にない自機の状態です。", nameof(lane)); }
            if (z0 >= length) { return Array.Empty<Command>(); }
            var last = Ceil(length - z0, speed);
            var layers = new List<ulong[]>();
            return Explore(l, v, z0, speed, obstacles, last, last, layers) ? Backtrack(layers) : null;
        }

        private static int Ceil(int value, int divisor) => value <= 0 ? 0 : ((value - 1) / divisor) + 1;

        private List<PlanObstacle> Expanded(ChunkDef chunk, int speed)
        {
            var obstacles = new List<PlanObstacle>(chunk.Obstacles.Count);
            foreach (var obstacle in chunk.Obstacles)
            {
                var box = obstacle.Box(_config);
                obstacles.Add(new PlanObstacle(obstacle.Kind, obstacle.Lane, box.Z - (speed - 1), box.Depth + (speed - 1)));
            }

            return obstacles;
        }

        private bool Explore(int startLane, int startVertical, int z0, int speed, IReadOnlyList<PlanObstacle> obstacles, int firstGoal, int last, List<ulong[]>? layers)
        {
            var size = _graph.LaneCount * _words;
            var current = new ulong[size];
            Set(current, startLane * _words, startVertical);
            layers?.Add(current);
            var image = new ulong[_words];
            var half = _config.RunnerDepth / 2;
            for (var k = 1; k <= last; k++)
            {
                var next = new ulong[size];
                for (var l = 0; l < _graph.LaneCount; l++)
                {
                    Array.Clear(image, 0, _words);
                    var any = false;
                    for (var w = 0; w < _words; w++)
                    {
                        var bits = current[(l * _words) + w];
                        while (bits != 0)
                        {
                            var v = (w * 64) + TrailingZeros(bits);
                            bits &= bits - 1;
                            for (var i = 0; i < _words; i++) { image[i] |= _verticalImage[(v * _words) + i]; }
                            any = true;
                        }
                    }

                    if (!any) { continue; }
                    foreach (var successor in _laneSuccessors[l])
                    {
                        for (var i = 0; i < _words; i++) { next[(successor * _words) + i] |= image[i]; }
                    }
                }

                var zFrom = z0 + ((k - 1) * speed) - half;
                var zTo = z0 + (k * speed) - half;
                foreach (var obstacle in obstacles)
                {
                    var z = Sweep3.Axis(zFrom, zTo, _config.RunnerDepth, obstacle.Z, obstacle.Depth);
                    if (!z.Hits) { continue; }
                    for (var l = 0; l < _graph.LaneCount; l++)
                    {
                        var zx = z.Intersect(_xSpans[l, obstacle.Lane]);
                        if (!zx.Hits) { continue; }
                        for (var w = 0; w < _words; w++)
                        {
                            var bits = next[(l * _words) + w];
                            while (bits != 0)
                            {
                                var bit = TrailingZeros(bits);
                                bits &= bits - 1;
                                var v = (w * 64) + bit;
                                if (zx.Intersect(_ySpans[v, (int)obstacle.Kind]).Hits) { next[(l * _words) + w] &= ~(1UL << bit); }
                            }
                        }
                    }
                }

                if (k >= firstGoal) { KeepRest(next); }
                if (IsEmpty(next)) { return false; }
                layers?.Add(next);
                current = next;
            }

            return true;
        }

        /// <summary>止まった状態（3 つのレーン × 接地）だけを残す。</summary>
        private void KeepRest(ulong[] states)
        {
            for (var l = 0; l < _graph.LaneCount; l++)
            {
                var isRestLane = l < RunConfig.Lanes;
                for (var w = 0; w < _words; w++)
                {
                    var keep = isRestLane && w == MotionGraph.RestVertical / 64 ? 1UL << (MotionGraph.RestVertical % 64) : 0UL;
                    states[(l * _words) + w] &= keep;
                }
            }
        }

        private Command[] Backtrack(List<ulong[]> layers)
        {
            var commands = new Command[layers.Count - 1];
            var (l, v) = FirstSet(layers[layers.Count - 1]);
            for (var k = layers.Count - 1; k >= 1; k--)
            {
                var found = false;
                var previous = layers[k - 1];
                for (var pl = 0; pl < _graph.LaneCount && !found; pl++)
                {
                    foreach (var steer in _steers)
                    {
                        if (found || _graph.LaneNext(pl, steer) != l) { continue; }
                        for (var pv = 0; pv < _graph.VerticalCount && !found; pv++)
                        {
                            if (!IsSet(previous, pl * _words, pv)) { continue; }
                            foreach (var leap in _leaps)
                            {
                                if (_graph.VerticalNext(pv, leap) != v) { continue; }
                                commands[k - 1] = new Command(steer, leap);
                                l = pl;
                                v = pv;
                                found = true;
                                break;
                            }
                        }
                    }
                }

                if (!found) { throw new InvalidOperationException("探索の経路を戻れません。"); }
            }

            return commands;
        }

        private (int Lane, int Vertical) FirstSet(ulong[] states)
        {
            for (var l = 0; l < _graph.LaneCount; l++)
            {
                for (var w = 0; w < _words; w++)
                {
                    var bits = states[(l * _words) + w];
                    if (bits != 0) { return (l, (w * 64) + TrailingZeros(bits)); }
                }
            }

            throw new InvalidOperationException("到達した状態がありません。");
        }

        private static bool IsEmpty(ulong[] states)
        {
            foreach (var bits in states)
            {
                if (bits != 0) { return false; }
            }

            return true;
        }

        private static void Set(ulong[] states, int offset, int bit) => states[offset + (bit / 64)] |= 1UL << (bit % 64);

        private static bool IsSet(ulong[] states, int offset, int bit) => (states[offset + (bit / 64)] & (1UL << (bit % 64))) != 0;

        // netstandard2.1 には BitOperations がないので、最下位の 1 の位置を数える。
        private static int TrailingZeros(ulong value)
        {
            var count = 0;
            if ((value & 0xFFFFFFFFUL) == 0) { count += 32; value >>= 32; }
            if ((value & 0xFFFFUL) == 0) { count += 16; value >>= 16; }
            if ((value & 0xFFUL) == 0) { count += 8; value >>= 8; }
            if ((value & 0xFUL) == 0) { count += 4; value >>= 4; }
            if ((value & 0x3UL) == 0) { count += 2; value >>= 2; }
            if ((value & 0x1UL) == 0) { count += 1; }
            return count;
        }
    }
}
