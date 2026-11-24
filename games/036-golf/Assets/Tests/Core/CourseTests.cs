using System;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    public sealed class CourseTests
    {
        [Test]
        public void ProductionCourseHasThreeValidHoles()
        {
            var course = Production();
            Assert.That(course.Holes.Count, Is.EqualTo(3));
            for (var i = 0; i < 3; i++) { Assert.That(course.Holes[i].Par, Is.EqualTo(3 + i)); }
            foreach (var hole in course.Holes)
            {
                Assert.That(hole.SurfaceAt(hole.Tee), Is.EqualTo(Surface.Tee));
                Assert.That(hole.SurfaceAt(hole.Cup), Is.EqualTo(Surface.Green));
                Assert.That(hole.SurfaceOfCell(-1, 0), Is.EqualTo(Surface.OutOfBounds));
                Assert.That(hole.SurfaceOfCell(hole.Width, 0), Is.EqualTo(Surface.OutOfBounds));
                Assert.That(hole.Trees.Count, Is.InRange(1, BallSim.MaxTrees));
                Assert.That(hole.GradeAt(hole.Cup).IsZero, Is.False, "グリーンに傾きがある");
            }
        }

        [Test]
        public void PaintingFollowsTheShapesAndOrder()
        {
            var hole = Lab("paint bunker ellipse 50 30 4 2\npaint water capsule 80 10 90 10 1.5\n");
            Assert.That(hole.SurfaceAt(At(50, 30)), Is.EqualTo(Surface.Bunker));
            Assert.That(hole.SurfaceAt(At(53.5, 30.5)), Is.EqualTo(Surface.Bunker));
            Assert.That(hole.SurfaceAt(At(55.5, 30.5)), Is.EqualTo(Surface.Fairway));
            Assert.That(hole.SurfaceAt(At(85.5, 10.5)), Is.EqualTo(Surface.Water));
            Assert.That(hole.SurfaceAt(At(85.5, 12.5)), Is.EqualTo(Surface.Fairway));
            Assert.That(hole.SurfaceAt(At(-0.001, 30)), Is.EqualTo(Surface.OutOfBounds), "格子の外は OB");
            Assert.That(Hole.Cell(-1), Is.EqualTo(-1));
            Assert.That(Hole.Cell(M - 1), Is.EqualTo(0));
        }

        [Test]
        public void RepaintingOffTheGreenClearsTheSlope()
        {
            var config = new GolfConfig();
            config.RollDecel[(int)Surface.Fairway] = 1;
            var hole = Lab("paint green rect 20 20 30 40\nslope 20 20 30 40 0 80\npaint fairway rect 20 20 30 40\nslope 155 18 185 42 0 10\n", config);
            Assert.That(hole.SurfaceAt(At(25, 30)), Is.EqualTo(Surface.Fairway));
            Assert.That(hole.GradeAt(At(25, 30)).IsZero, Is.True);
            Assert.That(hole.GradeAt(hole.Cup), Is.EqualTo(new IntGeometry.Vec2i(0, 10)), "塗り直していないグリーンの傾きは残る");
            var stayed = Run(Rolling(At(25, 30), IntGeometry.Vec2i.Zero), hole, config: config);
            Assert.That(stayed.Position, Is.EqualTo(At(25, 30)));

            Assert.Throws<FormatException>(() => Course.RequireFlatOffTheGreen(new[] { Surface.Fairway }, new short[] { 0 }, new short[] { 80 }));
            Assert.DoesNotThrow(() => Course.RequireFlatOffTheGreen(new[] { Surface.Green }, new short[] { 10 }, new short[] { -20 }));
        }

        [Test]
        public void TooManyTreesAreRejected()
        {
            string Trees(int count)
            {
                var text = new System.Text.StringBuilder();
                for (var i = 0; i < count; i++) { text.Append("tree ").Append(25 + ((i % 8) * 5)).Append(' ').Append(8 + ((i / 8) * 5)).Append(" 1 5\n"); }
                return text.ToString();
            }

            Assert.That(Lab(Trees(BallSim.MaxTrees)).Trees.Count, Is.EqualTo(BallSim.MaxTrees));
            Assert.Throws<FormatException>(() => Lab(Trees(BallSim.MaxTrees + 1)));
        }

        [Test]
        public void SlopeAppliesOnlyToGreenCells()
        {
            var hole = Lab("slope 0 0 200 60 10 -20\n");
            Assert.That(hole.GradeAt(hole.Cup), Is.EqualTo(new IntGeometry.Vec2i(10, -20)));
            Assert.That(hole.GradeAt(At(50, 30)).IsZero, Is.True);
        }

        [Test]
        public void FingerprintChangesWithTheContents()
        {
            Assert.That(Lab().Fingerprint, Is.EqualTo(Lab().Fingerprint));
            Assert.That(Lab("tree 50 30 1 5\n").Fingerprint, Is.Not.EqualTo(Lab().Fingerprint));
            Assert.That(Lab("paint bunker ellipse 50 30 4 2\n").Fingerprint, Is.Not.EqualTo(Lab().Fingerprint));
            Assert.That(Lab("slope 155 18 185 42 0 1\n").Fingerprint, Is.Not.EqualTo(Lab().Fingerprint));
        }

        [TestCase("hole 1 par 4 size 200 60\npaint fairway rect 0 0 200 60\npaint green ellipse 170 30 15 12\ncup 170 30\n", TestName = "ティーがない")]
        [TestCase("hole 1 par 4 size 200 60\npaint tee rect 2 26 10 34\ntee 5 30\ncup 170 30\n", TestName = "カップがグリーンにない")]
        [TestCase("hole 1 par 6 size 200 60\n", TestName = "par が範囲外")]
        [TestCase("hole 1 par 4 size 10 60\n", TestName = "奥行きが小さすぎる")]
        [TestCase("hole 1 par 4 size 200 60\npaint fairway rect 0 0 200 60\nbanana 1 2\n", TestName = "未定義の行")]
        [TestCase("tee 5 30\n", TestName = "hole の前に中身")]
        [TestCase("hole 1 par 4 size 200 60\npaint grass rect 0 0 1 1\n", TestName = "未定義の地面")]
        [TestCase("hole 1 par 4 size 200 60\npaint fairway rect 0 0 x 60\n", TestName = "数でない")]
        [TestCase("hole 1 par 4 size 200 60\npaint fairway rect 10 0 5 60\n", TestName = "矩形の向きが逆")]
        [TestCase("hole 1 par 4 size 200 60\npaint tee rect 2 26 10 34\npaint green ellipse 170 30 15 12\ntee 5 30\ncup 170 30\ntree 170 40 1 5\n", TestName = "木がグリーンの上")]
        [TestCase("hole 1 par 4 size 200 60\npaint tee rect 2 26 10 34\npaint green ellipse 170 30 15 12\ntee 5 30\ncup 170 30\ntree 7 30 1 5\n", TestName = "木がティーに近い")]
        [TestCase("hole 1 par 4 size 200 60\npaint tee rect 2 26 10 34\npaint green ellipse 170 30 15 12\ntee 5 30\ncup 170 30\ntree 60 30 5 5\n", TestName = "木の半径が範囲外")]
        [TestCase("hole 1 par 4 size 200 60\npaint tee rect 2 26 10 34\npaint green ellipse 170 30 15 12\ntee 5 30\ncup 170 30\nslope 0 0 50 50 10 0\n", TestName = "傾きの範囲にグリーンがない")]
        [TestCase("hole 1 par 4 size 200 60\npaint tee rect 2 26 10 34\npaint green ellipse 170 30 15 12\ntee 5 30\ncup 170 30\nslope 155 18 185 42 60 60\n", TestName = "傾きが大きすぎる")]
        [TestCase("hole 1 par 4 size 200 60\npaint tee rect 2 26 10 34\npaint green ellipse 170 30 15 12\ntee 5 30\ncup 184.98 30\n", TestName = "カップがグリーンの端")]
        [TestCase("hole 1 par 4 size 200 60\npaint tee rect 2 26 10 34\npaint green ellipse 30 30 10 10\ntee 5 30\ncup 30 30\n", TestName = "ティーとカップが近い")]
        [TestCase("hole 1 par 4 size 200 60\npaint tee rect 2 26 10 34\ntee 5 30\ntee 6 30\n", TestName = "ティーが 2 つ")]
        public void RejectsBrokenHoles(string text)
        {
            Assert.Throws<FormatException>(() => Course.ParseHoles(text, new GolfConfig()));
        }

        [Test]
        public void CourseNeedsExactlyThreeHolesInOrder()
        {
            Assert.Throws<FormatException>(() => Course.Parse(LabText(""), new GolfConfig()));
            var swapped = LabText("") + "\n" + LabText("").Replace("hole 1", "hole 3") + "\n" + LabText("").Replace("hole 1", "hole 2");
            Assert.Throws<FormatException>(() => Course.Parse(swapped, new GolfConfig()));
            Assert.That(LabCourse().Holes.Count, Is.EqualTo(3));
        }

        [Test]
        public void ErrorsNameTheLine()
        {
            var error = Assert.Throws<FormatException>(() => Course.ParseHoles("hole 1 par 4 size 200 60\n\nbanana\n", new GolfConfig()));
            Assert.That(error!.Message, Does.StartWith("3 "));
        }
    }
}
