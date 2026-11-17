using System;
using System.Reflection;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class FishingTests
    {
        private static FishingState Fight(FishingConfig? config = null, FishKind kind = FishKind.Crucian)
        {
            var game = new FishingState(config ?? new FishingConfig(), kind, 1);
            while (game.Phase != FishingPhase.Biting) { game.Step(default); }
            game.Step(new FishingCommand(true, false)); return game;
        }
        private static void Set(FishingState game, string property, object value) => typeof(FishingState).GetProperty(property)!.SetValue(game, value);
        [TestCase(FishKind.Crucian)]
        [TestCase(FishKind.Bass)]
        [TestCase(FishKind.Carp)]
        public void Release_LowersTension_ForEveryAction(FishKind kind)
        {
            var game = Fight(kind: kind);
            foreach (var action in new[] { FishAction.Run, FishAction.Rest, FishAction.Turn })
            {
                Set(game, nameof(game.Action), action); Set(game, nameof(game.Tension), 50d);
                game.Step(default); Assert.That(game.Tension, Is.LessThan(50));
            }
        }
        [Test]
        public void HighTension_BreaksOnlyAfterContinuousThreshold()
        {
            var game = Fight(); Set(game, nameof(game.Tension), 100d);
            for (var i = 0; i < 44; i++) { game.Step(new FishingCommand(false, true)); }
            Assert.That(game.Finished, Is.False); game.Step(new FishingCommand(false, true));
            Assert.That(game.Reason, Is.EqualTo(EscapeReason.BrokenLine));
            var tick = game.TickCount; game.Step(default); Assert.That(game.TickCount, Is.EqualTo(tick));
        }
        [Test]
        public void LowTension_LosesHookAtNinetiethTick()
        {
            var game = Fight(); Set(game, nameof(game.Tension), 0d);
            for (var i = 0; i < 89; i++) { game.Step(default); }
            Assert.That(game.Finished, Is.False); game.Step(default); Assert.That(game.Reason, Is.EqualTo(EscapeReason.LooseHook));
        }
        [Test]
        public void DangerCounters_ResetOutsideBand()
        {
            var game = Fight(); Set(game, nameof(game.HighTicks), 20); Set(game, nameof(game.LowTicks), 10);
            Set(game, nameof(game.Tension), 40d); game.Step(default);
            Assert.That(game.HighTicks, Is.Zero); Assert.That(game.LowTicks, Is.Zero);
        }
        [TestCase(80d, 1, 0)]
        [TestCase(79.999999d, 0, 0)]
        [TestCase(10d, 0, 1)]
        [TestCase(10.000001d, 0, 0)]
        public void DangerThresholds_AreInclusive(double tension, int high, int low)
        {
            var game = Fight(); Set(game, nameof(game.Action), FishAction.Rest);
            Set(game, nameof(game.Tension), tension + 32d / 60); game.Step(default);
            Assert.That(game.HighTicks, Is.EqualTo(high)); Assert.That(game.LowTicks, Is.EqualTo(low));
        }
        [Test]
        public void FishAi_CyclesThroughAllThreeActions_WithinConfiguredDurations()
        {
            var game = Fight(); var seen = 0;
            for (var tick = 0; tick < 600 && game.Phase == FishingPhase.Fighting; tick++)
            {
                seen |= 1 << (int)game.Action;
                game.Step(new FishingCommand(false, game.Tension < 55));
            }
            Assert.That(seen, Is.EqualTo(7));
        }
        [Test]
        public void Escape_WinsOverSimultaneousLanding()
        {
            var game = Fight(); Set(game, nameof(game.HighTicks), 44); Set(game, nameof(game.Tension), 100d);
            Set(game, nameof(game.Stamina), 0d); Set(game, nameof(game.Distance), .5d);
            game.Step(new FishingCommand(false, true)); Assert.That(game.Phase, Is.EqualTo(FishingPhase.Escaped));
        }
        [Test]
        public void Landing_RequiresBothDistanceAndExhaustion()
        {
            var game = Fight(); Set(game, nameof(game.Distance), .5d); game.Step(default);
            Assert.That(game.Phase, Is.EqualTo(FishingPhase.Fighting)); Set(game, nameof(game.Stamina), 0d); game.Step(default);
            Assert.That(game.Phase, Is.EqualTo(FishingPhase.Landing));
            for (var i = 0; i < 47; i++) { game.Step(default); }
            Assert.That(game.Finished, Is.False); game.Step(default); Assert.That(game.Phase, Is.EqualTo(FishingPhase.Caught));
        }
        [Test]
        public void Bite_FirstAndLastValidTicksAcceptHook_ButDeadlineEscapes()
        {
            foreach (var delay in new[] { 0, 59, 60 })
            {
                var game = new FishingState(new FishingConfig(), FishKind.Crucian, 1);
                while (game.Phase != FishingPhase.Biting) { game.Step(default); }
                for (var i = 0; i < delay; i++) { game.Step(default); }
                game.Step(new FishingCommand(true, false));
                Assert.That(game.Phase, Is.EqualTo(delay < 60 ? FishingPhase.Fighting : FishingPhase.Escaped));
            }
        }
        [Test]
        public void EarlyHook_AndDistanceEscape()
        {
            var early = new FishingState(new FishingConfig(), FishKind.Crucian, 1); early.Step(new FishingCommand(true, false));
            Assert.That(early.Reason, Is.EqualTo(EscapeReason.EarlyHook));
            var game = Fight(); Set(game, nameof(game.Distance), 30d); game.Step(default);
            Assert.That(game.Reason, Is.EqualTo(EscapeReason.TooFar));
        }
        [Test]
        public void SeedAndCommands_ReproduceStates()
        {
            var a = Fight(); var b = Fight();
            for (var i = 0; i < 2000; i++)
            {
                var command = new FishingCommand(false, a.Tension < 55); a.Step(command); b.Step(command);
                Assert.That(b.Tension, Is.EqualTo(a.Tension)); Assert.That(b.Distance, Is.EqualTo(a.Distance));
                Assert.That(b.Phase, Is.EqualTo(a.Phase)); Assert.That(b.Action, Is.EqualTo(a.Action));
            }
        }
        [TestCase(true)]
        [TestCase(false)]
        public void ConstantInput_Fails(bool reel)
        {
            foreach (var kind in new[] { FishKind.Crucian, FishKind.Bass, FishKind.Carp })
            {
                var game = Fight(kind: kind);
                for (var i = 0; i < 3600 && !game.Finished; i++) { game.Step(new FishingCommand(false, reel)); }
                Assert.That(game.Phase, Is.EqualTo(FishingPhase.Escaped));
            }
        }
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(-1f)]
        public void InvalidSettings_AreRejected(float value)
        { var config = new FishingConfig { ReelSpeed = value }; Assert.Throws<ArgumentException>(config.Validate); }
        [Test]
        public void FishProfiles_AndDerivedReleaseAreValidated()
        {
            var config = new FishingConfig(); config.Fish[2].Kind = FishKind.Bass; Assert.Throws<ArgumentException>(config.Validate);
            config = new FishingConfig { ReleaseTension = 25 }; config.Fish[2].Pull = 1.6f; Assert.Throws<ArgumentException>(config.Validate);
            config = new FishingConfig { WaitMax = 1 }; Assert.Throws<ArgumentException>(config.Validate);
            config = new FishingConfig { Fish = Array.Empty<FishProfile>() }; Assert.Throws<ArgumentException>(config.Validate);
        }
        [TestCase(.1f, 6)]
        [TestCase(1f / 60f, 1)]
        [TestCase(.75f, 45)]
        public void TickConversion_RespectsExactFloatAndNeighbours(float seconds, int expected)
        {
            var bits = BitConverter.SingleToInt32Bits(seconds);
            Assert.That(FishingConfig.Ticks(seconds), Is.EqualTo(expected));
            Assert.That(FishingConfig.Ticks(BitConverter.Int32BitsToSingle(bits + 1)), Is.EqualTo(expected + 1));
            Assert.That(FishingConfig.Ticks(BitConverter.Int32BitsToSingle(bits - 1)), Is.EqualTo(expected));
        }
        [Test]
        public void Configuration_IsCopiedAndUnknownFishRejected()
        {
            var config = new FishingConfig(); var game = new FishingState(config, FishKind.Crucian, 1);
            config.CastSeconds = 10; game.Step(default); Assert.That(game.RemainingTicks, Is.EqualTo(29));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FishingState(new FishingConfig(), (FishKind)99, 1));
        }
    }
}
