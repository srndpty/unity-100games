using Game.Core.Geometry;
using Game.Core.Play;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class GeometryTests
    {
        private static Box3 B(int x0, int y0, int z0, int x1, int y1, int z1) => new(new Vec3i(x0, y0, z0), new Vec3i(x1, y1, z1));

        [Test]
        public void Slab_HitsBoxInFront_AndMissesBoxBehind()
        {
            var ray = new Ray3(Vec3i.Zero, new Vec3i(0, 0, 10));
            Assert.That(Slab.Intersect(ray, B(-5, -5, 100, 5, 5, 200), out var t), Is.True);
            Assert.That(t, Is.EqualTo(new Fraction(100, 10)));
            Assert.That(Slab.Intersect(ray, B(-5, -5, -200, 5, 5, -100), out _), Is.False, "レイの後ろの箱には当たらない");
        }

        [Test]
        public void Slab_EdgeAndCornerCount_AsHits()
        {
            var box = B(0, 0, 100, 50, 50, 200);
            Assert.That(Slab.Intersect(new Ray3(new Vec3i(50, 20, 0), new Vec3i(0, 0, 1)), box, out _), Is.True, "面の縁ちょうど");
            Assert.That(Slab.Intersect(new Ray3(new Vec3i(50, 50, 0), new Vec3i(0, 0, 1)), box, out _), Is.True, "角ちょうど");
            Assert.That(Slab.Intersect(new Ray3(new Vec3i(51, 50, 0), new Vec3i(0, 0, 1)), box, out _), Is.False, "1 mm 外");

            // 斜めのレイが角をかすめる：(0,0,0) から (50, 50, 100) の向きは角 (50, 50, 100) を通る。
            Assert.That(Slab.Intersect(new Ray3(Vec3i.Zero, new Vec3i(50, 50, 100)), box, out var t), Is.True);
            Assert.That(t, Is.EqualTo(new Fraction(1, 1)));
            Assert.That(Slab.Intersect(new Ray3(Vec3i.Zero, new Vec3i(51, 50, 100)), box, out _), Is.False);
        }

        [Test]
        public void Slab_OriginInside_EntersAtZero()
        {
            Assert.That(Slab.Intersect(new Ray3(new Vec3i(10, 10, 10), new Vec3i(1, -3, 2)), B(0, 0, 0, 20, 20, 20), out var t), Is.True);
            Assert.That(t, Is.EqualTo(Fraction.Zero));
        }

        /// <summary>スラブ法を、箱の 6 面ごとに交点を求めて面の中かを確かめる素朴な実装と、乱択のレイと箱で照合する。</summary>
        [Test]
        public void Slab_MatchesPerFaceReference_OnRandomRaysAndBoxes()
        {
            var r = new SeededRandom(48);
            int hits = 0;
            for (int n = 0; n < 20000; n++)
            {
                var o = new Vec3i(r.Range(-60, 60), r.Range(-60, 60), r.Range(-60, 60));
                var d = new Vec3i(r.Range(-9, 9), r.Range(-9, 9), r.Range(-9, 9));
                if (d == Vec3i.Zero) { continue; }
                var lo = new Vec3i(r.Range(-40, 30), r.Range(-40, 30), r.Range(-40, 30));
                var box = new Box3(lo, lo + new Vec3i(r.Range(1, 30), r.Range(1, 30), r.Range(1, 30)));
                var expected = Reference(new Ray3(o, d), box, out var refT);
                var actual = Slab.Intersect(new Ray3(o, d), box, out var t);
                Assert.That(actual, Is.EqualTo(expected), $"o={o} d={d} box={box}");
                if (actual)
                {
                    hits++;
                    Assert.That(t, Is.EqualTo(refT), $"o={o} d={d} box={box}");
                }
            }

            Assert.That(hits, Is.GreaterThan(200), "当たる組も含む");
        }

        [Test]
        public void CameraPose_BasisIsOrthogonalAndEqualLength()
        {
            for (int wall = 0; wall < 4; wall++)
            {
                for (int pitch = 0; pitch < PitchTable.Count; pitch++)
                {
                    var c = new CameraPose(Vec3i.Zero, wall, pitch, 1500);
                    Assert.That(Vec3i.Dot(c.Forward, c.Up), Is.Zero);
                    Assert.That(Vec3i.Dot(c.Forward, c.Right), Is.Zero);
                    Assert.That(Vec3i.Dot(c.Up, c.Right), Is.Zero);
                    var h2 = (long)PitchTable.Hyp(pitch) * PitchTable.Hyp(pitch);
                    Assert.That(Vec3i.Dot(c.Forward, c.Forward), Is.EqualTo(h2));
                    Assert.That(Vec3i.Dot(c.Up, c.Up), Is.EqualTo(h2));
                    Assert.That(Vec3i.Dot(c.Right, c.Right), Is.EqualTo(h2));
                    Assert.That(c.Up.Y, Is.GreaterThan(0), "上は上を向く");
                }
            }
        }

        [Test]
        public void CameraPose_WallDirections_MatchUnityYaw()
        {
            // Unity の Quaternion.Euler(0, 90 × 壁, 0) × (0, 0, 1) は 北 (0,0,1)・東 (1,0,0)・南 (0,0,-1)・西 (-1,0,0)。右はそれぞれ 東・南・西・北。
            var forward = new[] { new Vec3i(0, 0, 1), new Vec3i(1, 0, 0), new Vec3i(0, 0, -1), new Vec3i(-1, 0, 0) };
            var right = new[] { new Vec3i(1, 0, 0), new Vec3i(0, 0, -1), new Vec3i(-1, 0, 0), new Vec3i(0, 0, 1) };
            for (int wall = 0; wall < 4; wall++)
            {
                var c = new CameraPose(Vec3i.Zero, wall, 0, 1500);
                Assert.That(c.Forward, Is.EqualTo(forward[wall]));
                Assert.That(c.Right, Is.EqualTo(right[wall]));
            }
        }

        [Test]
        public void CameraPose_RejectsOutOfRangeAndScreenOutside()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => _ = new CameraPose(Vec3i.Zero, 4, 0, 1500));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => _ = new CameraPose(Vec3i.Zero, 0, PitchTable.Count, 1500));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => _ = new CameraPose(Vec3i.Zero, 0, 0, CameraPose.MaxFocal + 1));
            var pose = new CameraPose(Vec3i.Zero, 0, 0, 1500);
            Assert.That(pose.TryRay(0, CameraPose.HalfHeight + 1, out _), Is.False);
            Assert.That(pose.TryRay(CameraPose.MaxHalfWidth + 1, 0, out _), Is.False);
            Assert.That(pose.TryRay(CameraPose.MaxHalfWidth, -CameraPose.HalfHeight, out _), Is.True);
        }

        [Test]
        public void Orientation_ClueFace_FacesOnlyAtExactSteps()
        {
            var bottom = new Vec3i(0, -1, 0);
            var back = new Vec3i(0, 0, 1);
            Assert.That(default(Orientation).Faces(bottom), Is.False);
            Assert.That(new Orientation(0, 1).Faces(bottom), Is.True, "x 軸まわりに 90°：底 (0,-1,0) は手前 (0,0,-1) へ");
            Assert.That(new Orientation(3, 1).Faces(bottom), Is.True, "縦の法線はヨーに依らない");
            Assert.That(new Orientation(0, 3).Faces(bottom), Is.False);
            Assert.That(default(Orientation).Faces(back), Is.False);
            Assert.That(new Orientation(4, 0).Faces(back), Is.True, "y 軸まわりに 180°：裏 (0,0,1) は手前へ");
            Assert.That(new Orientation(3, 0).Faces(back), Is.False, "135° は斜め");
            Assert.That(new Orientation(5, 0).Faces(back), Is.False);
            Assert.That(new Orientation(0, 2).Faces(back), Is.True, "x 軸まわりに 180° でも裏が手前へ");
            int facing = 0;
            for (int y = 0; y < Orientation.YawSteps; y++)
            {
                for (int p = 0; p < Orientation.PitchSteps; p++)
                {
                    if (new Orientation(y, p).Faces(back)) { facing++; }
                }
            }

            Assert.That(facing, Is.EqualTo(2), "32 通りの向きのうち 2 通りだけ");
            Assert.That(new Orientation(-1, -1), Is.EqualTo(new Orientation(7, 3)), "負の段は循環する");
        }

        /// <summary>手がかりは正立しているときだけ読める（試遊：円盤を縦に 180° 裏返すと星座が逆さ・並びが逆に見えて読み違えた）。</summary>
        [Test]
        public void Orientation_ClueIsReadableOnlyUpright()
        {
            var back = new Vec3i(0, 0, 1);
            var up = new Vec3i(0, 1, 0);
            Assert.That(new Orientation(4, 0).ShowsUpright(back, up), Is.True, "横に 180°：正立");
            Assert.That(new Orientation(0, 2).Faces(back), Is.True);
            Assert.That(new Orientation(0, 2).ShowsUpright(back, up), Is.False, "縦に 180°：手前を向くが上下が逆さ");
            var bottom = new Vec3i(0, -1, 0);
            var front = new Vec3i(0, 0, -1);
            Assert.That(new Orientation(0, 1).ShowsUpright(bottom, front), Is.True, "筒を縦に 90°：底が正立");
            Assert.That(new Orientation(4, 1).ShowsUpright(bottom, front), Is.False, "底は手前を向くが 180° 回っている");
            Assert.That(new Orientation(2, 1).ShowsUpright(bottom, front), Is.False, "底は手前を向くが 90° 回っている");
            Assert.That(new Orientation(1, 1).ShowsUpright(bottom, front), Is.False, "45° 回っている");
            foreach (var (n, u) in new[] { (back, up), (bottom, front) })
            {
                int readable = 0;
                for (int y = 0; y < Orientation.YawSteps; y++)
                {
                    for (int p = 0; p < Orientation.PitchSteps; p++)
                    {
                        if (new Orientation(y, p).ShowsUpright(n, u)) { readable++; }
                    }
                }

                Assert.That(readable, Is.EqualTo(1), "32 通りの向きのうち正立は 1 通りだけ");
            }
        }

        /// <summary>面ごとの交差：各面の平面との交点を求め、その面の長方形の中（縁を含む）なら候補にする。原点が中なら 0。</summary>
        private static bool Reference(Ray3 ray, Box3 box, out Fraction t)
        {
            t = Fraction.Zero;
            if (box.Contains(ray.Origin)) { return true; }
            var found = false;
            for (int axis = 0; axis < 3; axis++)
            {
                var d = ray.Direction[axis];
                if (d == 0) { continue; }
                foreach (var plane in new[] { box.Min[axis], box.Max[axis] })
                {
                    long num = plane - ray.Origin[axis];
                    var f = d > 0 ? new Fraction(num, d) : new Fraction(-num, -d);
                    if (f.Num < 0) { continue; }
                    var inside = true;
                    for (int other = 0; other < 3 && inside; other++)
                    {
                        if (other == axis) { continue; }

                        // 点の座標 o + d × num / den を整数の比較にする：(o × den + d × num) と 境界 × den。
                        var coord = ((long)ray.Origin[other] * f.Den) + ((long)ray.Direction[other] * f.Num);
                        inside = coord >= (long)box.Min[other] * f.Den && coord <= (long)box.Max[other] * f.Den;
                    }

                    if (inside && (!found || f < t))
                    {
                        t = f;
                        found = true;
                    }
                }
            }

            return found;
        }
    }
}
