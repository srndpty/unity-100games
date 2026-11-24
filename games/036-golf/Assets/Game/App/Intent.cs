using System;

namespace Game.App
{
    public enum IntentKind
    {
        /// <summary>← を押した。向きを左へ（押し続けると繰り返す）。</summary>
        AimLeftDown,

        AimLeftUp,

        /// <summary>→ を押した。</summary>
        AimRightDown,

        AimRightUp,

        /// <summary>↑：長いクラブへ。</summary>
        ClubUp,

        /// <summary>↓：短いクラブへ。</summary>
        ClubDown,

        /// <summary>W：打点を上へ。</summary>
        ImpactUp,

        /// <summary>S：打点を下へ。</summary>
        ImpactDown,

        /// <summary>A：打点を左へ。</summary>
        ImpactLeft,

        /// <summary>D：打点を右へ。</summary>
        ImpactRight,

        /// <summary>Space。プレイ中はスイング（振り始め・パワー・インパクト）、タイトル・結果では開始。</summary>
        Swing,

        /// <summary>Enter。開始・再開・再戦。</summary>
        Submit,

        /// <summary>Esc。プレイ中は一時停止、一時停止中は再開、結果ではタイトルへ。</summary>
        Cancel,

        /// <summary>R。一時停止中に新しいラウンドをやり直す。</summary>
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
