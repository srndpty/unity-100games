using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class StrataConfigTests
    {
        [Test]
        public void DefaultConfigIsValid()
        {
            Assert.DoesNotThrow(() => new StrataConfig().Validate());
            Assert.That(new StrataConfig().MaxLevel, Is.EqualTo(15));
        }

        [Test]
        public void InvalidValuesAreRejected()
        {
            AssertInvalid(config => config.GravityTicks = null!);
            AssertInvalid(config => config.GravityTicks = Array.Empty<int>());
            AssertInvalid(config => config.GravityTicks = new int[31]);
            AssertInvalid(config => config.GravityTicks = Ints(0));
            AssertInvalid(config => config.GravityTicks = Ints(601));
            AssertInvalid(config => config.GravityTicks = Ints(10, 11));
            AssertInvalid(config => config.SoftDropTicks = 0);
            AssertInvalid(config => config.LockDelayTicks = 0);
            AssertInvalid(config => config.MaxLockResets = -1);
            AssertInvalid(config => config.LineClearTicks = -1);
            AssertInvalid(config => config.LinesPerLevel = 0);
            AssertInvalid(config => config.LineScores = null!);
            AssertInvalid(config => config.LineScores = Ints(100, 300, 500));
            AssertInvalid(config => config.LineScores = Ints(100, 300, 500, -1));
            AssertInvalid(config => config.SoftDropScore = -1);
            AssertInvalid(config => config.HardDropScore = 101);
        }

        [Test]
        public void BoundaryValuesAreAccepted()
        {
            var config = new StrataConfig
            {
                GravityTicks = Ints(600, 600, 1),
                SoftDropTicks = 1,
                LockDelayTicks = 1,
                MaxLockResets = 0,
                LineClearTicks = 0,
                LinesPerLevel = 1,
                SoftDropScore = 0,
                HardDropScore = 100,
            };

            Assert.DoesNotThrow(config.Validate);
        }

        [Test]
        public void GameIsNotAffectedByLaterChangesToTheConfig()
        {
            var config = StrataTestKit.Config();
            config.GravityTicks[0] = 2;
            var game = new StrataGame(config, new ScriptedPieces(PieceKind.T), Board.Empty);
            var startY = game.Active!.Value.Y;

            config.GravityTicks[0] = 600;
            StrataTestKit.Ticks(game, 2);

            Assert.That(game.Active!.Value.Y, Is.EqualTo(startY - 1));
        }

        private static int[] Ints(params int[] values) => values;

        private static void AssertInvalid(Action<StrataConfig> change)
        {
            var config = new StrataConfig();
            change(config);
            Assert.That(config.Validate, Throws.InstanceOf<ArgumentException>());
        }
    }
}
