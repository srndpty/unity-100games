using System;

namespace Game.Core
{
    public readonly struct Point
    {
        public Point(float x, float y) { X = x; Y = y; }
        public float X { get; }
        public float Y { get; }
        public float Length => MathF.Sqrt(X * X + Y * Y);
        public Point Plus(Point other) => new(X + other.X, Y + other.Y);
        public Point Scale(float scale) => new(X * scale, Y * scale);
        public static Point Forward(float angle) => new(-MathF.Sin(angle), MathF.Cos(angle));
    }

    public static class Torus
    {
        public static float Wrap(float value, float period)
        {
            var result = (value + period / 2) % period;
            if (result < 0) { result += period; }
            return result - period / 2;
        }
        public static Point Wrap(Point p) => new(Wrap(p.X, AsteroidsConfig.Width), Wrap(p.Y, AsteroidsConfig.Height));
        public static Point Difference(Point to, Point from) => Wrap(new Point(to.X - from.X, to.Y - from.Y));

        // 正規化前の変位を渡す。設定上限により各軸の相対移動+半径は1周期未満。
        public static float? Contact(Point a, Point da, Point b, Point db, float radius)
        {
            double best = double.PositiveInfinity;
            var dx = (double)da.X - db.X; var dy = (double)da.Y - db.Y;
            var speed = dx * dx + dy * dy;
            for (var y = -1; y <= 1; y++)
            {
                for (var x = -1; x <= 1; x++)
                {
                    var px = (double)a.X - b.X + x * AsteroidsConfig.Width;
                    var py = (double)a.Y - b.Y + y * AsteroidsConfig.Height;
                    var c = px * px + py * py - (double)radius * radius;
                    if (c <= 0) { best = 0; continue; }
                    if (speed == 0) { continue; }
                    var dot = px * dx + py * dy;
                    var discriminant = dot * dot - speed * c;
                    if (dot >= 0 || discriminant < 0) { continue; }
                    var time = c / (-dot + Math.Sqrt(discriminant));
                    if (time >= 0 && time <= 1) { best = Math.Min(best, time); }
                }
            }
            return double.IsPositiveInfinity(best) ? null : (float)best;
        }
    }
}
