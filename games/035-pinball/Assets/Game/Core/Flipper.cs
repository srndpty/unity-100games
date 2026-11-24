using System;
using IntGeometry;
using TileMotion;

namespace Game.Core
{
    /// <summary>
    /// フリッパーのアクチュエータ。押している間は上の角度へ向けて <see cref="PinballConfig.FlipAccel"/> で加速し
    /// <see cref="PinballConfig.FlipMaxSpeed"/> で頭打ち、上の角度で止まって保つ。離すと休みの角度へ同じように戻る。
    /// 1 区間ごとに <see cref="Step"/> する。押した Tick の最初の区間から加速する（遅れ 0）。
    /// </summary>
    public sealed class Flipper
    {
        public Flipper(FlipperSpec spec)
        {
            Spec = spec;
            Angle = spec.RestAngle;
        }

        public FlipperSpec Spec { get; }

        public int Angle { get; private set; }

        /// <summary>角速度（1 区間あたり、反時計回りが正）。</summary>
        public int Speed { get; private set; }

        /// <summary>直前の区間に実際に回った角度（端で止まったときは止まるまでの分）。面の速さに使う。</summary>
        public int LastDelta { get; private set; }

        public bool Held { get; set; }

        public Vec2i Tip => Spec.TipAt(Angle);

        /// <summary>上の角度にあるか。</summary>
        public bool IsUp => Angle == Spec.UpAngle;

        public Flipper Clone() => (Flipper)MemberwiseClone();

        public void Step(PinballConfig config)
        {
            var toward = Held ? Spec.UpDirection : -Spec.UpDirection;
            var accel = Held ? config.FlipAccel : config.ReturnAccel;
            var max = Held ? config.FlipMaxSpeed : config.ReturnMaxSpeed;
            Speed = IntMath.Clamp(Speed + (toward * accel), -max, max);

            var low = Math.Min(Spec.RestAngle, Spec.UpAngle);
            var high = Math.Max(Spec.RestAngle, Spec.UpAngle);
            var next = Angle + Speed;
            if (next <= low || next >= high)
            {
                next = IntMath.Clamp(next, low, high);
                Speed = 0;
            }

            LastDelta = next - Angle;
            Angle = next;
        }

        /// <summary>
        /// 点 <paramref name="point"/>（フリッパーの上）が直前の区間に動いた速さ（速度の単位）。ω × r：
        /// 角度 1 単位 = 2π/65536 ラジアンなので、v = perp(r) × LastDelta × 2π × <see cref="Board.VelocityScale"/> / 65536。
        /// </summary>
        public Vec2i SurfaceVelocity(Vec2i point)
        {
            var r = point - Spec.Pivot;
            const long Scale = 1L << 24;
            var factor = (long)LastDelta * Table.TwoPiQ16;
            return new Vec2i(IntMath.RoundDiv(-(long)r.Y * factor, Scale), IntMath.RoundDiv((long)r.X * factor, Scale));
        }

        public void MixInto(ref StateHash hash)
        {
            hash.Mix(Angle);
            hash.Mix(Speed);
            hash.Mix(LastDelta);
            hash.Mix(Held);
        }
    }
}
