using System;

namespace Game.App
{
    public enum InputButton
    {
        Up,
        Down,
        Left,
        Right,

        /// <summary>決定（メニュー）・カーソルの点に打つ・死石の切り替え。</summary>
        Confirm,

        /// <summary>一時停止（一時停止中は戻る）・死石の確定。</summary>
        Cancel,

        /// <summary>パス。</summary>
        Pass,

        /// <summary>マウスのクリック。<see cref="Intent.Index"/> が項目・交点・ボタンの番号（外なら -1）、<see cref="Intent.Version"/> が押した画面の版。</summary>
        Click,
    }

    /// <summary>
    /// 入力の意図。押下だけを送る（碁は押したままの操作がない）。
    /// クリックは押した画面の版を持ち、当てはめる直前に今の版と照合して、前の画面・前の局面で押したクリックを捨てる（044・046）。
    /// </summary>
    public readonly struct Intent : IEquatable<Intent>
    {
        public Intent(InputButton button, int index = -1, int version = -1)
        {
            Button = button;
            Index = index;
            Version = version;
        }

        public InputButton Button { get; }

        public int Index { get; }

        public int Version { get; }

        public static Intent Press(InputButton button) => new(button);

        public static Intent Click(int index, int version) => new(InputButton.Click, index, version);

        public bool Equals(Intent other) => Button == other.Button && Index == other.Index && Version == other.Version;

        public override bool Equals(object? obj) => obj is Intent other && Equals(other);

        public override int GetHashCode() => ((int)Button * 397) ^ (Index << 8) ^ (Version << 16);

        public static bool operator ==(Intent left, Intent right) => left.Equals(right);

        public static bool operator !=(Intent left, Intent right) => !left.Equals(right);
    }

    /// <summary>効果音の合図。Flow が数を増やし、Unity 層は前回の描画との差だけ鳴らす（039）。</summary>
    public sealed class AudioCues
    {
        public int Place { get; internal set; }
        public int Capture { get; internal set; }
        public int Pass { get; internal set; }
        public int Reject { get; internal set; }
        public int Cursor { get; internal set; }
        public int Choose { get; internal set; }
        public int End { get; internal set; }
    }
}
