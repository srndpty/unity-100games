namespace Game.Core
{
    /// <summary>
    /// プレイ領域と単位。原点は左下、y は上向き（Unity のワールド座標と同じ向き）。
    /// 当たり判定の位置は 1 px = <see cref="Unit"/> 単位の整数。弾の内部の位置はさらに細かい
    /// <see cref="BulletPool.PositionShift"/> ビットを持つ。
    /// </summary>
    public static class Field
    {
        /// <summary>1 px の単位数。</summary>
        public const int Unit = 256;

        /// <summary>プレイ領域の幅（px）。</summary>
        public const int WidthPx = 384;

        /// <summary>プレイ領域の高さ（px）。</summary>
        public const int HeightPx = 448;

        /// <summary>弾が消える領域外の余白（px）。最も大きい弾と自機の半径の和より大きいので、余白の外の弾は自機に届かない。</summary>
        public const int MarginPx = 32;

        public const int Width = WidthPx * Unit;

        public const int Height = HeightPx * Unit;

        public const int TicksPerSecond = 60;

        /// <summary>プレイ領域（両端を含む）。</summary>
        public static Rect Bounds => new(0, 0, Width, Height);

        /// <summary>弾が生きていられる範囲（余白を含む）。</summary>
        public static Rect BulletBounds => new(-MarginPx * Unit, -MarginPx * Unit, Width + (MarginPx * Unit), Height + (MarginPx * Unit));
    }
}
