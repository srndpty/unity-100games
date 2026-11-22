using System;
using System.Collections.Generic;

namespace Game.App
{
    /// <summary>
    /// 押している操作キー。左右（上下）を両方押しているときは後から押したほうを使う（離すと、まだ押しているほうに戻る）。
    /// 射撃は、次の Tick までに押して離しても 1 Tick は押したことにする（短いクリックを取りこぼさない）。
    /// 照準はキーではないので持たない（<see cref="SiegeFlow.SetAim"/>）。
    /// </summary>
    public sealed class HeldControls
    {
        private readonly List<int> _horizontal = new();
        private readonly List<int> _vertical = new();
        private bool _fireHeld;
        private bool _fireLatched;

        /// <summary>左 -1・なし 0・右 1。</summary>
        public int Horizontal => _horizontal.Count == 0 ? 0 : _horizontal[_horizontal.Count - 1];

        /// <summary>下 -1・なし 0・上 1。</summary>
        public int Vertical => _vertical.Count == 0 ? 0 : _vertical[_vertical.Count - 1];

        public void Press(Control control)
        {
            if (control == Control.Fire)
            {
                _fireHeld = true;
                _fireLatched = true;
                return;
            }

            var (list, value) = Axis(control);
            list.Remove(value);
            list.Add(value);
        }

        public void Release(Control control)
        {
            if (control == Control.Fire)
            {
                _fireHeld = false;
                return;
            }

            var (list, value) = Axis(control);
            list.Remove(value);
        }

        /// <summary>この Tick に撃つか。短い押下の記録はここで消える。</summary>
        public bool TakeFire()
        {
            var fire = _fireHeld || _fireLatched;
            _fireLatched = false;
            return fire;
        }

        public void Clear()
        {
            _horizontal.Clear();
            _vertical.Clear();
            _fireHeld = false;
            _fireLatched = false;
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
