using System;

namespace Game.App
{
    /// <summary>押している間だけ効く操作。</summary>
    public enum Control
    {
        Left,
        Right,

        /// <summary>奥（ネットの向き）へ。</summary>
        Up,

        /// <summary>手前へ。</summary>
        Down,

        /// <summary>ストローク。サーブ待ちではトス、トス中はサーブ。</summary>
        Stroke,

        /// <summary>ロブ。</summary>
        Lob,
    }

    public enum IntentKind
    {
        /// <summary>決定。タイトル・結果では試合を始め、一時停止中は再開する。</summary>
        Submit,

        TogglePause,

        /// <summary>最初から始め直す（一時停止中・結果）。</summary>
        Restart,

        /// <summary>タイトルへ戻る（一時停止中・結果）。</summary>
        BackToTitle,

        /// <summary>終了（タイトル）。アプリの終了は Unity 層が行う。</summary>
        Quit,

        /// <summary>操作キーを押した。</summary>
        ControlDown,

        /// <summary>操作キーを離した。</summary>
        ControlUp,
    }

    /// <summary>
    /// 入力を変換したプレイヤーの意図。キーの押下・解放と画面の操作を 1 本の列に並べ、起きた順に処理する。
    /// <see cref="Time"/> は入力が起きた時刻（秒）。フレームの区切りではなく、この時刻でどの Tick に効くかを決める（打点の時機がフレームレートに依らない）。
    /// </summary>
    public readonly struct Intent : IEquatable<Intent>
    {
        private Intent(IntentKind kind, Control control, double time)
        {
            if (!double.IsFinite(time))
            {
                throw new ArgumentOutOfRangeException(nameof(time), time, "時刻は有限の値にしてください。");
            }

            Kind = kind;
            Control = control;
            Time = time;
        }

        public IntentKind Kind { get; }

        /// <summary>ControlDown と ControlUp の対象。それ以外では使わない。</summary>
        public Control Control { get; }

        /// <summary>入力が起きた時刻（秒）。<see cref="TennisFlow.Advance"/> に渡す時刻と同じ時計。</summary>
        public double Time { get; }

        public static Intent Of(IntentKind kind, double time)
        {
            if (kind == IntentKind.ControlDown || kind == IntentKind.ControlUp)
            {
                throw new ArgumentException("キーの押下・解放は Down / Up で作ってください。", nameof(kind));
            }

            if (!Enum.IsDefined(typeof(IntentKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "未定義の操作です。");
            }

            return new(kind, default, time);
        }

        public static Intent Down(Control control, double time) => new(IntentKind.ControlDown, Require(control), time);

        public static Intent Up(Control control, double time) => new(IntentKind.ControlUp, Require(control), time);

        public bool Equals(Intent other) => Kind == other.Kind && Control == other.Control && Time.Equals(other.Time);

        public override bool Equals(object? obj) => obj is Intent other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Kind, Control, Time);

        public static bool operator ==(Intent left, Intent right) => left.Equals(right);

        public static bool operator !=(Intent left, Intent right) => !left.Equals(right);

        public override string ToString() => Kind == IntentKind.ControlDown || Kind == IntentKind.ControlUp ? $"{Kind}({Control})@{Time}" : $"{Kind}@{Time}";

        private static Control Require(Control control)
        {
            if (!Enum.IsDefined(typeof(Control), control))
            {
                throw new ArgumentOutOfRangeException(nameof(control), control, "未定義の操作キーです。");
            }

            return control;
        }
    }
}
