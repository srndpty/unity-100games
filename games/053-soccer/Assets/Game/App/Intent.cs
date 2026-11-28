using System;

namespace Game.App
{
    public enum InputButton
    {
        /// <summary>上へ（メニューでは上の項目）。</summary>
        Up,
        Down,
        Left,
        Right,

        /// <summary>パス（守備ではタックル）。</summary>
        Pass,
        Shoot,
        Lob,

        /// <summary>選手の切替。</summary>
        Switch,

        /// <summary>決定（メニュー）。</summary>
        Confirm,

        /// <summary>一時停止（一時停止中は戻る）。</summary>
        Cancel,

        /// <summary>マウスのクリック。<see cref="Intent.Index"/> が項目の番号（項目の外なら -1）、<see cref="Intent.Version"/> が押した画面の版。</summary>
        Click,
    }

    /// <summary>
    /// 入力の意図。物理キーごとに番号（KeySlot）を付けて押下と解放を別に送る（035・037・042）。
    /// クリックは押した画面の版を持ち、当てはめる直前に今の画面と照合して、前の画面で押したクリックを捨てる（044）。
    /// </summary>
    public readonly struct Intent : IEquatable<Intent>
    {
        public Intent(InputButton button, int keySlot, bool pressed, int index = -1, int version = -1)
        {
            Button = button;
            KeySlot = keySlot;
            Pressed = pressed;
            Index = index;
            Version = version;
        }

        public InputButton Button { get; }
        public int KeySlot { get; }
        public bool Pressed { get; }
        public int Index { get; }
        public int Version { get; }

        public static Intent Press(InputButton button, int keySlot = 0) => new(button, keySlot, true);

        public static Intent Release(InputButton button, int keySlot = 0) => new(button, keySlot, false);

        public static Intent Click(int index, int version) => new(InputButton.Click, 0, true, index, version);

        public bool Equals(Intent other) =>
            Button == other.Button && KeySlot == other.KeySlot && Pressed == other.Pressed && Index == other.Index && Version == other.Version;

        public override bool Equals(object? obj) => obj is Intent other && Equals(other);

        public override int GetHashCode() => ((int)Button * 397) ^ (KeySlot * 31) ^ (Pressed ? 1 : 0) ^ (Index << 8) ^ (Version << 16);

        public static bool operator ==(Intent left, Intent right) => left.Equals(right);

        public static bool operator !=(Intent left, Intent right) => !left.Equals(right);
    }

    /// <summary>効果音の合図。Flow が数を増やし、Unity 層は前回の描画との差だけ鳴らす（039）。</summary>
    public sealed class AudioCues
    {
        public int Kick { get; internal set; }
        public int Goal { get; internal set; }
        public int Tackle { get; internal set; }
        public int Whistle { get; internal set; }
        public int Switch { get; internal set; }
        public int Cursor { get; internal set; }
        public int Choose { get; internal set; }
        public int Reject { get; internal set; }
    }
}
