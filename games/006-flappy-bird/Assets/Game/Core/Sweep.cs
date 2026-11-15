using System;

namespace Game.Core
{
    /// <summary>移動する円と矩形の厳密な掃引。辺の帯と四隅の円の和を調べる。</summary>
    public static class Sweep
    {
        public static float CircleRectangle(float x, float y, float dx, float dy, float radius, float left, float right, float bottom, float top)
        {
            var nearX = Math.Clamp(x, left, right); var nearY = Math.Clamp(y, bottom, top);
            if ((x - nearX) * (x - nearX) + (y - nearY) * (y - nearY) <= radius * radius) { return 0; }
            var time = float.PositiveInfinity;
            Edge(x, dx, left - radius, y, dy, bottom, top, ref time);
            Edge(x, dx, right + radius, y, dy, bottom, top, ref time);
            Edge(y, dy, bottom - radius, x, dx, left, right, ref time);
            Edge(y, dy, top + radius, x, dx, left, right, ref time);
            Corner(x - left, y - bottom, dx, dy, radius, ref time);
            Corner(x - left, y - top, dx, dy, radius, ref time);
            Corner(x - right, y - bottom, dx, dy, radius, ref time);
            Corner(x - right, y - top, dx, dy, radius, ref time);
            return time;
        }
        private static void Edge(float start, float delta, float edge, float other, float otherDelta, float min, float max, ref float time)
        {
            if (delta == 0) { return; }
            var t = (edge - start) / delta; var p = other + otherDelta * t;
            if (t >= 0 && t <= 1 && p >= min && p <= max) { time = Math.Min(time, t); }
        }
        private static void Corner(double x, double y, double dx, double dy, double radius, ref float time)
        {
            var a = dx * dx + dy * dy; var b = x * dx + y * dy;
            var c = x * x + y * y - radius * radius; var discriminant = b * b - a * c;
            if (a == 0 || discriminant < 0) { return; }
            var t = (-b - Math.Sqrt(discriminant)) / a;
            if (t >= 0 && t <= 1) { time = Math.Min(time, (float)t); }
        }
    }
}
