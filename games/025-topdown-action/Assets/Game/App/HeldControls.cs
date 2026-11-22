using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    /// <summary>
    /// 押している操作キーを、Tick ごとの <see cref="HeroInput"/> に変える。
    /// 左右（上下）を両方押しているときは後から押したほうを使う（離すと、まだ押しているほうに戻る）。
    /// 剣とロールは、次の Tick までに押して離しても 1 Tick は押したことにする（短い押下を取りこぼさない）。
    /// </summary>
    public sealed class HeldControls
    {
        private readonly List<int> _horizontal = new();
        private readonly List<int> _vertical = new();
        private bool _attackHeld;
        private bool _attackLatched;
        private bool _rollHeld;
        private bool _rollLatched;

        /// <summary>左 -1・なし 0・右 1。</summary>
        public int Horizontal => _horizontal.Count == 0 ? 0 : _horizontal[_horizontal.Count - 1];

        /// <summary>下 -1・なし 0・上 1。</summary>
        public int Vertical => _vertical.Count == 0 ? 0 : _vertical[_vertical.Count - 1];

        public void Press(Control control)
        {
            switch (control)
            {
                case Control.Attack:
                    _attackHeld = true;
                    _attackLatched = true;
                    break;
                case Control.Roll:
                    _rollHeld = true;
                    _rollLatched = true;
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
                case Control.Attack:
                    _attackHeld = false;
                    break;
                case Control.Roll:
                    _rollHeld = false;
                    break;
                default:
                    var (list, value) = Axis(control);
                    list.Remove(value);
                    break;
            }
        }

        /// <summary>この Tick の入力を取り出す。短い押下の記録はここで消える。</summary>
        public HeroInput TakeTickInput()
        {
            var input = new HeroInput(Horizontal, Vertical, _attackHeld || _attackLatched, _rollHeld || _rollLatched);
            _attackLatched = false;
            _rollLatched = false;
            return input;
        }

        public void Clear()
        {
            _horizontal.Clear();
            _vertical.Clear();
            _attackHeld = false;
            _attackLatched = false;
            _rollHeld = false;
            _rollLatched = false;
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
