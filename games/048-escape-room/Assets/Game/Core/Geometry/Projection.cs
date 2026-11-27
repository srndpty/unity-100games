using System;

namespace Game.Core.Geometry
{
    /// <summary>
    /// 見下ろす角度の表。角度は整数の直角三角形（cos・sin・斜辺）で持ち、レイの向きを整数のまま厳密に作る。
    /// Unity のカメラの角度（度）も同じ表から求める。
    /// </summary>
    public static class PitchTable
    {
        // (cos, sin, 斜辺)：0°・16.26°・22.62°・28.07°・36.87°・53.13°
        private static readonly int[] _cos = { 1, 24, 12, 15, 4, 3 };
        private static readonly int[] _sin = { 0, 7, 5, 8, 3, 4 };
        private static readonly int[] _hyp = { 1, 25, 13, 17, 5, 5 };

        public static int Count => _cos.Length;

        public static int Cos(int index) => _cos[index];

        public static int Sin(int index) => _sin[index];

        public static int Hyp(int index) => _hyp[index];

        /// <summary>見下ろす角度（度、正が下向き）。Unity の Euler の x にそのまま渡す。</summary>
        public static double Degrees(int index) => Math.Atan2(_sin[index], _cos[index]) * 180.0 / Math.PI;
    }

    /// <summary>
    /// 有理数 Num / Den（Den は正）。レイの媒介変数 t を厳密に比べる。分子は mm の差、分母はレイの向きの成分で、積は long に収まる。
    /// </summary>
    public readonly struct Fraction : IComparable<Fraction>, IEquatable<Fraction>
    {
        public Fraction(long num, long den)
        {
            if (den <= 0) { throw new ArgumentOutOfRangeException(nameof(den), "分母は正です。"); }
            Num = num;
            Den = den;
        }

        public long Num { get; }
        public long Den { get; }

        public static Fraction Zero => new(0, 1);

        public int CompareTo(Fraction other) => (Num * other.Den).CompareTo(other.Num * Den);

        public bool Equals(Fraction other) => CompareTo(other) == 0;

        public override bool Equals(object? obj) => obj is Fraction other && Equals(other);

        public override int GetHashCode() => (Num / Den).GetHashCode();

        public static bool operator <(Fraction a, Fraction b) => a.CompareTo(b) < 0;

        public static bool operator >(Fraction a, Fraction b) => a.CompareTo(b) > 0;

        public static bool operator <=(Fraction a, Fraction b) => a.CompareTo(b) <= 0;

        public static bool operator >=(Fraction a, Fraction b) => a.CompareTo(b) >= 0;

        public static bool operator ==(Fraction a, Fraction b) => a.Equals(b);

        public static bool operator !=(Fraction a, Fraction b) => !a.Equals(b);

        public double ToDouble() => (double)Num / Den;

        public override string ToString() => $"{Num}/{Den}";
    }

    /// <summary>レイ（原点と向き、どちらも整数）。点は原点 + 向き × t（t ≥ 0）。</summary>
    public readonly struct Ray3
    {
        public Ray3(Vec3i origin, Vec3i direction)
        {
            Origin = origin;
            Direction = direction;
        }

        public Vec3i Origin { get; }
        public Vec3i Direction { get; }
    }

    /// <summary>レイと箱の交差（スラブ法）。境界ちょうども当たりとし、比較は全て整数の有理数で厳密に行う。</summary>
    public static class Slab
    {
        /// <summary>レイが箱に触れるか。触れるなら入る点の t（原点が箱の中なら 0）を返す。</summary>
        public static bool Intersect(Ray3 ray, Box3 box, out Fraction enter)
        {
            enter = Fraction.Zero;
            var exit = Fraction.Zero;
            var hasExit = false;
            for (int axis = 0; axis < 3; axis++)
            {
                long o = ray.Origin[axis];
                long d = ray.Direction[axis];
                long lo = box.Min[axis];
                long hi = box.Max[axis];
                if (d == 0)
                {
                    if (o < lo || o > hi) { return false; }
                    continue;
                }

                Fraction near;
                Fraction far;
                if (d > 0)
                {
                    near = new Fraction(lo - o, d);
                    far = new Fraction(hi - o, d);
                }
                else
                {
                    near = new Fraction(o - hi, -d);
                    far = new Fraction(o - lo, -d);
                }

                if (near > enter) { enter = near; }
                if (!hasExit || far < exit)
                {
                    exit = far;
                    hasExit = true;
                }

                if (enter > exit) { return false; }
            }

            return true;
        }
    }

