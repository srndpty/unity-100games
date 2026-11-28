using System;
using System.Linq;
using Game.App;
using Game.Core;
using NUnit.Framework;
using IntGeometry;

namespace Game.App.Tests
{
    public sealed class TableFlowTests
    {
        [Test]
        public void FixedSeedsFinishAndKeepAllTilesAndPoints()
        {
            for (var seed = 0; seed < 128; seed++)
            {
                var match = new Match(seed);
                var step = 0;
                while (match.Phase != Phase.MatchResult && step++ < 10000)
                {
                    Assert.That(TableFlow.PlayCpu(match, 0), Is.True, $"seed={seed}, step={step}");
                    var physical = match.PhysicalTiles();
                    Assert.That(physical.Count, Is.EqualTo(136));
                    Assert.That(physical.Distinct().Count(), Is.EqualTo(136));
                    Assert.That(match.Seats.Sum(s => s.Points) + 1000 * match.Sticks, Is.EqualTo(100000));
                }

                Assert.That(match.Phase, Is.EqualTo(Phase.MatchResult), $"seed={seed}");
            }
        }

        [Test]
        public void SameSeedAndPolicyReproduceTheDecisionsAndResult()
        {
            var first = new Match(54);
            var second = new Match(54);
            for (var step = 0; step < 10000 && first.Phase != Phase.MatchResult; step++)
            {
                Assert.That(first.Phase, Is.EqualTo(second.Phase));
                Assert.That(first.Current, Is.EqualTo(second.Current));
                Assert.That(first.PhysicalTiles(), Is.EqualTo(second.PhysicalTiles()));
                Assert.That(TableFlow.PlayCpu(first, 0) && TableFlow.PlayCpu(second, 0), Is.True);
            }

            Assert.That(first.Phase, Is.EqualTo(Phase.MatchResult));
            Assert.That(second.Phase, Is.EqualTo(Phase.MatchResult));
            Assert.That(first.Seats.Select(s => s.Points), Is.EqualTo(second.Seats.Select(s => s.Points)));
        }

        [Test]
        public void FocusLossDiscardsInputAndRequiresResume()
        {
            var flow = new TableFlow(54, 0.3f);
            flow.Push(Intent.Start);
            flow.Step(0f);
            var match = flow.View!;
            var discard = match.LegalActions.First(m => m.Kind == MoveKind.Discard);
            flow.Push(Intent.Move, match.Revision, discard, flow.Generation);
            flow.Focus(false);
            flow.Step(1f);
            flow.Focus(true);
            flow.Push(Intent.Resume);
            flow.Step(0f);
            Assert.That(flow.Paused, Is.True);
            Assert.That(match.Seats[0].Discards, Is.Empty);
            flow.Push(Intent.Resume);
            flow.Step(0f);
            Assert.That(flow.Paused, Is.False);
            flow.Push(Intent.Title);
            flow.Step(0f);
            Assert.That(flow.Screen, Is.EqualTo(TableScreen.Title));
            Assert.That(flow.View, Is.Null);
        }

