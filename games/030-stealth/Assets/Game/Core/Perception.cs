using System;
using IntGeometry;
using TileMotion;

namespace Game.Core
{
    /// <summary>
    /// 視線の判定。2 点を結ぶ閉じた線分が、視線を遮るマス（壁・木箱）の閉じた正方形に 1 点でも触れたら遮られる。
    /// 斜めの角をちょうどかすめる視線は、その角を共有するマスのどれかが遮れば見えない（保守的）。整数だけで判定する。
    /// </summary>
    public static class LineOfSight
    {
        /// <summary><paramref name="from"/> から <paramref name="to"/> が見通せるか。</summary>
        public static bool Clear(FacilityMap map, Vec2i from, Vec2i to)
        {
            if (map == null) { throw new ArgumentNullException(nameof(map)); }

            // 閉じた正方形 [c·T, (c+1)·T] が x を含む列は floor((x - 1) / T)〜floor(x / T) の範囲にある。
            var fromColumn = Units.FloorDiv(Math.Min(from.X, to.X) - 1, Units.Tile);
            var toColumn = Units.FloorDiv(Math.Max(from.X, to.X), Units.Tile);
            var fromRow = Units.FloorDiv(Math.Min(from.Y, to.Y) - 1, Units.Tile);
            var toRow = Units.FloorDiv(Math.Max(from.Y, to.Y), Units.Tile);
            for (var row = fromRow; row <= toRow; row++)
            {
                for (var column = fromColumn; column <= toColumn; column++)
                {
                    if (map.BlocksSight(column, row) && Touches(from, to, new Cell(column, row))) { return false; }
                }
            }

            return true;
        }

        /// <summary>閉じた線分が、マスの閉じた正方形に触れるか（Liang–Barsky の区間を分数で比べる）。</summary>
        public static bool Touches(Vec2i from, Vec2i to, Cell cell)
        {
            var x0 = (long)cell.Column * Units.Tile;
            var y0 = (long)cell.Row * Units.Tile;
            long lowNum = 0;
            long lowDen = 1;
            long highNum = 1;
            long highDen = 1;
            return Clip(from.X, to.X, x0, x0 + Units.Tile, ref lowNum, ref lowDen, ref highNum, ref highDen)
                && Clip(from.Y, to.Y, y0, y0 + Units.Tile, ref lowNum, ref lowDen, ref highNum, ref highDen)
                && (lowNum * highDen) <= (highNum * lowDen);
        }

        private static bool Clip(long a, long b, long min, long max, ref long lowNum, ref long lowDen, ref long highNum, ref long highDen)
        {
            var d = b - a;
            if (d == 0) { return a >= min && a <= max; }
            long lo, hi, den;
            if (d > 0)
            {
                lo = min - a;
                hi = max - a;
                den = d;
            }
            else
            {
                lo = a - max;
                hi = a - min;
                den = -d;
            }

            if (lo * lowDen > lowNum * den)
            {
                lowNum = lo;
                lowDen = den;
            }

            if (hi * highDen < highNum * den)
            {
                highNum = hi;
                highDen = den;
            }

            return true;
        }
    }

    /// <summary>視界の形。視程（単位）と、向きから左右それぞれの角度（<see cref="Trig"/> の角度）。</summary>
    public readonly struct VisionSpec
    {
        public VisionSpec(int range, int halfAngle)
        {
            if (range < 0) { throw new ArgumentOutOfRangeException(nameof(range), range, "視程は 0 以上にしてください。"); }
            if (halfAngle < 0 || halfAngle > Trig.FullTurn / 2) { throw new ArgumentOutOfRangeException(nameof(halfAngle), halfAngle, "半視野角は 0〜半周にしてください。"); }
            Range = range;
            HalfAngle = halfAngle;
        }

        public int Range { get; }

        public int HalfAngle { get; }
    }

