using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    /// <summary>
    /// 押している操作キーを、Tick ごとの <see cref="PlayerInput"/> に変える。
    /// 左右を両方押しているときは後から押したほうを使う（離すと、まだ押しているほうに戻る）。
    /// ジャンプとショットは、次の Tick までに押して離しても 1 Tick は押したことにする（短い押下を取りこぼさない）。
    /// </summary>
    public sealed class HeldControls
    {
        private readonly List<int> _horizontal = new();
        private bool _jumpHeld;
        private bool _jumpLatched;
        private bool _shootHeld;
        private bool _shootLatched;

        /// <summary>左 -1・なし 0・右 1。</summary>
        public int Horizontal => _horizontal.Count == 0 ? 0 : _horizontal[_horizontal.Count - 1];

        public void Press(Control control)
        {
            switch (control)
            {
                case Control.Jump:
                    _jumpHeld = true;
                    _jumpLatched = true;
                    break;
                case Control.Shoot:
                    _shootHeld = true;
                    _shootLatched = true;
                    break;
                default:
                    var value = Direction(control);
                    _horizontal.Remove(value);
                    _horizontal.Add(value);
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
                case Control.Shoot:
                    _shootHeld = false;
                    break;
                default:
                    _horizontal.Remove(Direction(control));
                    break;
            }
        }

        /// <summary>この Tick の入力を取り出す。短い押下の記録はここで消える。</summary>
        public PlayerInput TakeTickInput()
        {
            var input = new PlayerInput(Horizontal, _jumpHeld || _jumpLatched, _shootHeld || _shootLatched);
            _jumpLatched = false;
            _shootLatched = false;
            return input;
        }

        public void Clear()
        {
            _horizontal.Clear();
            _jumpHeld = false;
            _jumpLatched = false;
            _shootHeld = false;
            _shootLatched = false;
        }

        private static int Direction(Control control) => control switch
        {
            Control.Left => -1,
            Control.Right => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(control), control, "未定義の操作キーです。"),
        };
    }
}
