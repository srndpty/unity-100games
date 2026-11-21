namespace Game.Core
{
    /// <summary>
    /// 地形との衝突解決。1 軸ずつ動かし、進む先の列（行）を近い順に調べて、最初の固体の手前で止める。
    /// 移動量がタイルより大きくても、途中の列（行）を飛ばさない。
    /// </summary>
    public static class BodyMotion
    {
        /// <summary>横に <paramref name="dx"/> 動かす。壁に当たって止まったら true。</summary>
        public static bool MoveX(TileMap map, ref Box box, int dx)
        {
            if (dx > 0)
            {
                var first = Units.FloorDiv(box.Right - 1, Units.Tile) + 1;
                var last = Units.FloorDiv(box.Right + dx - 1, Units.Tile);
                for (var column = first; column <= last; column++)
                {
                    if (map.AnySolidInColumn(column, box))
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
                    if (map.AnySolidInColumn(column, box))
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
        public static bool MoveY(TileMap map, ref Box box, int dy)
        {
            if (dy > 0)
            {
                var first = Units.FloorDiv(box.Top - 1, Units.Tile) + 1;
                var last = Units.FloorDiv(box.Top + dy - 1, Units.Tile);
                for (var row = first; row <= last; row++)
                {
                    if (map.AnySolidInRow(row, box))
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
                    if (map.AnySolidInRow(row, box))
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

    /// <summary>
    /// 接地検出。足元の 1 単位下に固体があるときだけ接地とする（床にちょうど接していること）。
    /// 敵との接触判定（<see cref="EnemyContact"/>）とは独立していて、敵の上に立つことはない。
    /// </summary>
    public static class GroundProbe
    {
        public static bool IsGrounded(TileMap map, Box box)
        {
            return map.AnySolidInRow(Units.FloorDiv(box.Bottom - 1, Units.Tile), box);
        }

        /// <summary>
        /// 向き <paramref name="direction"/> へ <paramref name="step"/> 進んだとき、前の端の真下に足場があるか（崖で引き返す敵用）。
        /// </summary>
        public static bool HasGroundAhead(TileMap map, Box box, int direction, int step)
        {
            var front = direction > 0 ? box.Right + step - 1 : box.Left - step;
            return map.IsSolid(Units.FloorDiv(front, Units.Tile), Units.FloorDiv(box.Bottom - 1, Units.Tile));
        }
    }
}
