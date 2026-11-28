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

        [TestCase(500, 2000, 8000, true)]
        [TestCase(500, 500, 500, true)]
        [TestCase(2001, 2000, 8000, false)]
        [TestCase(500, 8001, 8000, false)]
        public void Playouts_GrowWithTheLevel(int weak, int normal, int strong, bool valid)
        {
            var config = new MctsConfig { WeakPlayouts = weak, NormalPlayouts = normal, StrongPlayouts = strong };
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
