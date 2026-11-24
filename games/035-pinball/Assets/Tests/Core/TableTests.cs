using System;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class TableTests
    {
        [Test]
        public void ProductionTableIsValid()
        {
            var table = TestKit.Production();
            Assert.That(table.Colliders, Has.Count.GreaterThan(10));
            Assert.That(table.Left.Side, Is.EqualTo(FlipperSide.Left));
            Assert.That(table.Right.Side, Is.EqualTo(FlipperSide.Right));
            Assert.That(table.LaneRest, Is.EqualTo(new Vec2i(((21 * 1024) + 24448) / 2, 2560)));
            Assert.That(table.Left.RestAngle, Is.EqualTo(-5461), "-30 度");
            Assert.That(table.Left.UpAngle, Is.EqualTo(5461));
        }

        [Test]
        public void LabTableIsValid()
        {
            Assert.That(() => TestKit.Lab(), Throws.Nothing);
        }

        [TestCase("bumper 2 30 1\n", "バンパー")]
        [TestCase("target 2 30 3 30 0.2\n", "ターゲット")]
        [TestCase("wall 1 20 1 37 0.1\n", "16 マス")]
        [TestCase("wall 1 20 25 20 0.1\n", "台の外")]
        [TestCase("wall 4 2 4 3 0.1\n", "回る範囲")]
        [TestCase("wall 1 20 1 21 0\n", "半径")]
        [TestCase("gate 10 20 10 20 0.1\n", "線分")]
        [TestCase("wall 22 3 23 3 0.1\n", "置き場")]
        public void RejectsInvalidAdditions(string extra, string reason)
        {
            Assert.That(() => TestKit.Lab(extra), Throws.ArgumentException.With.Message.Contains(reason));
        }

        [TestCase("flipper L 3 3 3 0.3 -30 30", "flipper L 3 3 3 0.3 -30 70", "90 度")]
        [TestCase("flipper L 3 3 3 0.3 -30 30", "flipper L 3 3 3 0.3 30 -30", "内側")]
        [TestCase("flipper L 3 3 3 0.3 -30 30", "flipper L 3 3 9 0.3 -30 30", "8 マス")]
        [TestCase("lane 21 23.875 2.5", "lane 22 23 2.5", "レーンの幅")]
        [TestCase("drain 0.5", "drain 3", "drain")]
        public void RejectsInvalidParts(string from, string to, string reason)
        {
            var text = TestKit.LabText(string.Empty).Replace(from, to);
            Assert.That(text, Does.Not.Contain(from));
            Assert.That(() => Table.Parse(text, new PinballConfig()), Throws.ArgumentException.With.Message.Contains(reason));
        }

        /// <summary>レビュー P3-1：左右のフリッパーが回る範囲で重なる台は拒否する（フリッパーどうしは当たらない）。</summary>
        [TestCase("flipper R 8 3 3 0.3 210 150", true)]
        [TestCase("flipper R 9.4 3 3 0.3 210 150", true)]
        [TestCase("flipper R 12 3 3 0.3 210 150", false)]
        public void OverlappingFlippersAreRejected(string right, bool rejected)
        {
            var text = TestKit.LabText(string.Empty).Replace("flipper R 18 3 3 0.3 210 150", right);
            Assert.That(text, Does.Not.Contain("flipper R 18"));
            if (rejected)
            {
                Assert.That(() => Table.Parse(text, new PinballConfig()), Throws.ArgumentException.With.Message.Contains("フリッパーどうし"));
            }
            else
            {
                Assert.That(() => Table.Parse(text, new PinballConfig()), Throws.Nothing);
            }
        }

        [TestCase("flipper L 1 1 1 0.1 0 10\n", "2 つ")]
        [TestCase("lane 1 3 2\n", "lane")]
        [TestCase("post 1 2 3\n", "未知")]
        [TestCase("wall 1 2 x 4 0.1\n", "数ではありません")]
        [TestCase("wall 1 2 3 4\n", "5 個")]
        public void RejectsMalformedLines(string extra, string reason)
        {
            Assert.That(() => Table.ParseOnly(TestKit.LabText(extra)), Throws.InstanceOf<FormatException>().With.Message.Contains(reason));
        }

        [Test]
        public void ReportsTheLineNumber()
        {
            Assert.That(() => Table.ParseOnly("# comment\n\nwall 1 2 3\n"), Throws.InstanceOf<FormatException>().With.Message.StartsWith("3 行目"));
        }

        [Test]
        public void MissingPartsAreRejected()
        {
            Assert.That(() => Table.ParseOnly(TestKit.LabText(string.Empty).Replace("drain 0.5\n", string.Empty)), Throws.InstanceOf<FormatException>().With.Message.Contains("drain"));
            Assert.That(() => Table.ParseOnly(TestKit.LabText(string.Empty).Replace("flipper R 18 3 3 0.3 210 150\n", string.Empty)), Throws.InstanceOf<FormatException>().With.Message.Contains("フリッパー"));
        }

        [Test]
        public void FlipperTooFastForTheBallIsRejected()
        {
            // すり抜けない条件：先端の 1 区間の移動 < 球の半径 + フリッパーの半径。設定の値で破ると台の検証で拒否する。
            var config = new PinballConfig { FlipMaxSpeed = 4000 };
            Assert.That(() => Table.Parse(TestKit.DataText("Table.txt"), config), Throws.ArgumentException.With.Message.Contains("すり抜け"));
            config = new PinballConfig { ReturnMaxSpeed = 4000 };
            Assert.That(() => Table.Parse(TestKit.DataText("Table.txt"), config), Throws.ArgumentException.With.Message.Contains("すり抜け"));
        }

        [Test]
        public void ConfigRangesAreChecked()
        {
            Assert.That(() => new PinballConfig().Validate(), Throws.Nothing);
            Assert.That(() => new PinballConfig { MaxSpeed = 512 * 256 + 1 }.Validate(), Throws.InstanceOf<ArgumentOutOfRangeException>(), "1 区間の移動が球の半径を超える");
            Assert.That(() => new PinballConfig { PlungerMaxSpeed = 10000 }.Validate(), Throws.InstanceOf<ArgumentOutOfRangeException>(), "最小 > 最大");
            Assert.That(() => new PinballConfig { Balls = 0 }.Validate(), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => new PinballConfig { WallRestitution = 4097 }.Validate(), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => new PinballConfig { BumperKick = -1 }.Validate(), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => new PinballConfig { Gravity = 0 }.Validate(), Throws.InstanceOf<ArgumentOutOfRangeException>());
        }
    }
}
