using System;
using IntGeometry;
using TileMotion;

namespace Game.Core
{
    public enum RunStatus
    {
        Playing,
        Succeeded,
        Failed,
    }

    public enum FailReason
    {
        None,

        /// <summary>露玉の中心が盤の外（下・左右）へ出た。</summary>
        Fell,

        /// <summary>トゲに触れた。</summary>
        Spike,
    }

    /// <summary>描画・音に知らせる出来事。</summary>
    [Flags]
    public enum RunEvents
    {
        None = 0,
        Cut = 1,
        Succeeded = 1 << 1,
        Failed = 1 << 2,
    }

    /// <summary>
    /// 1 問の試合。物理（Verlet の点と距離拘束）を整数だけで解く。
    /// <para>
    /// 点の並び：ロープごとに「支点・節点 1〜n」、最後に露玉。ロープ r の k 番目の点（k = 0 が支点、k = 節の数 が露玉）を
    /// <see cref="RopePoint"/> で読む。節 j は k = j と k = j + 1 を結ぶ。
    /// </para>
    /// <para>
    /// 操作と物理の分離：<see cref="Cut"/> は Tick と Tick の間にだけ呼ばれ、その時点の形で交差を判定してすぐ反映する。
    /// <see cref="Tick"/> は「積分 → 拘束の反復 → 露玉の移動量の制限 → 壁の押し出し → 目標・トゲの接触 → 落下」の順で 1 Tick 進める。
    /// </para>
    /// </summary>
    public sealed class PuzzleRun
    {
        /// <summary>切り離した断片は、全ての節点がこれより下へ落ちたら消える（以後は動かさない）。</summary>
        public const int FragmentFloor = -2 * Board.Unit;

        private readonly RopeConfig _config;
        private readonly int[] _x;
        private readonly int[] _y;
        private readonly int[] _ox;
        private readonly int[] _oy;
        private readonly int[] _w;
        private readonly int[] _first;
        private readonly int[] _links;
        private readonly int[] _rest;
        private readonly int[] _cut;
        private readonly bool[] _gone;
        private readonly int _body;
        private RunEvents _events;

        /// <param name="config">物理の設定（検証して複製する）。</param>
        /// <param name="puzzle">問（設定に照らして検証する）。</param>
        public PuzzleRun(RopeConfig config, Puzzle puzzle)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            Puzzle = puzzle ?? throw new ArgumentNullException(nameof(puzzle));
            config.Validate();
            puzzle.Validate(config);
            _config = config.Copy();

            var ropes = puzzle.Ropes.Count;
            var points = 1;
            foreach (var rope in puzzle.Ropes) { points += rope.Links; }
            _x = new int[points];
            _y = new int[points];
            _ox = new int[points];
            _oy = new int[points];
            _w = new int[points];
            _first = new int[ropes];
            _links = new int[ropes];
            _rest = new int[ropes];
            _cut = new int[ropes];
            _gone = new bool[ropes];
            _body = points - 1;
            Reset();
        }

        private PuzzleRun(PuzzleRun other)
        {
            Puzzle = other.Puzzle;
            _config = other._config;
            _x = (int[])other._x.Clone();
            _y = (int[])other._y.Clone();
            _ox = (int[])other._ox.Clone();
            _oy = (int[])other._oy.Clone();
            _w = other._w;
            _first = other._first;
            _links = other._links;
            _rest = other._rest;
            _cut = (int[])other._cut.Clone();
            _gone = (bool[])other._gone.Clone();
            _body = other._body;
            _events = other._events;
            Ticks = other.Ticks;
            Moves = other.Moves;
            Status = other.Status;
            Reason = other.Reason;
            LastCutTick = other.LastCutTick;
            LastCutPoint = other.LastCutPoint;
        }

        public Puzzle Puzzle { get; }

        /// <summary>試合の設定の複製（書き換えても試合に影響しない）。</summary>
        public RopeConfig Config => _config.Copy();

