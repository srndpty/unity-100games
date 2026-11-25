using System;

namespace Game.App
{
    public enum IntentKind
    {
        /// <summary>機首上げ（S または ↓）。</summary>
        PitchUpDown,

        PitchUpUp,

        /// <summary>機首下げ（W または ↑）。</summary>
        PitchDownDown,

        PitchDownUp,

        /// <summary>左ロール（A または ←）。</summary>
        RollLeftDown,

        RollLeftUp,

        /// <summary>右ロール（D または →）。</summary>
        RollRightDown,

        RollRightUp,

        /// <summary>左ヨー（Q）。</summary>
        YawLeftDown,

        YawLeftUp,

        /// <summary>右ヨー（E）。</summary>
        YawRightDown,

        YawRightUp,

        /// <summary>スロットルを上げる（Shift）。</summary>
        ThrottleUpDown,

        ThrottleUpUp,

        /// <summary>スロットルを下げる（Ctrl）。</summary>
        ThrottleDownDown,

        ThrottleDownUp,

        /// <summary>ミサイル（Space）。押した瞬間に 1 回だけ効く。</summary>
        FireDown,

        FireUp,

        /// <summary>目標の切り替え（Tab）。押した瞬間に 1 回だけ効く。</summary>
        CycleDown,

        CycleUp,

        /// <summary>Enter。開始・再開・再挑戦。</summary>
        Submit,

        /// <summary>Esc。プレイ中は一時停止、一時停止中は再開、結果ではタイトルへ。</summary>
        Cancel,

        /// <summary>R。一時停止中にやり直す。</summary>
        Retry,

        /// <summary>Backspace。一時停止からタイトルへ戻る。</summary>
        Back,
    }

    /// <summary>
    /// 入力を変換したプレイヤーの意図。キーを 1 本の列に並べ、起きた順に処理する。
    /// <see cref="Time"/> は入力が起きた時刻（秒）で、その時刻を含む Tick の前に反映する。
    /// <see cref="Key"/> は同じ操作に割り当てた物理キーの番号で、押している印をキーごとに持つ（035/037）。
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
