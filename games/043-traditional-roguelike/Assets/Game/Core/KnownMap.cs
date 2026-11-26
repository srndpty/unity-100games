using System;
using IntGeometry;

namespace Game.Core
{
    /// <summary>
    /// 自機が知っている地図。視界（今見えているマス）とは別に持つ。
    /// 地形は一度見たら覚え続ける。品は「最後に見たときにあった品」を覚え、見えない所で品が動いても記憶は古いまま。
    /// 敵は覚えない（今見えている敵だけを <see cref="PlayerView"/> が出す）。
    /// </summary>
    public sealed class KnownMap
    {
        /// <summary>品の記憶がないことを表す値。</summary>
        public const int NoItem = -1;

        private readonly bool[] _seen;
        private readonly int[] _item;

        public KnownMap(int width, int height)
        {
            Width = width;
            Height = height;
            _seen = new bool[width * height];
            _item = new int[width * height];
            for (int i = 0; i < _item.Length; i++) { _item[i] = NoItem; }
        }

        public int Width { get; }

        public int Height { get; }

        public bool IsSeen(Vec2i p) => InBounds(p) && _seen[Index(p)];

        /// <summary>覚えている品。覚えていなければ null。</summary>
        public ItemKind? RememberedItem(Vec2i p)
        {
            if (!InBounds(p)) { return null; }
            var v = _item[Index(p)];
            return v == NoItem ? null : (ItemKind)v;
        }

        /// <summary>見えているマスの地形を覚え、品の記憶を今の品で上書きする。</summary>
        public void Observe(bool[] visible, Func<Vec2i, ItemKind?> itemAt)
        {
            for (int i = 0; i < visible.Length; i++)
            {
                if (!visible[i]) { continue; }
                _seen[i] = true;
                var item = itemAt(new Vec2i(i % Width, i / Width));
                _item[i] = item.HasValue ? (int)item.Value : NoItem;
            }
        }

        /// <summary>地図の巻物：床と、床に接する壁を既知にする。品の記憶は変えない（品と敵は明かさない）。</summary>
        public void RevealTerrain(FloorGrid grid)
        {
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    var p = new Vec2i(x, y);
                    if (grid.IsWalkable(p) || TouchesFloor(grid, p)) { _seen[Index(p)] = true; }
                }
            }
        }

        public KnownMap Clone()
        {
            var copy = new KnownMap(Width, Height);
            Array.Copy(_seen, copy._seen, _seen.Length);
            Array.Copy(_item, copy._item, _item.Length);
            return copy;
        }

        internal void Mix(ref TileMotion.StateHash hash)
        {
            for (int i = 0; i < _seen.Length; i++)
            {
                hash.Mix(_seen[i] ? _item[i] + 2 : 0);
            }
        }

        private static bool TouchesFloor(FloorGrid grid, Vec2i p)
        {
            for (int d = 0; d < Directions.Count; d++)
            {
                if (grid.IsWalkable(p + ((Direction)d).Offset())) { return true; }
            }

            return false;
        }

        private bool InBounds(Vec2i p) => p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;

        private int Index(Vec2i p) => (p.Y * Width) + p.X;
    }
}
