using System;

namespace Game.App
{
    /// <summary>押している間だけ効く（または押した瞬間に効く）キーの役割。</summary>
    public enum Control
    {
        /// <summary>矢印・WASD の左右上下。物理キーごとに番号を分ける（矢印 0、WASD 1）。</summary>
        Left,
        Right,
        Up,
        Down,

        /// <summary>テンキーの 8 方向。物理キーの番号が方向（<see cref="Game.Core.Direction"/> の値）。</summary>
        Pad,

        /// <summary>Shift：押している間は斜めにしか動かない（矢印・WASD の 2 キーで斜め）。</summary>
        Diagonal,

        /// <summary>Ctrl：押している間は方向キーで向きだけ変える（時間を使わない）。</summary>
        Turn,

        /// <summary>足踏み（Space・テンキー 5）。押したままなら繰り返す。</summary>
        Wait,

        /// <summary>足元の品を拾う（G）。</summary>
        Pickup,

        /// <summary>階段を降りる（. ・Enter はプレイ中の決定として同じ）。</summary>
        Descend,

        /// <summary>押している間だけ、全体の地図を重ねて出す（M）。時間は使わない。</summary>
        Map,
    }

    public enum IntentKind
    {
        /// <summary>決定（Enter）。タイトル → 操作説明 → 開始、プレイ中は階段を降りる、一時停止は再開、結果はタイトルへ、持ち物は使う。</summary>
        Submit,

        /// <summary>戻る（Esc）。プレイ中は一時停止、一時停止・持ち物は閉じる、操作説明はタイトルへ。</summary>
        Back,

        /// <summary>持ち物の開閉（I・Tab）。</summary>
        ToggleInventory,

        /// <summary>タイトルへ戻る（一時停止中・結果）。</summary>
        BackToTitle,

        /// <summary>終了（タイトル）。アプリの終了は Unity 層が行う。</summary>
        Quit,

        KeyDown,
        KeyUp,

        /// <summary>持ち物の選択を <see cref="Intent.Argument"/> 動かす（左右は ±1、上下は ±<see cref="CrawlFlow.InventoryColumns"/>）。</summary>
        InventoryMove,

        /// <summary>選んだ品を使う・投げる・置く（持ち物の画面）。</summary>
        InventoryUse,
        InventoryThrow,
        InventoryDrop,
    }

    /// <summary>
    /// 入力を変換したプレイヤーの意図。キーと画面の操作を 1 本の列に並べ、起きた順に処理する（012）。
    /// <see cref="Time"/> は入力が起きた時刻（秒）で、押したままの連続移動の間合いを決める。
    /// </summary>
    public readonly struct Intent
    {
        /// <summary>1 つの役割に割り当てられる物理キーの番号の上限（テンキーは 8 方向で 0〜7）。</summary>
        public const int MaxKey = 7;

        private Intent(IntentKind kind, Control control, int argument, double time)
        {
            if (!double.IsFinite(time)) { throw new ArgumentOutOfRangeException(nameof(time), time, "時刻は有限の値にしてください。"); }
            if (!Enum.IsDefined(typeof(IntentKind), kind)) { throw new ArgumentOutOfRangeException(nameof(kind), kind, "未定義の操作です。"); }
            Kind = kind;
            Control = control;
            Argument = argument;
            Time = time;
        }

        public IntentKind Kind { get; }

        public Control Control { get; }

        /// <summary>キーの押下・解放では物理キーの番号、持ち物の選択では動かす量。</summary>
        public int Argument { get; }

        public double Time { get; }

        public int Key => Argument;

        public static Intent Of(IntentKind kind, double time)
        {
            if (kind == IntentKind.KeyDown || kind == IntentKind.KeyUp || kind == IntentKind.InventoryMove)
            {
                throw new ArgumentException("キーは Down / Up、選択は Move で作ってください。", nameof(kind));
            }

            return new(kind, default, 0, time);
        }

        public static Intent Move(int delta, double time) => new(IntentKind.InventoryMove, default, delta, time);

        public static Intent Down(Control control, double time, int key = 0) => new(IntentKind.KeyDown, Require(control), RequireKey(key), time);

        public static Intent Up(Control control, double time, int key = 0) => new(IntentKind.KeyUp, Require(control), RequireKey(key), time);

        public override string ToString() => $"{Kind}({Control},{Argument})@{Time}";

        private static int RequireKey(int key)
        {
            if (key < 0 || key > MaxKey) { throw new ArgumentOutOfRangeException(nameof(key), key, "物理キーの番号は 0〜7 です。"); }
            return key;
        }

        private static Control Require(Control control)
        {
            if (!Enum.IsDefined(typeof(Control), control)) { throw new ArgumentOutOfRangeException(nameof(control), control, "未定義のキーの役割です。"); }
            return control;
        }
    }
}
