using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    /// <summary>
    /// 押している操作キーを、Tick ごとの <see cref="PlayerInput"/> に変える。
    /// 左右を両方押しているときは後から押したほうに進む（離すと、まだ押しているほうに戻る）。
    /// ジャンプは、次の Tick までに押して離しても 1 Tick は押したことにする（短い押下を取りこぼさない）。
    /// </summary>
    public sealed class HeldControls
    {
        private readonly List<int> _directions = new();
        private bool _jumpHeld;
        private bool _jumpLatched;

        public bool Run { get; private set; }

        /// <summary>左 -1・なし 0・右 1。</summary>
        public int Horizontal => _directions.Count == 0 ? 0 : _directions[_directions.Count - 1];

        public void Press(Control control)
        {
            switch (control)
            {
                case Control.Run:
                    Run = true;
                    break;
                case Control.Jump:
                    _jumpHeld = true;
                    _jumpLatched = true;
                    break;
                default:
                    var direction = ToDirection(control);
                    _directions.Remove(direction);
                    _directions.Add(direction);
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
                case Control.Jump:
                    _jumpHeld = false;
                    break;
                default:
                    _directions.Remove(ToDirection(control));
                    break;
            }
        }

        /// <summary>この Tick の入力を取り出す。短い押下の記録はここで消える。</summary>
        public PlayerInput TakeTickInput()
        {
            var input = new PlayerInput(Horizontal, Run, _jumpHeld || _jumpLatched);
            _jumpLatched = false;
            return input;
        }

        public void Clear()
        {
            _directions.Clear();
            Run = false;
            _jumpHeld = false;
            _jumpLatched = false;
        }

        private static int ToDirection(Control control) => control switch
        {
            Control.Left => -1,
            Control.Right => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(control), control, "未定義の操作キーです。"),
        };
    }
}
