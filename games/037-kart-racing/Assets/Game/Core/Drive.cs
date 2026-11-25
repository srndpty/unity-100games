using System;

namespace Game.Core
{
    /// <summary>1 Tick の操作。旋回は -1（右）・0・+1（左）。</summary>
    public readonly struct Drive : IEquatable<Drive>
    {
        public Drive(bool accelerate, bool brake, int steer, bool drift)
        {
            if (steer < -1 || steer > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(steer), steer, "旋回は -1、0、1 のいずれかにしてください。");
            }

            Accelerate = accelerate;
            Brake = brake;
            Steer = steer;
            Drift = drift;
        }

        public static Drive None => default;

        public bool Accelerate { get; }

        public bool Brake { get; }

        public int Steer { get; }

        public bool Drift { get; }

        public bool Equals(Drive other) =>
            Accelerate == other.Accelerate && Brake == other.Brake && Steer == other.Steer && Drift == other.Drift;

        public override bool Equals(object? obj) => obj is Drive other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Accelerate, Brake, Steer, Drift);

        public static bool operator ==(Drive left, Drive right) => left.Equals(right);

        public static bool operator !=(Drive left, Drive right) => !left.Equals(right);
    }
}
