using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class DemoMatchesTests
    {
        private const int MaxSteps = 2000;

        [Test]
        public void 用意した場面で2連鎖を組める()
        {
            var match = DemoMatches.TwoChain(new SporeConfig(), 1);
            match.Player.Apply(GameCommand.MoveLeft);
            match.Player.Apply(GameCommand.SoftDropOn);
            for (var i = 0; i < MaxSteps && match.Player.MaxChain < 2; i++) { match.Tick(); }
            Assert.That(match.Player.MaxChain, Is.EqualTo(2));
            Assert.That(match.Player.Score, Is.EqualTo(200));
        }

        [Test]
        public void プレイヤーが負ける場面()
        {
            var match = DemoMatches.PlayerAboutToLose(new SporeConfig(), 1);
            match.Player.Apply(GameCommand.SoftDropOn);
            for (var i = 0; i < MaxSteps && !match.IsOver; i++) { match.Tick(); }
            Assert.That(match.Outcome, Is.EqualTo(MatchOutcome.CpuWin));
        }

        [Test]
        public void CPUが負ける場面()
        {
            var match = DemoMatches.CpuAboutToLose(new SporeConfig(), 1);
            match.Cpu.Apply(GameCommand.SoftDropOn);
            for (var i = 0; i < MaxSteps && !match.IsOver; i++) { match.Tick(); }
            Assert.That(match.Outcome, Is.EqualTo(MatchOutcome.PlayerWin));
        }
    }
}
