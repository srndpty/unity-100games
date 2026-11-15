using System;
using System.Reflection;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class FlappyTests
    {
        private static void Set(object target, string name, object value) => target.GetType().GetProperty(name)!.SetValue(target, value);
        private static PipeState[] Pipes(FlappyState state) => (PipeState[])typeof(FlappyState).GetField("_pipes", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(state)!;
        private static PipeState Pipe(float x, float center, float gap, bool scored = false)
        {
            object pipe = new PipeState(); Set(pipe, "X", x); Set(pipe, "Center", center); Set(pipe, "Gap", gap); Set(pipe, "Scored", scored); return (PipeState)pipe;
        }
        [Test]
        public void Flap_ReplacesVelocity_AndGravityLimitsFall()
        {
            var state = new FlappyState(new FlappyConfig(), 1);
            state.Step(new FlappyCommand(true)); Assert.That(state.Velocity, Is.EqualTo(5.7f).Within(.0001));
            state.Step(new FlappyCommand(true)); Assert.That(state.Velocity, Is.EqualTo(5.7f).Within(.0001));
            for (var i = 0; i < 120; i++) { state.Step(default); }
            Assert.That(state.Lost, Is.True); Assert.That(state.Velocity, Is.GreaterThanOrEqualTo(-10));
            var ticks = state.TickCount; state.Step(new FlappyCommand(true)); Assert.That(state.TickCount, Is.EqualTo(ticks));
        }
        [TestCase(-6.69f, -10f)]
        [TestCase(6.69f, 6f)]
        public void BothWalls_StopAtContact(float y, float velocity)
        {
            var state = new FlappyState(new FlappyConfig(), 2); Set(state, "BirdY", y); Set(state, "Velocity", velocity);
            state.Step(default); Assert.That(state.Lost, Is.True); Assert.That(Math.Abs(state.BirdY), Is.EqualTo(6.7f).Within(.0001));
        }
        [Test]
        public void Passing_ScoresOnlyOnce_AndRecycledPipeClearsFlag()
        {
            var state = new FlappyState(new FlappyConfig(), 1); var pipes = Pipes(state);
            pipes[0] = Pipe(-7.01f, 0, 4.5f); state.Step(default); Assert.That(state.Score, Is.EqualTo(1));
            state.Step(default); Assert.That(state.Score, Is.EqualTo(1));
            pipes[0] = Pipe(-12.71f, 0, 4.5f, true); state.Step(default);
            Assert.That(state.PipeAt(0).X, Is.GreaterThan(12)); Assert.That(state.PipeAt(0).Scored, Is.False);
        }
        [Test]
        public void Collision_PreventsLaterOrSimultaneousPoint_ButKeepsEarlierPoint()
        {
            foreach (var earlier in new[] { false, true })
            {
                var state = new FlappyState(new FlappyConfig(), 1); Set(state, "BirdY", -6.65f); Set(state, "Velocity", -10f);
                var pipes = Pipes(state); pipes[0] = Pipe(earlier ? -7.01f : -6.98f, 0, 4.5f);
                state.Step(default); Assert.That(state.Lost, Is.True); Assert.That(state.Score, Is.EqualTo(earlier ? 1 : 0));
            }
        }
        [Test]
        public void ExactSimultaneousContactAndPass_PrefersCollision()
        {
            var c = new FlappyConfig(); var state = new FlappyState(c, 1); Set(state, "BirdY", -6.7f);
            Pipes(state)[0] = Pipe(FlappyConfig.BirdX - c.BirdRadius - c.PipeWidth / 2, 0, 4.5f);
            state.Step(default); Assert.That(state.Score, Is.Zero); Assert.That(state.Lost, Is.True);
        }
        [Test]
        public void PipeContact_Loses()
        {
            var state = new FlappyState(new FlappyConfig(), 1); Pipes(state)[0] = Pipe(-6, 3, 3.5f);
            state.Step(default); Assert.That(state.Lost, Is.True);
        }
        [Test]
        public void Sweep_DetectsFastCrossing_AndKeepsRoundedCorners()
        {
            Assert.That(Sweep.CircleRectangle(-3, 0, 6, 0, .3f, -1, 1, -1, 1), Is.EqualTo(1.7f / 6).Within(.00001));
            Assert.That(float.IsPositiveInfinity(Sweep.CircleRectangle(-1.29f, 1.29f, 0, 0, .3f, -1, 1, -1, 1)), Is.True);
            Assert.That(Sweep.CircleRectangle(-1.2f, 1.2f, 0, 0, .3f, -1, 1, -1, 1), Is.Zero);
            Assert.That(Sweep.CircleRectangle(-3, 1.3f, 6, 0, .3f, -1, 1, -1, 1), Is.LessThan(1));
        }
        [TestCase(0)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(24)]
        [TestCase(25)]
        [TestCase(1000)]
        public void Difficulty_IsBounded(long score)
        {
            var state = new FlappyState(new FlappyConfig(), 1); Set(state, "Score", score);
            Assert.That(state.Level, Is.EqualTo(Math.Min(5, score / 5))); Assert.That(state.Speed, Is.LessThanOrEqualTo(5.5f));
        }
        [Test]
        public void Score_Saturates()
        {
            var state = new FlappyState(new FlappyConfig(), 1); Set(state, "Score", long.MaxValue);
            Pipes(state)[0] = Pipe(-7.01f, 0, 4.5f); state.Step(default); Assert.That(state.Score, Is.EqualTo(long.MaxValue));
        }
        [Test]
        public void SeedAndCommands_ReproduceEveryState()
        {
            var a = new FlappyState(new FlappyConfig(), 123); var b = new FlappyState(new FlappyConfig(), 123);
            for (var i = 0; i < 500; i++)
            {
                var command = new FlappyCommand(i % 36 == 0); a.Step(command); b.Step(command);
                Assert.That(b.BirdY, Is.EqualTo(a.BirdY)); Assert.That(b.Score, Is.EqualTo(a.Score));
                for (var j = 0; j < a.PipeCount; j++) { Assert.That(b.PipeAt(j), Is.EqualTo(a.PipeAt(j))); }
            }
            Assert.That(new FlappyState(new FlappyConfig(), 124).PipeAt(0).Center, Is.Not.EqualTo(a.PipeAt(0).Center));
        }
        [Test]
        public void Configuration_IsCopied()
        {
            var config = new FlappyConfig(); var state = new FlappyState(config, 1); config.Gravity = -30;
            state.Step(default); Assert.That(state.Velocity, Is.EqualTo(-.3f).Within(.00001));
        }
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        [TestCase(-1)]
        [TestCase(100)]
        public void InvalidFloatSettings_Reject(float value)
        {
            foreach (var field in typeof(FlappyConfig).GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (field.FieldType != typeof(float)) { continue; }
                var config = new FlappyConfig(); field.SetValue(config, value);
                Assert.Throws<ArgumentException>(() => config.Validate(), field.Name);
            }
        }
        [Test]
        public void DifficultyBoundary_UsesRepresentableFloatAndRejectsItsSuccessor()
        {
            new FlappyConfig().Validate();
            var adjacent = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(.2f) + 1);
            Assert.Throws<ArgumentException>(() => new FlappyConfig { GapDecrease = adjacent }.Validate());
            new FlappyConfig { Speed = 5, SpeedIncrease = .4f }.Validate();
            adjacent = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(.4f) + 1);
            Assert.Throws<ArgumentException>(() => new FlappyConfig { Speed = 5, SpeedIncrease = adjacent }.Validate());
        }
        [Test]
        public void Capacity_BoundsAllWidthAndSpacingCombinations()
        {
            for (var w = 0; w <= 30; w++)
            {
                for (var d = 0; d <= 40; d++)
                {
                    var c = new FlappyConfig { PipeWidth = .5f + w * .05f, Spacing = 5 + d * .1f };
                    c.Validate(); Assert.That(c.Capacity, Is.LessThanOrEqualTo(FlappyConfig.MaxCapacity));
                    Assert.That((c.Capacity - 1) * c.Spacing, Is.GreaterThan(FlappyConfig.Width + c.PipeWidth));
                }
            }
        }
        [Test]
        public void InvalidCombinations_Reject()
        {
            Assert.Throws<ArgumentException>(() => new FlappyConfig { Gap = 3.5f }.Validate());
            Assert.Throws<ArgumentException>(() => new FlappyConfig { PointsPerLevel = 0 }.Validate());
            Assert.Throws<ArgumentException>(() => new FlappyConfig { MaxLevel = 6 }.Validate());
            Assert.Throws<ArgumentNullException>(() => new FlappyState(null!, 1));
        }
    }
}
