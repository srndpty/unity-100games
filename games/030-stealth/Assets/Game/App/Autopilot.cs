using System;
using System.Collections.Generic;
using Game.Core;
using IntGeometry;

namespace Game.App
{
    /// <summary>
    /// 目標のマスへ、歩けるマスの経路（4 近傍）に沿って自機を動かす入力を作る。マスごとの重み（<paramref name="costs"/>）があれば、
    /// 重みの和が最小の経路（ダイクストラ）、なければ歩数が最小の経路を使う。
    /// </summary>
    public sealed class Steering
    {
        private readonly FacilityMap _map;
        private readonly int[]? _costs;
        private readonly long[] _total;
        private readonly bool[] _done;
        private Cell? _goal;

        public Steering(FacilityMap map, int[]? costs = null)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
            if (costs != null && costs.Length != map.Count) { throw new ArgumentException("重みの数がマスの数と違います。", nameof(costs)); }
            _costs = costs;
            _total = new long[map.Count];
            _done = new bool[map.Count];
        }

        public FacilityMap Map => _map;

        /// <summary>今の目標（目的物を持つ前は目的物、持った後は出口）。</summary>
        public static Cell GoalOf(Infiltration match) => match.HasObjective ? match.Facility.Exit : match.Facility.Objective;

        /// <summary>
        /// 次のマスの中心へ向かう入力。各軸は残りが歩幅の半分以下なら動かさない（行き過ぎて往復しない）。
        /// 目標のマスの中心に着いていれば止まる。
        /// </summary>
        public IntruderInput Toward(Infiltration match, Cell goal, Gait gait)
        {
            if (match == null) { throw new ArgumentNullException(nameof(match)); }
            var center = match.Intruder.Center;
            var here = Cell.Of(center);
            var target = here == goal ? goal.Center : NextStep(here, goal).Center;
            var speed = gait switch
            {
                Gait.Sneak => match.Config.SneakSpeed,
                Gait.Run => match.Config.RunSpeed,
                _ => match.Config.WalkSpeed,
            };
            var x = Axis(target.X - center.X, speed);
            var y = Axis(target.Y - center.Y, speed);
            return new IntruderInput(x, y, gait == Gait.Run, gait == Gait.Sneak);
        }

        /// <summary><paramref name="from"/> から目標へ 1 歩進んだマス（着いている・届かないなら <paramref name="from"/>）。</summary>
        public Cell NextStep(Cell from, Cell goal)
        {
            Prepare(goal);
            if (!_map.Contains(from)) { return from; }
            var here = _total[_map.IndexOf(from)];
            if (here <= 0) { return from; }
            var best = from;
            var bestTotal = here;
            for (var direction = 0; direction < 4; direction++)
            {
                var next = PathField.Neighbor(from, direction);
                if (!_map.Contains(next) || !_map.IsWalkable(next)) { continue; }
                var total = _total[_map.IndexOf(next)];
                if (total >= 0 && total < bestTotal)
                {
                    best = next;
                    bestTotal = total;
                }
            }

            return best;
        }

        private static int Axis(int delta, int speed) => Math.Abs(delta) * 2 <= speed ? 0 : Math.Sign(delta);

