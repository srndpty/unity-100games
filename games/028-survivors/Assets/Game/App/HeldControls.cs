using System;
using System.Collections.Generic;

namespace Game.App
{
    /// <summary>
    /// 押している移動キー。左右（上下）を両方押しているときは後から押したほうを使う（離すと、まだ押しているほうに戻る。026 と同じ）。
    /// </summary>
    public sealed class HeldControls
    {
        private readonly List<int> _horizontal = new();
        private readonly List<int> _vertical = new();

        /// <summary>左 -1・なし 0・右 1。</summary>
        public int Horizontal => _horizontal.Count == 0 ? 0 : _horizontal[_horizontal.Count - 1];

        /// <summary>下 -1・なし 0・上 1。</summary>
        public int Vertical => _vertical.Count == 0 ? 0 : _vertical[_vertical.Count - 1];

        public void Press(Control control)
        {
            var (list, value) = Axis(control);
            list.Remove(value);
            list.Add(value);
        }

        public void Release(Control control)
        {
            var (list, value) = Axis(control);
            list.Remove(value);
        }

        public void Clear()
        {
            _horizontal.Clear();
            _vertical.Clear();
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
