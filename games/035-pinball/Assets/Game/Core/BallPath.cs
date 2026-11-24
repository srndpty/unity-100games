using System;
using IntGeometry;

namespace Game.Core
{
    /// <summary>掃引の相手：線分 a-b と、球の中心がこれより近づいたら触れたとする距離（半径の和）。</summary>
    public readonly struct Shape
    {
        public Shape(Vec2i a, Vec2i b, int reach, int id)
        {
            A = a;
            B = b;
            Reach = reach;
            Id = id;
        }

        public Vec2i A { get; }

        public Vec2i B { get; }

        public int Reach { get; }

        /// <summary>呼び出し側の識別子（衝突物の番号など）。</summary>
        public int Id { get; }
    }

    /// <summary>
    /// 1 区間の球の経路と最初の接触（連続衝突検出）。
    /// <para>
    /// 経路は始点 p0 から終点 p1 への直線を、長い軸に沿って 1 単位ずつ刻んだ点の列 p(k)（k = 0..K、K = max(|dx|, |dy|)、短い軸は四捨五入）とみなす。
    /// 隣り合う点は √2 単位以内なので、厚さ（2 × 半径の和）が 2 単位以上ある相手をすり抜けない。斜めのかすりも点の列で拾う。
    /// 最初に相手の内側（距離 &lt; 半径の和）に入った点を接触とし、同じ点で複数に入ったら並びの先の相手を取る。
    /// </para>
    /// <para>
    /// 全ての点を調べる代わりに、保守的前進で飛ばす：点 p(k) と最も近い相手の隙間（距離の切り捨て − 半径の和）を g とすると、
    /// k から j 歩先の点までの移動は √2 × j 以下なので、j &lt; g / √2 の点は内側に入らない。歩幅を max(1, g / 2) にする。
    /// 結果は全ての点を調べた場合と一致する（テストで照合）。
    /// </para>
    /// </summary>
    public static class BallPath
    {
        /// <summary>接触がなければ -1。あれば <paramref name="shapes"/> の添字を返し、<paramref name="step"/> に入った点の番号 k を書く。</summary>
        public static int First(Vec2i p0, Vec2i p1, Shape[] shapes, int count, int[] scratch, out int step)
        {
            if (shapes == null) { throw new ArgumentNullException(nameof(shapes)); }
            if (scratch == null) { throw new ArgumentNullException(nameof(scratch)); }
            step = 0;

            // 経路の外接矩形を半径の和だけ広げて相手の外接矩形と重なるものだけを候補にする（並びの順は保つ）。
            var candidates = 0;
            var minX = Math.Min(p0.X, p1.X);
            var maxX = Math.Max(p0.X, p1.X);
            var minY = Math.Min(p0.Y, p1.Y);
            var maxY = Math.Max(p0.Y, p1.Y);
            for (var i = 0; i < count; i++)
            {
                var shape = shapes[i];
                var reach = shape.Reach;
                if (Math.Max(shape.A.X, shape.B.X) + reach < minX || Math.Min(shape.A.X, shape.B.X) - reach > maxX) { continue; }
                if (Math.Max(shape.A.Y, shape.B.Y) + reach < minY || Math.Min(shape.A.Y, shape.B.Y) - reach > maxY) { continue; }
                scratch[candidates++] = i;
            }

            if (candidates == 0) { return -1; }

            var total = Steps(p0, p1);
            var k = 0;
            while (true)
            {
                var point = PointAt(p0, p1, k, total);
                var gap = long.MaxValue;
                for (var c = 0; c < candidates; c++)
                {
                    var shape = shapes[scratch[c]];
                    if (Capsule.Inside(point, shape.A, shape.B, shape.Reach))
                    {
                        step = k;
                        return scratch[c];
                    }

                    gap = Math.Min(gap, Capsule.DistanceFloor(point, shape.A, shape.B) - shape.Reach);
                }

                if (k == total) { return -1; }
                var advance = (int)Math.Max(1, Math.Min(gap / 2, total));
                k = Math.Min(total, k + advance);
            }
        }

        /// <summary>経路の点の数 − 1（長い軸の移動量）。</summary>
        public static int Steps(Vec2i p0, Vec2i p1) => Math.Max(Math.Abs(p1.X - p0.X), Math.Abs(p1.Y - p0.Y));

        /// <summary>経路の k 番目の点（長い軸はちょうど k 単位、短い軸は四捨五入）。</summary>
        public static Vec2i PointAt(Vec2i p0, Vec2i p1, int k, int total)
        {
            if (total == 0) { return p0; }
            var d = p1 - p0;
            return new Vec2i(p0.X + IntMath.RoundDiv((long)d.X * k, total), p0.Y + IntMath.RoundDiv((long)d.Y * k, total));
        }
    }
}