        public long Ticks { get; private set; }

        /// <summary>手数（切ったロープの本数）。</summary>
        public int Moves { get; private set; }

        public RunStatus Status { get; private set; }

        public FailReason Reason { get; private set; }

        /// <summary>最後に切った Tick（まだなら -1）。描画の演出用で、以後の動きを変えないのでハッシュに含めない。</summary>
        public long LastCutTick { get; private set; }

        /// <summary>最後に切った節の中点（描画の演出用）。</summary>
        public Vec2i LastCutPoint { get; private set; }

        public Vec2i Body => new(_x[_body], _y[_body]);

        public int RopeCount => _links.Length;

        /// <summary>ロープの節の数。</summary>
        public int LinksOf(int rope) => _links[rope];

        /// <summary>切った節の番号（切っていなければ -1）。</summary>
        public int CutLinkOf(int rope) => _cut[rope];

        public bool IsCut(int rope) => _cut[rope] >= 0;

        /// <summary>露玉側の断片が盤の下へ落ちて消えたか。</summary>
        public bool IsFragmentGone(int rope) => _gone[rope];

        /// <summary>ロープ <paramref name="rope"/> の k 番目の点（0 = 支点、<see cref="LinksOf"/> = 露玉）。</summary>
        public Vec2i RopePoint(int rope, int k)
        {
            var index = Index(rope, k);
            return new Vec2i(_x[index], _y[index]);
        }

        /// <summary>ロープが今、露玉を拘束しているか（切っていない）。</summary>
        public bool Holds(int rope) => _cut[rope] < 0;

        public PuzzleRun Clone() => new(this);

        /// <summary>初期状態に戻す（手数 0、Tick 0）。</summary>
        public void Reset()
        {
            var next = 0;
            var body = Puzzle.Body;
            for (var r = 0; r < Puzzle.Ropes.Count; r++)
            {
                var spec = Puzzle.Ropes[r];
                _first[r] = next;
                _links[r] = spec.Links;
                _rest[r] = spec.RestLength;
                _cut[r] = -1;
                _gone[r] = false;

                // 支点から露玉まで等間隔に並べる（ロープが長ければ節はたるんでいて、落ちて垂れる）。
                for (var k = 0; k < spec.Links; k++)
                {
                    var x = spec.Anchor.X + (int)(((long)body.X - spec.Anchor.X) * k / spec.Links);
                    var y = spec.Anchor.Y + (int)(((long)body.Y - spec.Anchor.Y) * k / spec.Links);
                    Place(next + k, x, y, k == 0 ? 0 : _config.NodeInverseMass);
                }

                next += spec.Links;
            }

            Place(_body, body.X, body.Y, _config.BodyInverseMass);
            Ticks = 0;
            Moves = 0;
            Status = RunStatus.Playing;
            Reason = FailReason.None;
            LastCutTick = -1;
            LastCutPoint = Vec2i.Zero;
            _events = RunEvents.None;
        }

        /// <summary>
        /// なぞりの線分 <paramref name="from"/>→<paramref name="to"/> が横切った、まだ切っていないロープを切る。切った本数を返す。
        /// 1 本のロープは 1 回だけ切れる（同じなぞりで 2 つの節を横切っても 1 回、切れた断片は切れない）。
        /// 終わった試合・長さ 0 のなぞりでは何もしない。端点が <see cref="Board.AcceptsSwipePoint"/> の外なら例外。
        /// </summary>
        public int Cut(Vec2i from, Vec2i to)
        {
            var mask = Crossed(from, to);
            if (mask == 0 || Status != RunStatus.Playing) { return 0; }

            var count = 0;
            for (var r = 0; r < RopeCount; r++)
            {
                if ((mask & (1 << r)) == 0) { continue; }
                var link = FirstCrossedLink(r, from, to);
                _cut[r] = link;
                Moves++;
                count++;
                var a = RopePoint(r, link);
                var b = RopePoint(r, link + 1);
                LastCutPoint = new Vec2i((int)(((long)a.X + b.X) / 2), (int)(((long)a.Y + b.Y) / 2));
            }

            LastCutTick = Ticks;
            _events |= RunEvents.Cut;
            return count;
        }

