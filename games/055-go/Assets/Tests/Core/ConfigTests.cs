using System;
using Game.Core;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class ConfigTests
    {
        [Test]
        public void Defaults_AreValid()
        {
            Assert.That(new GoConfig().Validate(), Is.Empty);
        }

        [TestCase(-81, true)]
        [TestCase(-82, false)]
        [TestCase(81, true)]
        [TestCase(82, false)]
        public void Komi_Bounds(int komi, bool valid)
        {
            var config = new GoConfig { Komi = komi };
            Assert.That(config.Validate().Count == 0, Is.EqualTo(valid));
        }

        [TestCase(2, true)]
        [TestCase(1, false)]
        [TestCase(1000, true)]
        [TestCase(1001, false)]
        public void MoveLimit_Bounds(int limit, bool valid)
        {
            var config = new GoConfig { MoveLimit = limit };
            Assert.That(config.Validate().Count == 0, Is.EqualTo(valid));
        }

        [Test]
        public void InvalidConfig_IsRejectedByTheGame()
        {
            Assert.That(() => new GoGame(new GoConfig { MoveLimit = 0 }), Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void GameKeepsItsOwnCopy()
        {
            var config = new GoConfig();
            var game = new GoGame(config);
            config.Komi = 0;
            Assert.That(game.Komi, Is.EqualTo(7));
        }
    }
}
