using System;

namespace Game.App
{
    /// <summary>押している間だけ効く操作と、押した瞬間に 1 回効く操作。</summary>
    public enum Control
    {
        Left,
        Right,
        Up,
        Down,

        /// <summary>基本攻撃（左ボタン）。押している間は攻撃間隔ごとに続ける。</summary>
        Attack,

        /// <summary>薙ぎ払い（右ボタン）。押した瞬間だけ。</summary>
        Skill,

        /// <summary>回復薬（Q）。押した瞬間だけ。</summary>
        Potion,

        /// <summary>拾う（E）。押した瞬間だけ。</summary>
        Pick,
    }

    public enum IntentKind
    {
        /// <summary>決定。タイトル → 操作説明 → 開始、一時停止中は再開、結果ではタイトルへ。</summary>
        Submit,

        /// <summary>戻る（Esc）。プレイ中は一時停止、一時停止・持ち物は閉じる、操作説明はタイトルへ。</summary>
        Back,

        /// <summary>持ち物の開閉（I / Tab）。</summary>
        ToggleInventory,

        /// <summary>タイトルへ戻る（一時停止・結果）。</summary>
        BackToTitle,

        /// <summary>終了（タイトル）。アプリの終了は Unity 層が行う。</summary>
        Quit,
        ControlDown,
        ControlUp,

        /// <summary>持ち物の <see cref="Intent.Argument"/> 番を装備する（持ち物の画面）。</summary>
        Equip,

        /// <summary>枠 <see cref="Intent.Argument"/> の装備を外す（持ち物の画面）。</summary>
        Unequip,

        /// <summary>持ち物の <see cref="Intent.Argument"/> 番を捨てる（持ち物の画面）。</summary>
        Discard,

        /// <summary>地面の品 <see cref="Intent.Argument"/>（番号）を拾う（名前札のクリック、プレイ中）。</summary>
        PickGround,
    }

    /// <summary>
    /// 入力を変換したプレイヤーの意図。キー・マウス・画面の操作を 1 本の列に並べ、起きた順に処理する（012）。
    /// <see cref="Time"/> は入力が起きた時刻（秒）で、どの Tick に効くかを決める（022〜026）。
    /// </summary>
    public readonly struct Intent
    {
        private Intent(IntentKind kind, Control control, long argument, double time)
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

        /// <summary>持ち物の番号・枠・地面の品の番号。キーの押下・解放では物理キーの番号（0〜<see cref="MaxKey"/>）。</summary>
        public long Argument { get; }

        public double Time { get; }

        public static Intent Of(IntentKind kind, double time)
        {
            if (kind == IntentKind.ControlDown || kind == IntentKind.ControlUp) { throw new ArgumentException("キーは Down / Up で作ってください。", nameof(kind)); }
            return new(kind, default, 0, time);
        }

        public static Intent With(IntentKind kind, long argument, double time)
        {
            if (kind != IntentKind.Equip && kind != IntentKind.Unequip && kind != IntentKind.Discard && kind != IntentKind.PickGround)
            {
                throw new ArgumentException("番号を持つ操作ではありません。", nameof(kind));
            }

            return new(kind, default, argument, time);
        }

        /// <summary>1 つの操作に割り当てられる物理キーの番号の上限。</summary>
        public const int MaxKey = 7;

        /// <summary>操作 <paramref name="control"/> の物理キー <paramref name="key"/> 番を押した（1 つの操作に複数のキーを割り当てるとき、キーごとに番号を分ける。035・037）。</summary>
        public static Intent Down(Control control, double time, int key = 0) => new(IntentKind.ControlDown, Require(control), RequireKey(key), time);

        public static Intent Up(Control control, double time, int key = 0) => new(IntentKind.ControlUp, Require(control), RequireKey(key), time);

        /// <summary>キーの押下・解放の物理キーの番号。</summary>
        public int Key => (int)Argument;

        public override string ToString() => $"{Kind}({Control},{Argument})@{Time}";

        private static int RequireKey(int key)
        {
            if (key < 0 || key > MaxKey) { throw new ArgumentOutOfRangeException(nameof(key), key, "物理キーの番号は 0〜7 です。"); }
            return key;
        }

        private static Control Require(Control control)
        {
            if (!Enum.IsDefined(typeof(Control), control)) { throw new ArgumentOutOfRangeException(nameof(control), control, "未定義の操作キーです。"); }
            return control;
        }
    }
}
