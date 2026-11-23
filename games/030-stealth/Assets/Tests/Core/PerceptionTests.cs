using IntGeometry;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>センサ（視線・視覚・聴覚）。題材固有テスト「遮蔽物越しに視認しない」。</summary>
    public sealed class PerceptionTests
    {
        private static readonly string[] _corridor =
        {
            "##########",
            "#........#",
            "#...#....#",
            "#...X....#",
            "#........#",
            "##########",
        };

        private static FacilityMap Map => FacilityMap.Parse(_corridor);

        [Test]
        public void WallBlocksSight()
        {
            // 行 3 の列 4 が壁。左右から壁越しに見ない。
            Assert.That(LineOfSight.Clear(Map, C(2, 3), C(6, 3)), Is.False);
            Assert.That(Vision.Sees(Map, C(2, 3), Trig.Right, new VisionSpec(8 * T, 8192), C(6, 3)), Is.False);
        }

        [Test]
        public void CrateBlocksSight()
        {
            // 行 2 の列 4 が木箱。木箱も視線を遮る。
            Assert.That(LineOfSight.Clear(Map, C(2, 2), C(6, 2)), Is.False);
            Assert.That(Vision.Sees(Map, C(2, 2), Trig.Right, new VisionSpec(8 * T, 8192), C(6, 2)), Is.False);
        }

        [Test]
        public void OpenLineIsClear()
        {
            Assert.That(LineOfSight.Clear(Map, C(1, 1), C(8, 1)), Is.True);
            Assert.That(Vision.Sees(Map, C(1, 1), Trig.Right, new VisionSpec(8 * T, 8192), C(8, 1)), Is.True);
        }

        [Test]
        public void GrazingTheCornerOfAWallIsBlocked()
        {
            // 壁 (4,3) の右上の角 (5T, 4T) だけをちょうどかすめる線は見えない。角をわずかに外れれば見える。
            var map = Map;
            var from = new Vec2i(3 * T, (4 * T) + 128);
            var corner = new Vec2i(5 * T, 4 * T);
            var to = corner + (corner - from);
            Assert.That(LineOfSight.Touches(from, to, new Cell(4, 3)), Is.True);
            Assert.That(LineOfSight.Clear(map, from, to), Is.False);
            Assert.That(LineOfSight.Clear(map, from, to + new Vec2i(2, 0)), Is.True);
        }

        [Test]
        public void SightAlongAWallFaceIsBlocked()
        {
            // 壁の面の上をちょうど通る線（閉じた正方形に触れる）は遮られる。1 単位離れれば見える。
            var map = Map;
            Assert.That(LineOfSight.Clear(map, new Vec2i(2 * T, 4 * T), new Vec2i(7 * T, 4 * T)), Is.False);
            Assert.That(LineOfSight.Clear(map, new Vec2i(2 * T, (4 * T) + 1), new Vec2i(7 * T, (4 * T) + 1)), Is.True);
        }

        [Test]
        public void TouchesMatchesSeparatingAxisReference()
        {
            // 線分と正方形の交差を、外積で角が線の片側にそろうかを見る別の式と照合する。格子線・角に乗る座標を多めに出す。
            var random = new SeededRandom(30);
            for (var i = 0; i < 20000; i++)
            {
                var a = RandomPoint(random);
                var b = random.Next(10) == 0 ? a : RandomPoint(random);
                var cell = new Cell(random.Range(0, 5), random.Range(0, 5));
                Assert.That(LineOfSight.Touches(a, b, cell), Is.EqualTo(ReferenceTouches(a, b, cell)), $"{a}→{b} {cell}");
            }
        }

        [Test]
        public void ClearMatchesReferenceOnRandomMaps()
        {
            var random = new SeededRandom(31);
            for (var trial = 0; trial < 200; trial++)
            {
                var rows = new string[8];
                for (var r = 0; r < rows.Length; r++)
                {
                    var chars = new char[8];
                    for (var c = 0; c < chars.Length; c++) { chars[c] = random.Next(5) == 0 ? (random.Next(2) == 0 ? '#' : 'X') : '.'; }
                    rows[r] = new string(chars);
                }

                rows[0] = "E.....O.";
                var map = FacilityMap.Parse(rows);
                for (var k = 0; k < 50; k++)
                {
                    var a = RandomPoint(random);
                    var b = RandomPoint(random);
                    var expected = true;
                    for (var row = -1; row <= map.Height; row++)
                    {
                        for (var column = -1; column <= map.Width; column++)
                        {
                            if (map.BlocksSight(column, row) && ReferenceTouches(a, b, new Cell(column, row))) { expected = false; }
                        }
                    }

                    Assert.That(LineOfSight.Clear(map, a, b), Is.EqualTo(expected), $"{a}→{b}");
                }
            }
        }

        [Test]
        public void RangeBoundaryIsInclusive()
        {
            var map = FacilityMap.Parse(OpenRoom());
            var eye = C(2, 4);
            var spec = new VisionSpec(5 * T, 8192);
            Assert.That(Vision.Sees(map, eye, Trig.Right, spec, eye + new Vec2i(5 * T, 0)), Is.True);
            Assert.That(Vision.Sees(map, eye, Trig.Right, spec, eye + new Vec2i((5 * T) + 1, 0)), Is.False);
        }

        [Test]
        public void ConeEdge()
        {
            // 半視野角 40°：向きから 38° は見え、42° は見えない。真後ろ・真横も見えない。
            var map = FacilityMap.Parse(OpenRoom());
            var eye = C(3, 4);
            var spec = new VisionSpec(8 * T, 7282);
            Assert.That(Vision.Sees(map, eye, Trig.Right, spec, Vision.PointAt(eye, Degrees(38), 3 * T)), Is.True);
            Assert.That(Vision.Sees(map, eye, Trig.Right, spec, Vision.PointAt(eye, Degrees(-38), 3 * T)), Is.True);
            Assert.That(Vision.Sees(map, eye, Trig.Right, spec, Vision.PointAt(eye, Degrees(42), 3 * T)), Is.False);
            Assert.That(Vision.Sees(map, eye, Trig.Right, spec, Vision.PointAt(eye, Degrees(-42), 3 * T)), Is.False);
            Assert.That(Vision.Sees(map, eye, Trig.Right, spec, eye + new Vec2i(-T, 0)), Is.False);
            Assert.That(Vision.Sees(map, eye, Trig.Right, spec, eye + new Vec2i(0, T)), Is.False);
        }

        [Test]
        public void WideConeOverNinetyDegrees()
        {
            // 半視野角 120°：真横は見え、真後ろは見えない。
            var eye = new Vec2i(0, 0);
            Assert.That(Vision.InCone(Trig.Right, Degrees(120), new Vec2i(0, 100)), Is.True);
            Assert.That(Vision.InCone(Trig.Right, Degrees(120), new Vec2i(-100, 1)), Is.False);
            Assert.That(Vision.InCone(Trig.Right, Trig.FullTurn / 2, new Vec2i(-100, 0)), Is.True);
            Assert.That(Vision.InCone(Trig.Right, 0, eye), Is.True);
        }

        [Test]
        public void FanAgreesWithSees()
        {
            // 扇の各光線について、届く距離の点は見え、その 2 単位先は（視程の内側なら）見えない。
            var map = Map;
            var eye = C(1, 3);
            var spec = new VisionSpec(7 * T, 8192);
            var distances = new int[17];
            Vision.Fan(map, eye, Trig.Right, spec, distances);
            for (var i = 0; i < distances.Length; i++)
            {
                var angle = Vision.FanAngle(Trig.Right, spec.HalfAngle, i, distances.Length);
                Assert.That(LineOfSight.Clear(map, eye, Vision.PointAt(eye, angle, distances[i])), Is.True, $"光線 {i}");
                if (distances[i] < spec.Range) { Assert.That(LineOfSight.Clear(map, eye, Vision.PointAt(eye, angle, distances[i] + 2)), Is.False, $"光線 {i}"); }
            }

            // 真ん中の光線は (4,3) の壁の面の 1 単位手前で止まる（面に触れたら遮られる）。
            Assert.That(distances[8], Is.EqualTo((4 * T) - eye.X - 1));
        }

        [Test]
        public void HearingGoesAroundWalls()
        {
            // 壁の向こう（直線で 2 マス）でも、回り込む経路が遠ければ聞こえない。
            var map = FacilityMap.Parse(
                "#########",
                "#...#...#",
                "#...#...#",
                "#...#...#",
                "#E..#..O#",
                "#.......#",
                "#########");
            var hearing = new Hearing(map);
            hearing.Spread(new Cell(3, 4), 3);
            Assert.That(hearing.Hears(new Cell(5, 4)), Is.False);
            Assert.That(hearing.DistanceTo(new Cell(3, 1)), Is.EqualTo(3));
            hearing.Spread(new Cell(3, 4), 7);
            Assert.That(hearing.Hears(new Cell(5, 4)), Is.False);
            hearing.Spread(new Cell(3, 4), 8);
            Assert.That(hearing.DistanceTo(new Cell(5, 4)), Is.EqualTo(8));
        }

        [Test]
        public void HearingPassesThroughCrates()
        {
            var map = FacilityMap.Parse(
                "#######",
                "#E.X.O#",
                "#######");
            var hearing = new Hearing(map);
            hearing.Spread(new Cell(2, 1), 2);
            Assert.That(hearing.DistanceTo(new Cell(4, 1)), Is.EqualTo(2));
            hearing.Spread(new Cell(2, 1), 1);
            Assert.That(hearing.Hears(new Cell(4, 1)), Is.False);
        }

        [Test]
        public void SilentStepReachesOnlyItsOwnCell()
        {
            var map = FacilityMap.Parse(OpenRoom());
            var hearing = new Hearing(map);
            hearing.Spread(new Cell(5, 5), 0);
            Assert.That(hearing.Hears(new Cell(5, 5)), Is.True);
            Assert.That(hearing.Hears(new Cell(6, 5)), Is.False);
            Assert.That(() => hearing.Spread(new Cell(5, 5), -1), Throws.InstanceOf<System.ArgumentOutOfRangeException>());
        }

        private static int Degrees(int degrees) => (int)((long)degrees * Trig.FullTurn / 360);

        private static Vec2i RandomPoint(SeededRandom random)
        {
            // 半分は 64 単位の格子（格子線・角に乗る）、半分は任意の座標。
            return random.Next(2) == 0
                ? new Vec2i(random.Range(-4, 5 * 4 + 1) * 64, random.Range(-4, 5 * 4 + 1) * 64)
                : new Vec2i(random.Range(-256, (5 * T) + 256), random.Range(-256, (5 * T) + 256));
        }

        /// <summary>分離軸：外接箱が離れているか、正方形の 4 隅が線の同じ側に厳密にあれば交わらない。</summary>
        private static bool ReferenceTouches(Vec2i a, Vec2i b, Cell cell)
        {
            long x0 = cell.Column * T;
            long y0 = cell.Row * T;
            long x1 = x0 + T;
            long y1 = y0 + T;
            if (System.Math.Max(a.X, b.X) < x0 || System.Math.Min(a.X, b.X) > x1 || System.Math.Max(a.Y, b.Y) < y0 || System.Math.Min(a.Y, b.Y) > y1) { return false; }
            if (a == b) { return true; }
            long dx = b.X - a.X;
            long dy = b.Y - a.Y;
            var positive = 0;
            var negative = 0;
            foreach (var (x, y) in new[] { (x0, y0), (x1, y0), (x0, y1), (x1, y1) })
            {
                var cross = (dx * (y - a.Y)) - (dy * (x - a.X));
                if (cross > 0) { positive++; }
                else if (cross < 0) { negative++; }
            }

            return positive != 4 && negative != 4;
        }
    }
}
