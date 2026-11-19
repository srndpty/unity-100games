using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class OthelloConfigTests
    {
        [Test]
        public void DefaultsAreValid()
        {
            Assert.DoesNotThrow(() => new OthelloConfig().Validate());
        }

        [Test]
        public void DefaultDepthsAreOneThreeAndFive()
        {
            var config = new OthelloConfig();
            Assert.That(config.DepthOf(CpuLevel.Weak), Is.EqualTo(1));
            Assert.That(config.DepthOf(CpuLevel.Normal), Is.EqualTo(3));
            Assert.That(config.DepthOf(CpuLevel.Strong), Is.EqualTo(5));
            Assert.Throws<ArgumentOutOfRangeException>(() => config.DepthOf((CpuLevel)3));
        }

        [TestCase(nameof(OthelloConfig.WeakDepth), 0)]
        [TestCase(nameof(OthelloConfig.NormalDepth), OthelloConfig.MaxDepth + 1)]
        [TestCase(nameof(OthelloConfig.StrongDepth), -1)]
        [TestCase(nameof(OthelloConfig.FlipTicks), -1)]
        [TestCase(nameof(OthelloConfig.FlipTicks), 601)]
        [TestCase(nameof(OthelloConfig.PassTicks), -1)]
        [TestCase(nameof(OthelloConfig.CpuThinkTicks), 601)]
        public void OutOfRangeValuesAreRejected(string field, int value)
        {
            var config = new OthelloConfig();
            typeof(OthelloConfig).GetField(field)!.SetValue(config, value);
            var error = Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());
            Assert.That(error!.ParamName, Is.EqualTo(field));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new OthelloGame(config));
        }

        [TestCase(nameof(OthelloConfig.WeakDepth), 1)]
        [TestCase(nameof(OthelloConfig.StrongDepth), OthelloConfig.MaxDepth)]
        [TestCase(nameof(OthelloConfig.FlipTicks), 0)]
        [TestCase(nameof(OthelloConfig.PassTicks), 600)]
        public void BoundaryValuesAreAccepted(string field, int value)
        {
            var config = new OthelloConfig();
            typeof(OthelloConfig).GetField(field)!.SetValue(config, value);
            Assert.DoesNotThrow(() => config.Validate());
        }

        [Test]
        public void CopyIsIndependent()
        {
            var config = new OthelloConfig();
            var copy = config.Copy();
            config.StrongDepth = 2;
            Assert.That(copy.StrongDepth, Is.EqualTo(5));
        }
    }
}
