using System.Numerics;

namespace Game.Core
{
    /// <summary>
    /// ミサイルの誘導。目標とミサイルの位置・速度だけから、欲しい横向きの加速（µm/Tick²）を返す純粋関数。
    /// 曲げられる量の上限や速さはミサイルの運動（<see cref="MissileBody"/>）が決める。
    /// </summary>
    public interface IGuidance
    {
        Vec3l Command(Vec3l missilePosition, Vec3l missileVelocity, Vec3l targetPosition, Vec3l targetVelocity);
    }

    /// <summary>
    /// 比例航法（pure PN）。視線の回転の角速度 ω = (r × v) / |r|² に対して、a = N · ω × V（V はミサイルの速度）。
    /// 視線が回らなければ（衝突コースなら）曲げない。中間値は mm 単位に直し、最後の割り算だけ BigInteger で行う。
    /// </summary>
    public sealed class ProNav : IGuidance
    {
        public ProNav(int gain)
        {
            Gain = gain;
        }

        public int Gain { get; }

        public Vec3l Command(Vec3l missilePosition, Vec3l missileVelocity, Vec3l targetPosition, Vec3l targetVelocity)
        {
            var r = ToMillimeters(targetPosition - missilePosition);
            var v = ToMillimeters(targetVelocity - missileVelocity);
            var m = ToMillimeters(missileVelocity);
            var r2 = r.LengthSquared;
            if (r2 == 0) { return Vec3l.Zero; }

            // (r × v) × V。mm 単位で r ≤ 2·10^7、v・V ≤ 2·10^4 なので long に収まる。
            var turn = Cross(Cross(r, v), m);
            var scale = (BigInteger)Gain * Units.PerMillimeter;
            return new Vec3l(Divide(turn.X * scale, r2), Divide(turn.Y * scale, r2), Divide(turn.Z * scale, r2));
        }

        internal static Vec3l Cross(Vec3l a, Vec3l b) => new(
            (a.Y * b.Z) - (a.Z * b.Y),
            (a.Z * b.X) - (a.X * b.Z),
            (a.X * b.Y) - (a.Y * b.X));

        private static Vec3l ToMillimeters(Vec3l value) =>
            new(value.X / Units.PerMillimeter, value.Y / Units.PerMillimeter, value.Z / Units.PerMillimeter);

        /// <summary>割り算の結果を ±2^40 に収める（ごく近いと要求が巨大になる。運動の上限で切られるので向きだけが意味を持つ）。</summary>
        private static long Divide(BigInteger numerator, long denominator)
        {
            var value = BigInteger.Divide(numerator, denominator);
            if (value > CommandLimit) { return CommandLimit; }
            if (value < -CommandLimit) { return -CommandLimit; }
            return (long)value;
        }

        private const long CommandLimit = 1L << 40;
    }
}
