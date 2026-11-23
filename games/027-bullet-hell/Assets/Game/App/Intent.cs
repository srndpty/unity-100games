using System;

namespace Game.App
{
    /// <summary>押している間だけ効く操作（ボムは押した瞬間の 1 回）。</summary>
    public enum Control
    {
        Left,
        Right,
        Up,
        Down,

        /// <summary>低速（Shift）。</summary>
        Slow,

        /// <summary>ショット（Z）。</summary>
        Shot,

        /// <summary>ボム（X）。押した Tick に 1 回だけ使う。</summary>
        Bomb,
    }

    public enum IntentKind
    {
        /// <summary>決定。タイトル・結果では最初から始め、一時停止中は再開する。</summary>
        Submit,

        TogglePause,

        /// <summary>最初から始め直す（一時停止中・結果）。</summary>
        Restart,

        /// <summary>負けた結果から、そのボスの 1 つ目の弾幕へ再挑戦する。</summary>
        RetryBoss,

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
    /// <see cref="Time"/> は入力が起きた時刻（秒）。この時刻でどの Tick に効くかを決める（022〜026 と同じ）。
    /// </summary>
    public readonly struct Intent
    {
        private Intent(IntentKind kind, Control control, double time)
        {
            if (!double.IsFinite(time)) { throw new ArgumentOutOfRangeException(nameof(time), time, "時刻は有限の値にしてください。"); }
            Kind = kind;
            Control = control;
            Time = time;
        }

        public IntentKind Kind { get; }

        /// <summary>ControlDown と ControlUp の対象。それ以外では使わない。</summary>
        public Control Control { get; }

        /// <summary>入力が起きた時刻（秒）。<see cref="VeilFlow.Advance"/> に渡す時刻と同じ時計。</summary>
        public double Time { get; }

        public static Intent Of(IntentKind kind, double time)
        {
            if (kind == IntentKind.ControlDown || kind == IntentKind.ControlUp) { throw new ArgumentException("キーの押下・解放は Down / Up で作ってください。", nameof(kind)); }
            if (!Enum.IsDefined(typeof(IntentKind), kind)) { throw new ArgumentOutOfRangeException(nameof(kind), kind, "未定義の操作です。"); }
            return new(kind, default, time);
        }

        public static Intent Down(Control control, double time) => new(IntentKind.ControlDown, Require(control), time);

        public static Intent Up(Control control, double time) => new(IntentKind.ControlUp, Require(control), time);

        public override string ToString() => Kind == IntentKind.ControlDown || Kind == IntentKind.ControlUp ? $"{Kind}({Control})@{Time}" : $"{Kind}@{Time}";

        private static Control Require(Control control)
        {
            if (!Enum.IsDefined(typeof(Control), control)) { throw new ArgumentOutOfRangeException(nameof(control), control, "未定義の操作キーです。"); }
            return control;
        }
    }
}
