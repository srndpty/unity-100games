using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class CaseFlowTests
    {
        [Test]
        public void FocusLossDropsQueuedAndLaterInputsUntilFocusReturns()
        {
            var flow = new CaseFlow(CaseFile.Museum, 3);
            flow.Enqueue(Operation.Start);
            flow.Focus(false);
            flow.Enqueue(Operation.Start);
            Assert.That(flow.Flush(), Is.False);
            flow.Focus(true);
            flow.PointerIdle();
            Assert.That(flow.Flush(), Is.False);
            flow.Enqueue(Operation.Start);
            flow.Flush();
            flow.Enqueue(Operation.Question);
            flow.Flush();
            flow.Enqueue(Operation.SelectEvidence, 2);
            flow.Flush();
            flow.Enqueue(Operation.Present);
            Assert.That(flow.Flush(), Is.True);
            Assert.That(flow.Case.Target, Is.EqualTo(2));
        }

        [Test]
        public void LeavingAnInvestigationAndRetryingAfterLossResetsTheCase()
        {
            var flow = new CaseFlow(CaseFile.Museum, 1);
            flow.Enqueue(Operation.Start);
            flow.Flush();
            flow.Enqueue(Operation.Question);
            flow.Flush();
            flow.Enqueue(Operation.Title);
            flow.Enqueue(Operation.Present);
            flow.Flush();
            Assert.That(flow.Screen, Is.EqualTo(Screen.Title));
            flow.Enqueue(Operation.Start);
            flow.Flush();
            Assert.That(flow.Case.Owns(2), Is.False);
            flow.Enqueue(Operation.Present);
            flow.Enqueue(Operation.Title);
            flow.Flush();
            Assert.That(flow.Screen, Is.EqualTo(Screen.Result));
            Assert.That(flow.Case.Attempts, Is.Zero);
            flow.Enqueue(Operation.Start);
            flow.Flush();
            Assert.That(flow.Case.Attempts, Is.EqualTo(1));
            Assert.That(flow.Case.Target, Is.Zero);
        }

        [TestCase(true, Operation.Present)]
        [TestCase(false, Operation.Present)]
        [TestCase(true, Operation.Title)]
        [TestCase(false, Operation.Title)]
        public void FocusRestorationClickIsDiscarded(bool focusFirst, Operation operation)
        {
            var flow = new CaseFlow(CaseFile.Museum, 3);
            flow.Enqueue(Operation.Start);
            flow.Flush();
            flow.Focus(false);
            if (focusFirst) { flow.Focus(true); }
            flow.Enqueue(operation);
            if (!focusFirst) { flow.Focus(true); }
            Assert.That(flow.Flush(), Is.False);
            Assert.That(flow.Screen, Is.EqualTo(Screen.Case));
            Assert.That(flow.Case.Attempts, Is.EqualTo(3));

            flow.PointerIdle();
            flow.Enqueue(operation);
            Assert.That(flow.Flush(), Is.True);
        }

        [Test]
        public void RejectsRepeatedAndRetargetedPresentations()
        {
            var flow = new CaseFlow(CaseFile.Museum, 3);
            flow.Enqueue(Operation.Start);
            flow.Flush();
            flow.Enqueue(Operation.Question);
            flow.Flush();
            var oldVersion = flow.ViewVersion;
            flow.Enqueue(Operation.SelectEvidence, 2);
            flow.Enqueue(Operation.Present);
            flow.Flush();
            Assert.That(flow.Case.Attempts, Is.EqualTo(3));
            Assert.That(flow.Case.Target, Is.Zero);
            flow.Enqueue(Operation.Present, viewVersion: oldVersion);
            Assert.That(flow.Flush(), Is.False);
            flow.Enqueue(Operation.SelectEvidence, 0);
            flow.Flush();
            flow.Enqueue(Operation.Present);
            flow.Enqueue(Operation.Present);
            flow.Flush();
            Assert.That(flow.Case.Attempts, Is.EqualTo(2));
        }
    }
}