        /// <summary>なぞりの線分が今横切る、まだ切っていないロープの集合（ビット r がロープ r）。状態は変えない。</summary>
        public int Crossed(Vec2i from, Vec2i to)
        {
            if (!Board.AcceptsSwipePoint(from)) { throw new ArgumentOutOfRangeException(nameof(from), from, "なぞりの点が範囲外です。"); }
            if (!Board.AcceptsSwipePoint(to)) { throw new ArgumentOutOfRangeException(nameof(to), to, "なぞりの点が範囲外です。"); }
            var mask = 0;
            for (var r = 0; r < RopeCount; r++)
            {
                if (_cut[r] < 0 && FirstCrossedLink(r, from, to) >= 0) { mask |= 1 << r; }
            }

            return mask;
        }

        /// <summary>1 Tick 進める。終わった試合では何もしない。</summary>
        public void Tick()
        {
            if (Status != RunStatus.Playing) { return; }
            Ticks++;
            var start = Body;

            Integrate();
            for (var i = 0; i < _config.Iterations; i++)
            {
                for (var r = 0; r < RopeCount; r++) { SolveRope(r); }
            }

            LimitBody(start);
            // 毎 Tick の処理で ReadOnlyCollection を foreach で回さない（列挙子が箱に入って割り当てが起きる。027 の知見）。
            var walls = Puzzle.Walls;
            for (var i = 0; i < walls.Count; i++) { PushOut(walls[i]); }
            DropFragments();

            var end = Body;
            Contact(start, end);
            if (Status == RunStatus.Playing && (end.X < 0 || end.X > Board.Width || end.Y < 0))
            {
                Finish(RunStatus.Failed, FailReason.Fell);
            }
        }

        /// <summary>前回の呼び出し以降に起きた出来事を取り出す。</summary>
        public RunEvents TakeEvents()
        {
            var events = _events;
            _events = RunEvents.None;
            return events;
        }

        /// <summary>以後の動きを決める状態のハッシュ（位置・前の位置・切った節・消えた断片・手数・Tick・結果）。</summary>
        public uint Hash()
        {
            var hash = StateHash.Start();
            hash.Mix(Ticks);
            hash.Mix(Moves);
            hash.Mix((int)Status);
            hash.Mix((int)Reason);
            for (var i = 0; i < _x.Length; i++)
            {
                hash.Mix(_x[i]);
                hash.Mix(_y[i]);
                hash.Mix(_ox[i]);
                hash.Mix(_oy[i]);
            }

            for (var r = 0; r < RopeCount; r++)
            {
                hash.Mix(_cut[r]);
                hash.Mix(_gone[r]);
            }

            return hash.Value;
        }

        private int Index(int rope, int k)
        {
            if ((uint)rope >= (uint)RopeCount) { throw new ArgumentOutOfRangeException(nameof(rope), rope, "ロープの番号が範囲外です。"); }
            if (k < 0 || k > _links[rope]) { throw new ArgumentOutOfRangeException(nameof(k), k, "点の番号が範囲外です。"); }
            return k == _links[rope] ? _body : _first[rope] + k;
        }

        private void Place(int index, int x, int y, int inverseMass)
        {
            _x[index] = x;
            _y[index] = y;
            _ox[index] = x;
            _oy[index] = y;
            _w[index] = inverseMass;
        }

        private int FirstCrossedLink(int rope, Vec2i from, Vec2i to)
        {
            for (var j = 0; j < _links[rope]; j++)
            {
                if (Segment.Intersects(from, to, RopePoint(rope, j), RopePoint(rope, j + 1))) { return j; }
            }

            return -1;
        }

