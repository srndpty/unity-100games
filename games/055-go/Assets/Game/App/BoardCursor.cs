using Game.Core;

namespace Game.App
{
    /// <summary>キー操作のカーソル。キーを使うまでは出さない。端で止まる。最初は天元。</summary>
    public sealed class BoardCursor
    {
        public const int Start = (4 * Points.Size) + 4;

        public int Point { get; private set; } = Start;

        public bool Visible { get; private set; }

        /// <summary>右・上が正（盤の表記の向き）。</summary>
        public void Move(int dx, int dy)
        {
            Visible = true;
            var column = Clamp(Points.Column(Point) + dx);
            var row = Clamp(Points.Row(Point) + dy);
            Point = Points.At(column, row);
        }

        /// <summary>クリックした点へ置く（見えているかは変えない）。</summary>
        public void MoveTo(int point)
        {
            if (Points.IsOnBoard(point)) { Point = point; }
        }

        public void Reset()
        {
            Point = Start;
            Visible = false;
        }

        private static int Clamp(int value) => value < 0 ? 0 : value >= Points.Size ? Points.Size - 1 : value;
    }
}