    /// <summary>
    /// 視覚のセンサ。状態を持たず、判断（<see cref="GuardBrain"/>）を知らない。
    /// 見えるのは、距離が視程以下、向きとの角が半視野角以下、かつ視線が遮られていないとき。
    /// </summary>
    public static class Vision
    {
        /// <summary>目 <paramref name="eye"/>・向き <paramref name="facing"/> から <paramref name="target"/> が見えるか。</summary>
        public static bool Sees(FacilityMap map, Vec2i eye, int facing, VisionSpec spec, Vec2i target)
        {
            var offset = target - eye;
            if (offset.LengthSquared > (long)spec.Range * spec.Range) { return false; }
            return InCone(facing, spec.HalfAngle, offset) && LineOfSight.Clear(map, eye, target);
        }

        /// <summary>
        /// 向き <paramref name="facing"/> との角が <paramref name="halfAngle"/> 以下か。角度表の値で cosθ ≥ cos(半視野角) を、
        /// 平方して整数で比べる。重なった点（差 0）はいつも内側。
        /// </summary>
        public static bool InCone(int facing, int halfAngle, Vec2i offset)
        {
            if (offset.IsZero || halfAngle >= Trig.FullTurn / 2) { return true; }
            var dot = ((long)Trig.Cos(facing) * offset.X) + ((long)Trig.Sin(facing) * offset.Y);
            var c = (long)Trig.Cos(halfAngle);
            var lhs = dot * dot;
            var rhs = c * c * offset.LengthSquared;
            return c >= 0 ? dot >= 0 && lhs >= rhs : dot >= 0 || lhs <= rhs;
        }

        /// <summary>扇の <paramref name="index"/> 本目（全 <paramref name="count"/> 本）の光線の向き。両端は視野の端。</summary>
        public static int FanAngle(int facing, int halfAngle, int index, int count)
        {
            if (count < 2) { throw new ArgumentOutOfRangeException(nameof(count), count, "光線は 2 本以上にしてください。"); }
            if (index < 0 || index >= count) { throw new ArgumentOutOfRangeException(nameof(index), index, "光線の番号が範囲外です。"); }
            return Trig.Normalize(facing - halfAngle + (int)((2L * halfAngle * index) / (count - 1)));
        }

        /// <summary>
        /// 描画用の扇。各光線について、視線（<see cref="LineOfSight.Clear"/>）が通る最も遠い距離（視程まで）を
        /// <paramref name="distances"/> に書く。判定と同じ関数を使うので、描いた扇と見える範囲が食い違わない。
        /// </summary>
        public static void Fan(FacilityMap map, Vec2i eye, int facing, VisionSpec spec, int[] distances)
        {
            if (distances == null) { throw new ArgumentNullException(nameof(distances)); }
            for (var i = 0; i < distances.Length; i++)
            {
                var angle = FanAngle(facing, spec.HalfAngle, i, distances.Length);
                distances[i] = Reach(map, eye, angle, spec.Range);
            }
        }

        /// <summary>向き <paramref name="angle"/> の光線が見通せる距離（0〜<paramref name="range"/>）。</summary>
        public static int Reach(FacilityMap map, Vec2i eye, int angle, int range)
        {
            if (LineOfSight.Clear(map, eye, PointAt(eye, angle, range))) { return range; }
            var low = 0;
            var high = range;
            while (high - low > 1)
            {
                var mid = low + ((high - low) / 2);
                if (LineOfSight.Clear(map, eye, PointAt(eye, angle, mid))) { low = mid; }
                else { high = mid; }
            }

            return low;
        }

        public static Vec2i PointAt(Vec2i origin, int angle, int length)
        {
            return new(origin.X + IntMath.RoundDiv((long)Trig.Cos(angle) * length, Trig.One), origin.Y + IntMath.RoundDiv((long)Trig.Sin(angle) * length, Trig.One));
        }
    }

    /// <summary>
    /// 聴覚のセンサ。音源のマスから、壁を通らない 4 近傍の経路でのマス数（木箱は通す）を数える。
    /// 届く距離（マス数）以内のマスにいる警備員だけが聞く。壁越しの直線距離では聞かない。
    /// </summary>
    public sealed class Hearing
    {
        private readonly FacilityMap _map;
        private readonly int[] _distance;
        private readonly int[] _queue;