        /// <summary>
        /// 節 <paramref name="j"/> が今、拘束として効いているか（描くか）。切った節と、断片と露玉を結ぶ最後の節、消えた断片の節は効かない。
        /// </summary>
        public bool IsLinkActive(int rope, int j)
        {
            if ((uint)rope >= (uint)RopeCount) { throw new ArgumentOutOfRangeException(nameof(rope), rope, "ロープの番号が範囲外です。"); }
            var cut = _cut[rope];
            if (cut < 0 || j < cut) { return true; }
            return j > cut && j < _links[rope] - 1 && !_gone[rope];
        }

        /// <summary>点が動くか。支点と、消えた断片の節点は動かない。</summary>
        private bool IsLive(int index)
        {
            if (_w[index] == 0) { return false; }
            if (index == _body) { return true; }
            for (var r = 0; r < RopeCount; r++)
            {
                var k = index - _first[r];
                if (k > 0 && k < _links[r]) { return !_gone[r] || k <= _cut[r]; }
            }

            return true;
        }

        private void Integrate()
        {
            var max = (long)_config.MaxStep * _config.MaxStep;
            for (var i = 0; i < _x.Length; i++)
            {
                if (!IsLive(i)) { continue; }

                // 速さ = 位置の差 × 減衰（0 方向へ切り捨て）。重力を足してから移動量の上限で切り詰める。
                var vx = (int)((long)(_x[i] - _ox[i]) * _config.Damping / RopeConfig.RatioDenominator);
                var vy = (int)((long)(_y[i] - _oy[i]) * _config.Damping / RopeConfig.RatioDenominator) - _config.Gravity;
                var v = new Vec2i(vx, vy);
                if (v.LengthSquared > max) { v = IntMath.Scale(v, _config.MaxStep); }
                _ox[i] = _x[i];
                _oy[i] = _y[i];
                _x[i] += v.X;
                _y[i] += v.Y;
            }
        }

        private void SolveRope(int rope)
        {
            var rest = _rest[rope];
            for (var j = 0; j < _links[rope]; j++)
            {
                if (!IsLinkActive(rope, j)) { continue; }
                var a = Index(rope, j);
                var b = Index(rope, j + 1);
                var wa = _w[a];
                var wb = _w[b];
                var total = wa + wb;
                if (total == 0) { continue; }

                // 伸びたときだけ縮める（たるみは許す）。補正を逆質量で配分する。
                long dx = _x[b] - _x[a];
                long dy = _y[b] - _y[a];
                var squared = (dx * dx) + (dy * dy);
                if (squared <= (long)rest * rest) { continue; }
                var distance = IntMath.Sqrt(squared);
                var stretch = distance - rest;
                if (stretch <= 0) { continue; }
                var denominator = distance * total;
                _x[a] += IntMath.RoundDiv(dx * stretch * wa, denominator);
                _y[a] += IntMath.RoundDiv(dy * stretch * wa, denominator);
                _x[b] -= IntMath.RoundDiv(dx * stretch * wb, denominator);
                _y[b] -= IntMath.RoundDiv(dy * stretch * wb, denominator);
            }
        }

        /// <summary>露玉の 1 Tick の移動を上限で切り詰める（拘束の補正で大きく動いても壁をすり抜けない）。</summary>
        private void LimitBody(Vec2i start)
        {
            var move = Body - start;
            if (move.LengthSquared <= (long)_config.MaxStep * _config.MaxStep) { return; }
            var limited = IntMath.Scale(move, _config.MaxStep);
            _x[_body] = start.X + limited.X;
            _y[_body] = start.Y + limited.Y;
        }