        [TestCase("random")]
        [TestCase("calls")]
        [TestCase("kans")]
        [TestCase("pass-ron")]
        public void OtherPoliciesPreserveStateAndExerciseTransitions(string policy)
        {
            var phases = new int[(int)Phase.MatchResult + 1];
            var moves = new int[(int)MoveKind.Next + 1];
            var skippedRon = 0;
            for (var seed = 0; seed < 24; seed++)
            {
                var match = new Match(seed);
                var random = new SeededRandom(673 + seed);
                for (var step = 0; step < 10000 && match.Phase != Phase.MatchResult; step++)
                {
                    phases[(int)match.Phase]++;
                    var seat = Enumerable.Range(0, 4).First(s => match.LegalActions(s).Count > 0);
                    var legal = match.LegalActions(seat);
                    var candidates = legal.Where(m => policy switch
                    {
                        "calls" => m.Kind is MoveKind.Chi or MoveKind.Pon or MoveKind.OpenKan,
                        "kans" => m.Kind is MoveKind.ClosedKan or MoveKind.OpenKan or MoveKind.AddedKan,
                        "pass-ron" => m.Kind == MoveKind.Pass && legal.Any(candidate => candidate.Kind == MoveKind.Ron),
                        _ => false,
                    }).ToArray();
                    var move = candidates.Length > 0 ? candidates[random.Next(candidates.Length)] :
                        policy is "calls" or "kans" or "random" ? legal[random.Next(legal.Count)] : Cpu.Choose(match.ViewFor(seat));
                    if (move.Kind == MoveKind.Pass && legal.Any(candidate => candidate.Kind == MoveKind.Ron)) { skippedRon++; }

                    moves[(int)move.Kind]++;
                    Assert.That(match.Apply(seat, match.Revision, move), Is.True);
                    Assert.That(match.PhysicalTiles().Distinct().Count(), Is.EqualTo(Tiles.Total));
                    Assert.That(match.PhysicalTiles().Count, Is.EqualTo(Tiles.Total));
                    Assert.That(match.Seats.Sum(p => p.Points) + 1000 * match.Sticks, Is.EqualTo(100000));
                }

                Assert.That(match.Phase, Is.EqualTo(Phase.MatchResult));
                phases[(int)Phase.MatchResult]++;
            }

            if (policy == "calls") { Assert.That(moves[(int)MoveKind.Chi], Is.Positive); }
            if (policy == "kans") { Assert.That(moves[(int)MoveKind.AddedKan], Is.Positive); }
            if (policy == "pass-ron") { Assert.That(skippedRon, Is.Positive); }

            TestContext.WriteLine(policy + " phases=" + string.Join(",", phases) +
                " moves=" + string.Join(",", moves) + " skippedRon=" + skippedRon);
        }

        [Test]
        public void StartAndResumeDiscardOldInputEvenWithMatchingRevision()
        {
            var flow = new TableFlow(54, 100f);
            var initial = new Match(54);
            var move = new Move(MoveKind.Discard, initial.Drawn);
            flow.Push(Intent.Start);
            flow.Push(Intent.Move, 0, move, flow.Generation);
            flow.Step(0f);
            var match = flow.View!;
            Assert.That(match.Seats[0].Discards, Is.Empty);

            var old = flow.Generation;
            flow.Focus(false);
            flow.Focus(true);
            flow.Step(0f);
            flow.Push(Intent.Resume);
            flow.Push(Intent.Move, 0, move, old);
            flow.Step(0f);
            Assert.That(match.Seats[0].Discards, Is.Empty);
            flow.Push(Intent.Move, 0, move, old);
            flow.Step(0f);
            Assert.That(match.Seats[0].Discards, Is.Empty);
            flow.Push(Intent.Move, 0, move, flow.Generation);
            flow.Step(0f);
            Assert.That(match.Seats[0].Discards.Count, Is.EqualTo(1));
        }

        [Test]
        public void RiichiUsesLiveWaitsInsteadOfLegalActionOrder()
        {
            var types = new[] { 22, 0, 1, 2, 3, 4, 5, 9, 10, 11, 21, 15, 15, 21 };
            var ids = Enumerable.Range(0, Tiles.Total).ToList();
            var wall = Enumerable.Repeat(-1, Tiles.Total).ToArray();
            for (var i = 0; i < types.Length; i++)
            {
                var id = ids.First(tile => Tiles.Type(tile) == types[i]);
                wall[i * 4] = id;
                ids.Remove(id);
            }

            var remaining = 0;
            for (var i = 0; i < wall.Length; i++)
            {
                if (wall[i] < 0) { wall[i] = ids[remaining++]; }
            }

            var match = new Match(0, _ => wall);
            var discardFive = new Move(MoveKind.Riichi, wall[0]);
            var discardFour = new Move(MoveKind.Riichi, wall[52]);
            var choices = new[] { discardFive, discardFour };
            Assert.That(choices.All(match.LegalActions(0).Contains), Is.True);
            var view = match.ViewFor(0);
            var chosen = Cpu.Choose(view);
            Assert.That(chosen.Kind, Is.EqualTo(MoveKind.Riichi));
            Assert.That(Tiles.Type(chosen.Value), Is.EqualTo(Tiles.Type(discardFour.Value)));
            Assert.That(Cpu.RecommendDiscard(view, MoveKind.Riichi), Is.EqualTo(chosen.Value));
        }

    }
}
