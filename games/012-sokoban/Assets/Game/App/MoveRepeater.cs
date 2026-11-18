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

        // 待ち時間を設定した直後の Tick の経過時間には、キー操作より前の時間が含まれるので数えない。
        private bool _skipNextTick;

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
            RestartDelay();
        }

        /// <summary>離した方向を外す。まだ押している方向があれば、開始遅延からリピートし直す。</summary>
        public void Release(Direction direction)
        {
            var wasCurrent = _held.Count > 0 && _held[_held.Count - 1] == direction;
            _held.Remove(direction);
            if (wasCurrent) { RestartDelay(); }
        }

        /// <summary>押している方向を忘れる。キーを押し直すまでリピートしない。</summary>
        public void Clear() => _held.Clear();

        /// <summary>
        /// 時間を進め、リピートする方向を返す。処理落ちしても1回の呼び出しで1手までとする。
        /// キー操作と同じフレームの呼び出しでは時間を進めない。
        /// </summary>
        public Direction? Tick(float deltaSeconds)
        {
            if (_skipNextTick)
            {
                _skipNextTick = false;
                return null;
            }

            if (_held.Count == 0 || !float.IsFinite(deltaSeconds) || deltaSeconds <= 0) { return null; }
            _remaining -= deltaSeconds;
            if (_remaining > 0) { return null; }
            _remaining = _interval;
            return _held[_held.Count - 1];
        }

        private void RestartDelay()
        {
            _remaining = _initialDelay;
            _skipNextTick = true;
        }
    }
}
