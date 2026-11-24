using System;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    public sealed class StageBookTests
    {
        private static readonly SiegeConfig _config = new();

        [Test]
        public void ProductionHasFiveValidStagesWithTargets()
        {
            var book = Production();
            Assert.That(book.Stages.Count, Is.EqualTo(StageBook.ProductionCount));
            foreach (var stage in book.Stages)
            {
                Assert.That(stage.TargetCount, Is.GreaterThan(0), stage.Name);
                Assert.That(stage.Hint, Is.Not.Empty, stage.Name);
            }
        }

        [Test]
        public void ProductionUsesAllThreeShotKinds()
        {
            var used = new bool[3];
            foreach (var stage in Production().Stages)
            {
                foreach (var shot in stage.Shots) { used[(int)shot] = true; }
            }

            Assert.That(used, Is.All.True);
        }

        [Test]
        public void ParsesPiecesJointsAndShotsInOrder()
        {
            var stage = Small("heavy bomb");
            Assert.That(stage.Name, Is.EqualTo("テスト"));
            Assert.That(stage.Launcher, Is.EqualTo(new V2(4f, 2.5f)));
            Assert.That(stage.Shots, Is.EqualTo(new[] { ShotKind.Heavy, ShotKind.Bomb }));
            Assert.That(stage.Pieces[1], Is.EqualTo(new PieceDef(Material.Lantern, new V2(20f, 2.3f), new V2(0.6f, 0.6f))));
            Assert.That(stage.Joints[0], Is.EqualTo(new JointDef(0, 1, 500f)));
            Assert.That(stage.TargetCount, Is.EqualTo(2));
            Assert.DoesNotThrow(() => stage.Validate(_config));
        }

        [TestCase("launcher 4 2.5\nshots normal\nblock wood 20 1 0.4 2\n", TestName = "標的がない")]
        [TestCase("launcher 4 2.5\nshots\nblock lantern 20 0.3 0.6 0.6\n", TestName = "弾がない")]
        [TestCase("launcher 4 2.5\nshots normal normal normal normal normal normal\nblock lantern 20 0.3 0.6 0.6\n", TestName = "弾が多すぎる")]
        [TestCase("launcher 4 2.5\nshots normal\nblock lantern 20 0.2 0.6 0.6\n", TestName = "地面にめり込む")]
        [TestCase("launcher 4 2.5\nshots normal\nblock lantern 31.9 0.3 0.6 0.6\n", TestName = "盤の右にはみ出す")]
        [TestCase("launcher 4 2.5\nshots normal\nblock lantern 20 0.3 0.6 0.6\nblock wood 20.2 0.5 0.4 1\n", TestName = "部材が重なる")]
        [TestCase("launcher 4 2.5\nshots normal\nblock lantern 20 0.3 0.05 0.6\n", TestName = "小さすぎる")]
        [TestCase("launcher 4 2.5\nshots normal\nblock lantern 6 0.3 0.6 0.6\n", TestName = "発射台に近すぎる")]
        [TestCase("launcher 40 2.5\nshots normal\nblock lantern 20 0.3 0.6 0.6\n", TestName = "発射台が盤の外")]
        [TestCase("launcher 4 2.5\nshots normal\nblock lantern 20 0.3 0.6 0.6\njoint 0 1 100\n", TestName = "接合の番号が範囲外")]
        [TestCase("launcher 4 2.5\nshots normal\nblock lantern 20 0.3 0.6 0.6\nblock wood 21 0.5 1 1\njoint 0 0 100\n", TestName = "自分との接合")]
        [TestCase("launcher 4 2.5\nshots normal\nblock lantern 20 0.3 0.6 0.6\nblock wood 21 0.5 1 1\njoint 0 1 0\n", TestName = "外れる力が0")]
        [TestCase("launcher 4 2.5\nshots normal\nblock lantern 20 0.3 0.6 0.6\nblock wood 22 0.5 1 1\njoint 0 1 100\n", TestName = "接していない接合")]
        [TestCase("launcher 4 2.5\nshots normal\nblock lantern 20 0.3 0.6 0.6\nblock wood 20.8 0.5 1 1\njoint 0 1 100\njoint 1 0 100\n", TestName = "同じ組の接合")]
        public void InvalidStagesAreRejected(string body)
        {
            Assert.Throws<FormatException>(() => One(body).Validate(_config));
        }

        [TestCase("stage a\nlauncher 1 1\n", TestName = "endがない")]
        [TestCase("launcher 1 1\n", TestName = "stageの外")]
        [TestCase("stage a\nshots normal\nend\n", TestName = "launcherがない")]
        [TestCase("stage a\nlauncher 1 1\nlauncher 1 1\nend\n", TestName = "launcherが2つ")]
        [TestCase("stage a\nlauncher 1\nend\n", TestName = "数が足りない")]
        [TestCase("stage a\nlauncher 1 x\nend\n", TestName = "数でない")]
        [TestCase("stage a\nlauncher 1 NaN\nend\n", TestName = "NaN")]
        [TestCase("stage a\nlauncher 1 1e9\nend\n", TestName = "範囲外の数")]
        [TestCase("stage a\nlauncher 1 1\nshots rocket\nend\n", TestName = "未知の弾")]
        [TestCase("stage a\nlauncher 1 1\nblock gold 1 1 1 1\nend\n", TestName = "未知の素材")]
        [TestCase("stage a\nlauncher 1 1\njoint -1 0 5\nend\n", TestName = "負の番号")]
        [TestCase("stage a\nlauncher 1 1\nwall 1 1\nend\n", TestName = "未知の項目")]
        public void MalformedTextIsRejectedWithALineNumber(string text)
        {
            var error = Assert.Throws<FormatException>(() => StageBook.Parse(text));
            Assert.That(error!.Message, Does.Contain("行目").Or.Contain("end"));
        }

        [Test]
        public void ProductionRejectsAWrongStageCount()
        {
            var one = "stage a\nhint b\nlauncher 4 2.5\nshots normal\nblock lantern 20 0.3 0.6 0.6\nend\n";
            Assert.Throws<FormatException>(() => StageBook.Production(one, _config));
        }

        [Test]
        public void TouchingPiecesAreNotOverlapping()
        {
            var a = new PieceDef(Material.Wood, new V2(1f, 1f), new V2(2f, 2f));
            var b = new PieceDef(Material.Wood, new V2(3f, 1f), new V2(2f, 2f));
            Assert.That(Stage.Overlap(a, b), Is.False);
            Assert.That(Stage.Gap(a, b), Is.EqualTo(0f));
            var c = new PieceDef(Material.Wood, new V2(2.9f, 1f), new V2(2f, 2f));
            Assert.That(Stage.Overlap(a, c), Is.True);
        }

        [Test]
        public void ShotPlanParsesShotsAndOptionalDetonation()
        {
            var plans = ShotPlan.Parse("stage\nshot -1.5 -0.5\nshot -2 -1 40\nend\nstage\nend\n");
            Assert.That(plans.Count, Is.EqualTo(2));
            Assert.That(plans[0][0].Pull, Is.EqualTo(new V2(-1.5f, -0.5f)));
            Assert.That(plans[0][0].DetonateAfter, Is.EqualTo(-1));
            Assert.That(plans[0][1].DetonateAfter, Is.EqualTo(40));
            Assert.That(plans[1], Is.Empty);
            Assert.Throws<FormatException>(() => ShotPlan.Parse("shot 1 1\n"));
            Assert.Throws<FormatException>(() => ShotPlan.Parse("stage\nshot 1\nend\n"));
            Assert.Throws<FormatException>(() => ShotPlan.Parse("stage\nshot 1 1 -3\nend\n"));
            Assert.Throws<FormatException>(() => ShotPlan.Parse("stage\nshot 1 1\n"));
        }
    }
}
