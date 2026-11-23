using System.Collections.Generic;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>1 Tick の箱の掃引（各軸の重なる時刻の共通部分）。</summary>
    public sealed class SweepTests
    {
        [Test]
        public void FastBox_ThroughThinObstacle_Hits()
        {
            // 始めも終わりも重ならないが、途中で薄い障害物を通り抜ける。
            var from = new Box3(0, 0, 0, 10, 10, 10);
            var to = new Box3(0, 0, 200, 10, 10, 10);
            var thin = new Box3(0, 0, 100, 10, 10, 2);
            Assert.That(Sweep3.Boxes(from, to, thin).Hits, Is.True);
            Assert.That(Sweep3.Boxes(from, to, thin).Contact, Is.EqualTo(new Moment(90, 200)));
        }

        [Test]
        public void EndingExactlyOnTheFace_DoesNotHit()
        {
            var from = new Box3(0, 0, 0, 10, 10, 10);
            var to = new Box3(0, 0, 90, 10, 10, 10);
            var wall = new Box3(0, 0, 100, 10, 10, 10);
            Assert.That(Sweep3.Boxes(from, to, wall).Hits, Is.False, "前の面にちょうど接するだけ");
            var further = new Box3(0, 0, 91, 10, 10, 10);
            Assert.That(Sweep3.Boxes(from, further, wall).Hits, Is.True, "1 単位めり込む");
        }

        [Test]
        public void DiagonalPastTheCorner_DoesNotHit_ButCuttingTheCornerDoes()
        {
            var wall = new Box3(0, 0, 100, 100, 100, 100);

            // 横は Tick の前半だけ、奥行きは後半だけ重なる位置関係：同時には重ならない。
            var from = new Box3(90, 0, 0, 10, 100, 10);
            var past = new Box3(190, 0, 180, 10, 100, 10);
            Assert.That(Sweep3.Boxes(from, past, wall).Hits, Is.False);

            // 横に動く量が小さいと、角を削る時刻がある。
            var cut = new Box3(100, 0, 180, 10, 100, 10);
            Assert.That(Sweep3.Boxes(from, cut, wall).Hits, Is.True);
        }

        [Test]
        public void StandingStill_InsideOrOutside()
        {
            var box = new Box3(0, 0, 0, 10, 10, 10);
            Assert.That(Sweep3.Boxes(box, box, new Box3(5, 5, 5, 10, 10, 10)).Hits, Is.True);
            Assert.That(Sweep3.Boxes(box, box, new Box3(10, 0, 0, 10, 10, 10)).Hits, Is.False, "面で接するだけ");
        }

        [Test]
        public void Hurdle_FromAbove_IsNotAHit_ButFromTheFrontOrACornerIs()
        {
            var hurdle = new Box3(0, 0, 0, 10, 10, 10);

            // 真上から降りてきて上の面に触れる：厳しい判定では当たるが、踏み越えられる障害物では当たりにしない。
            var above = new Box3(0, 20, 0, 10, 10, 10);
            var down = new Box3(0, 5, 3, 10, 10, 10);
            Assert.That(Sweep3.Boxes(above, down, hurdle).Hits, Is.True);
            Assert.That(Sweep3.HitsFromFrontOrSide(above, down, hurdle, out _), Is.False);

            // もう重なっている（前の Tick に上から入った）ときも当たりにしない。
            Assert.That(Sweep3.HitsFromFrontOrSide(down, new Box3(0, 0, 6, 10, 10, 10), hurdle, out _), Is.False);

            // 前から：当たり。触れた時刻も厳しい判定と同じ。
            var front = new Box3(0, 0, -20, 10, 10, 10);
            var into = new Box3(0, 0, 0, 10, 10, 10);
            Assert.That(Sweep3.HitsFromFrontOrSide(front, into, hurdle, out var contact), Is.True);
            Assert.That(contact, Is.EqualTo(Sweep3.Boxes(front, into, hurdle).Contact));

            // 横から（レーン移動）：当たり。
            Assert.That(Sweep3.HitsFromFrontOrSide(new Box3(-20, 0, 0, 10, 10, 10), new Box3(0, 0, 0, 10, 10, 10), hurdle, out _), Is.True);

            // 前の面と上の面に同時に触れる（角）：当たり。
            Assert.That(Sweep3.HitsFromFrontOrSide(new Box3(0, 20, -20, 10, 10, 10), new Box3(0, 0, 0, 10, 10, 10), hurdle, out _), Is.True);

        }

        [Test]
        public void RandomBoxes_MatchEventPointReference()
        {
            var random = new SeededRandom(29);
            var hits = 0;
            for (var i = 0; i < 20000; i++)
            {
                var from = RandomBox(random);
                var to = new Box3(from.X + random.Range(-12, 12), from.Y + random.Range(-12, 12), from.Z + random.Range(-12, 12), from.Width, from.Height, from.Depth);
                var obstacle = RandomBox(random);
                var expected = ReferenceHits(from, to, obstacle);
                Assert.That(Sweep3.Boxes(from, to, obstacle).Hits, Is.EqualTo(expected), $"{from} → {to} vs {obstacle}");
                if (expected) { hits++; }
            }

            Assert.That(hits, Is.InRange(2000, 18000), "当たる場合と当たらない場合の両方を試す");
        }

        private static Box3 RandomBox(SeededRandom random) =>
            new(random.Range(-10, 10), random.Range(-10, 10), random.Range(-10, 10), random.Range(1, 8), random.Range(1, 8), random.Range(1, 8));

        /// <summary>
        /// 参照実装：各軸で境界をまたぐ時刻（と 0・1）を全て挙げ、その時刻と隣どうしの中点で、3 軸とも厳密に重なるかを調べる。
        /// 重なる時刻の集合は開区間なので、空でなければ境界の時刻の間の中点のどれかを含む。
        /// </summary>
        private static bool ReferenceHits(Box3 from, Box3 to, Box3 obstacle)
        {
            var axes = new (long A, long D, long S, long Q, long R)[]
            {
                (from.X, to.X - from.X, from.Width, obstacle.X, obstacle.Width),
                (from.Y, to.Y - from.Y, from.Height, obstacle.Y, obstacle.Height),
                (from.Z, to.Z - from.Z, from.Depth, obstacle.Z, obstacle.Depth),
            };
            const long Den = 2 * 720720; // 1〜24 の分母（移動量）の公倍数の 2 倍：中点も整数で表せる。
            var times = new List<long> { 0, Den };
            foreach (var (a, d, s, q, r) in axes)
            {
                if (d == 0) { continue; }
                foreach (var edge in new[] { q - s - a, q + r - a })
                {
                    var t = edge * Den / d;
                    if (edge * Den % d == 0 && t > 0 && t < Den) { times.Add(t); }
                }
            }

            times.Sort();
            var candidates = new List<long>(times);
            for (var i = 0; i + 1 < times.Count; i++) { candidates.Add((times[i] + times[i + 1]) / 2); }
            foreach (var t in candidates)
            {
                var all = true;
                foreach (var (a, d, s, q, r) in axes)
                {
                    // 位置 a + d t（t = n / Den）の区間 [p, p + s] と [q, q + r] の厳密な重なり。
                    var p = (a * Den) + (d * t);
                    if (!(p < (q + r) * Den && p + (s * Den) > q * Den)) { all = false; break; }
                }

                if (all) { return true; }
            }

            return false;
        }
    }
}
