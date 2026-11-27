using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class FlowTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public void ProductionBattleReachesResult(bool defend)
        {
            var flow = new Flow(new Rules());
            Demonstration.Send(flow, Intent.Start);
            for (var wave = 0; wave < 10 && flow.Screen == Screen.Play; wave++)
            {
                if (defend) { Demonstration.Prepare(flow); }
                Demonstration.Send(flow, Intent.Wave);
                for (var tick = 0; tick < 3000 && flow.Defense.Phase == Phase.Running; tick++) { flow.Step(); }
                Assert.That(flow.Defense.Phase, Is.Not.EqualTo(Phase.Running));
            }

            Assert.That(flow.Defense.Phase, Is.EqualTo(defend ? Phase.Won : Phase.Lost),
                $"Wave {flow.Defense.Wave}, lives {flow.Defense.Lives}");
            Demonstration.Send(flow, Intent.Start);
            Assert.That(flow.Defense.Wave, Is.EqualTo(1));
            Assert.That(flow.Defense.Money, Is.EqualTo(160));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void FocusLossRejectsQueuedAndRestorationClicks(bool focusFirst)
        {
            var flow = new Flow(new Rules());
            Demonstration.Send(flow, Intent.Start);
            flow.Push(Intent.Wave);
            flow.Focus(false);
            if (focusFirst) { flow.Focus(true); }
            flow.Push(Intent.Resume);
            if (!focusFirst) { flow.Focus(true); }
            flow.Advance(1);
            Assert.That(flow.Paused, Is.True);
            Assert.That(flow.Defense.Phase, Is.EqualTo(Phase.Preparation));
            flow.PointerIdle();
            Demonstration.Send(flow, Intent.Resume);
            Demonstration.Send(flow, Intent.Wave);
            flow.Advance(1.0 / Defense.TickRate);
            Assert.That(flow.Defense.Tick, Is.EqualTo(1));
        }

        [Test]
        public void EconomyOrdersRejectSelectionChangesAndDoubleUpgrade()
        {
            var flow = new Flow(new Rules());
            Demonstration.Send(flow, Intent.Start);
            Demonstration.Send(flow, Intent.Slot, 0);
            Demonstration.Send(flow, Intent.Build);
            Demonstration.Send(flow, Intent.Slot, 1);
            Demonstration.Send(flow, Intent.Build);
            Demonstration.Send(flow, Intent.Slot, 0);
            flow.Push(Intent.Slot, 1);
            flow.Push(Intent.Sell);
            flow.Dispatch();
            Assert.That(flow.Defense.Towers[1], Is.Not.Null);
            Assert.That(flow.Defense.Towers[0], Is.Not.Null);
            flow.Push(Intent.Upgrade);
            flow.Push(Intent.Upgrade);
            flow.Dispatch();
            Assert.That(flow.Defense.Towers[1]!.Level, Is.EqualTo(2));
            Assert.That(flow.Defense.Money, Is.EqualTo(47));
        }

        [Test]
        public void PreparationDriverFailsFastWhilePaused()
        {
            var flow = new Flow(new Rules());
            Demonstration.Send(flow, Intent.Start);
            Demonstration.Send(flow, Intent.Slot, 2);
            Demonstration.Send(flow, Intent.Build);
            flow.Focus(false);
            Assert.That(() => Demonstration.Prepare(flow), Throws.InvalidOperationException);
            Assert.That(flow.Defense.Towers[2]!.Level, Is.EqualTo(1));
        }
    }
}
