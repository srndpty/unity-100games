using System;

namespace Game.Core
{
    /// <summary>
    /// 横・高さ・奥行きの 3 軸の箱。各軸は半開区間 [Min, Max) で、端がちょうど接するだけでは重ならない。
    /// 奥行きは中心と半幅で持つので、2 つの箱は z の差が半幅の和より小さいときだけ重なる。
    /// </summary>
    public readonly struct DepthBox : IEquatable<DepthBox>
    {
        public DepthBox(int minX, int maxX, int minY, int maxY, int z, int depth)
        {
            if (maxX <= minX || maxY <= minY) { throw new ArgumentOutOfRangeException(nameof(maxX), "箱の幅と高さは 1 以上です。"); }
            if (depth <= 0) { throw new ArgumentOutOfRangeException(nameof(depth), "奥行きの半幅は 1 以上です。"); }
            MinX = minX;
            MaxX = maxX;
            MinY = minY;
            MaxY = maxY;
            Z = z;
            Depth = depth;
        }

        public int MinX { get; }

        public int MaxX { get; }

        public int MinY { get; }

        public int MaxY { get; }

        public int Z { get; }

        public int Depth { get; }

        public int MinZ => Z - Depth;

        public int MaxZ => Z + Depth;

        /// <summary>足元の位置と向きから、原点からの箱（右向きで定義）を置く。</summary>
        public static DepthBox Place(int x, int y, int z, int facing, BoxShape shape)
        {
            int minX = facing >= 0 ? x + shape.X : x - shape.X - shape.W;
            return new DepthBox(minX, minX + shape.W, y + shape.Y, y + shape.Y + shape.H, z, shape.Depth);
        }

        public bool Overlaps(DepthBox other) =>
            MinX < other.MaxX && other.MinX < MaxX
            && MinY < other.MaxY && other.MinY < MaxY
            && DepthOverlaps(other);

        /// <summary>奥行きだけの重なり。z の差が半幅の和より小さい。</summary>
        public bool DepthOverlaps(DepthBox other) => Math.Abs((long)Z - other.Z) < (long)Depth + other.Depth;

        public bool Equals(DepthBox other) =>
            MinX == other.MinX && MaxX == other.MaxX && MinY == other.MinY && MaxY == other.MaxY && Z == other.Z && Depth == other.Depth;

        public override bool Equals(object? obj) => obj is DepthBox other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(MinX, MaxX, MinY, MaxY, Z, Depth);

        public static bool operator ==(DepthBox left, DepthBox right) => left.Equals(right);

        public static bool operator !=(DepthBox left, DepthBox right) => !left.Equals(right);
    }

    /// <summary>足元の中央からの箱の形（右向き）。x は前が正、y は上が正、Depth は奥行きの半幅。</summary>
    public readonly struct BoxShape : IEquatable<BoxShape>
    {
        public const int Limit = 1 << 22;

        public BoxShape(int x, int y, int w, int h, int depth)
        {
            if (w <= 0 || h <= 0 || depth <= 0) { throw new ArgumentOutOfRangeException(nameof(w), "箱の大きさは 1 以上です。"); }
            if (Math.Abs(x) > Limit || Math.Abs(y) > Limit || w > Limit || h > Limit || depth > Limit)
            {
                throw new ArgumentOutOfRangeException(nameof(x), "箱が大きすぎます。");
            }

            X = x;
            Y = y;
            W = w;
            H = h;
            Depth = depth;
        }

        public int X { get; }

        public int Y { get; }

        public int W { get; }

        public int H { get; }

        public int Depth { get; }

        public bool Equals(BoxShape other) => X == other.X && Y == other.Y && W == other.W && H == other.H && Depth == other.Depth;

        public override bool Equals(object? obj) => obj is BoxShape other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Y, W, H, Depth);

        public static bool operator ==(BoxShape left, BoxShape right) => left.Equals(right);

        public static bool operator !=(BoxShape left, BoxShape right) => !left.Equals(right);
    }

    /// <summary>画面への投影と描画順。描画・撮影・テストはここだけを使う。</summary>
    public static class Depth
    {
        /// <summary>画面の縦位置（単位）。奥ほど上、跳ぶほど上。</summary>
        public static int ScreenY(int y, int z, BrawlConfig config)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            return (int)((long)z * config.DepthNum / config.DepthDen) + y;
        }

        /// <summary>影（地面）の画面の縦位置。</summary>
        public static int ShadowY(int z, BrawlConfig config) => ScreenY(0, z, config);

        /// <summary>
        /// 描く順（先に描くものから）。z の大きい（奥の）順、同じ z は出現番号の小さい順。高さは順に影響しない。
        /// 結果は <paramref name="order"/> の先頭から書き、書いた数を返す。割り当てはしない。
        /// </summary>
        public static int DrawOrder(Brawl brawl, int[] order)
        {
            if (brawl == null) { throw new ArgumentNullException(nameof(brawl)); }
            if (order == null) { throw new ArgumentNullException(nameof(order)); }
            int count = 0;
            for (int slot = 0; slot < brawl.SlotCount; slot++)
            {
                if (!brawl.At(slot).InWorld) { continue; }
                if (count >= order.Length) { throw new ArgumentException("配列が足りません。", nameof(order)); }
                int index = count++;
                order[index] = slot;
                while (index > 0 && DrawsBefore(brawl.At(slot), brawl.At(order[index - 1])))
                {
                    order[index] = order[index - 1];
                    order[index - 1] = slot;
                    index--;
                }
            }

            return count;
        }

        /// <summary>a を b より先に（奥に）描くか。</summary>
        public static bool DrawsBefore(Actor a, Actor b)
        {
            if (a == null) { throw new ArgumentNullException(nameof(a)); }
            if (b == null) { throw new ArgumentNullException(nameof(b)); }
            if (a.Z != b.Z) { return a.Z > b.Z; }
            return a.Serial < b.Serial;
        }
    }
}
