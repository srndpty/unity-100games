using System;

namespace Game.App
{
    public enum IntentKind
    {
        Up,
        Down,

        /// <summary>決定（Enter・Space）。</summary>
        Confirm,

        /// <summary>Esc：一段戻る。壁の視点では一時停止。一時停止中は戻る。</summary>
        Pause,

        /// <summary>右クリック：持ち物の選択を外す → 手に取った物を置く → 近景から壁へ。</summary>
        Back,

        /// <summary>隣の壁へ向き直る（<see cref="Intent.Index"/>：+1 右・-1 左）。</summary>
        Turn,

        /// <summary>ヒント（H）。</summary>
        Hint,

        /// <summary>リセット（一時停止の中の R）。</summary>
        Reset,

        /// <summary>マウスの左ボタンを押した。<see cref="Intent.Target"/> が押した場所、<see cref="Intent.Version"/> が押した画面の版。</summary>
        Click,

        /// <summary>左ボタンを押したまま動かした（<see cref="Intent.X"/>・<see cref="Intent.Y"/>：画面の整数座標の差）。</summary>
        Drag,
    }

    public enum ClickTarget
    {
        /// <summary>何もない所（タイトル・操作説明・結果ではどこでも）。</summary>
        Background,
        Menu,

        /// <summary>持ち物の枠（<see cref="Intent.Index"/> が枠の番号）。</summary>
        Item,

        /// <summary>部屋（<see cref="Intent.X"/>・<see cref="Intent.Y"/> が画面の整数座標）。</summary>
        World,
    }

    /// <summary>
    /// 入力の意図。クリックは押した画面の版を持ち、当てはめる直前に今の画面と照合して、前の画面・前の視点で押したクリックを捨てる（044・047）。
    /// </summary>
    public readonly struct Intent : IEquatable<Intent>
    {
        private Intent(IntentKind kind, ClickTarget target, int index, int x, int y, int version)
        {
            Kind = kind;
            Target = target;
            Index = index;
            X = x;
            Y = y;
            Version = version;
        }

        public IntentKind Kind { get; }
        public ClickTarget Target { get; }
        public int Index { get; }
        public int X { get; }
        public int Y { get; }
        public int Version { get; }

        public static Intent Key(IntentKind kind, int index = 0) => new(kind, ClickTarget.Background, index, 0, 0, -1);

        public static Intent Click(ClickTarget target, int index, int version) => new(IntentKind.Click, target, index, 0, 0, version);

        public static Intent ClickWorld(int x, int y, int version) => new(IntentKind.Click, ClickTarget.World, -1, x, y, version);

        public static Intent Drag(int dx, int dy) => new(IntentKind.Drag, ClickTarget.World, -1, dx, dy, -1);

        public bool Equals(Intent other) =>
            Kind == other.Kind && Target == other.Target && Index == other.Index && X == other.X && Y == other.Y && Version == other.Version;

        public override bool Equals(object? obj) => obj is Intent other && Equals(other);

        public override int GetHashCode() => unchecked(((int)Kind * 397) ^ ((int)Target * 31) ^ (Index << 8) ^ (X * 7919) ^ (Y * 104729) ^ (Version << 16));

        public static bool operator ==(Intent left, Intent right) => left.Equals(right);

        public static bool operator !=(Intent left, Intent right) => !left.Equals(right);
    }

    /// <summary>効果音の合図。Flow が数を増やし、Unity 層は前回の描画との差だけ鳴らす（039）。</summary>
    public sealed class AudioCues
    {
        public int Cursor { get; internal set; }
        public int Choose { get; internal set; }
        public int Move { get; internal set; }
        public int Click { get; internal set; }
        public int Reject { get; internal set; }
        public int Pickup { get; internal set; }
        public int Install { get; internal set; }
        public int Mistake { get; internal set; }
        public int Clue { get; internal set; }
        public int Solve { get; internal set; }
        public int Hint { get; internal set; }
        public int Escape { get; internal set; }
    }
}