        public Hearing(FacilityMap map)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _distance = new int[map.Count];
            _queue = new int[map.Count];
            Clear();
        }

        /// <summary>今の音源のマス（なければ null）。</summary>
        public Cell? Source { get; private set; }

        public int Radius { get; private set; }

        /// <summary>音源 <paramref name="source"/> から <paramref name="radius"/> マスまで広げる。前の音は消える。</summary>
        public void Spread(Cell source, int radius)
        {
            if (radius < 0) { throw new ArgumentOutOfRangeException(nameof(radius), radius, "届く距離は 0 以上にしてください。"); }
            Clear();
            if (!_map.Contains(source) || _map.BlocksSound(source)) { return; }
            Source = source;
            Radius = radius;
            var head = 0;
            var tail = 0;
            var start = _map.IndexOf(source);
            _distance[start] = 0;
            _queue[tail++] = start;
            while (head < tail)
            {
                var index = _queue[head++];
                var d = _distance[index];
                if (d == radius) { continue; }
                var cell = _map.CellAt(index);
                Visit(new Cell(cell.Column + 1, cell.Row), d + 1, ref tail);
                Visit(new Cell(cell.Column - 1, cell.Row), d + 1, ref tail);
                Visit(new Cell(cell.Column, cell.Row + 1), d + 1, ref tail);
                Visit(new Cell(cell.Column, cell.Row - 1), d + 1, ref tail);
            }
        }

        /// <summary>音源からのマス数。届いていなければ -1。</summary>
        public int DistanceTo(Cell cell) => _map.Contains(cell) ? _distance[_map.IndexOf(cell)] : -1;

        /// <summary>そのマスで聞こえるか。</summary>
        public bool Hears(Cell cell) => DistanceTo(cell) >= 0;

        public void Clear()
        {
            Source = null;
            Radius = 0;
            for (var i = 0; i < _distance.Length; i++) { _distance[i] = -1; }
        }

        private void Visit(Cell cell, int distance, ref int tail)
        {
            if (!_map.Contains(cell) || _map.BlocksSound(cell)) { return; }
            var index = _map.IndexOf(cell);
            if (_distance[index] >= 0) { return; }
            _distance[index] = distance;
            _queue[tail++] = index;
        }
    }

    /// <summary>
    /// 警備員がその Tick に感じたもの。センサ（視覚・聴覚・接触・無線）と体の状態から作り、判断（<see cref="GuardBrain"/>）が読む。
    /// 判断は地図も視線も知らず、これだけで状態を決める。
    /// </summary>
    public readonly struct Percept
    {
        public Percept(Vec2i? seen, int seenDistance, Cell? heard, Cell? radio, Vec2i? touched, Cell? standingOn, int facing)
        {
            if (seenDistance < 0) { throw new ArgumentOutOfRangeException(nameof(seenDistance), seenDistance, "距離は 0 以上にしてください。"); }
            Seen = seen;
            SeenDistance = seenDistance;
            Heard = heard;
            Radio = radio;
            Touched = touched;
            StandingOn = standingOn;
            Facing = facing;
        }

        /// <summary>見えた侵入者の位置（見えなければ null）。</summary>
        public Vec2i? Seen { get; }

        /// <summary>見えた侵入者までの距離（単位、切り捨て）。</summary>
        public int SeenDistance { get; }

        /// <summary>聞こえた足音のマス（聞こえなければ null）。</summary>
        public Cell? Heard { get; }

        /// <summary>他の警備員が発見を知らせた位置（なければ null）。</summary>
        public Cell? Radio { get; }

        /// <summary>前の Tick に侵入者と体が触れた位置（触れていなければ null）。</summary>
        public Vec2i? Touched { get; }

        /// <summary>体がマスの中心にちょうど立っていればそのマス（移動の途中なら null）。</summary>
        public Cell? StandingOn { get; }

        /// <summary>今の向き（見回しの基準に使う）。</summary>
        public int Facing { get; }

        public static Percept Nothing(Cell? standingOn, int facing) => new(null, 0, null, null, null, standingOn, facing);
    }
}
