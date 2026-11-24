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
        public Intent(IntentKind kind, double time)
        {
            if (!double.IsFinite(time)) { throw new ArgumentOutOfRangeException(nameof(time), time, "時刻は有限の値にしてください。"); }
            if (!Enum.IsDefined(typeof(IntentKind), kind)) { throw new ArgumentOutOfRangeException(nameof(kind), kind, "未定義の操作です。"); }
            Kind = kind;
            Time = time;
        }

        public IntentKind Kind { get; }

        public double Time { get; }

        public bool Equals(Intent other) => Kind == other.Kind && Time.Equals(other.Time);

        public override bool Equals(object? obj) => obj is Intent other && Equals(other);

        public override int GetHashCode() => ((int)Kind * 397) ^ Time.GetHashCode();

        public static bool operator ==(Intent left, Intent right) => left.Equals(right);

        public static bool operator !=(Intent left, Intent right) => !left.Equals(right);

        public override string ToString() => $"{Kind}@{Time}";
    }
}
