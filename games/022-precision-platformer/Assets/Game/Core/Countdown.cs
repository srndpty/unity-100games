using System;

namespace Game.Core
{
    /// <summary>
    /// 残り Tick 数で表す猶予。接地猶予と入力バッファを「何 Tick 前に起きたか」で明示的に管理する。
    /// 値型なので、持ち主の複製（<see cref="Player.Clone"/>）でそのまま複製される。
    /// </summary>
    public struct Countdown : IEquatable<Countdown>
    {
        public Countdown(int remaining)
        {
            if (remaining < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(remaining), remaining, "0 以上にしてください。");
            }

            Remaining = remaining;
        }

        /// <summary>この Tick を含めて、あと何 Tick 有効か。</summary>
        public int Remaining { get; private set; }

        public bool IsActive => Remaining > 0;

        /// <summary>この Tick から <paramref name="ticks"/> Tick の間、有効にする。</summary>
        public void Arm(int ticks)
        {
            if (ticks < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "0 以上にしてください。");
            }

            Remaining = ticks;
        }

        /// <summary>1 Tick 経過させる。</summary>
        public void Tick()
        {
            if (Remaining > 0)
            {
                Remaining--;
            }
        }

        public void Clear() => Remaining = 0;

        public bool Equals(Countdown other) => Remaining == other.Remaining;

        public override bool Equals(object? obj) => obj is Countdown other && Equals(other);

        public override int GetHashCode() => Remaining;

        public override string ToString() => $"Countdown({Remaining})";

        public static bool operator ==(Countdown left, Countdown right) => left.Equals(right);

        public static bool operator !=(Countdown left, Countdown right) => !left.Equals(right);
    }
}
