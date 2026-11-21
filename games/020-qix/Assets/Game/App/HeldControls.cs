using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    /// <summary>
    /// 押している操作キー。向きのキーを複数押しているときは、後から押したほうを使う
    /// （離すと、まだ押しているうちで最後に押した向きに戻る）。
    /// </summary>
    public sealed class HeldControls
    {
        private readonly List<Direction> _directions = new();

        public bool Draw { get; private set; }

        public Direction Direction => _directions.Count == 0 ? Direction.None : _directions[_directions.Count - 1];

        public void Press(Control control)
        {
            if (control == Control.Draw)
            {
                Draw = true;
                return;
            }

            var direction = ToDirection(control);
            _directions.Remove(direction);
            _directions.Add(direction);
        }

        public void Release(Control control)
        {
            if (control == Control.Draw)
            {
                Draw = false;
                return;
            }

            _directions.Remove(ToDirection(control));
        }

        public void Clear()
        {
            _directions.Clear();
            Draw = false;
        }

        private static Direction ToDirection(Control control) => control switch
        {
            Control.Up => Direction.Up,
            Control.Down => Direction.Down,
            Control.Left => Direction.Left,
            _ => Direction.Right,
        };
    }
}