        /// <summary>露玉を壁の外へ押し出し、壁へ向かう速さを消して、壁に沿う速さに摩擦を掛ける。</summary>
        private void PushOut(Rect wall)
        {
            var r = _config.BodyRadius;
            var center = Body;
            if (!Puzzle.Overlaps(wall, center, r)) { return; }

            Vec2i normal;
            var closest = new Vec2i(IntMath.Clamp(center.X, wall.MinX, wall.MaxX), IntMath.Clamp(center.Y, wall.MinY, wall.MaxY));
            if (closest == center)
            {
                // 中心が壁の中：最も浅い辺から外へ出す（同じ深さなら左・右・下・上の順）。
                var left = center.X - wall.MinX;
                var right = wall.MaxX - center.X;
                var bottom = center.Y - wall.MinY;
                var top = wall.MaxY - center.Y;
                var least = Math.Min(Math.Min(left, right), Math.Min(bottom, top));
                if (least == left) { normal = new Vec2i(-1, 0); center = new Vec2i(wall.MinX - r, center.Y); }
                else if (least == right) { normal = new Vec2i(1, 0); center = new Vec2i(wall.MaxX + r, center.Y); }
                else if (least == bottom) { normal = new Vec2i(0, -1); center = new Vec2i(center.X, wall.MinY - r); }
                else { normal = new Vec2i(0, 1); center = new Vec2i(center.X, wall.MaxY + r); }
            }
            else
            {
                normal = center - closest;
                center = closest + IntMath.Scale(normal, r);
            }

            _x[_body] = center.X;
            _y[_body] = center.Y;

            // 速さ v = 位置 - 前の位置。壁へ向かう成分（v・n < 0）を消し、残り（壁に沿う成分）に摩擦を掛ける。
            var v = new Vec2i(center.X - _ox[_body], center.Y - _oy[_body]);
            var dot = Vec2i.Dot(v, normal);
            if (dot < 0)
            {
                var length = normal.LengthSquared;
                v = new Vec2i(v.X - IntMath.RoundDiv(normal.X * dot, length), v.Y - IntMath.RoundDiv(normal.Y * dot, length));
                v = new Vec2i(
                    (int)((long)v.X * _config.WallFriction / RopeConfig.RatioDenominator),
                    (int)((long)v.Y * _config.WallFriction / RopeConfig.RatioDenominator));
            }

            _ox[_body] = center.X - v.X;
            _oy[_body] = center.Y - v.Y;
        }

        /// <summary>切り離した断片が全て盤の下へ落ちたら消す。</summary>
        private void DropFragments()
        {
            for (var r = 0; r < RopeCount; r++)
            {
                if (_cut[r] < 0 || _gone[r]) { continue; }
                var below = true;
                for (var k = _cut[r] + 1; k < _links[r] && below; k++) { below = _y[_first[r] + k] < FragmentFloor; }
                _gone[r] = below;
            }
        }

        /// <summary>
        /// この Tick の露玉の軌跡（始め → 終わりの直線）で、目標・トゲに触れたかを厳密に判定する。
        /// 同じ Tick に両方へ触れたら失敗（トゲを優先する。どちらが先かは問わない）。
        /// </summary>
        private void Contact(Vec2i start, Vec2i end)
        {
            var r = _config.BodyRadius;
            var spikes = Puzzle.Spikes;
            for (var i = 0; i < spikes.Count; i++)
            {
                var spike = spikes[i];
                if (Sweep.Touches(start, end, spike.Center, spike.Center, r + spike.Radius))
                {
                    Finish(RunStatus.Failed, FailReason.Spike);
                    return;
                }
            }

            var goal = Puzzle.Goal;
            if (Sweep.Touches(start, end, goal.Center, goal.Center, r + goal.Radius)) { Finish(RunStatus.Succeeded, FailReason.None); }
        }

        private void Finish(RunStatus status, FailReason reason)
        {
            Status = status;
            Reason = reason;
            _events |= status == RunStatus.Succeeded ? RunEvents.Succeeded : RunEvents.Failed;
        }
    }
}
