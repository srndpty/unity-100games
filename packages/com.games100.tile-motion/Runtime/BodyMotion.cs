namespace TileMotion
{
    /// <summary>
    /// タイルの地形。列・行はタイル単位の整数で、範囲外をどう扱うか（左右の外は壁など）は実装が決める。
    /// </summary>
    public interface ISolidGrid
    {
        bool IsSolid(int column, int row);
    }

    /// <summary>箱が重なる行・列の範囲で固体を探す。</summary>
    public static class SolidQuery
    {
        /// <summary>箱が重なる行の中に、列 <paramref name="column"/> の固体があるか。</summary>
        public static bool AnyInColumn(ISolidGrid grid, int column, Box box)
        {
            var from = Units.FloorDiv(box.Bottom, Units.Tile);
            var to = Units.FloorDiv(box.Top - 1, Units.Tile);
            for (var row = from; row <= to; row++)
            {
                if (grid.IsSolid(column, row)) { return true; }
            }

            return false;
        }

        /// <summary>箱が重なる列の中に、行 <paramref name="row"/> の固体があるか。</summary>
        public static bool AnyInRow(ISolidGrid grid, int row, Box box)
        {
            var from = Units.FloorDiv(box.Left, Units.Tile);
            var to = Units.FloorDiv(box.Right - 1, Units.Tile);
            for (var column = from; column <= to; column++)
            {
                if (grid.IsSolid(column, row)) { return true; }
            }

            return false;
        }

        /// <summary>箱が重なるタイルのどれかが固体か。</summary>
        public static bool Overlaps(ISolidGrid grid, Box box)
        {
            var from = Units.FloorDiv(box.Left, Units.Tile);
            var to = Units.FloorDiv(box.Right - 1, Units.Tile);
            for (var column = from; column <= to; column++)
            {
                if (AnyInColumn(grid, column, box)) { return true; }
            }

            return false;
        }
    }

    /// <summary>
    /// 地形との衝突解決。1 軸ずつ動かし、進む先の列（行）を近い順に調べて、最初の固体の手前で止める。
    /// 移動量がタイルより大きくても、途中の列（行）を飛ばさない。
    /// </summary>
    public static class BodyMotion
    {
        /// <summary>横に <paramref name="dx"/> 動かす。壁に当たって止まったら true。</summary>
        public static bool MoveX(ISolidGrid grid, ref Box box, int dx)
        {
            if (dx > 0)
            {
                var first = Units.FloorDiv(box.Right - 1, Units.Tile) + 1;
                var last = Units.FloorDiv(box.Right + dx - 1, Units.Tile);
                for (var column = first; column <= last; column++)
                {
                    if (SolidQuery.AnyInColumn(grid, column, box))
                    {
                        box = box.At((column * Units.Tile) - box.Width, box.Y);
                        return true;
                    }
                }
            }
            else if (dx < 0)
            {
                var first = Units.FloorDiv(box.Left, Units.Tile) - 1;
                var last = Units.FloorDiv(box.Left + dx, Units.Tile);
                for (var column = first; column >= last; column--)
                {
                    if (SolidQuery.AnyInColumn(grid, column, box))
                    {
                        box = box.At((column + 1) * Units.Tile, box.Y);
                        return true;
                    }
                }
            }

            box = box.Offset(dx, 0);
            return false;
        }

        /// <summary>縦に <paramref name="dy"/> 動かす（上が正）。床か天井に当たって止まったら true。</summary>
        public static bool MoveY(ISolidGrid grid, ref Box box, int dy)
        {
            if (dy > 0)
            {
                var first = Units.FloorDiv(box.Top - 1, Units.Tile) + 1;
                var last = Units.FloorDiv(box.Top + dy - 1, Units.Tile);
                for (var row = first; row <= last; row++)
                {
                    if (SolidQuery.AnyInRow(grid, row, box))
                    {
                        box = box.At(box.X, (row * Units.Tile) - box.Height);
                        return true;
                    }
                }
            }
            else if (dy < 0)
            {
                var first = Units.FloorDiv(box.Bottom, Units.Tile) - 1;
                var last = Units.FloorDiv(box.Bottom + dy, Units.Tile);
                for (var row = first; row >= last; row--)
                {
                    if (SolidQuery.AnyInRow(grid, row, box))
                    {
                        box = box.At(box.X, (row + 1) * Units.Tile);
                        return true;
                    }
                }
            }

            box = box.Offset(0, dy);
            return false;
        }
    }

    /// <summary>接地検出。足元の 1 単位下に固体があるときだけ接地とする（床にちょうど接していること）。</summary>
    public static class GroundProbe
    {
        public static bool IsGrounded(ISolidGrid grid, Box box)
        {
            return SolidQuery.AnyInRow(grid, Units.FloorDiv(box.Bottom - 1, Units.Tile), box);
        }
    }
}
