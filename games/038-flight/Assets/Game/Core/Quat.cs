using System;
using IntGeometry;

namespace Game.Core
{
    /// <summary>
    /// 整数のクォータニオン（成分は Q30、1.0 = 2^30）。姿勢を表す単位クォータニオンとして使う。
    /// ベクトルの回し方・軸の向きは Unity の <c>Quaternion</c> と同じ式なので、描画では成分をそのまま渡せる。
    /// 成分が 2^30 程度なら、積の 4 項の和（Q60）も 2 乗の和も long に収まる。
    /// </summary>
    public readonly struct Quat : IEquatable<Quat>
    {
        public Quat(long w, long x, long y, long z)
        {
            W = w;
            X = x;
            Y = y;
            Z = z;
        }

        public long W { get; }

        public long X { get; }

        public long Y { get; }

        public long Z { get; }

        public static Quat Identity => new(Fixed.One, 0, 0, 0);

        /// <summary>ノルムの 2 乗（Q60）。</summary>
        public long NormSquared => (W * W) + (X * X) + (Y * Y) + (Z * Z);

        public Quat Conjugate => new(W, -X, -Y, -Z);

        /// <summary>右（+x、機体の右翼）。</summary>
        public Vec3l Right => new(
            Fixed.One - (2 * Fixed.Round((Y * Y) + (Z * Z))),
            2 * Fixed.Round((X * Y) + (W * Z)),
            2 * Fixed.Round((X * Z) - (W * Y)));

        /// <summary>上（+y、機体の上面）。</summary>
        public Vec3l Up => new(
            2 * Fixed.Round((X * Y) - (W * Z)),
            Fixed.One - (2 * Fixed.Round((X * X) + (Z * Z))),
            2 * Fixed.Round((Y * Z) + (W * X)));

        /// <summary>前（+z、機首）。</summary>
        public Vec3l Forward => new(
            2 * Fixed.Round((X * Z) + (W * Y)),
            2 * Fixed.Round((Y * Z) - (W * X)),
            Fixed.One - (2 * Fixed.Round((X * X) + (Y * Y))));

        /// <summary>ハミルトン積。<c>a * b</c> は b の回転の後に a の回転（Unity と同じ）。</summary>
        public static Quat operator *(Quat a, Quat b) => new(
            Fixed.Round((a.W * b.W) - (a.X * b.X) - (a.Y * b.Y) - (a.Z * b.Z)),
            Fixed.Round((a.W * b.X) + (a.X * b.W) + (a.Y * b.Z) - (a.Z * b.Y)),
            Fixed.Round((a.W * b.Y) - (a.X * b.Z) + (a.Y * b.W) + (a.Z * b.X)),
            Fixed.Round((a.W * b.Z) + (a.X * b.Y) - (a.Y * b.X) + (a.Z * b.W)));

        public static bool operator ==(Quat left, Quat right) => left.Equals(right);

        public static bool operator !=(Quat left, Quat right) => !left.Equals(right);

        /// <summary>軸 (x, y, z)（Q30 の単位ベクトル）まわりに <paramref name="angle"/>（1 周 = 65536）回す。</summary>
        public static Quat AxisAngle(Vec3l axis, int angle)
        {
            // 半角のラジアンは ±π/2 に収まる。
            var half = Fixed.Radians(Fixed.Wrap(angle)) / 2;
            var sin = Fixed.SinRadians(half);
            var cos = Fixed.SinRadians((Fixed.PiQ30 / 2) - Math.Abs(half));
            return new Quat(cos, Fixed.Round(axis.X * sin), Fixed.Round(axis.Y * sin), Fixed.Round(axis.Z * sin));
        }

        /// <summary>機首の方位（北 = 0、右回りが正）と機首上げの角度から姿勢を作る（ロールなし）。</summary>
        public static Quat FromHeadingPitch(int heading, int pitch) =>
            AxisAngle(new Vec3l(0, Fixed.One, 0), heading) * AxisAngle(new Vec3l(Fixed.One, 0, 0), -pitch);

        /// <summary>
        /// 機体の軸まわりに小さく回す（1 Tick の角速度、単位は角度）。ピッチ（x）・ヨー（y）・ロール（z）の順に依らない 1 次の近似
        /// <c>(1, ω/2)</c> を右から掛けて正規化する。1 Tick の回転は数度なので、近似の角度の誤差は 10^-4 程度。
        /// </summary>
        public Quat RotateBody(int pitchX, int yawY, int rollZ)
        {
            if (pitchX == 0 && yawY == 0 && rollZ == 0) { return this; }
            var delta = new Quat(Fixed.One, Fixed.Radians(pitchX) / 2, Fixed.Radians(yawY) / 2, Fixed.Radians(rollZ) / 2);
            return (this * delta).Normalized();
        }

        /// <summary>長さ 1（2^30）へ直す。</summary>
        public Quat Normalized()
        {
            var norm = IntMath.Sqrt(NormSquared);
            if (norm == 0) { throw new InvalidOperationException("長さ 0 のクォータニオンは正規化できません。"); }
            return new Quat(
                Fixed.RoundDiv(W << Fixed.Shift, norm),
                Fixed.RoundDiv(X << Fixed.Shift, norm),
                Fixed.RoundDiv(Y << Fixed.Shift, norm),
                Fixed.RoundDiv(Z << Fixed.Shift, norm));
        }

        /// <summary>ベクトルを回す。成分が 2^31 程度まで（速度・相対位置の差など）。</summary>
        public Vec3l Rotate(Vec3l v)
        {
            var r = Right;
            var u = Up;
            var f = Forward;
            return new Vec3l(
                Fixed.Round((r.X * v.X) + (u.X * v.Y) + (f.X * v.Z)),
                Fixed.Round((r.Y * v.X) + (u.Y * v.Y) + (f.Y * v.Z)),
                Fixed.Round((r.Z * v.X) + (u.Z * v.Y) + (f.Z * v.Z)));
        }

        public bool Equals(Quat other) => W == other.W && X == other.X && Y == other.Y && Z == other.Z;

        public override bool Equals(object? obj) => obj is Quat other && Equals(other);

        public override int GetHashCode() => unchecked((W.GetHashCode() * 397) ^ (X.GetHashCode() * 131) ^ (Y.GetHashCode() * 31) ^ Z.GetHashCode());

        public override string ToString() => $"({W}, {X}, {Y}, {Z})";
    }
}
