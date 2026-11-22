using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    /// <summary>
    /// 押している操作キーを、Tick ごとの <see cref="AthleteCommand"/> に変える。
    /// 左右（上下）を両方押しているときは後から押したほうを使う（離すと、まだ押しているほうに戻る）。
    /// ストロークとロブは、次の Tick までに押して離しても 1 Tick は押したことにする（短い押下を取りこぼさない）。
    /// </summary>
    public sealed class HeldControls
    {
        private readonly List<int> _horizontal = new();
        private readonly List<int> _vertical = new();
        private bool _strokeHeld;
        private bool _strokeLatched;
        private bool _lobHeld;
        private bool _lobLatched;

        /// <summary>左 -1・なし 0・右 1。</summary>
        public int Horizontal => _horizontal.Count == 0 ? 0 : _horizontal[_horizontal.Count - 1];

        /// <summary>手前 -1・なし 0・奥 1。</summary>
        public int Vertical => _vertical.Count == 0 ? 0 : _vertical[_vertical.Count - 1];

        public void Press(Control control)
        {
            switch (control)
            {
                case Control.Stroke:
                    _strokeHeld = true;
                    _strokeLatched = true;
                    break;
                case Control.Lob:
                    _lobHeld = true;
                    _lobLatched = true;
                    break;
                case Control.Left:
                case Control.Right:
                    Push(_horizontal, control == Control.Left ? -1 : 1);
                    break;
                case Control.Up:
                case Control.Down:
                    Push(_vertical, control == Control.Down ? -1 : 1);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(control), control, "未定義の操作キーです。");
            }
        }

        public void Release(Control control)
        {
            switch (control)
            {
                case Control.Stroke:
                    _strokeHeld = false;
                    break;
                case Control.Lob:
                    _lobHeld = false;
                    break;
                case Control.Left:
                case Control.Right:
                    _horizontal.Remove(control == Control.Left ? -1 : 1);
                    break;
                case Control.Up:
                case Control.Down:
                    _vertical.Remove(control == Control.Down ? -1 : 1);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(control), control, "未定義の操作キーです。");
            }
        }

        /// <summary>この Tick の操作を取り出す。短い押下の記録はここで消える。</summary>
        public AthleteCommand TakeTickCommand()
        {
            var command = new AthleteCommand(Horizontal, Vertical, _strokeHeld || _strokeLatched, _lobHeld || _lobLatched);
            _strokeLatched = false;
            _lobLatched = false;
            return command;
        }

        public void Clear()
        {
            _horizontal.Clear();
            _vertical.Clear();
            _strokeHeld = false;
            _strokeLatched = false;
            _lobHeld = false;
            _lobLatched = false;
        }

        private static void Push(List<int> list, int value)
        {
            list.Remove(value);
            list.Add(value);
        }
    }
}
