using System;
using IntGeometry;

namespace Game.Core
{
    /// <summary>
    /// 台の大きさと座標の単位。座標は整数で、1 マス = <see cref="Unit"/> 単位。台は横 <see cref="Columns"/> × 縦 <see cref="Rows"/> マス、
    /// 原点は左下、y は上向き（重力は −y）。
    /// </summary>
    public static class Board
    {
        public const int Unit = 1024;

        public const int Columns = 24;

        public const int Rows = 44;

        public const int Width = Columns * Unit;

        public const int Height = Rows * Unit;

        /// <summary>1 Tick を分ける区間の数。物理（重力・フリッパー・球の移動）は区間ごとに進む（60 Tick/秒 × 8 = 480 回/秒）。</summary>
        public const int Substeps = 8;

        /// <summary>速度の単位：1 区間あたり 1/<see cref="VelocityScale"/> 単位。</summary>
        public const int VelocityScale = 256;

        /// <summary>
        /// 衝突物の線分の長さの上限（16 マス）。点と線分の距離を long で厳密に比べるための制限（外積の 2 乗が long に収まる）。
        /// 長い壁はデータで分割する。
        /// </summary>
        public const int MaxSegmentLength = 16 * Unit;

        /// <summary>球がこの範囲（台の外に 2 マス）から出たら失ったとする（どの経路でも台の外へ飛び続けない）。</summary>
        public const int Margin = 2 * Unit;

        public static bool InBounds(Vec2i point) =>
            point.X >= -Margin && point.X <= Width + Margin && point.Y >= -Margin && point.Y <= Height + Margin;

        public static bool Contains(Vec2i point) => point.X >= 0 && point.X <= Width && point.Y >= 0 && point.Y <= Height;
    }

    /// <summary>点と線分の距離（整数で厳密）。線分の長さは <see cref="Board.MaxSegmentLength"/> 以下、点は線分の近く（数マス以内）を前提にする。</summary>
    public static class Capsule
    {
        /// <summary>点 <paramref name="point"/> と線分 a-b の距離が <paramref name="radius"/> 未満か（ちょうど等しいときは false）。a == b なら点との距離。</summary>
        public static bool Inside(Vec2i point, Vec2i a, Vec2i b, int radius)
        {
            var r2 = (long)radius * radius;
            var ab = b - a;
            var ap = point - a;
            var length2 = ab.LengthSquared;
            if (length2 == 0) { return ap.LengthSquared < r2; }
            var dot = Vec2i.Dot(ap, ab);
            if (dot <= 0) { return ap.LengthSquared < r2; }
            if (dot >= length2) { return (point - b).LengthSquared < r2; }
            var cross = Cross(ab, ap);
            return cross * cross < r2 * length2;
        }

        /// <summary>
        /// 点と線分の距離の切り捨て（整数）。保守的前進で「あと何単位は離れているか」を見積もるのに使う。
        /// 内側の判定には使わない（<see cref="Inside"/> が厳密）。
        /// </summary>
        public static long DistanceFloor(Vec2i point, Vec2i a, Vec2i b)
        {
            var ab = b - a;
            var ap = point - a;
            var length2 = ab.LengthSquared;
            if (length2 == 0) { return IntMath.Sqrt(ap.LengthSquared); }
            var dot = Vec2i.Dot(ap, ab);
            if (dot <= 0) { return IntMath.Sqrt(ap.LengthSquared); }
            if (dot >= length2) { return IntMath.Sqrt((point - b).LengthSquared); }
            var cross = Math.Abs(Cross(ab, ap));

            // |cross| / |ab| の切り捨て：floor(sqrt(cross^2 / length2))。cross^2 は long に収まる範囲を前提にする。
            return IntMath.Sqrt(cross * cross / length2);
        }

        /// <summary>線分 a-b の上で点に最も近い点（四捨五入）。</summary>
        public static Vec2i Closest(Vec2i point, Vec2i a, Vec2i b)
        {
            var ab = b - a;
            var length2 = ab.LengthSquared;
            if (length2 == 0) { return a; }
            var dot = Vec2i.Dot(point - a, ab);
            if (dot <= 0) { return a; }
            if (dot >= length2) { return b; }
            return new Vec2i(a.X + IntMath.RoundDiv(ab.X * dot, length2), a.Y + IntMath.RoundDiv(ab.Y * dot, length2));
        }

        /// <summary>
        /// 2 つの線分 p0-p1 と a-b の最短距離が <paramref name="radius"/> 未満か（厳密）。交わるか、4 つの端点と相手の線分の距離のどれかが半径未満なら true。
        /// </summary>
        public static bool SegmentsWithin(Vec2i p0, Vec2i p1, Vec2i a, Vec2i b, int radius)
        {
            if (Intersects(p0, p1, a, b)) { return true; }
            return Inside(p0, a, b, radius) || Inside(p1, a, b, radius) || Inside(a, p0, p1, radius) || Inside(b, p0, p1, radius);
        }

        /// <summary>線分どうしが共有点を持つか（外積で厳密に判定、033 の <c>Segment</c> と同じ）。長さ 0 の線分は false。</summary>
        public static bool Intersects(Vec2i a0, Vec2i a1, Vec2i b0, Vec2i b1)
        {
            if (a0 == a1 || b0 == b1) { return false; }
            var d1 = Cross(b1 - b0, a0 - b0);
            var d2 = Cross(b1 - b0, a1 - b0);
            var d3 = Cross(a1 - a0, b0 - a0);
            var d4 = Cross(a1 - a0, b1 - a0);
            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) { return true; }
            return (d1 == 0 && Within(b0, b1, a0)) || (d2 == 0 && Within(b0, b1, a1)) || (d3 == 0 && Within(a0, a1, b0)) || (d4 == 0 && Within(a0, a1, b1));
        }

        /// <summary>u × v。</summary>
        public static long Cross(Vec2i u, Vec2i v) => ((long)u.X * v.Y) - ((long)u.Y * v.X);

        private static bool Within(Vec2i a, Vec2i b, Vec2i c) =>
            c.X >= Math.Min(a.X, b.X) && c.X <= Math.Max(a.X, b.X) && c.Y >= Math.Min(a.Y, b.Y) && c.Y <= Math.Max(a.Y, b.Y);
    }
}
