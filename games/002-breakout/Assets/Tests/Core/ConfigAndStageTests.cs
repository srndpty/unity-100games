using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class ConfigAndStageTests
    {
        [Test]
        public void DefaultConfig_IsValidAndHasThreeStages()
        {
            var config = new BreakoutConfig();

            Assert.DoesNotThrow(config.Validate);
            Assert.That(config.Stages, Has.Length.EqualTo(3));
        }

        [Test]
        public void Parse_ReadsKindsInRowMajorOrder()
        {
            var layout = StageParser.Parse(new StageDefinition("N.HP", "E..."), columns: 4, stageIndex: 0);

            Assert.That(layout.Rows, Is.EqualTo(2));
            Assert.That(layout.Bricks, Has.Count.EqualTo(4));
            Assert.That(layout.Bricks[0].Kind, Is.EqualTo(BrickKind.Normal));
            Assert.That(layout.Bricks[1].Kind, Is.EqualTo(BrickKind.Hard));
            Assert.That(layout.Bricks[1].Column, Is.EqualTo(2));
            Assert.That(layout.Bricks[2].Kind, Is.EqualTo(BrickKind.PierceItem));
            Assert.That(layout.Bricks[3].Kind, Is.EqualTo(BrickKind.ExpandItem));
            Assert.That(layout.Bricks[3].Row, Is.EqualTo(1));
        }

        [TestCase("N.X.")]
        [TestCase("N..")]
        [TestCase("N....")]
        [TestCase("....")]
        [TestCase("n...")]
        public void Parse_RejectsInvalidRows(string row)
        {
            Assert.Throws<ArgumentException>(() => StageParser.Parse(new StageDefinition(row), columns: 4, stageIndex: 0));
        }

        [Test]
        public void Parse_RejectsMissingRows()
        {
            Assert.Throws<ArgumentException>(() => StageParser.Parse(null, 4, 0));
            Assert.Throws<ArgumentException>(() => StageParser.Parse(new StageDefinition(), 4, 0));
            Assert.Throws<ArgumentException>(() => StageParser.Parse(new StageDefinition("NNNN", null!), 4, 0));
        }

        [Test]
        public void BrickField_PlacesBricksOnGrid()
        {
            var config = TestStages.Config(".....NN.....");
            var field = new BrickField(config, StageParser.Parse(config.Stages[0], config.Columns, 0));

            Assert.That(field.Bricks[0].MinX, Is.EqualTo(-1f));
            Assert.That(field.Bricks[0].MaxX, Is.EqualTo(0f));
            Assert.That(field.Bricks[1].MaxY, Is.EqualTo(6.5f));
            Assert.That(field.Bricks[1].MinY, Is.EqualTo(6f));
            Assert.That(field.HasAliveAt(5, 0), Is.True);
            Assert.That(field.HasAliveAt(4, 0), Is.False);
            Assert.That(field.HasAliveAt(-1, 0), Is.False);
            Assert.That(field.HasAliveAt(5, 1), Is.False, "範囲外の段");
        }

        [TestCase(nameof(BreakoutConfig.FieldWidth), float.NaN)]
        [TestCase(nameof(BreakoutConfig.BallSpeed), float.PositiveInfinity)]
        [TestCase(nameof(BreakoutConfig.PaddleSpeed), float.NegativeInfinity)]
        [TestCase(nameof(BreakoutConfig.FieldWidth), 12.5f)]
        [TestCase(nameof(BreakoutConfig.BallSize), 0.5f)]
        [TestCase(nameof(BreakoutConfig.GridTopMargin), 0.2f)]
        [TestCase(nameof(BreakoutConfig.ExpandScale), 0.9f)]
        [TestCase(nameof(BreakoutConfig.ExpandScale), 6f)]
        [TestCase(nameof(BreakoutConfig.CapsuleSpeed), 60f)]
        [TestCase(nameof(BreakoutConfig.MaxBounceAngleDegrees), 90f)]
        [TestCase(nameof(BreakoutConfig.PaddleInset), 0.2f)]
        public void Validate_RejectsOutOfRangeValues(string field, float value)
        {
            var config = new BreakoutConfig();
            typeof(BreakoutConfig).GetField(field)!.SetValue(config, value);

            Assert.Throws<ArgumentException>(config.Validate);
        }

        [Test]
        public void Validate_RejectsInvalidCounts()
        {
            Assert.Throws<ArgumentException>(new BreakoutConfig { Lives = 0 }.Validate);
            Assert.Throws<ArgumentException>(new BreakoutConfig { TicksPerSecond = 0 }.Validate);
            Assert.Throws<ArgumentException>(new BreakoutConfig { PierceDurationTicks = 0 }.Validate);
            Assert.Throws<ArgumentException>(new BreakoutConfig { StageClearDelayTicks = 0 }.Validate);
            Assert.Throws<ArgumentException>(new BreakoutConfig { Stages = Array.Empty<StageDefinition>() }.Validate);
            Assert.Throws<ArgumentException>(new BreakoutConfig { Stages = null! }.Validate);
            Assert.Throws<ArgumentException>(TestStages.Config("NNNNNNNNNNN").Validate, "列数が足りない面");
        }

        [Test]
        public void Validate_RejectsStagesReachingDownToPaddle()
        {
            var rows = new string[26];
            Array.Fill(rows, "NNNNNNNNNNNN");

            Assert.Throws<ArgumentException>(TestStages.Config(rows).Validate);
        }

        [Test]
        public void Validate_MaxSpeedBoundary()
        {
            var config = new BreakoutConfig();
            var limit = config.MaxTravelPerTick / config.TickDuration;

            config.MaxBallSpeed = limit * 0.999f;
            Assert.DoesNotThrow(config.Validate);

            config.MaxBallSpeed = limit * 1.001f;
            Assert.Throws<ArgumentException>(config.Validate);
        }

        [Test]
        public void Validate_MaxSpeedMustCoverEveryStageLaunchSpeed()
        {
            var config = new BreakoutConfig { MaxBallSpeed = 8f };

            Assert.Throws<ArgumentException>(config.Validate);
        }
    }
}
