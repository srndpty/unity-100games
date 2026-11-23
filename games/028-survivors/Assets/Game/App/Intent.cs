using System;
using Game.Core;

namespace Game.App
{
    /// <summary>押している間だけ効く操作（移動）。3 択の間は候補の選び直しにも使う。</summary>
    public enum Control
    {
        Left,
        Right,
        Up,
        Down,
    }

    public enum IntentKind
    {
        /// <summary>決定。タイトル・結果では試合を始め、一時停止中は再開し、3 択では選んでいる候補に決める。</summary>
        Submit,

        TogglePause,

        /// <summary>最初から始め直す（一時停止中・結果）。</summary>
        Restart,

        /// <summary>タイトルへ戻る（一時停止中・結果）。</summary>
        BackToTitle,

        /// <summary>終了（タイトル）。アプリの終了は Unity 層が行う。</summary>
        Quit,

        /// <summary>3 択の <see cref="Intent.Choice"/> 番を選ぶ（数字キー・クリック）。</summary>
        Choose,

        /// <summary>操作キーを押した。</summary>
        ControlDown,

        /// <summary>操作キーを離した。</summary>
        ControlUp,
    }

    /// <summary>
    /// 入力を変換したプレイヤーの意図。キーの押下・解放・クリックと画面の操作を 1 本の列に並べ、起きた順に処理する（012 の知見）。
    /// <see cref="Time"/> は入力が起きた時刻（秒）。この時刻でどの Tick に効くかを決める（022〜026 と同じ）。
    /// </summary>
    public readonly struct Intent
    {
        private Intent(IntentKind kind, Control control, int choice, double time)
        {
            if (!double.IsFinite(time)) { throw new ArgumentOutOfRangeException(nameof(time), time, "時刻は有限の値にしてください。"); }
            Kind = kind;
            Control = control;
            Choice = choice;
            Time = time;
        }

        public IntentKind Kind { get; }

        /// <summary>ControlDown と ControlUp の対象。それ以外では使わない。</summary>
        public Control Control { get; }

        /// <summary>Choose の候補の番号（0 始まり）。それ以外では使わない。</summary>
        public int Choice { get; }

        /// <summary>入力が起きた時刻（秒）。<see cref="TideFlow.Advance"/> に渡す時刻と同じ時計。</summary>
        public double Time { get; }

        public static Intent Of(IntentKind kind, double time)
        {
            if (kind == IntentKind.ControlDown || kind == IntentKind.ControlUp) { throw new ArgumentException("キーの押下・解放は Down / Up で作ってください。", nameof(kind)); }
            if (kind == IntentKind.Choose) { throw new ArgumentException("候補の選択は Pick で作ってください。", nameof(kind)); }
            if (!Enum.IsDefined(typeof(IntentKind), kind)) { throw new ArgumentOutOfRangeException(nameof(kind), kind, "未定義の操作です。"); }
            return new(kind, default, 0, time);
        }

        public static Intent Down(Control control, double time) => new(IntentKind.ControlDown, Require(control), 0, time);

        public static Intent Up(Control control, double time) => new(IntentKind.ControlUp, Require(control), 0, time);

        /// <summary>3 択の <paramref name="choice"/> 番（0 始まり）を選ぶ。</summary>
        public static Intent Pick(int choice, double time)
        {
            if (choice < 0 || choice >= TideRun.ChoiceSlots) { throw new ArgumentOutOfRangeException(nameof(choice), choice, $"0〜{TideRun.ChoiceSlots - 1} で指定してください。"); }
            return new(IntentKind.Choose, default, choice, time);
        }

        public override string ToString() => Kind switch
        {
            IntentKind.ControlDown or IntentKind.ControlUp => $"{Kind}({Control})@{Time}",
            IntentKind.Choose => $"Choose({Choice})@{Time}",
            _ => $"{Kind}@{Time}",
        };

        private static Control Require(Control control)
        {
            if (!Enum.IsDefined(typeof(Control), control)) { throw new ArgumentOutOfRangeException(nameof(control), control, "未定義の操作キーです。"); }
            return control;
        }
    }
}
