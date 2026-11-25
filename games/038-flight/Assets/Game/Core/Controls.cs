using System;

namespace Game.Core
{
    /// <summary>
    /// 1 Tick の操縦入力。各軸は -<see cref="Full"/>〜<see cref="Full"/>（キーボードは ±Full、操縦役は途中の値も使う）。
    /// Pitch 正 = 機首上げ、Roll 正 = 右へ傾ける、Yaw 正 = 機首を右へ、Throttle 正 = スロットルを上げる。
    /// </summary>
    public readonly struct Controls : IEquatable<Controls>
    {
        public const int Full = 1024;

        public Controls(int pitch, int roll, int yaw, int throttle)
        {
            Pitch = pitch;
            Roll = roll;
            Yaw = yaw;
            Throttle = throttle;
        }

        public int Pitch { get; }

        public int Roll { get; }

        public int Yaw { get; }

        public int Throttle { get; }

        public static Controls None => default;

        public static bool operator ==(Controls left, Controls right) => left.Equals(right);

        public static bool operator !=(Controls left, Controls right) => !left.Equals(right);

        /// <summary>範囲外の軸があれば例外。</summary>
        public void Validate()
        {
            Check(Pitch, nameof(Pitch));
            Check(Roll, nameof(Roll));
            Check(Yaw, nameof(Yaw));
            Check(Throttle, nameof(Throttle));
        }

        public bool Equals(Controls other) => Pitch == other.Pitch && Roll == other.Roll && Yaw == other.Yaw && Throttle == other.Throttle;

        public override bool Equals(object? obj) => obj is Controls other && Equals(other);

        public override int GetHashCode() => unchecked((Pitch * 397) ^ (Roll * 131) ^ (Yaw * 31) ^ Throttle);

        public override string ToString() => $"(P {Pitch}, R {Roll}, Y {Yaw}, T {Throttle})";

        private static void Check(int value, string name)
        {
            if (value < -Full || value > Full) { throw new ArgumentOutOfRangeException(name, value, $"-{Full}〜{Full} にしてください。"); }
        }
    }
}
