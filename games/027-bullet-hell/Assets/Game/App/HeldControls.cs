using System;
using System.Collections.Generic;

namespace Game.App
{
    /// <summary>
    /// 押している操作キー。左右（上下）を両方押しているときは後から押したほうを使う（離すと、まだ押しているほうに戻る）。
    /// ショットは、次の Tick までに押して離しても 1 Tick は押したことにする。ボムは押すごとに 1 回だけ渡し、
    /// 離すまでの間に押下がもう一度届いても（重複した押下の出来事）2 回目にはしない。
    /// </summary>
    public sealed class HeldControls
    {
        private readonly List<int> _horizontal = new();
        private readonly List<int> _vertical = new();
        private bool _shotHeld;
        private bool _shotLatched;
        private bool _bombLatched;
        private bool _bombHeld;

        /// <summary>左 -1・なし 0・右 1。</summary>
        public int Horizontal => _horizontal.Count == 0 ? 0 : _horizontal[_horizontal.Count - 1];

        /// <summary>下 -1・なし 0・上 1。</summary>
        public int Vertical => _vertical.Count == 0 ? 0 : _vertical[_vertical.Count - 1];

        public bool Slow { get; private set; }

        public void Press(Control control)
        {
            switch (control)
            {
                case Control.Slow:
                    Slow = true;
                    return;
                case Control.Shot:
                    _shotHeld = true;
                    _shotLatched = true;
                    return;
                case Control.Bomb:
                    if (!_bombHeld) { _bombLatched = true; }
                    _bombHeld = true;
                    return;
                default:
                    var (list, value) = Axis(control);
                    list.Remove(value);
                    list.Add(value);
                    return;
            }
        }

        public void Release(Control control)
        {
            switch (control)
            {
                case Control.Slow:
                    Slow = false;
                    return;
                case Control.Shot:
                    _shotHeld = false;
                    return;
                case Control.Bomb:
                    _bombHeld = false;
                    return;
                default:
                    var (list, value) = Axis(control);
                    list.Remove(value);
                    return;
            }
        }

        /// <summary>この Tick に撃つか。短い押下の記録はここで消える。</summary>
        public bool TakeShot()
        {
            var shot = _shotHeld || _shotLatched;
            _shotLatched = false;
            return shot;
        }

        /// <summary>この Tick にボムを使うか。押した記録はここで消える。</summary>
        public bool TakeBomb()
        {
            var bomb = _bombLatched;
            _bombLatched = false;
            return bomb;
        }

        public void Clear()
        {
            _horizontal.Clear();
            _vertical.Clear();
            Slow = false;
            _shotHeld = false;
            _shotLatched = false;
            _bombLatched = false;
            _bombHeld = false;
        }

        private (List<int> List, int Value) Axis(Control control) => control switch
        {
            Control.Left => (_horizontal, -1),
            Control.Right => (_horizontal, 1),
            Control.Down => (_vertical, -1),
            Control.Up => (_vertical, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(control), control, "未定義の操作キーです。"),
        };
    }
}
