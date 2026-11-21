using System;

namespace Game.App
{
    /// <summary>押している間だけ効く操作。</summary>
    public enum Control
    {
        Left,
        Right,

        /// <summary>走りキー。押している間は最高速が上がる。</summary>
        Run,
        Jump,
    }

    public enum IntentKind
    {
        /// <summary>決定。タイトル・結果では試合を始め、一時停止中は再開する。</summary>
        Submit,

        TogglePause,

        /// <summary>1 面から始め直す（一時停止中・結果画面）。</summary>
        Restart,

        /// <summary>タイトルへ戻る（一時停止中・結果画面）。</summary>
        BackToTitle,

        /// <summary>終了（タイトル）。アプリの終了は Unity 層が行う。</summary>
        Quit,

        /// <summary>操作キーを押した。</summary>
        ControlDown,

        /// <summary>操作キーを離した。</summary>
        ControlUp,
    }

    /// <summary>入力を変換したプレイヤーの意図。キーの押下・解放と画面の操作を 1 本の列に並べ、押した順に処理する。</summary>
    public readonly struct Intent
    {
        private Intent(IntentKind kind, Control control)
        {
            Kind = kind;
            Control = control;
        }

        public IntentKind Kind { get; }

        /// <summary>ControlDown と ControlUp の対象。それ以外では使わない。</summary>
        public Control Control { get; }

        public static Intent Of(IntentKind kind)
        {
            if (kind == IntentKind.ControlDown || kind == IntentKind.ControlUp)
            {
                throw new ArgumentException("キーの押下・解放は Down / Up で作ってください。", nameof(kind));
            }

            if (!Enum.IsDefined(typeof(IntentKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "未定義の操作です。");
            }

            return new(kind, default);
        }

        public static Intent Down(Control control) => new(IntentKind.ControlDown, Require(control));

        public static Intent Up(Control control) => new(IntentKind.ControlUp, Require(control));

        public override string ToString() => Kind == IntentKind.ControlDown || Kind == IntentKind.ControlUp ? $"{Kind}({Control})" : Kind.ToString();

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
