using System;
using IntGeometry;

namespace Game.Core
{
    /// <summary>1 打の決め事。向きは <c>Trig</c> の単位、パワーは ‰、インパクトのずれは Tick（負は早い）。</summary>
    public readonly struct ShotSetup : IEquatable<ShotSetup>
    {
        public ShotSetup(int club, int aim, int impactX, int impactY, int power, int error, bool mishit)
        {
            if (club < 0 || club >= Clubs.Count) { throw new ArgumentOutOfRangeException(nameof(club), club, "未定義のクラブです。"); }
            if (Math.Abs(impactX) > GolfConfig.MaxImpact || Math.Abs(impactY) > GolfConfig.MaxImpact) { throw new ArgumentOutOfRangeException(nameof(impactX), "打点は −2〜+2 にしてください。"); }
            if (power < 0 || power > 1000) { throw new ArgumentOutOfRangeException(nameof(power), power, "パワーは 0〜1000 にしてください。"); }
            Club = club;
            Aim = Trig.Normalize(aim);
            ImpactX = impactX;
            ImpactY = impactY;
            Power = power;
            Error = error;
            Mishit = mishit;
        }

        public int Club { get; }

        public int Aim { get; }

        /// <summary>打点の左右（正は右。右へ曲がる）。</summary>
        public int ImpactX { get; }

        /// <summary>打点の上下（正は上。低く出て転がる、負は高く出て止まる）。</summary>
        public int ImpactY { get; }

        public int Power { get; }

        public int Error { get; }

        public bool Mishit { get; }

        /// <summary>ずれ 0・ミスなしの同じ打ち方（予測線に使う）。</summary>
        public ShotSetup Ideal => new(Club, Aim, ImpactX, ImpactY, Power, 0, false);

        public bool Equals(ShotSetup other) =>
            Club == other.Club && Aim == other.Aim && ImpactX == other.ImpactX && ImpactY == other.ImpactY && Power == other.Power && Error == other.Error && Mishit == other.Mishit;

        public override bool Equals(object? obj) => obj is ShotSetup other && Equals(other);

        public override int GetHashCode() => unchecked((((((((Club * 397) ^ Aim) * 397) ^ ImpactX) * 397) ^ ImpactY) * 397) ^ (Power * 31) ^ (Error * 7) ^ (Mishit ? 1 : 0));

        public static bool operator ==(ShotSetup left, ShotSetup right) => left.Equals(right);

        public static bool operator !=(ShotSetup left, ShotSetup right) => !left.Equals(right);
    }

    /// <summary>打ち出し：決め事と置き場所から、打った直後の球の状態を作る（純粋関数）。</summary>
    public static class Launch
    {
        public static BallState From(GolfConfig config, ShotSetup setup, Vec2i position, Surface lie)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            if (lie == Surface.Water || lie == Surface.OutOfBounds) { throw new ArgumentOutOfRangeException(nameof(lie), lie, "池・OB からは打てません。"); }
            var power = setup.Mishit ? setup.Power * config.MishitPower / 1000 : setup.Power;
            var speed = (int)((long)config.ClubSpeed[setup.Club] * power / 1000 * config.LiePower[(int)lie] / 1000);
            var ball = new BallState { Position = position, FirstBite = Units.Ratio };
            if (setup.Club == Clubs.Putter)
            {
                // 遅い（ずれが正）と右＝時計回り。
                var angle = setup.Aim - (setup.Error * config.PuttAnglePerErrorTick);
                ball.Phase = BallPhase.Rolling;
                ball.Velocity = Direction(angle, speed);
                return ball;
            }

            var loft = config.ClubLoft[setup.Club] - (setup.ImpactY * config.LoftPerImpact);
            var horizontal = (int)((long)speed * Trig.Cos(loft) / Trig.One);
            ball.Phase = BallPhase.Flying;
            ball.Velocity = Direction(setup.Aim, horizontal);
            ball.VelocityZ = (int)((long)speed * Trig.Sin(loft) / Trig.One);
            ball.Spin = (setup.ImpactX * config.SpinPerImpact) + (setup.Error * config.SpinPerErrorTick);
            ball.FirstBite = IntMath.Clamp(config.ClubBite[setup.Club] + (setup.ImpactY * config.BitePerImpact), 0, Units.Ratio);
            return ball;
        }

        /// <summary>向き <paramref name="angle"/> で長さ <paramref name="length"/> のベクトル。</summary>
        public static Vec2i Direction(int angle, int length) =>
            new((int)((long)length * Trig.Cos(angle) / Trig.One), (int)((long)length * Trig.Sin(angle) / Trig.One));
    }
}
