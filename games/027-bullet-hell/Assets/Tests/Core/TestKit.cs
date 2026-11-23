using System;

namespace Game.Core.Tests
{
    /// <summary>テストの共通の部品。</summary>
    internal static class TestKit
    {
        /// <summary>px の位置を単位へ。</summary>
        public static Vec2i Px(int x, int y) => new(x * Field.Unit, y * Field.Unit);

        /// <summary>px/Tick の速さを単位へ。</summary>
        public static int Speed(double pxPerTick) => (int)Math.Round(pxPerTick * BulletPool.SpeedOne);

        /// <summary>度を角度の単位へ。</summary>
        public static int Degrees(double degrees) => (int)Math.Round(degrees * Trig.FullTurn / 360.0);

        public static BulletSpawn Bullet(
            Vec2i origin,
            int angle,
            int speed,
            int accel = 0,
            int? maxSpeed = null,
            int curve = 0,
            int life = 0,
            BulletShape shape = BulletShape.Small) =>
            new(origin, angle, speed, accel, maxSpeed ?? Math.Max(speed, 16 * BulletPool.SpeedOne), curve, life, shape, BulletColor.Red);

        /// <summary>
        /// 弾 1 発の素朴な参照実装（置き場とは別に書いた、1 発ずつ進める版）。
        /// 置き場の SoA の実装と同じ弾道式を、配列や詰め直しなしで追う。
        /// </summary>
        public sealed class ReferenceBullet
        {
            private long _x;
            private long _y;
            private int _angle;
            private int _speed;
            private readonly int _accel;
            private readonly int _maxSpeed;
            private readonly int _curve;

            public ReferenceBullet(BulletSpawn spawn)
            {
                _x = (long)spawn.Origin.X << BulletPool.PositionShift;
                _y = (long)spawn.Origin.Y << BulletPool.PositionShift;
                _angle = spawn.Angle;
                _speed = spawn.Speed;
                _accel = spawn.Accel;
                _maxSpeed = spawn.MaxSpeed;
                _curve = spawn.Curve;
            }

            public Vec2i Position => new((int)(_x >> BulletPool.PositionShift), (int)(_y >> BulletPool.PositionShift));

            public void Step()
            {
                _angle = (_angle + _curve) % Trig.FullTurn;
                if (_angle < 0) { _angle += Trig.FullTurn; }
                _speed = Math.Min(Math.Max(_speed + _accel, 0), _maxSpeed);
                _x += (long)_speed * Trig.Cos(_angle);
                _y += (long)_speed * Trig.Sin(_angle);
            }
        }
    }
}
