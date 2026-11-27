using System;
using System.Linq;
using Game.Core.Data;
using Game.Core.Play;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>本番の台本の検証（数・名前・区間の検証を通ること）。</summary>
    public sealed class DataTests
    {
        private static readonly string[] _fixedDayEvents = { "e_open", "e_meteor" };

        private static SummerBook B => Lab.RealBook;

        [Test]
        public void RealBook_HasTheScope()
        {
            Assert.That(B.Days, Is.EqualTo(30));
            Assert.That(B.Actions.Count, Is.EqualTo(7));
            Assert.That(B.Characters.Count, Is.EqualTo(2), "攻略対象は 2 人");
            Assert.That(B.Characters.All(c => c.Heart >= 0), Is.True);
            Assert.That(B.Endings.Count, Is.EqualTo(3));
            Assert.That(B.Endings.Count(e => e.IsDefault), Is.EqualTo(1));
            Assert.That(B.Events.Count, Is.InRange(15, 30));
            Assert.That(B.Events.All(e => e.Lines.Count <= 5), Is.True, "会話は短く");
        }

        [Test]
        public void RealBook_FixedDayEvents_HaveTopPriority()
        {
            // 確率 100 の日付固定のイベント（始まり・流星群）は、その枠で最高の優先度。
            foreach (var id in _fixedDayEvents)
            {
                var e = B.Events[B.EventIndex(id)];
                Assert.That(e.Chance, Is.EqualTo(100));
                Assert.That(e.DayFrom, Is.EqualTo(e.DayTo));
                var rivals = B.Events.Where(o => o.Slot == e.Slot && o.Index != e.Index && o.OpenOn(e.DayFrom, B.WeekdayOf(e.DayFrom)));
                Assert.That(rivals.All(o => o.Priority < e.Priority), Is.True, id);
            }
        }

        [Test]
        public void RealBook_StatsStayInRange_OverRandomSummers()
        {
            var bounds = new StatBounds(B);
            for (int seed = 0; seed < 100; seed++)
            {
                var s = new Summer(B, seed);
                var rng = new SeededRandom(seed + 3);
                for (int i = 0; i < 1000 && s.Phase != Phase.Ended; i++)
                {
                    if (s.Phase == Phase.Action)
                    {
                        var options = Enumerable.Range(0, B.Actions.Count).Where(s.CanChoose).ToArray();
                        Lab.Do(s, s.ChooseAction(options[rng.Next(options.Length)]));
                    }
                    else
                    {
                        var n = B.Events[s.Scene].Choices.Count;
                        Lab.Do(s, n > 0 ? s.Choose(rng.Next(n)) : s.Continue());
                    }

                    foreach (var st in B.Stats)
                    {
                        Assert.That(s.Stat(st.Index), Is.InRange(st.Min, st.Max));
                        var (lo, hi) = bounds.StatOn(st.Index, s.Day);
                        Assert.That(s.Stat(st.Index), Is.InRange(lo, hi), $"seed {seed} day {s.Day} {st.Id}");
                    }
                }

                Assert.That(s.Phase, Is.EqualTo(Phase.Ended));
                Assert.That(s.Log.Count(e => e.Kind == LogKind.Event && e.Index == B.EventIndex("e_meteor")), Is.EqualTo(1), "最終日の夜は流星群");
            }
        }

        [Test]
        public void RealBook_BreakingANumber_IsRejectedWithItsLine()
        {
            // 3 日目までに学力 90 以上は届かない（区間の検証）。
            const string From = "@event e_reading slot after day 8..25 priority 70 chance 100 when action == library and shiori >= 20";
            const string To = "@event e_reading slot after day 2..3 priority 70 chance 100 when action == library and study >= 90";
            var text = Lab.DataText("Summer.txt");
            Assert.That(text.Contains(From, StringComparison.Ordinal), Is.True);
            var r = SummerCompiler.Compile(text.Replace(From, To, StringComparison.Ordinal));
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Errors.Any(e => e.Line > 0 && e.Message.Contains("e_reading", StringComparison.Ordinal)), Is.True, string.Join("\n", r.Errors));
        }
    }
}
