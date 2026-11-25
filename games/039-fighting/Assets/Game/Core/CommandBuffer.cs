using System.Collections.Generic;
using TileMotion;

namespace Game.Core
{
    /// <summary>方向の履歴、必殺の猶予、攻撃ボタンの先行入力。</summary>
    public sealed class CommandBuffer
    {
        private static readonly int[] _rightMotion = { 2, 3, 6 };
        private static readonly int[] _leftMotion = { 2, 1, 4 };

        private readonly List<Mark> _marks = new();
        private int _direction = 5;
        private bool _lightHeld;
        private bool _heavyHeld;
        private bool _sweepHeld;
        private bool _lightSeen;
        private bool _heavySeen;
        private int _lightTick;
        private int _heavyTick;
        private bool _armed;
        private int _armedTick;
        private AttackButton _button;
        private Countdown _buffer;

        public AttackButton Button => _button;

        public bool AttackReady => _armed && _buffer.IsActive;

        public int BufferRemaining => _buffer.Remaining;

        public void Clear()
        {
            _marks.Clear();
            _direction = 5;
            _lightHeld = false;
            _heavyHeld = false;
            _sweepHeld = false;
            _lightSeen = false;
            _heavySeen = false;
            _armed = false;
            _button = AttackButton.None;
            _buffer.Clear();
        }

        public void Observe(int tick, Pad pad, DuelConfig config)
        {
            if (pad.Direction != _direction)
            {
                _direction = pad.Direction;
                _marks.Add(new Mark(tick, pad.Direction));
            }

            int oldest = tick - config.HistoryTicks;
            int write = 0;
            for (int i = 0; i < _marks.Count; i++)
            {
                if (_marks[i].Tick >= oldest)
                {
                    _marks[write] = _marks[i];
                    write++;
                }
            }

            if (write < _marks.Count) { _marks.RemoveRange(write, _marks.Count - write); }
            if (pad.Light && !_lightHeld)
            {
                _lightSeen = true;
                _lightTick = tick;
                Arm(tick, AttackButton.Light, config.BufferTicks);
            }

            if (pad.Heavy && !_heavyHeld)
            {
                _heavySeen = true;
                _heavyTick = tick;
                Arm(tick, AttackButton.Heavy, config.BufferTicks);
            }

            if (pad.Sweep && !_sweepHeld) { Arm(tick, AttackButton.Sweep, config.BufferTicks); }
            _lightHeld = pad.Light;
            _heavyHeld = pad.Heavy;
            _sweepHeld = pad.Sweep;
        }

        public void TickBuffer()
        {
            _buffer.Tick();
            if (!_buffer.IsActive) { _armed = false; }
        }

        public bool ThrowReady(int tick)
        {
            if (!_lightSeen || !_heavySeen) { return false; }
            int gap = _lightTick > _heavyTick ? _lightTick - _heavyTick : _heavyTick - _lightTick;
            int later = _lightTick > _heavyTick ? _lightTick : _heavyTick;
            return gap <= 1 && later == tick;
        }

        public bool SpecialReady(int facing, DuelConfig config)
        {
            if (!_armed || !_buffer.IsActive || _button != AttackButton.Light) { return false; }
            var need = facing > 0 ? _rightMotion : _leftMotion;
            int cursor = _armedTick;
            int window = config.ButtonWindow;
            for (int step = need.Length - 1; step >= 0; step--)
            {
                int found = int.MinValue;
                for (int i = _marks.Count - 1; i >= 0; i--)
                {
                    var mark = _marks[i];
                    if (mark.Tick > cursor) { continue; }
                    if (mark.Direction != need[step]) { continue; }
                    if (cursor - mark.Tick > window) { break; }
                    found = mark.Tick;
                    break;
                }

                if (found == int.MinValue) { return false; }
                cursor = found;
                window = config.StepWindow;
            }

            return true;
        }

        public void Consume()
        {
            _armed = false;
            _button = AttackButton.None;
            _buffer.Clear();
            _lightSeen = false;
            _heavySeen = false;
        }

        public void Contribute(ref StateHash hash)
        {
            hash.Mix(_direction);
            hash.Mix(_lightHeld);
            hash.Mix(_heavyHeld);
            hash.Mix(_sweepHeld);
            hash.Mix(_marks.Count);
            for (int i = 0; i < _marks.Count; i++)
            {
                hash.Mix(_marks[i].Tick);
                hash.Mix(_marks[i].Direction);
            }

            hash.Mix(_armed);
            hash.Mix(_armedTick);
            hash.Mix((int)_button);
            hash.Mix(_buffer.Remaining);
            hash.Mix(_lightSeen);
            hash.Mix(_lightTick);
            hash.Mix(_heavySeen);
            hash.Mix(_heavyTick);
        }

        public CommandBuffer Clone()
        {
            var copy = new CommandBuffer()
            {
                _direction = _direction,
                _lightHeld = _lightHeld,
                _heavyHeld = _heavyHeld,
                _sweepHeld = _sweepHeld,
                _lightSeen = _lightSeen,
                _heavySeen = _heavySeen,
                _lightTick = _lightTick,
                _heavyTick = _heavyTick,
                _armed = _armed,
                _armedTick = _armedTick,
                _button = _button,
                _buffer = _buffer,
            };
            for (int i = 0; i < _marks.Count; i++) { copy._marks.Add(_marks[i]); }
            return copy;
        }

        private void Arm(int tick, AttackButton button, int bufferTicks)
        {
            _armed = true;
            _armedTick = tick;
            _button = button;
            _buffer.Arm(bufferTicks);
        }

        private readonly struct Mark
        {
            public Mark(int tick, int direction)
            {
                Tick = tick;
                Direction = direction;
            }

            public int Tick { get; }

            public int Direction { get; }
        }
    }
}
