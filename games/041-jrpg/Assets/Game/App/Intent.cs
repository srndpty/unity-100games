using System;
using Game.Core;

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
        Menu = 6,
        Pause = 7,
    }

    public readonly struct Intent : IEquatable<Intent>
    {
        public Intent(InputButton button, int keySlot, bool pressed, double time)
        {
            Button = button;
            KeySlot = keySlot;
            Pressed = pressed;
            Time = time;
        }

        public InputButton Button { get; }
        public int KeySlot { get; }
        public bool Pressed { get; }
        public double Time { get; }

        public bool Equals(Intent other) =>
            Button == other.Button &&
            KeySlot == other.KeySlot &&
            Pressed == other.Pressed &&
            Time.Equals(other.Time);

        public override bool Equals(object? obj) => obj is Intent other && Equals(other);

        public override int GetHashCode() => HashCode.Combine((int)Button, KeySlot, Pressed, Time);

        public static bool operator ==(Intent left, Intent right) => left.Equals(right);
        public static bool operator !=(Intent left, Intent right) => !left.Equals(right);
    }

    /// <summary>メニューや移動のキー長押しリピートを Tick 単位で数える（押下フレームの経過時間は含めない）。</summary>
    public sealed class KeyRepeatTracker
    {
        private readonly int _initialDelayTicks;
        private readonly int _intervalTicks;
        private Direction? _activeDirection;
        private int _cooldownTicks;

        public KeyRepeatTracker(int initialDelayTicks = 16, int intervalTicks = 6)
        {
            _initialDelayTicks = Math.Max(1, initialDelayTicks);
            _intervalTicks = Math.Max(1, intervalTicks);
        }

        public void Reset()
        {
            _activeDirection = null;
            _cooldownTicks = 0;
        }

        public void OnDirectionPressed(Direction dir)
        {
            _activeDirection = dir;
            _cooldownTicks = _initialDelayTicks;
        }

        public bool TickRepeat(Direction? currentlyHeld, out Direction repeatedDir)
        {
            repeatedDir = Direction.Up;
            if (!currentlyHeld.HasValue)
            {
                Reset();
                return false;
            }

            if (!_activeDirection.HasValue || _activeDirection.Value != currentlyHeld.Value)
            {
                _activeDirection = currentlyHeld.Value;
                _cooldownTicks = _initialDelayTicks;
                return false;
            }

            if (_cooldownTicks > 0)
            {
                _cooldownTicks--;
            }

            if (_cooldownTicks == 0)
            {
                _cooldownTicks = _intervalTicks;
                repeatedDir = currentlyHeld.Value;
                return true;
            }

            return false;
        }
    }

    public sealed class AudioCueCounter
    {
        public int CursorMove { get; internal set; }
        public int Confirm { get; internal set; }
        public int Cancel { get; internal set; }
        public int Reject { get; internal set; }
        public int Step { get; internal set; }
        public int Portal { get; internal set; }
        public int Chest { get; internal set; }
        public int DoorUnlock { get; internal set; }
        public int Attack { get; internal set; }
        public int Spell { get; internal set; }
        public int Damage { get; internal set; }
        public int Heal { get; internal set; }
        public int LevelUp { get; internal set; }
        public int Victory { get; internal set; }
        public int Defeat { get; internal set; }
        public int Save { get; internal set; }
    }
}
