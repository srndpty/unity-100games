using System;
using IntGeometry;

namespace Game.Core
{
    /// <summary>線分の交差（外積で厳密に判定する）。</summary>
    public static class Segment
    {
        /// <summary>
        /// 線分 a0-a1 と b0-b1 が共有点を持つか（端点で触れる・重なる場合も含む）。どちらかの長さが 0 なら false
        /// （なぞりの押しただけの点では切らない）。座標は <see cref="Board.AcceptsSwipePoint"/> の範囲を前提にする（外積が long に収まる）。
        /// </summary>
        public static bool Intersects(Vec2i a0, Vec2i a1, Vec2i b0, Vec2i b1)
        {
            if (a0 == a1 || b0 == b1) { return false; }
            var d1 = Cross(b0, b1, a0);
            var d2 = Cross(b0, b1, a1);
            var d3 = Cross(a0, a1, b0);
            var d4 = Cross(a0, a1, b1);
            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) { return true; }
            return (d1 == 0 && Within(b0, b1, a0)) || (d2 == 0 && Within(b0, b1, a1)) || (d3 == 0 && Within(a0, a1, b0)) || (d4 == 0 && Within(a0, a1, b1));
        }

        /// <summary>(b - a) × (c - a)。</summary>
        private static long Cross(Vec2i a, Vec2i b, Vec2i c) =>
            (((long)b.X - a.X) * ((long)c.Y - a.Y)) - (((long)b.Y - a.Y) * ((long)c.X - a.X));

        /// <summary>同じ直線上の点 c が、a-b を囲む矩形の中にあるか。</summary>
        private static bool Within(Vec2i a, Vec2i b, Vec2i c) =>
            c.X >= Math.Min(a.X, b.X) && c.X <= Math.Max(a.X, b.X) && c.Y >= Math.Min(a.Y, b.Y) && c.Y <= Math.Max(a.Y, b.Y);
    }
}
