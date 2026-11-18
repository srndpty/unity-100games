using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    /// <summary>方向キーの長押しリピート。最後に押した方向を優先する。</summary>
    public sealed class MoveRepeater
    {
        private readonly List<Direction> _held = new();
        private readonly float _initialDelay;
        private readonly float _interval;
        private float _remaining;

        public MoveRepeater(float initialDelay, float interval)
        {
            if (!float.IsFinite(initialDelay) || initialDelay <= 0) { throw new ArgumentOutOfRangeException(nameof(initialDelay), initialDelay, "正の秒数を指定してください。"); }
            if (!float.IsFinite(interval) || interval <= 0) { throw new ArgumentOutOfRangeException(nameof(interval), interval, "正の秒数を指定してください。"); }
            _initialDelay = initialDelay;
            _interval = interval;
        }

        public void Press(Direction direction)
        {
            _held.Remove(direction);
            _held.Add(direction);
            _remaining = _initialDelay;
        }

        /// <summary>離した方向を外す。まだ押している方向があれば、開始遅延からリピートし直す。</summary>
        public void Release(Direction direction)
        {
            var wasCurrent = _held.Count > 0 && _held[_held.Count - 1] == direction;
            _held.Remove(direction);
            if (wasCurrent) { _remaining = _initialDelay; }
        }

        /// <summary>押している方向を忘れる。キーを押し直すまでリピートしない。</summary>
        public void Clear() => _held.Clear();

        /// <summary>時間を進め、リピートする方向を返す。処理落ちしても1回の呼び出しで1手までとする。</summary>
        public Direction? Tick(float deltaSeconds)
        {
            if (_held.Count == 0 || !float.IsFinite(deltaSeconds) || deltaSeconds <= 0) { return null; }
            _remaining -= deltaSeconds;
            if (_remaining > 0) { return null; }
            _remaining = _interval;
            return _held[_held.Count - 1];
        }
    }
}
