using IntGeometry;

namespace Game.Core
{
    /// <summary>整数の外積と向き。0 は直線上、正は左（反時計回り）。</summary>
    internal static class Geom
    {
        public static long Cross(Vec2i a, Vec2i b) => ((long)a.X * b.Y) - ((long)a.Y * b.X);

        public static int Orient(Vec2i a, Vec2i b, Vec2i c)
        {
            var value = Cross(b - a, c - a);
            if (value > 0) { return 1; }
            if (value < 0) { return -1; }
            return 0;
        }

        public static int Sign(long value)
        {
            if (value > 0) { return 1; }
            if (value < 0) { return -1; }
            return 0;
        }

        /// <summary>from から to への最短の角度差（-半周〜半周）。</summary>
        public static int AngleDelta(int from, int to)
        {
            var delta = Trig.Normalize(to - from);
            if (delta > (Trig.FullTurn / 2)) { delta -= Trig.FullTurn; }
            return delta;
        }

        public static int Mod(int index, int count) => ((index % count) + count) % count;
    }
}