        /// <summary>目標からの重みの和を数える（マスの数が少ないので、未確定の最小を毎回探す素朴なダイクストラ）。</summary>
        private void Prepare(Cell goal)
        {
            if (_goal == goal) { return; }
            _goal = goal;
            for (var i = 0; i < _total.Length; i++)
            {
                _total[i] = -1;
                _done[i] = false;
            }

            if (!_map.Contains(goal) || !_map.IsWalkable(goal)) { return; }
            _total[_map.IndexOf(goal)] = 0;
            while (true)
            {
                var current = -1;
                for (var i = 0; i < _total.Length; i++)
                {
                    if (!_done[i] && _total[i] >= 0 && (current < 0 || _total[i] < _total[current])) { current = i; }
                }

                if (current < 0) { return; }
                _done[current] = true;
                var cell = _map.CellAt(current);
                for (var direction = 0; direction < 4; direction++)
                {
                    var next = PathField.Neighbor(cell, direction);
                    if (!_map.Contains(next) || !_map.IsWalkable(next)) { continue; }
                    var index = _map.IndexOf(next);
                    var total = _total[current] + (_costs?[current] ?? 1);
                    if (!_done[index] && (_total[index] < 0 || total < _total[index])) { _total[index] = total; }
                }
            }
        }
    }

    /// <summary>
    /// マスごとの露出度。邪魔されない巡回を試走し、一定 Tick ごとに各マスの中心が誰かの視界（平常の視程）に入っていたか、
    /// 誰かの近く（忍び足の足音が届く距離 + 1 マス以内）にあった回数を数える。
    /// 見つからない限り警備員の動きは自機に依らないので、施設ごとに 1 回数えればよい。
    /// </summary>
    public static class Exposure
    {
        /// <summary>重み：1 + <paramref name="weight"/> × 見えていた割合（切り捨て）。歩けないマスは 0。</summary>
        public static int[] Costs(WardConfig config, Facility facility, int ticks = 2400, int every = 8, int weight = 60)
        {
            if (facility == null) { throw new ArgumentNullException(nameof(facility)); }
            var map = facility.Map;
            var seen = new int[map.Count];
            var samples = 0;
            var match = new Infiltration(config, facility, 0);
            var spec = match.Config.VisionAt(AlertLevel.Normal);

            // 忍び足でも聞こえる・体が触れる近さ。
            var near = match.Config.SneakNoise + 1;
            for (var t = 0; t < ticks; t++)
            {
                match.Tick(IntruderInput.None);
                if (t % every != 0) { continue; }
                samples++;
                for (var i = 0; i < map.Count; i++)
                {
                    var cell = map.CellAt(i);
                    if (!map.IsWalkable(cell)) { continue; }
                    foreach (var guard in match.Guards)
                    {
                        if (Cell.Manhattan(cell, Cell.Of(guard.Body.Position)) <= near || Vision.Sees(map, guard.Body.Position, guard.Body.Facing, spec, cell.Center))
                        {
                            seen[i]++;
                            break;
                        }
                    }
                }
            }

            var costs = new int[map.Count];
            for (var i = 0; i < map.Count; i++)
            {
                costs[i] = map.IsWalkable(map.CellAt(i)) ? 1 + (int)((long)weight * seen[i] / samples) : 0;
            }

            return costs;
        }
    }

    /// <summary>
    /// 気づかれないように進む操縦役。数 Tick ごとに、候補の動き（進む・その場で待つ・来た道を戻る。進む・戻るは途中から待つ）を
    /// <see cref="Infiltration.Clone"/> で <see cref="Horizon"/> Tick 先まで試して選ぶ。経路は露出度（<see cref="Exposure"/>）を重みにした最短経路。
    /// <list type="bullet">
    /// <item>厳しい（<c>strict</c>）：誰も見えず・聞こえず・全員が巡回のままの候補だけを選ぶ（見つからずに脱出する経路）。どれも危ないときは、気づかれるまでが最も長い候補。
    /// 見つからない限り乱数は使われないので、どのシードでも同じ道になる。</item>
    /// <item>緩い：見つかった後に逃げる。見られる・聞かれる Tick が最も少ない候補を選び（見られるのは聞かれる 2 倍悪い。捕まる候補は選ばない）、走って逃げる候補も試す。</item>
    /// </list>
    /// </summary>
    public sealed class StealthPilot : IPilot
    {
        public const int Commit = 4;

        private static readonly int[] _advanceTicks = { int.MaxValue, 90, 60, 30, 15, 6, 0 };
        private static readonly int[] _retreatTicks = { 15, 30, 60, 90 };
        private static readonly Gait[] _quietGaits = { Gait.Walk, Gait.Sneak };
        private static readonly Gait[] _allGaits = { Gait.Run, Gait.Walk, Gait.Sneak };
        private static readonly Gait[] _waitOnly = { Gait.Walk };

        private readonly bool _strict;
        private readonly Queue<IntruderInput> _queue = new();
        private readonly List<Cell> _trail = new();
        private readonly List<IntruderInput> _scratch = new();
        private Infiltration? _match;
        private Steering? _steering;
        private Steering? _back;
        private Steering? _direct;

        public StealthPilot(bool strict = true)
        {
            _strict = strict;
            Horizon = 150;
        }

        /// <summary>先読みの Tick 数。</summary>
        public int Horizon { get; }

        /// <summary>候補を試した回数（性能の目安）。</summary>
        public int Trials { get; private set; }

        public IntruderInput InputFor(Infiltration match)
        {
            if (match == null) { throw new ArgumentNullException(nameof(match)); }
            if (!ReferenceEquals(match, _match)) { Reset(match); }
            var here = Cell.Of(match.Intruder.Center);
            if (_trail.Count == 0 || _trail[_trail.Count - 1] != here) { _trail.Add(here); }
            if (_queue.Count == 0) { Plan(match); }
            return _queue.Dequeue();
        }

        /// <summary>試合と判断の状態を捨てる（試合が変わったときにも自動で行う）。</summary>
        public void Reset(Infiltration? match)
        {
            _match = match;
            _queue.Clear();
            _trail.Clear();
            _steering = match != null ? new Steering(match.Map, Exposure.Costs(match.Config, match.Facility)) : null;
            _back = match != null ? new Steering(match.Map) : null;
            _direct = match != null ? new Steering(match.Map) : null;
        }

        /// <summary>その試合の状態で、誰にも気づかれていないか（誰も見えず・聞こえず・全員が巡回のまま、捕まっていない）。</summary>
        public static bool Unnoticed(Infiltration match)
        {
            if (match == null) { throw new ArgumentNullException(nameof(match)); }
            if (match.Status == MatchStatus.Caught) { return false; }
            foreach (var guard in match.Guards)
            {
                if (guard.Sees || guard.Hears || guard.Brain.Mode != GuardMode.Patrol) { return false; }
            }

            return true;
        }

        /// <summary>その Tick の見つかりやすさ：誰かに見えていれば 2、聞こえているだけなら 1、どちらもなければ 0。</summary>
        public static int Exposed(Infiltration match)
        {
            if (match == null) { throw new ArgumentNullException(nameof(match)); }
            var exposed = 0;
            foreach (var guard in match.Guards)
            {
                if (guard.Sees) { return 2; }
                if (guard.Hears) { exposed = 1; }
            }

            return exposed;
        }

        private void Plan(Infiltration match)
        {
            var best = int.MinValue;
            List<IntruderInput>? bestInputs = null;

            // 進む量の多い順に試す。最後が待つ（進む量 0）。
            foreach (var ticks in _advanceTicks)
            {
                foreach (var gait in ticks == 0 ? _waitOnly : _strict ? _quietGaits : _allGaits)
                {
                    if (Consider(match, ticks, gait, Route.Weighted, ref best, ref bestInputs)) { return; }
                }
            }

            foreach (var ticks in _retreatTicks)
            {
                foreach (var gait in _strict ? _waitOnly : _allGaits)
                {
                    if (Consider(match, ticks, _strict ? Gait.Sneak : gait, Route.Back, ref best, ref bestInputs)) { return; }
                }
            }

            // 緩いときは、歩数が最短の経路（別の扉を通ることがある）でも進んでみる。
            if (!_strict)
            {
                foreach (var ticks in _advanceTicks)
                {
                    if (ticks == 0) { continue; }
                    foreach (var gait in _allGaits)
                    {
                        if (Consider(match, ticks, gait, Route.Direct, ref best, ref bestInputs)) { return; }
                    }
                }
            }

            Enqueue(bestInputs!);
        }

        /// <summary>候補を試す。危なげがなければそれに決めて true。そうでなければ点の最もよい候補を覚える。</summary>
        private bool Consider(Infiltration match, int ticks, Gait gait, Route route, ref int best, ref List<IntruderInput>? bestInputs)
        {
            var score = Try(match, ticks, gait, route);
            if (score >= (_strict ? Horizon : 2 * Horizon))
            {
                Enqueue(_scratch);
                return true;
            }

            if (score > best)
            {
                best = score;
                bestInputs = new List<IntruderInput>(_scratch);
            }

            return false;
        }

        /// <summary>
        /// <paramref name="moveTicks"/> の間進む（戻る）、その後は待つ。厳しいときは気づかれずにいられた Tick 数（最大 <see cref="Horizon"/>）、
        /// 緩いときは 2 × <see cref="Horizon"/> から見つかりやすさ（<see cref="Exposed"/>）の和を引いた値を返す。
        /// 脱出したら最大の値、捕まったら -1。
        /// </summary>
        private int Try(Infiltration match, int moveTicks, Gait gait, Route route)
        {
            Trials++;
            _scratch.Clear();
            var sim = match.Clone();
            var backGoal = _trail[Math.Max(0, _trail.Count - 4)];
            var hidden = 0;
            for (var t = 0; t < Horizon; t++)
            {
                IntruderInput input;
                if (t >= moveTicks) { input = IntruderInput.None; }
                else if (route == Route.Back) { input = _back!.Toward(sim, backGoal, gait); }
                else if (route == Route.Direct) { input = _direct!.Toward(sim, Steering.GoalOf(sim), gait); }
                else { input = _steering!.Toward(sim, Steering.GoalOf(sim), gait); }
                _scratch.Add(input);
                sim.Tick(input);
                if (sim.Status == MatchStatus.Escaped) { return _strict ? Horizon : 2 * Horizon; }
                if (sim.Status == MatchStatus.Caught) { return -1; }
                if (_strict)
                {
                    if (!Unnoticed(sim)) { return t; }
                }
                else
                {
                    hidden -= Exposed(sim);
                }
            }

            return _strict ? Horizon : (2 * Horizon) + hidden;
        }

        private enum Route
        {
            /// <summary>露出度を重みにした経路で目標へ。</summary>
            Weighted,

            /// <summary>来た道を数マス戻る。</summary>
            Back,

            /// <summary>歩数が最短の経路で目標へ。</summary>
            Direct,
        }

        private void Enqueue(List<IntruderInput> inputs)
        {
            for (var i = 0; i < Commit && i < inputs.Count; i++) { _queue.Enqueue(inputs[i]); }
            if (_queue.Count == 0) { _queue.Enqueue(IntruderInput.None); }
        }
    }

    /// <summary>
    /// 見つかってから逃げ切る経路の確認用の操縦役。最初は警備員を気にせず目的物へ歩き、離れた警備員に見られたら止まって
    /// 見つかるのを待つ。見つかったら（厳戒に上がったら）緩い <see cref="StealthPilot"/> に替えて逃げ、そのまま目的物と出口へ向かう。
    /// </summary>
    public sealed class BoldPilot : IPilot
    {
        private readonly StealthPilot _careful = new(strict: false);
        private Infiltration? _match;
        private Steering? _steering;

        public IntruderInput InputFor(Infiltration match)
        {
            if (match == null) { throw new ArgumentNullException(nameof(match)); }
            if (!ReferenceEquals(match, _match))
            {
                _match = match;
                _steering = new Steering(match.Map);
            }

            if (match.Discoveries > 0) { return _careful.InputFor(match); }

            // まだ見つかっていない：見えている警備員が遠ければ止まって見つかるのを待ち、近ければ逃げる。
            var nearest = long.MaxValue;
            foreach (var guard in match.Guards)
            {
                if (guard.Sees) { nearest = Math.Min(nearest, Vec2i.DistanceSquared(guard.Body.Position, match.Intruder.Center)); }
            }

            if (nearest == long.MaxValue) { return _steering!.Toward(match, Steering.GoalOf(match), Gait.Walk); }
            var keep = (long)BaitDistance * BaitDistance;
            return nearest >= keep ? IntruderInput.None : _careful.InputFor(match);
        }

        /// <summary>見つかるのを待つ最短の距離（単位）。これより近い警備員に見られたら逃げる。</summary>
        public const int BaitDistance = 3 * TileMotion.Units.Tile;
    }
}
