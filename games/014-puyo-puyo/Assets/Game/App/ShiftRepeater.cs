using System;
using System.Collections.Generic;

namespace Game.App
{
    public enum ShiftDirection
    {
        Left,
        Right,
    }

    /// <summary>左右キーの長押しを Tick 単位で繰り返す。後から押した方向を優先する。</summary>
    public sealed class ShiftRepeater
    {
        private readonly List<ShiftDirection> _held = new();
        private readonly int _delayTicks;
        private readonly int _intervalTicks;
        private int _remaining;
        private bool _restarted;

        public ShiftRepeater(int delayTicks, int intervalTicks)
        {
            if (delayTicks <= 0) { throw new ArgumentOutOfRangeException(nameof(delayTicks), delayTicks, "1 以上を指定してください。"); }
            if (intervalTicks <= 0) { throw new ArgumentOutOfRangeException(nameof(intervalTicks), intervalTicks, "1 以上を指定してください。"); }
            _delayTicks = delayTicks;
            _intervalTicks = intervalTicks;
        }

        public void Press(ShiftDirection direction)
        {
            _held.Remove(direction);
            _held.Add(direction);
            Restart();
        }

        /// <summary>離した方向を外す。反対の方向を押したままなら、開始遅延から繰り返し直す。</summary>
        public void Release(ShiftDirection direction)
        {
            var wasCurrent = _held.Count > 0 && _held[_held.Count - 1] == direction;
            _held.Remove(direction);
            if (wasCurrent) { Restart(); }
        }

        /// <summary>押している方向を忘れる。キーを押し直すまで繰り返さない。</summary>
        public void Clear() => _held.Clear();

        /// <summary>前回の呼び出し以降に開始遅延を設定し直したかを取り出す。</summary>
        public bool TakeRestarted()
        {
            var restarted = _restarted;
            _restarted = false;
            return restarted;
        }

        /// <summary>1 Tick 進め、繰り返す方向を返す。</summary>
        public ShiftDirection? Tick()
        {
            if (_held.Count == 0) { return null; }
            _remaining--;
            if (_remaining > 0) { return null; }
            _remaining = _intervalTicks;
            return _held[_held.Count - 1];
        }

        private void Restart()
        {
            _remaining = _delayTicks;
            _restarted = true;
        }
    }
}
