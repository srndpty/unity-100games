using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    /// <summary>
    /// 押している操作キーを、Tick ごとの <see cref="IntruderInput"/> に変える。
    /// 左右（上下）を両方押しているときは後から押したほうを使う（離すと、まだ押しているほうに戻る）。
    /// 走りと忍び足は押している間だけ効く（025 の HeldControls と同じ考え方）。
    /// </summary>
    public sealed class HeldControls
    {
        private readonly List<int> _horizontal = new();
        private readonly List<int> _vertical = new();

        /// <summary>左 -1・なし 0・右 1。</summary>
        public int Horizontal => _horizontal.Count == 0 ? 0 : _horizontal[_horizontal.Count - 1];

        /// <summary>下 -1・なし 0・上 1。</summary>
        public int Vertical => _vertical.Count == 0 ? 0 : _vertical[_vertical.Count - 1];

        public bool Run { get; private set; }

        public bool Sneak { get; private set; }

        public void Press(Control control)
        {
            switch (control)
            {
                case Control.Run:
                    Run = true;
                    break;
                case Control.Sneak:
                    Sneak = true;
                    break;
                default:
                    var (list, value) = Axis(control);
                    list.Remove(value);
                    list.Add(value);
                    break;
            }
        }

        public void Release(Control control)
        {
            switch (control)
            {
                case Control.Run:
                    Run = false;
                    break;
                case Control.Sneak:
                    Sneak = false;
                    break;
                default:
                    var (list, value) = Axis(control);
                    list.Remove(value);
                    break;
            }
        }

        public IntruderInput TickInput() => new(Horizontal, Vertical, Run, Sneak);

        public void Clear()
        {
            _horizontal.Clear();
            _vertical.Clear();
            Run = false;
            Sneak = false;
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