    /// <summary>
    /// 視点の宣言（壁の向き・目の位置・見下ろす角度・焦点距離）からカメラの基底を作り、画面の点からレイを作る。
    /// 画面の座標は縦の半分を <see cref="HalfHeight"/> とした整数（中央が 0、右と上が正）。横は画面の比率の分だけ広がる。
    /// Unity のカメラも同じ宣言から作る（位置・回転・縦の画角）ので、Core の当たりと描画が一致する。
    /// </summary>
    public readonly struct CameraPose
    {
        /// <summary>画面の縦の半分（画面の整数座標の単位）。</summary>
        public const int HalfHeight = 1000;

        /// <summary>受け付ける画面の横の半分の上限（21:9 の幅を含む）。</summary>
        public const int MaxHalfWidth = 2400;

        public const int MinFocal = 400;
        public const int MaxFocal = 6000;

        public CameraPose(Vec3i eye, int wall, int pitch, int focal)
        {
            if (wall < 0 || wall > 3) { throw new ArgumentOutOfRangeException(nameof(wall), "壁の向きは 0〜3 です。"); }
            if (pitch < 0 || pitch >= PitchTable.Count) { throw new ArgumentOutOfRangeException(nameof(pitch), $"見下ろす角度は 0〜{PitchTable.Count - 1} です。"); }
            if (focal < MinFocal || focal > MaxFocal) { throw new ArgumentOutOfRangeException(nameof(focal), $"焦点距離は {MinFocal}〜{MaxFocal} です。"); }
            Eye = eye;
            Wall = wall;
            Pitch = pitch;
            Focal = focal;
            var c = PitchTable.Cos(pitch);
            var s = PitchTable.Sin(pitch);
            var h = PitchTable.Hyp(pitch);

            // 北向き（壁 0）の基底を斜辺倍した整数：前 (0, -s, c)・上 (0, c, s)・右 (h, 0, 0)。
            Forward = Yaw(new Vec3i(0, -s, c), wall);
            Up = Yaw(new Vec3i(0, c, s), wall);
            Right = Yaw(new Vec3i(h, 0, 0), wall);
        }

        public Vec3i Eye { get; }

        /// <summary>向いている壁（0 北 +z・1 東 +x・2 南 -z・3 西 -x）。ヨーは 90° × 壁。</summary>
        public int Wall { get; }

        public int Pitch { get; }

        /// <summary>焦点距離（画面の整数座標の単位）。縦の画角は 2 atan(HalfHeight / Focal)。</summary>
        public int Focal { get; }

        /// <summary>基底（斜辺倍の整数、互いに直交し長さが等しい）。</summary>
        public Vec3i Forward { get; }

        public Vec3i Up { get; }
        public Vec3i Right { get; }

        /// <summary>ヨーの角度（度）。</summary>
        public int YawDegrees => Wall * 90;

        /// <summary>縦の画角（度）。Unity の Camera.fieldOfView に渡す。</summary>
        public double VerticalFovDegrees => 2.0 * Math.Atan2(HalfHeight, Focal) * 180.0 / Math.PI;

        /// <summary>画面の点（整数座標）を通るレイ。範囲外なら false。</summary>
        public bool TryRay(int sx, int sy, out Ray3 ray)
        {
            ray = default;
            if (sx < -MaxHalfWidth || sx > MaxHalfWidth || sy < -HalfHeight || sy > HalfHeight) { return false; }
            ray = new Ray3(Eye, (Right * sx) + (Up * sy) + (Forward * Focal));
            return true;
        }

        /// <summary>部屋の点を画面の整数座標へ写す（四捨五入）。目の後ろなら false。</summary>
        public bool TryProject(Vec3i point, out int sx, out int sy)
        {
            sx = 0;
            sy = 0;
            var v = point - Eye;
            var depth = Vec3i.Dot(Forward, v);
            if (depth <= 0) { return false; }

            // 基底はどれも斜辺倍なので、比をとると斜辺が消える。
            sx = IntGeometry.IntMath.RoundDiv(Focal * Vec3i.Dot(Right, v), depth);
            sy = IntGeometry.IntMath.RoundDiv(Focal * Vec3i.Dot(Up, v), depth);
            return true;
        }

        /// <summary>ベクトルを壁の向きへ回す（y 軸まわりに 90° × 壁、Unity の Quaternion.Euler(0, 90 × 壁, 0) と同じ向き）。</summary>
        public static Vec3i Yaw(Vec3i v, int wall) => (wall & 3) switch
        {
            0 => v,
            1 => new Vec3i(v.Z, v.Y, -v.X),
            2 => new Vec3i(-v.X, v.Y, -v.Z),
            _ => new Vec3i(-v.Z, v.Y, v.X),
        };
    }
}
