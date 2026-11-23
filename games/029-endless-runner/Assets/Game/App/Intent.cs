using System;

namespace Game.App
{
    /// <summary>押した瞬間に 1 回だけ効く操作。</summary>
    public enum RunMove
    {
        Left,
        Right,
        Jump,
        Slide,
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
        Press,
    }

    /// <summary>
    /// 入力を変換したプレイヤーの意図。キーの押下と画面の操作を 1 本の列に並べ、起きた順に処理する。
    /// <see cref="Time"/> は入力が起きた時刻（秒）で、この時刻を含む Tick に効く（022〜028 と同じ）。
    /// </summary>
    public readonly struct Intent
    {
        private Intent(IntentKind kind, RunMove move, double time)
        {
            if (!double.IsFinite(time)) { throw new ArgumentOutOfRangeException(nameof(time), time, "時刻は有限の値にしてください。"); }
            Kind = kind;
            Move = move;
            Time = time;
        }

        public IntentKind Kind { get; }

        /// <summary>Press の対象。それ以外では使わない。</summary>
        public RunMove Move { get; }

        public double Time { get; }

        public static Intent Of(IntentKind kind, double time)
        {
            if (kind == IntentKind.Press) { throw new ArgumentException("キーの押下は Press で作ってください。", nameof(kind)); }
            if (!Enum.IsDefined(typeof(IntentKind), kind)) { throw new ArgumentOutOfRangeException(nameof(kind), kind, "未定義の操作です。"); }
            return new(kind, default, time);
        }

        public static Intent Press(RunMove move, double time)
        {
            if (!Enum.IsDefined(typeof(RunMove), move)) { throw new ArgumentOutOfRangeException(nameof(move), move, "未定義の操作キーです。"); }
            return new(IntentKind.Press, move, time);
        }

        public override string ToString() => Kind == IntentKind.Press ? $"Press({Move})@{Time}" : $"{Kind}@{Time}";
    }
}
