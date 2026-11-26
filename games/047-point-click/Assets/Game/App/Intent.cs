using System;

namespace Game.App
{
    public enum IntentKind
    {
        Up,
        Down,

        /// <summary>決定・台詞を送る（Enter・Space）。</summary>
        Confirm,

        /// <summary>一時停止（Esc）。一時停止中は戻る。</summary>
        Pause,

        /// <summary>動詞と持ち物の選択を外す（右クリック）。</summary>
        Deselect,

        /// <summary>動詞のキー（1〜4）。<see cref="Intent.Index"/> が動詞の番号。</summary>
        VerbKey,

        /// <summary>マウスのクリック。<see cref="Intent.Target"/> がクリックした場所、<see cref="Intent.Version"/> が押した画面の版。</summary>
        Click,
    }

    public enum ClickTarget
    {
        /// <summary>何もない所（タイトル・操作説明・結果ではどこでも）。</summary>
        Background,
        Menu,
        Verb,
        Item,
        Topic,

        /// <summary>部屋の中（<see cref="Intent.X"/>・<see cref="Intent.Y"/> が部屋の座標）。</summary>
        World,
    }

    /// <summary>
    /// 入力の意図。クリックは押した画面の版を持ち、当てはめる直前に今の画面と照合して、前の画面（前の台詞）で押したクリックを捨てる（044・046）。
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

        public static Intent Key(IntentKind kind, int index = -1) => new(kind, ClickTarget.Background, index, 0, 0, -1);

        public static Intent Click(ClickTarget target, int index, int version) => new(IntentKind.Click, target, index, 0, 0, version);

        public static Intent ClickWorld(int x, int y, int version) => new(IntentKind.Click, ClickTarget.World, -1, x, y, version);

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
        public int Command { get; internal set; }
        public int Reject { get; internal set; }
        public int Pickup { get; internal set; }
        public int Solve { get; internal set; }
        public int Door { get; internal set; }
        public int Escape { get; internal set; }
    }
}
