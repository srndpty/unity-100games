using System.Numerics;

namespace Game.Core
{
    /// <summary>
    /// 1 Tick の間に始めから終わりへ直線で動く 2 点が、距離 <c>radius</c> より近づくか（ちょうど radius は近づかない）。
    /// 相対位置 d(t) = d0 + t·dv（0 ≤ t ≤ 1）の最小の長さを、BigInteger で厳密に比べる。
    /// </summary>
    public static class Proximity
    {
        public static bool Within(Vec3l aStart, Vec3l aEnd, Vec3l bStart, Vec3l bEnd, long radius)
        {
            var d0 = bStart - aStart;
            var dv = (bEnd - bStart) - (aEnd - aStart);
            var r2 = (BigInteger)radius * radius;
            if (Dot(d0, d0) < r2) { return true; }

            var dd = Dot(dv, dv);
            if (dd.IsZero) { return false; }

            // 最も近い時刻 t* = -(d0·dv) / |dv|²。0 以下なら始めが最も近い（もう比べた）。
            var s = -Dot(d0, dv);
            if (s.Sign <= 0) { return false; }
            if (s >= dd)
            {
                var end = d0 + dv;
                return Dot(end, end) < r2;
            }

            // |d0|² - s² / |dv|² < r²  ⇔  |d0|²·|dv|² - s² < r²·|dv|²
            return (Dot(d0, d0) * dd) - (s * s) < r2 * dd;
        }

        private static BigInteger Dot(Vec3l a, Vec3l b) =>
            ((BigInteger)a.X * b.X) + ((BigInteger)a.Y * b.Y) + ((BigInteger)a.Z * b.Z);
    }
}
