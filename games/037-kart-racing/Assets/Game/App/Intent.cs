using System;

namespace Game.App
{
    public enum IntentKind
    {
        /// <summary>左へ旋回（← または A）。</summary>
        SteerLeftDown,

        SteerLeftUp,

        /// <summary>右へ旋回（→ または D）。</summary>
        SteerRightDown,

        SteerRightUp,

        /// <summary>加速（W または ↑）。</summary>
        AccelDown,

        AccelUp,

        /// <summary>ブレーキ（S または ↓）。</summary>
        BrakeDown,

        BrakeUp,

        /// <summary>ドリフト（Space）。</summary>
        DriftDown,

        DriftUp,

        /// <summary>Enter。開始・再開・再戦。</summary>
        Submit,

        /// <summary>Esc。プレイ中は一時停止、一時停止中は再開、結果ではタイトルへ。</summary>
        Cancel,

        /// <summary>R。一時停止中にレースをやり直す。</summary>
        Retry,

        /// <summary>Backspace。一時停止からタイトルへ戻る。</summary>
        Back,
    }

    /// <summary>
    /// 入力を変換したプレイヤーの意図。キーを 1 本の列に並べ、起きた順に処理する。
    /// <see cref="Time"/> は入力が起きた時刻（秒）で、その時刻を含む Tick の前に反映する。
    /// </summary>
    public readonly struct Intent : IEquatable<Intent>
    {
        public const int MaxKeys = 32;

        public Intent(IntentKind kind, double time)
            : this(kind, time, 0)
        {
        }

        public Intent(IntentKind kind, double time, int key)
        {
            if (!double.IsFinite(time)) { throw new ArgumentOutOfRangeException(nameof(time), time, "時刻は有限の値にしてください。"); }
            if (!Enum.IsDefined(typeof(IntentKind), kind)) { throw new ArgumentOutOfRangeException(nameof(kind), kind, "未定義の操作です。"); }
            if (key < 0 || key >= MaxKeys) { throw new ArgumentOutOfRangeException(nameof(key), key, "キーの番号は 0〜31 にしてください。"); }
            Kind = kind;
            Time = time;
            Key = key;
        }

        public IntentKind Kind { get; }

        public double Time { get; }

        public int Key { get; }

        public int KeyBit => 1 << Key;

        public bool Equals(Intent other) => Kind == other.Kind && Time.Equals(other.Time) && Key == other.Key;

        public override bool Equals(object? obj) => obj is Intent other && Equals(other);

        public override int GetHashCode() => (((int)Kind * 397) ^ Time.GetHashCode()) + Key;

        public static bool operator ==(Intent left, Intent right) => left.Equals(right);

        public static bool operator !=(Intent left, Intent right) => !left.Equals(right);

        public override string ToString() => $"{Kind}[{Key}]@{Time}";
    }
}
