using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>難度曲線：単調に増え、上限で止まる。</summary>
    public sealed class DifficultyTests
    {
        private static readonly RunConfig _config = new();
        private const long M = TestKit.M;
        private static readonly int[] _allSpeeds = { 40, 48, 56, 64, 72, 80 };
        private static readonly int[] _tierThreeSpeeds = { 64, 72, 80 };

        [Test]
        public void SpeedAndTier_AreMonotonic_AndNeverExceedTheCap()
        {
            var speed = 0;
            var tier = 0;
            for (long meters = 0; meters <= 5000; meters += 7)
            {
                var s = Difficulty.Speed(_config, meters * M);
                var t = Difficulty.Tier(_config, meters * M);
                Assert.That(s, Is.GreaterThanOrEqualTo(speed).And.LessThanOrEqualTo(_config.TopSpeed));
                Assert.That(t, Is.GreaterThanOrEqualTo(tier).And.LessThanOrEqualTo(_config.MaxTier));
                speed = s;
                tier = t;
            }
        }

        [Test]
        public void SpeedSteps_AtExactlyTheLevelDistance()
        {
            var step = _config.SpeedLevelMeters * M;
            Assert.That(Difficulty.Speed(_config, step - 1), Is.EqualTo(_config.BaseSpeed));
            Assert.That(Difficulty.Speed(_config, step), Is.EqualTo(_config.BaseSpeed + _config.SpeedStep));
        }

        [Test]
        public void CapIsReachedAtCapMeters_AndStaysThere()
        {
            var cap = Difficulty.CapMeters(_config) * M;
            Assert.That(Difficulty.CapMeters(_config), Is.EqualTo(1000));
            Assert.That(Difficulty.Speed(_config, cap), Is.EqualTo(_config.TopSpeed));
            Assert.That(Difficulty.Tier(_config, cap), Is.EqualTo(_config.MaxTier));
            Assert.That(Difficulty.Speed(_config, cap - 1), Is.LessThan(_config.TopSpeed), "既定では速さが最後に上限に達する");
            foreach (var far in new[] { cap * 10, long.MaxValue / 2 })
            {
                Assert.That(Difficulty.Speed(_config, far), Is.EqualTo(_config.TopSpeed));
                Assert.That(Difficulty.Tier(_config, far), Is.EqualTo(_config.MaxTier));
            }
        }

        [Test]
        public void TierUnlocks_AtTierDistance_NotBeforeTheRunway()
        {
            Assert.That(Difficulty.Tier(_config, 0), Is.EqualTo(1));
            Assert.That(Difficulty.Tier(_config, (_config.TierMeters * M) - 1), Is.EqualTo(1));
            Assert.That(Difficulty.Tier(_config, _config.TierMeters * M), Is.EqualTo(2));
            Assert.That(Difficulty.Unlocks(_config, 1), Is.EqualTo(_config.RunwayMeters * M));
            Assert.That(Difficulty.Unlocks(_config, 3), Is.EqualTo(2 * _config.TierMeters * M));
        }

        [Test]
        public void SpeedsFor_CoverFromTheUnlockSpeedToTheTop()
        {
            Assert.That(RunRules.SpeedsFor(_config, 1), Is.EqualTo(_allSpeeds));
            Assert.That(RunRules.SpeedsFor(_config, 3), Is.EqualTo(_tierThreeSpeeds), "600 m で解禁：段階 3 から");
        }

        [Test]
        public void InvalidConfig_IsRejected()
        {
            Assert.That(() => new RunConfig { SpeedStep = 60 }.Validate(), Throws.ArgumentException.With.Message.Contains("最も速い"));
            Assert.That(() => new RunConfig { RowLength = 200 }.Validate(), Throws.ArgumentException);
            Assert.That(() => new RunConfig { MaxTier = 0 }.Validate(), Throws.TypeOf<System.ArgumentOutOfRangeException>());
            Assert.That(() => new RunConfig { RunnerWidth = 191 }.Validate(), Throws.ArgumentException.With.Message.Contains("偶数"));
            Assert.That(() => new RunConfig { RunnerWidth = 640 }.Validate(), Throws.TypeOf<System.ArgumentOutOfRangeException>(), "レーンより広い自機");
            Assert.That(() => new RunConfig { InputBufferTicks = 0 }.Validate(), Throws.TypeOf<System.ArgumentOutOfRangeException>());
        }
    }
}
