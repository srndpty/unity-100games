using System.Linq;
using Game.Core.Data;
using Game.Core.Play;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>操縦役の 3 方針が、複数のシードで狙った ED に届く（本番の台本の到達の確かめ）。</summary>
    public sealed class PilotTests
    {
        private static SummerBook B => AppLab.Book;

        private static Summer Run(PilotPolicy policy, int seed)
        {
            var s = new Summer(B, seed);
            new Autopilot(B, policy, seed + 99).Play(s);
            Assert.That(s.Phase, Is.EqualTo(Phase.Ended));
            return s;
        }

        [TestCase(PilotPolicy.Sumi, "sumi")]
        [TestCase(PilotPolicy.Shiori, "shiori")]
        [TestCase(PilotPolicy.Drift, "summer")]
        public void Policy_ReachesItsEnding_OnEverySeed(PilotPolicy policy, string ending)
        {
            var target = B.EndingIndex(ending);
            var misses = Enumerable.Range(0, 40).Where(seed => Run(policy, seed).Ending != target).ToList();
            Assert.That(misses, Is.Empty, $"{policy} が {ending} に届かなかったシード：{string.Join(",", misses)}");
        }

        [Test]
        public void Endings_HoldTheirConditions_AndHigherOnesDoNot()
        {
            foreach (PilotPolicy policy in new[] { PilotPolicy.Sumi, PilotPolicy.Shiori, PilotPolicy.Drift })
            {
                for (int seed = 0; seed < 30; seed++)
                {
                    var s = Run(policy, seed);
                    var chosen = B.Endings[s.Ending];
                    Assert.That(chosen.When == null || chosen.When.Eval(s), Is.True);
                    foreach (var e in B.EndingsByPriority)
                    {
                        if (e.Index == s.Ending) { break; }
                        Assert.That(e.When!.Eval(s), Is.False, $"{policy} seed {seed}: 上の ED {e.Id}");
                    }
                }
            }
        }

        [Test]
        public void FestivalInvitations_AreExclusive_AndAtMostOnePerSlot()
        {
            var a = B.EventIndex("e_festival_sumi");
            var b = B.EventIndex("e_festival_shiori");
            foreach (PilotPolicy policy in new[] { PilotPolicy.Sumi, PilotPolicy.Shiori, PilotPolicy.Drift })
            {
                for (int seed = 0; seed < 30; seed++)
                {
                    var s = Run(policy, seed);
                    foreach (var day in s.Log.Where(e => e.Kind == LogKind.Event).GroupBy(e => e.Day))
                    {
                        var ids = day.Select(e => e.Index).ToList();
                        Assert.That(ids.Count(i => B.Events[i].Slot == Slot.After), Is.LessThanOrEqualTo(1));
                        Assert.That(ids.Count(i => B.Events[i].Slot == Slot.Night), Is.LessThanOrEqualTo(1));
                        Assert.That(ids.Contains(a) && ids.Contains(b), Is.False);
                        for (int i = 0; i < ids.Count; i++)
                        {
                            for (int j = i + 1; j < ids.Count; j++) { Assert.That(B.Exclusive(ids[i], ids[j]), Is.False); }
                        }
                    }
                }
            }
        }

        [Test]
        public void SameSeedAndPolicy_SameEventSequence()
        {
            foreach (PilotPolicy policy in new[] { PilotPolicy.Sumi, PilotPolicy.Shiori, PilotPolicy.Drift })
            {
                var a = Run(policy, 5);
                var b = Run(policy, 5);
                Assert.That(b.Log, Is.EqualTo(a.Log));
                Assert.That(b.Hash(), Is.EqualTo(a.Hash()));
            }
        }

        /// <summary>本番の台本・固定のシードの最終ハッシュ（.NET と Mono で同じ値）。</summary>
        [TestCase(PilotPolicy.Sumi, 0x1D23CE38u)]
        [TestCase(PilotPolicy.Shiori, 0x9DE01A7Eu)]
        [TestCase(PilotPolicy.Drift, 0xDC02ECF8u)]
        public void FinalHash_IsFixed(PilotPolicy policy, uint expected)
        {
            var s = Run(policy, 2026);
            TestContext.WriteLine($"{policy} hash 0x{s.Hash():X8} ending {B.Endings[s.Ending].Id}");
            Assert.That(s.Hash(), Is.EqualTo(expected));
        }
    }
}
