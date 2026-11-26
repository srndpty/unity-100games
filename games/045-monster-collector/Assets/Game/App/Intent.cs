using System;

namespace Game.App
{
    public enum InputButton
    {
        Up = 0,
        Right = 1,
        Down = 2,
        Left = 3,
        Confirm = 4,
        Cancel = 5,
        Pause = 6,
    }

    /// <summary>入力の意図。物理キーごとに番号（KeySlot）を付け、押下と解放を別に送る（035・037）。</summary>
    public readonly struct Intent : IEquatable<Intent>
    {
        public Intent(InputButton button, int keySlot, bool pressed)
        {
            Button = button;
            KeySlot = keySlot;
            Pressed = pressed;
        }

        public InputButton Button { get; }

        public int KeySlot { get; }

        public bool Pressed { get; }

        public bool Equals(Intent other) => Button == other.Button && KeySlot == other.KeySlot && Pressed == other.Pressed;

        public override bool Equals(object? obj) => obj is Intent other && Equals(other);

        public override int GetHashCode() => ((int)Button * 397) ^ (KeySlot * 31) ^ (Pressed ? 1 : 0);

        public static bool operator ==(Intent left, Intent right) => left.Equals(right);

        public static bool operator !=(Intent left, Intent right) => !left.Equals(right);
    }

    /// <summary>効果音の合図。Flow と演出が数を増やし、Unity 層は前回の描画との差だけ鳴らす（039）。</summary>
    public sealed class AudioCues
    {
        public int Cursor { get; internal set; }

        public int Confirm { get; internal set; }

        public int Cancel { get; internal set; }

        public int Reject { get; internal set; }

        public int Bump { get; internal set; }

        public int Encounter { get; internal set; }

        public int Hit { get; internal set; }

        public int SuperHit { get; internal set; }

        public int WeakHit { get; internal set; }

        public int Status { get; internal set; }

        public int Faint { get; internal set; }

        public int Ball { get; internal set; }

        public int Capture { get; internal set; }

        public int Heal { get; internal set; }

        public int LevelUp { get; internal set; }

        public int Victory { get; internal set; }

        public int Defeat { get; internal set; }

        public int Save { get; internal set; }
    }
}
