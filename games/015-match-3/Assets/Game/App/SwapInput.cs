using System;
using Game.Core;

namespace Game.App
{
    /// <summary>
    /// マウスとキーボードの操作を入れ替えの意図に変える。盤面のルールは知らず、入れ替えの合否はゲームが決める。
    /// マウス：クリックで選び、隣をクリックするか、押したまま隣へドラッグすると入れ替える。
    /// キーボード：矢印でカーソルを動かし、Space で選び、選んだまま矢印を押すとその方向と入れ替える。
    /// </summary>
    public sealed class SwapInput
    {
        /// <summary>ドラッグとみなす、押したマスの中心からの距離（マス単位）。</summary>
        public const double DragThreshold = 0.6;

        private Point? _dragOrigin;
        private bool _deselectOnRelease;

        /// <summary>選んでいるマス。</summary>
        public Point? Selected { get; private set; }

        /// <summary>キーボードのカーソル。</summary>
        public Point Cursor { get; private set; } = new(Board.Width / 2, Board.Height / 2);

        /// <summary>最後の操作がキーボードか。カーソルの表示に使う。</summary>
        public bool KeyboardActive { get; private set; }

        /// <summary>
        /// マウスのボタンを押した。位置は盤面座標（左下が (0, 0)、1マスが 1）。
        /// 選んだマスの隣を押すと入れ替えを返す。
        /// </summary>
        public Swap? PointerDown(double x, double y)
        {
            KeyboardActive = false;
            if (!TryCell(x, y, out var cell))
            {
                Clear();
                return null;
            }

            Cursor = cell;
            if (Selected is Point selected && selected.IsAdjacentTo(cell))
            {
                Clear();
                return new Swap(selected, cell);
            }

            _deselectOnRelease = Selected == cell;
            Selected = cell;
            _dragOrigin = cell;
            return null;
        }

        /// <summary>押したままマウスを動かした。押したマスの中心から十分離れると、その方向の隣と入れ替える。</summary>
        public Swap? PointerMove(double x, double y)
        {
            if (_dragOrigin is not Point origin || !double.IsFinite(x) || !double.IsFinite(y)) { return null; }
            var dx = x - (origin.X + 0.5);
            var dy = y - (origin.Y + 0.5);
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) < DragThreshold) { return null; }
            var target = Math.Abs(dx) >= Math.Abs(dy) ? origin.Offset(Math.Sign(dx), 0) : origin.Offset(0, Math.Sign(dy));
            Clear();
            return new Swap(origin, target);
        }

        /// <summary>マウスのボタンを離した。選んでいたマスをもう一度クリックしたなら選択を外す。</summary>
        public void PointerUp()
        {
            if (_dragOrigin != null && _deselectOnRelease) { Selected = null; }
            _dragOrigin = null;
            _deselectOnRelease = false;
        }

        /// <summary>矢印キー。選んでいればその方向と入れ替え、選んでいなければカーソルを動かす。</summary>
        public Swap? Move(int dx, int dy)
        {
            if (Math.Abs(dx) + Math.Abs(dy) != 1) { throw new ArgumentException("上下左右の1マスで指定してください。"); }
            KeyboardActive = true;
            _dragOrigin = null;
            if (Selected is Point selected)
            {
                var target = selected.Offset(dx, dy);
                Clear();
                if (Board.IsInside(target)) { Cursor = target; }
                return new Swap(selected, target);
            }

            var moved = Cursor.Offset(dx, dy);
            if (Board.IsInside(moved)) { Cursor = moved; }
            return null;
        }

        /// <summary>Space。カーソルのマスを選ぶ。選んでいるマスなら外す。</summary>
        public void ToggleSelect()
        {
            KeyboardActive = true;
            _dragOrigin = null;
            Selected = Selected == Cursor ? null : Cursor;
        }

        /// <summary>選択とドラッグを取り消す。カーソルの位置は残す。</summary>
        public void Clear()
        {
            Selected = null;
            _dragOrigin = null;
            _deselectOnRelease = false;
        }

        private static bool TryCell(double x, double y, out Point cell)
        {
            cell = default;
            if (!double.IsFinite(x) || !double.IsFinite(y) || x < 0 || y < 0 || x >= Board.Width || y >= Board.Height) { return false; }
            cell = new Point((int)x, (int)y);
            return true;
        }
    }
}
