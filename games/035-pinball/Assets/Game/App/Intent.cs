using System;

namespace Game.App
{
    public enum IntentKind
    {
        /// <summary>左フリッパーのキーを押した（Z・←・左 Shift）。</summary>
        LeftDown,

        LeftUp,

        /// <summary>右フリッパーのキーを押した（/・→・右 Shift）。</summary>
        RightDown,

        RightUp,

        /// <summary>Space を押した。プレイ中はプランジャーを引く、タイトル・結果では開始。</summary>
        PlungerDown,

        PlungerUp,

        /// <summary>Enter。開始・再開・再戦。</summary>
        Submit,

        /// <summary>Esc。プレイ中は一時停止、一時停止中は再開、結果ではタイトルへ。</summary>
        Cancel,

        /// <summary>R。一時停止中に新しい試合をやり直す。</summary>
        Retry,

        /// <summary>Backspace。一時停止からタイトルへ戻る。</summary>
        Back,
    }

    /// <summary>
    /// 入力を変換したプレイヤーの意図。キーを 1 本の列に並べ、起きた順に処理する（012）。
    /// <see cref="Time"/> は入力が起きた時刻（秒）で、どの Tick の境目で効くかを決める（022）。
    /// </summary>
    public readonly struct Intent : IEquatable<Intent>
    {
        /// <summary>1 つの操作に割り当てられるキーの数の上限（キーの番号は 0〜31）。</summary>
        public const int MaxKeys = 32;

        public Intent(IntentKind kind, double time)
            : this(kind, time, 0)
        {
        }

        /// <param name="key">押下・解放の意図で、その操作に割り当てたキーの番号（0〜31）。同じ操作の別のキーは別々に押す・離す。</param>
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

        /// <summary>操作に割り当てたキーの番号（押下・解放の意図だけが使う）。</summary>
        public int Key { get; }

        /// <summary>キーの番号のビット。</summary>
        public int KeyBit => 1 << Key;

        public bool Equals(Intent other) => Kind == other.Kind && Time.Equals(other.Time) && Key == other.Key;

        public override bool Equals(object? obj) => obj is Intent other && Equals(other);

        public override int GetHashCode() => (((int)Kind * 397) ^ Time.GetHashCode()) + Key;

        public static bool operator ==(Intent left, Intent right) => left.Equals(right);

        public static bool operator !=(Intent left, Intent right) => !left.Equals(right);

        public override string ToString() => $"{Kind}[{Key}]@{Time}";
    }
}
