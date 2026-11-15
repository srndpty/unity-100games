using System;

namespace Game.Core
{
    public readonly struct Point
    {
        public Point(float x, float y) { X = x; Y = y; }
        public float X { get; }
        public float Y { get; }
    }

    // Tick内の時刻は0〜1。端に着いた時刻からは静止する。
    public readonly struct Motion
    {
        public Motion(Point start, float dx, float dy, float stop = 1f)
        {
            Start = start; Dx = dx; Dy = dy; Stop = stop;
        }
        public Point Start { get; }
        public float Dx { get; }
        public float Dy { get; }
        public float Stop { get; }
        public Point At(float time) => new(Start.X + Dx * Math.Min(time, Stop), Start.Y + Dy * Math.Min(time, Stop));

        public static float? Contact(Motion a, Motion b, float halfWidth, float halfHeight)
        {
            var split1 = Math.Min(a.Stop, b.Stop);
            var split2 = Math.Max(a.Stop, b.Stop);
            return Segment(a, b, halfWidth, halfHeight, 0, split1)
                ?? Segment(a, b, halfWidth, halfHeight, split1, split2)
                ?? Segment(a, b, halfWidth, halfHeight, split2, 1);
        }

        private static float? Segment(Motion a, Motion b, float hw, float hh, float from, float to)
        {
            var p = a.At(from); var q = b.At(from);
            var endA = a.At(to); var endB = b.At(to);
            var enter = 0f; var exit = 1f;
            if (!Axis(p.X - q.X, endA.X - endB.X - p.X + q.X, hw, ref enter, ref exit)
                || !Axis(p.Y - q.Y, endA.Y - endB.Y - p.Y + q.Y, hh, ref enter, ref exit)) { return null; }
            return from + (to - from) * enter;
        }

        private static bool Axis(float offset, float delta, float half, ref float enter, ref float exit)
        {
            if (delta == 0) { return Math.Abs(offset) <= half; }
            var first = (-half - offset) / delta;
            var last = (half - offset) / delta;
            if (first > last) { var swap = first; first = last; last = swap; }
            enter = Math.Max(enter, first); exit = Math.Min(exit, last);
            return enter <= exit;
        }
    }
}
