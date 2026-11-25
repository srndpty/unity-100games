using System;
using IntGeometry;

namespace Game.Core
{
    /// <summary>円と静止した壁の線分。1 mm 刻みの整数距離で最初の食い込みを探し、端は範囲に入るとき <c>Sweep.First</c> でも確かめる。</summary>
    public static class WallContact
    {
        public readonly struct Hit
        {
            public Hit(bool hit, int index, int numerator, int denominator)
            {
                IsHit = hit;
                Index = index;
                Numerator = numerator;
                Denominator = denominator <= 0 ? 1 : denominator;
            }

            public bool IsHit { get; }

            public int Index { get; }

            /// <summary>移動の始点を 0、終点を <see cref="Denominator"/> とした接触の時刻。</summary>
            public int Numerator { get; }

            public int Denominator { get; }

            public bool IsEarlierThan(Hit other)
            {
                if (!IsHit) { return false; }
                if (!other.IsHit) { return true; }
                return (long)Numerator * other.Denominator < (long)other.Numerator * Denominator;
            }
        }

        /// <summary>移動が壁に食い込む最初の接触。ちょうど接するだけでは触れない。</summary>
        public static Hit First(Vec2i start, Vec2i delta, WallSeg[] walls, int radius)
        {
            if (walls == null) { throw new ArgumentNullException(nameof(walls)); }
            if (radius <= 0) { throw new ArgumentOutOfRangeException(nameof(radius), radius, "半径は正にしてください。"); }

            var best = new Hit(false, -1, 1, 1);
            var minX = Math.Min(start.X, start.X + delta.X) - radius;
            var maxX = Math.Max(start.X, start.X + delta.X) + radius;
            var minY = Math.Min(start.Y, start.Y + delta.Y) - radius;
            var maxY = Math.Max(start.Y, start.Y + delta.Y) + radius;
            var samples = Math.Max(Math.Abs(delta.X), Math.Abs(delta.Y));
            if (samples < 1) { samples = 1; }

            for (var i = 0; i < walls.Length; i++)
            {
                var wall = walls[i];
                if (wall.MaxX < minX || wall.MinX > maxX || wall.MaxY < minY || wall.MinY > maxY) { continue; }

                var hit = Against(start, delta, wall, radius, samples, i);
                if (hit.IsEarlierThan(best)) { best = hit; }
            }

            return best;
        }

        /// <summary>内側の法線の方向へ、半径の位置まで押し出す。すでに外なら動かさない。</summary>
        public static Vec2i PushOut(Vec2i point, WallSeg wall, int radius)
        {
            var closest = Closest(point, wall.A, wall.B);
            long dx = point.X - closest.X;
            long dy = point.Y - closest.Y;
            var dist2 = (dx * dx) + (dy * dy);
            if (dist2 >= (long)radius * radius) { return point; }
            var signed = ((dx * wall.Inward.X) + (dy * wall.Inward.Y)) / Trig.One;
            if (signed < radius)
            {
                var push = radius + 1 - signed;
                return new Vec2i(
                    point.X + CeilAway(wall.Inward.X * push, Trig.One),
                    point.Y + CeilAway(wall.Inward.Y * push, Trig.One));
            }

            var away = new Vec2i((int)dx, (int)dy);
            if (away.IsZero) { away = wall.Inward; }
            return closest + IntMath.Scale(away, radius + 1);
        }

        private static int CeilAway(long numerator, long denominator)
        {
            if (numerator >= 0) { return (int)((numerator + denominator - 1) / denominator); }
            return (int)-(((-numerator) + denominator - 1) / denominator);
        }

        private static Vec2i Closest(Vec2i point, Vec2i a, Vec2i b)
        {
            long vx = b.X - a.X;
            long vy = b.Y - a.Y;
            var len2 = (vx * vx) + (vy * vy);
            if (len2 <= 0) { return a; }
            var dot = (((long)point.X - a.X) * vx) + (((long)point.Y - a.Y) * vy);
            if (dot <= 0) { return a; }
            if (dot >= len2) { return b; }
            return new Vec2i(a.X + (int)((vx * dot) / len2), a.Y + (int)((vy * dot) / len2));
        }

        /// <summary>壁の内側へ向かう成分を捨て、接線方向の移動だけを残す。</summary>
        public static Vec2i Slide(Vec2i delta, Vec2i inward)
        {
            var dot = ((long)delta.X * inward.X) + ((long)delta.Y * inward.Y);
            if (dot >= 0) { return delta; }
            var scale = Trig.One * (long)Trig.One;
            return new Vec2i(
                delta.X - (int)((inward.X * dot) / scale),
                delta.Y - (int)((inward.Y * dot) / scale));
        }

        /// <summary>点が線分から半径より近いか。</summary>
        public static bool CloserThan(Vec2i point, Vec2i a, Vec2i b, int radius)
        {
            long vx = b.X - a.X;
            long vy = b.Y - a.Y;
            var len2 = (vx * vx) + (vy * vy);
            long wx = point.X - a.X;
            long wy = point.Y - a.Y;
            var limit = (long)radius * radius;
            if (len2 <= 0) { return ((wx * wx) + (wy * wy)) < limit; }
            var dot = (wx * vx) + (wy * vy);
            if (dot <= 0) { return ((wx * wx) + (wy * wy)) < limit; }
            if (dot >= len2)
            {
                long ux = point.X - b.X;
                long uy = point.Y - b.Y;
                return ((ux * ux) + (uy * uy)) < limit;
            }

            var cross = (wx * vy) - (wy * vx);
            return (cross * cross) < (limit * len2);
        }

        private static Hit Against(Vec2i start, Vec2i delta, WallSeg wall, int radius, int samples, int index)
        {
            var best = new Hit(false, index, 1, 1);
            for (var k = 0; k <= samples; k++)
            {
                var point = new Vec2i(
                    start.X + (int)(((long)delta.X * k) / samples),
                    start.Y + (int)(((long)delta.Y * k) / samples));
                if (CloserThan(point, wall.A, wall.B, radius))
                {
                    best = new Hit(true, index, k, samples);
                    break;
                }
            }

            best = EarlierEndpoint(start, delta, wall.A, radius, index, best);
            best = EarlierEndpoint(start, delta, wall.B, radius, index, best);
            return best;
        }

        private static Hit EarlierEndpoint(Vec2i start, Vec2i delta, Vec2i end, int radius, int index, Hit best)
        {
            var relative = end - start;
            var endPoint = start + delta;
            if (!Fits(relative) || !Fits(delta) || !Fits(endPoint - start)) { return best; }
            Touch touch;
            try
            {
                touch = Sweep.First(Vec2i.Zero, delta, relative, relative, radius);
            }
            catch (ArgumentOutOfRangeException)
            {
                return best;
            }

            if (!touch.Hit || touch.Denominator > int.MaxValue || touch.Numerator > int.MaxValue) { return best; }
            var hit = new Hit(true, index, (int)touch.Numerator, (int)touch.Denominator);
            return hit.IsEarlierThan(best) ? hit : best;
        }

        private static bool Fits(Vec2i value) =>
            Math.Abs(value.X) <= Sweep.FirstLimit && Math.Abs(value.Y) <= Sweep.FirstLimit;
    }
}
