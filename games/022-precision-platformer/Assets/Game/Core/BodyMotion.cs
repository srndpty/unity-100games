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

    /// <summary>接地検出。足元の 1 単位下に固体があるときだけ接地とする（床にちょうど接していること）。</summary>
    public static class GroundProbe
    {
        public static bool IsGrounded(TileMap map, Box box)
        {
            return map.AnySolidInRow(Units.FloorDiv(box.Bottom - 1, Units.Tile), box);
        }
    }

    /// <summary>壁の検出。壁蹴りと壁ずりに使う。</summary>
    public static class WallProbe
    {
        /// <summary>向き <paramref name="side"/>（右 1・左 -1）に、隙間 <paramref name="reach"/> 単位未満で固体の壁があるか（1 なら接しているときだけ）。</summary>
        public static bool Near(TileMap map, Box box, int side, int reach)
        {
            if (side != 1 && side != -1)
            {
                throw new System.ArgumentOutOfRangeException(nameof(side), side, "1 か -1 にしてください。");
            }

            if (reach < 1 || reach >= Units.Tile)
            {
                throw new System.ArgumentOutOfRangeException(nameof(reach), reach, $"1〜{Units.Tile - 1} の範囲で指定してください。");
            }

            int first;
            int last;
            if (side > 0)
            {
                first = Units.FloorDiv(box.Right, Units.Tile);
                last = Units.FloorDiv(box.Right + reach - 1, Units.Tile);
            }
            else
            {
                first = Units.FloorDiv(box.Left - reach, Units.Tile);
                last = Units.FloorDiv(box.Left - 1, Units.Tile);
            }

            for (var column = first; column <= last; column++)
            {
                if (map.AnySolidInColumn(column, box))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 壁蹴りで蹴る壁の向き（右 1・左 -1、なければ 0）。両側にあるときは向いている側の壁を蹴る。
        /// </summary>
        public static int KickSide(TileMap map, Box box, int facing, int reach)
        {
            var right = Near(map, box, 1, reach);
            var left = Near(map, box, -1, reach);
            if (right && left)
            {
                return facing >= 0 ? 1 : -1;
            }

            return right ? 1 : left ? -1 : 0;
        }
    }
}
