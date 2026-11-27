using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class InvestigationTests
    {
        [Test]
        public void AcquiresEvidenceAndProvesAllThreeContradictions()
        {
            var state = new Investigation(CaseFile.Museum, 3);
            Assert.That(state.Present(0, 2), Is.EqualTo(ActionResult.Rejected));
            Assert.That(state.Question(3), Is.EqualTo(ActionResult.Rejected));
            Assert.That(state.Question(0), Is.EqualTo(ActionResult.Acquired));
            Assert.That(state.Question(0), Is.EqualTo(ActionResult.Rejected));
            Assert.That(state.Present(0, 2), Is.EqualTo(ActionResult.Correct));
            Assert.That(state.Present(0, 2), Is.EqualTo(ActionResult.Rejected));
            Assert.That(state.IsUnlocked(2) && state.IsUnlocked(3), Is.True);
            Assert.That(state.Present(2, 6), Is.EqualTo(ActionResult.Rejected));
            Assert.That(state.Question(3), Is.EqualTo(ActionResult.Acquired));
            Assert.That(state.Present(2, 6), Is.EqualTo(ActionResult.Correct));
            Assert.That(state.ProofOf(0), Is.SameAs(state.File.Matches[0]));
            Assert.That(state.ProofOf(2), Is.SameAs(state.File.Matches[1]));
            Assert.That(state.ProofOf(4), Is.Null);
            Assert.That(state.Question(4), Is.EqualTo(ActionResult.Acquired));
            Assert.That(state.Present(4, 7), Is.EqualTo(ActionResult.Won));
            Assert.That(state.Outcome, Is.EqualTo(Outcome.Won));
            Assert.That(state.Attempts, Is.EqualTo(3));
            Assert.That(state.Question(1), Is.EqualTo(ActionResult.Rejected));
        }

        [Test]
        public void WrongEvidenceCostsOneAttemptWithoutUnlockingTestimony()
        {
            var state = new Investigation(CaseFile.Museum, 3);
            Assert.That(state.Present(0, -1), Is.EqualTo(ActionResult.Rejected));
            Assert.That(state.Question(99), Is.EqualTo(ActionResult.Rejected));
            Assert.That(state.Present(1, 0), Is.EqualTo(ActionResult.Rejected));
            Assert.That(state.Present(0, 0), Is.EqualTo(ActionResult.Incorrect));
            Assert.That(state.Attempts, Is.EqualTo(2));
            Assert.That(state.Target, Is.Zero);
            Assert.That(state.IsUnlocked(2), Is.False);
            Assert.That(state.Present(0, 0), Is.EqualTo(ActionResult.Incorrect));
            Assert.That(state.Present(0, 0), Is.EqualTo(ActionResult.Lost));
            Assert.That(state.Present(-1, 0), Is.EqualTo(ActionResult.Rejected));
            Assert.That(state.Attempts, Is.Zero);
        }

        private static readonly int[] _firstUnlock = { 2, 3 };

        [TestCase(0, 2)]
        [TestCase(42, 2)]
        [TestCase(1, 6)]
        public void RejectsInvalidCaseRoutes(int next, int evidence)
        {
            var file = CaseFile.Museum;
            var matches = new[] { new Match(0, evidence, next, _firstUnlock, "記録"), file.Matches[1], file.Matches[2] };
            Assert.That(() => new CaseFile(file.Testimony, file.Evidence, matches), Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void RejectsEvidenceWithoutAnAcquisitionRouteAndEmptyInterviews()
        {
            var file = CaseFile.Museum;
            var statements = new List<Testimony>(file.Testimony);
            statements[0] = new Testimony("警備員", "証言", _onlyLog);
            Assert.That(() => new CaseFile(statements, file.Evidence, file.Matches),
                Throws.TypeOf<ArgumentException>().With.Message.EqualTo("取得できない証拠があります。"));
            statements[0] = new Testimony("警備員", "証言", Array.Empty<int>());
            Assert.That(() => new CaseFile(statements, file.Evidence, file.Matches),
                Throws.TypeOf<ArgumentException>().With.Message.EqualTo("尋問報酬がない証言があります。"));
        }

        private static readonly int[] _onlyLog = { 2 };

        [TestCase(0, 0, 2)]
        [TestCase(1, 2, 6)]
        [TestCase(2, 4, 7)]
        public void TestsAllTestimonyAndEvidencePairs(int stage, int target, int correctEvidence)
        {
            var file = CaseFile.Museum;
            for (var statement = 0; statement < file.Testimony.Count; statement++)
            {
                for (var evidence = 0; evidence < file.Evidence.Count; evidence++)
                {
                    var state = new Investigation(file, 3);
                    for (var step = 0; step <= stage; step++)
                    {
                        for (var id = 0; id < file.Testimony.Count; id++) { state.Question(id); }
                        if (step == stage) { break; }
                        var match = file.Matches[step];
                        Assert.That(state.Present(match.TestimonyId, match.EvidenceId), Is.EqualTo(ActionResult.Correct));
                    }

                    var expected = ActionResult.Rejected;
                    if (statement == target && state.Owns(evidence))
                    {
                        expected = evidence != correctEvidence ? ActionResult.Incorrect
                            : stage == 2 ? ActionResult.Won : ActionResult.Correct;
                    }

                    Assert.That(state.Present(statement, evidence), Is.EqualTo(expected), $"段階 {stage}、証言 {statement}、証拠 {evidence}");
                    Assert.That(state.Attempts, Is.EqualTo(expected == ActionResult.Incorrect ? 2 : 3));
                }
            }
        }
    }
}
