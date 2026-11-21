using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    /// <summary>
    /// 押している操作キーを、Tick ごとの <see cref="PlayerInput"/> に変える。
    /// 左右・上下を両方押しているときは後から押したほうを使う（離すと、まだ押しているほうに戻る）。
    /// ジャンプとダッシュは、次の Tick までに押して離しても 1 Tick は押したことにする（短い押下を取りこぼさない）。
    /// </summary>
    public sealed class HeldControls
    {
        private readonly List<int> _horizontal = new();
        private readonly List<int> _vertical = new();
        private bool _jumpHeld;
        private bool _jumpLatched;
        private bool _dashHeld;
        private bool _dashLatched;

        /// <summary>左 -1・なし 0・右 1。</summary>
        public int Horizontal => Last(_horizontal);

        /// <summary>下 -1・なし 0・上 1。</summary>
        public int Vertical => Last(_vertical);

        public void Press(Control control)
        {
            switch (control)
            {
                case Control.Jump:
                    _jumpHeld = true;
                    _jumpLatched = true;
                    break;
                case Control.Dash:
                    _dashHeld = true;
                    _dashLatched = true;
                    break;
                default:
                    var (axis, value) = Direction(control);
                    axis.Remove(value);
                    axis.Add(value);
                    break;
            }
        }

        public void Release(Control control)
        {
            switch (control)
            {
                case Control.Jump:
                    _jumpHeld = false;
                    break;
                case Control.Dash:
                    _dashHeld = false;
                    break;
                default:
                    var (axis, value) = Direction(control);
                    axis.Remove(value);
                    break;
            }
        }

        /// <summary>この Tick の入力を取り出す。短い押下の記録はここで消える。</summary>
        public PlayerInput TakeTickInput()
        {
            var input = new PlayerInput(Horizontal, Vertical, _jumpHeld || _jumpLatched, _dashHeld || _dashLatched);
            _jumpLatched = false;
            _dashLatched = false;
            return input;
        }

        public void Clear()
        {
            _horizontal.Clear();
            _vertical.Clear();
            _jumpHeld = false;
            _jumpLatched = false;
            _dashHeld = false;
            _dashLatched = false;
        }

        private static int Last(List<int> axis) => axis.Count == 0 ? 0 : axis[axis.Count - 1];

        private (List<int> Axis, int Value) Direction(Control control) => control switch
        {
            Control.Left => (_horizontal, -1),
            Control.Right => (_horizontal, 1),
            Control.Up => (_vertical, 1),
            Control.Down => (_vertical, -1),
            _ => throw new ArgumentOutOfRangeException(nameof(control), control, "未定義の操作キーです。"),
        };
    }
}
