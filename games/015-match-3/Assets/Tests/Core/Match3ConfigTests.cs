using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class Match3ConfigTests
    {
        [Test]
        public void DefaultsAreValid()
        {
            Assert.DoesNotThrow(() => new Match3Config().Validate());
        }

        [TestCase(nameof(Match3Config.Colors), 2)]
        [TestCase(nameof(Match3Config.Colors), 7)]
        [TestCase(nameof(Match3Config.Moves), 0)]
        [TestCase(nameof(Match3Config.Moves), 1000)]
        [TestCase(nameof(Match3Config.TargetScore), 0)]
        [TestCase(nameof(Match3Config.TargetScore), 10_000_001)]
        [TestCase(nameof(Match3Config.ScorePerPiece), 0)]
        [TestCase(nameof(Match3Config.ScorePerPiece), 10_001)]
        [TestCase(nameof(Match3Config.SwapTicks), -1)]
        [TestCase(nameof(Match3Config.ClearTicks), 601)]
        [TestCase(nameof(Match3Config.FallTicksPerRow), -1)]
        [TestCase(nameof(Match3Config.ReshuffleTicks), -1)]
        [TestCase(nameof(Match3Config.MaxGenerationAttempts), 0)]
        public void OutOfRangeValuesAreRejected(string field, int value)
        {
            var config = new Match3Config();
            typeof(Match3Config).GetField(field)!.SetValue(config, value);

            Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new Match3Game(config, 1));
        }

        [TestCase(nameof(Match3Config.Colors), 3)]
        [TestCase(nameof(Match3Config.Colors), 6)]
        [TestCase(nameof(Match3Config.Moves), 1)]
        [TestCase(nameof(Match3Config.Moves), 999)]
        [TestCase(nameof(Match3Config.SwapTicks), 0)]
        [TestCase(nameof(Match3Config.ClearTicks), 600)]
        public void BoundaryValuesAreAccepted(string field, int value)
        {
            var config = new Match3Config();
            typeof(Match3Config).GetField(field)!.SetValue(config, value);

            Assert.DoesNotThrow(() => _ = new Match3Game(config, 1));
        }
    }
}
