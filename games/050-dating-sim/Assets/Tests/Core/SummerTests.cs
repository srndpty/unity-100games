using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core.Data;
using Game.Core.Play;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class SummerTests
    {
        private static readonly int[] _overMax = { 10, 101, 0 };
        private static readonly int[] _tooFew = { 10, 0 };

        private static SummerBook B => Lab.SmallBook;

        private static int A(string id) => B.ActionIndex(id);

        private static int E(string id) => B.EventIndex(id);

        private static int S(string id) => B.StatIndex(id);

        /// <summary>シード付きの乱択で、選べる行動から 1 つ選ぶ。選択肢も乱択。</summary>
        private static void PlayRandom(Summer s, SeededRandom rng, Action<Summer>? after = null)
        {
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

                after?.Invoke(s);
            }

            Assert.That(s.Phase, Is.EqualTo(Phase.Ended));
        }

        private static IEnumerable<LogEntry> EventsOf(Summer s) => s.Log.Where(e => e.Kind == LogKind.Event);

        // ---- 1 日の流れ ----

        [Test]
        public void NewSummer_StartsOnDayOneMorning()
        {
            var s = new Summer(B, 1);
            Assert.That(s.Day, Is.EqualTo(1));
            Assert.That(s.Weekday, Is.EqualTo(Weekday.Mon));
            Assert.That(s.Phase, Is.EqualTo(Phase.Action));
            Assert.That(s.Action, Is.EqualTo(-1));
            Assert.That(s.Stat(S("study")), Is.EqualTo(10));
            Assert.That(s.Events.Single().Kind, Is.EqualTo(SummerEventKind.DayStarted));
        }

        [Test]
        public void Meet_OnDayOne_ShowsFirstEvent_ThenChoiceSetsFlag()
        {
            var s = new Summer(B, 1);
            Lab.Do(s, s.ChooseAction(A("meet")));
            Assert.That(s.Phase, Is.EqualTo(Phase.Scene));
            Assert.That(s.Scene, Is.EqualTo(E("e_first")), "優先度 50 は e_c（5）より先");
            Assert.That(s.Events.Any(e => e.Kind == SummerEventKind.StatChanged && e.Index == S("aoi")), Is.True);
            Lab.Do(s, s.Choose(0));
            Assert.That(s.Flag(B.FlagIndex("met")), Is.True);
            Assert.That(s.Scene, Is.EqualTo(E("e_a")), "夜は好感度があれば e_a（同じ優先度の e_b より定義が先）");
            Lab.Do(s, s.Continue());
            Assert.That(s.Day, Is.EqualTo(2));
            Assert.That(s.Done(E("e_first")), Is.True);
            Assert.That(s.FiredToday, Is.Empty);
        }

        [Test]
        public void OnceEvent_DoesNotRepeat()
        {
            var s = new Summer(B, 1);
            Lab.Play(s, Lab.Prefer("meet", "rest"));
            Assert.That(EventsOf(s).Count(e => e.Index == E("e_first")), Is.EqualTo(1));
        }

        [Test]
        public void SamePriority_DefinitionOrderWins()
        {
            for (int seed = 0; seed < 30; seed++)
            {
                var s = new Summer(B, seed);
                Lab.Play(s, Lab.Prefer("meet", "rest"));
                foreach (var day in EventsOf(s).GroupBy(e => e.Day))
                {
                    // e_c（e_a と排他）が出た日だけは e_a が候補から外れる（Exclusive_FirstChosenWins）。
                    if (day.Any(e => e.Index == E("e_c"))) { continue; }
                    Assert.That(day.Any(e => e.Index == E("e_b")), Is.False, $"seed {seed} day {day.Key}: e_b は e_a に負ける");
                }
            }
        }

        // ---- 拒否は状態を変えない ----

        private static void AssertRefused(Summer s, Func<Summer, Outcome> command, Refusal reason)
        {
            var before = s.Hash();
            var o = command(s);
            Assert.That(o.Reason, Is.EqualTo(reason));
            Assert.That(s.Hash(), Is.EqualTo(before));
            Assert.That(s.Events, Is.Empty, "拒否で出来事は残らない");
        }

        [Test]
        public void Refusals_LeaveStateUnchanged()
        {
            var s = new Summer(B, 3);
            AssertRefused(s, x => x.ChooseAction(-1), Refusal.OutOfRange);
            AssertRefused(s, x => x.ChooseAction(B.Actions.Count), Refusal.OutOfRange);
            AssertRefused(s, x => x.Continue(), Refusal.NotNow);
            AssertRefused(s, x => x.Choose(0), Refusal.NotNow);

            Lab.Do(s, s.ChooseAction(A("meet")));
            Assert.That(s.Scene, Is.EqualTo(E("e_first")));
            AssertRefused(s, x => x.ChooseAction(A("study")), Refusal.NotNow);
            AssertRefused(s, x => x.Continue(), Refusal.NotNow);
            AssertRefused(s, x => x.Choose(2), Refusal.OutOfRange);
            AssertRefused(s, x => x.Choose(-1), Refusal.OutOfRange);
            Lab.Do(s, s.Choose(1));
            Assert.That(s.Phase, Is.EqualTo(Phase.Scene));
            AssertRefused(s, x => x.Choose(0), Refusal.NotNow);

            Lab.Play(s, Lab.Prefer("rest"));
            AssertRefused(s, x => x.ChooseAction(0), Refusal.Ended);
            AssertRefused(s, x => x.Continue(), Refusal.Ended);
            AssertRefused(s, x => x.Choose(0), Refusal.Ended);
        }

        [Test]
        public void Sunday_RefusesMeet()
        {
            var s = new Summer(B, 5);
            while (s.Day < 7) { Lab.Step(s, Lab.Prefer("rest")); }
            Assert.That(s.Weekday, Is.EqualTo(Weekday.Sun));
            Assert.That(s.CanChoose(A("meet")), Is.False);
            AssertRefused(s, x => x.ChooseAction(A("meet")), Refusal.Unavailable);
            Assert.That(s.CanChoose(A("study")), Is.True);
        }

        [Test]
        public void FullFatigue_ForcesRestNextMorning()
        {
            var s = new Summer(B, 2);
            while (s.Day < 5) { Lab.Step(s, Lab.Prefer("study")); }
            Assert.That(s.Stat(S("fatigue")), Is.EqualTo(100), "30 × 4 = 120 を 100 で飽和");
            Assert.That(s.Forced, Is.True);
            Assert.That(s.CanChoose(A("study")), Is.False);
            AssertRefused(s, x => x.ChooseAction(A("study")), Refusal.Unavailable);
            Assert.That(s.CanChoose(A("rest")), Is.True);
            Lab.Do(s, s.ChooseAction(A("rest")));
            Assert.That(s.Stat(S("fatigue")), Is.EqualTo(50));
            while (s.Phase == Phase.Scene) { Lab.Step(s, Lab.Prefer("rest")); }
            Assert.That(s.Forced, Is.False);
        }

        // ---- 能力値の範囲外逸脱なし（題材固有） ----

        [Test]
        public void Stats_SaturateAtBothEnds()
        {
            var s = new Summer(B, 4);
            Lab.Do(s, s.ChooseAction(A("rest")));
            Assert.That(s.Stat(S("fatigue")), Is.EqualTo(0), "0 から 50 減らしても 0");
            Assert.That(s.Events.Any(e => e.Kind == SummerEventKind.StatChanged && e.Index == S("fatigue")), Is.False, "変わらなければ出来事なし");
        }

        [Test]
        public void Stats_SaturateAtBoundary_ExactAndOneOver()
        {
            // 上限ちょうど（10 + 90 = 100）と 1 超え（10 + 91）。下限ちょうど（50 - 50 = 0）と 1 超え（50 - 51）。
            const string StudyLine = "@action study 勉強 : study += 3..5 ; fatigue += 30";
            int Study(int amount)
            {
                var src = Lab.Small.Replace("x200", "x100", StringComparison.Ordinal).Replace("x50", "x100", StringComparison.Ordinal).Replace(StudyLine, $"@action study 勉強 : study += {amount}", StringComparison.Ordinal);
                var b = Lab.Compile(src);
                var s = new Summer(b, 1);
                Lab.Do(s, s.ChooseAction(b.ActionIndex("study")));
                return s.Stat(b.StatIndex("study"));
            }

            Assert.That(Study(90), Is.EqualTo(100));
            Assert.That(Study(91), Is.EqualTo(100));
            Assert.That(Study(89), Is.EqualTo(99));

            int Rest(int initial, int amount)
            {
                var src = Lab.Small.Replace("@stat fatigue 疲労 0..100 = 0 cost", $"@stat fatigue 疲労 0..100 = {initial} cost", StringComparison.Ordinal)
                    .Replace("fatigue -= 50", $"fatigue -= {amount}", StringComparison.Ordinal);
                var b = Lab.Compile(src);
                var s = new Summer(b, 1);
                Lab.Do(s, s.ChooseAction(b.ActionIndex("rest")));
                return s.Stat(b.StatIndex("fatigue"));
            }

            Assert.That(Rest(50, 50), Is.EqualTo(0));
            Assert.That(Rest(50, 51), Is.EqualTo(0));
            Assert.That(Rest(50, 49), Is.EqualTo(1));
        }

        [Test]
        public void Stats_StayInRange_OverManyRandomSummers()
        {
            for (int seed = 0; seed < 200; seed++)
            {
                var s = new Summer(B, seed);
                PlayRandom(s, new SeededRandom(seed + 1000), x =>
                {
                    foreach (var st in B.Stats)
                    {
                        var v = x.Stat(st.Index);
                        Assert.That(v, Is.InRange(st.Min, st.Max), $"seed {seed} day {x.Day} {st.Id}");
                    }
                });
            }
        }

        [Test]
        public void Stats_StayInsideStatBounds()
        {
            // 区間の検証の見積もりは、実際の値を必ず含む（過大評価）。
            var bounds = new StatBounds(B);
            for (int seed = 0; seed < 100; seed++)
            {
                var s = new Summer(B, seed);
                PlayRandom(s, new SeededRandom(seed + 7), x =>
                {
                    foreach (var st in B.Stats)
                    {
                        var (lo, hi) = bounds.StatOn(st.Index, x.Day);
                        Assert.That(x.Stat(st.Index), Is.InRange(lo, hi), $"seed {seed} day {x.Day} {st.Id}");
                    }
                });
            }
        }

        // ---- 同日排他イベントは重複しない（題材固有） ----

        [Test]
        public void SameDay_AtMostOneEventPerSlot_AndExclusivePairsNeverTogether()
        {
            var seenC = 0;
            var seenA = 0;
            for (int seed = 0; seed < 300; seed++)
            {
                var s = new Summer(B, seed);
                PlayRandom(s, new SeededRandom(seed * 31 + 5));
                foreach (var day in EventsOf(s).GroupBy(e => e.Day))
                {
                    var ids = day.Select(e => e.Index).ToList();
                    Assert.That(ids.Count(i => B.Events[i].Slot == Slot.After), Is.LessThanOrEqualTo(1), $"seed {seed} day {day.Key}");
                    Assert.That(ids.Count(i => B.Events[i].Slot == Slot.Night), Is.LessThanOrEqualTo(1), $"seed {seed} day {day.Key}");
                    Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Count));
                    for (int i = 0; i < ids.Count; i++)
                    {
                        for (int j = i + 1; j < ids.Count; j++)
                        {
                            Assert.That(B.Exclusive(ids[i], ids[j]), Is.False, $"seed {seed} day {day.Key}");
                        }
                    }

                    if (ids.Contains(E("e_c"))) { seenC++; }
                    if (ids.Contains(E("e_a"))) { seenA++; }
                }
            }

            Assert.That(seenC, Is.GreaterThan(0), "排他の組が実際に候補になった");
            Assert.That(seenA, Is.GreaterThan(0));
        }

        [Test]
        public void Exclusive_FirstChosenWins()
        {
            // e_c（行動の後）が出た日は、夜に好感度があっても e_a は出ず、次の e_b が出る。
            var found = false;
            for (int seed = 0; seed < 200 && !found; seed++)
            {
                var s = new Summer(B, seed);
                Lab.Play(s, Lab.Prefer("meet", "rest"));
                foreach (var day in EventsOf(s).GroupBy(e => e.Day))
                {
                    var ids = day.Select(e => e.Index).ToList();
                    if (!ids.Contains(E("e_c"))) { continue; }
                    Assert.That(ids, Has.No.Member(E("e_a")));
                    Assert.That(ids, Has.Member(E("e_b")), "e_a が除かれたので同じ優先度の e_b");
                    found = true;
                }
            }

            Assert.That(found, Is.True);
        }

        [Test]
        public void Select_IsFilteredByCondition()
        {
            // 好感度 0 の夜は e_a・e_b のどちらも出ない。
            var s = new Summer(B, 9);
            Lab.Do(s, s.ChooseAction(A("study")));
            while (s.Phase == Phase.Scene) { Lab.Step(s, Lab.Prefer("study")); }
            Assert.That(EventsOf(s).Any(e => B.Events[e.Index].Slot == Slot.Night), Is.False);
        }

        // ---- 条件を満たさない ED に入らない（題材固有） ----

        private sealed class Fake : IConditionState
        {
            public int[] Stats { get; } = new int[3];
            public bool[] Flags { get; } = new bool[1];

            public int Stat(int index) => Stats[index];

            public bool Flag(int index) => Flags[index];

            public int Day => 8;
            public Weekday Weekday => Weekday.Mon;
            public int Action => -1;
        }

        [TestCase(29, true, false)]
        [TestCase(30, true, true)]
        [TestCase(100, false, false)]
        [TestCase(30, false, false)]
        public void EndingCondition_Boundary(int aoi, bool met, bool expected)
        {
            var f = new Fake();
            f.Stats[S("aoi")] = aoi;
            f.Flags[B.FlagIndex("met")] = met;
            Assert.That(B.Endings[B.EndingIndex("aoi")].When!.Eval(f), Is.EqualTo(expected));
        }

        [Test]
        public void Ending_MeetEveryDay_WithFlag_IsAoi_WithoutFlag_IsAlone()
        {
            var s = new Summer(B, 1);
            Lab.Play(s, Lab.Prefer("meet", "rest"));
            Assert.That(s.Ending, Is.EqualTo(B.EndingIndex("aoi")));
            Assert.That(s.Events[s.Events.Count - 1].Kind, Is.EqualTo(SummerEventKind.Ending));

            var t = new Summer(B, 1);
            Lab.Do(t, t.ChooseAction(A("meet")));
            Lab.Do(t, t.Choose(1));
            Lab.Play(t, Lab.Prefer("meet", "rest"));
            Assert.That(t.Stat(S("aoi")), Is.GreaterThanOrEqualTo(30));
            Assert.That(t.Ending, Is.EqualTo(B.EndingIndex("alone")), "フラグがなければ好感度が足りても入らない");
        }

        [Test]
        public void Ending_IsHighestPriorityThatHolds_OverRandomSummers()
        {
            var counts = new int[B.Endings.Count];
            for (int seed = 0; seed < 300; seed++)
            {
                var s = new Summer(B, seed);
                PlayRandom(s, new SeededRandom(seed + 77));
                var chosen = B.Endings[s.Ending];
                Assert.That(chosen.When == null || chosen.When.Eval(s), Is.True, $"seed {seed}");
                foreach (var e in B.EndingsByPriority)
                {
                    if (e.Index == s.Ending) { break; }
                    Assert.That(e.When!.Eval(s), Is.False, $"seed {seed}: 上の ED {e.Id} が成り立つ");
                }

                counts[s.Ending]++;
            }

            Assert.That(counts, Has.All.GreaterThan(0), "乱択でも両方の ED に届く");
        }

        // ---- 乱数・再現性 ----

        [Test]
        public void SameSeed_SameActions_SameEventsAndHash()
        {
            for (int seed = 0; seed < 50; seed++)
            {
                var a = new Summer(B, seed);
                var b = new Summer(B, seed);
                PlayRandom(a, new SeededRandom(seed));
                PlayRandom(b, new SeededRandom(seed));
                Assert.That(b.Log, Is.EqualTo(a.Log));
                Assert.That(b.Hash(), Is.EqualTo(a.Hash()));
            }
        }

        [Test]
        public void DifferentSeeds_GiveDifferentSummers()
        {
            var logs = new HashSet<string>(StringComparer.Ordinal);
            for (int seed = 0; seed < 20; seed++)
            {
                var s = new Summer(B, seed);
                Lab.Play(s, Lab.Prefer("study", "rest"));
                logs.Add(string.Join(",", s.Log));
            }

            Assert.That(logs.Count, Is.GreaterThan(10));
        }

        [Test]
        public void ExtraActionDraws_DoNotChangeEventRolls()
        {
            // 勉強に効果を 1 つ足すと行動の乱数を引く回数が変わるが、イベントの系列は別なので出るイベントは同じ。
            var more = Lab.Compile(Lab.Replace("study += 3..5 ; fatigue += 30", "study += 3..5 ; fatigue += 30 ; study += 0..2"));
            for (int seed = 0; seed < 50; seed++)
            {
                var a = new Summer(B, seed);
                var b = new Summer(more, seed);
                var pick = Lab.Prefer("study", "meet", "rest");
                Lab.Play(a, pick);
                Lab.Play(b, pick);
                Assert.That(EventsOf(b), Is.EqualTo(EventsOf(a)), $"seed {seed}");
                Assert.That(b.Log.Where(e => e.Kind == LogKind.Action), Is.EqualTo(a.Log.Where(e => e.Kind == LogKind.Action)), "結果の段階は最初に引くので同じ");
            }
        }

        [Test]
        public void Clone_IsIndependent_AndContinuesTheSame()
        {
            var s = new Summer(B, 11);
            for (int i = 0; i < 5; i++) { Lab.Step(s, Lab.Prefer("meet")); }
            var c = s.Clone();
            Assert.That(c.Hash(), Is.EqualTo(s.Hash()));
            Lab.Play(c, Lab.Prefer("meet", "rest"));
            Assert.That(c.Hash(), Is.Not.EqualTo(s.Hash()));
            Lab.Play(s, Lab.Prefer("meet", "rest"));
            Assert.That(s.Hash(), Is.EqualTo(c.Hash()));
        }

        [Test]
        public void Hash_SeesEventRngAndFiredToday()
        {
            // 同じ朝でもシードが違えばイベントの系列が違う（以後が分かれる）。
            Assert.That(new Summer(B, 1).Hash(), Is.Not.EqualTo(new Summer(B, 2).Hash()));
            var s = new Summer(B, 1);
            Lab.Do(s, s.ChooseAction(A("meet")));
            var h = s.Hash();
            Lab.Do(s, s.Choose(0));
            Assert.That(s.Hash(), Is.Not.EqualTo(h));
        }

        /// <summary>固定のシードと方針の最終ハッシュ（.NET と Mono で同じ値）。</summary>
        [Test]
        public void FinalHash_IsFixed()
        {
            var s = new Summer(B, 12345);
            PlayRandom(s, new SeededRandom(54321));
            TestContext.WriteLine($"hash 0x{s.Hash():X8} ending {s.Ending} log {string.Join(",", s.Log)}");
            Assert.That(s.Hash(), Is.EqualTo(0x31B33CE7u));
        }

        // ---- 日の始めの写し ----

        [Test]
        public void Snapshot_RestoresTheSameMorning_AndTheSameContinuation()
        {
            var s = new Summer(B, 21);
            while (s.Day < 4) { Lab.Step(s, Lab.Prefer("meet")); }
            var snap = s.Snapshot();
            var r = Summer.Restore(B, snap, out var reason);
            Assert.That(reason, Is.Null);
            Assert.That(r!.Hash(), Is.EqualTo(s.Hash()));
            Lab.Play(s, Lab.Prefer("study", "rest"));
            Lab.Play(r, Lab.Prefer("study", "rest"));
            Assert.That(r.Hash(), Is.EqualTo(s.Hash()));
            Assert.That(snap.Day, Is.EqualTo(4), "写しは後の進行で変わらない");
        }

        [Test]
        public void Snapshot_OnlyInTheMorning()
        {
            var s = new Summer(B, 1);
            Lab.Do(s, s.ChooseAction(A("meet")));
            Assert.Throws<InvalidOperationException>(() => s.Snapshot());
        }

        [Test]
        public void Restore_RejectsSnapshotsThatDoNotFit()
        {
            var ok = new Summer(B, 1).Snapshot();
            SummerSnapshot With(int day = 1, int[]? stats = null, bool[]? flags = null, LogEntry[]? log = null) =>
                new(ok.Seed, day, stats ?? ok.Stats.ToArray(), flags ?? ok.Flags.ToArray(), ok.Done.ToArray(), log ?? ok.Log.ToArray());

            Assert.That(Summer.Restore(B, With(day: 0), out var r1), Is.Null);
            Assert.That(r1!.Contains("日 0", StringComparison.Ordinal), Is.True, r1);
            Assert.That(Summer.Restore(B, With(day: 9), out _), Is.Null);
            Assert.That(Summer.Restore(B, With(stats: _overMax), out var r2), Is.Null);
            Assert.That(r2!.Contains("fatigue", StringComparison.Ordinal), Is.True, r2);
            Assert.That(Summer.Restore(B, With(stats: _tooFew), out _), Is.Null);
            Assert.That(Summer.Restore(B, With(flags: Array.Empty<bool>()), out _), Is.Null);
            Assert.That(Summer.Restore(B, With(day: 2, log: new[] { new LogEntry(2, LogKind.Action, 0, 0) }), out _), Is.Null, "今日以降の記録");
            Assert.That(Summer.Restore(B, With(day: 2, log: new[] { new LogEntry(1, LogKind.Event, 99, 0) }), out _), Is.Null);
            Assert.That(Summer.Restore(B, With(day: 8), out _), Is.Not.Null);
        }

        [Test]
        public void Restore_RecomputesForcedRest()
        {
            var ok = new Summer(B, 1).Snapshot();
            var stats = ok.Stats.ToArray();
            stats[S("fatigue")] = 100;
            var r = Summer.Restore(B, new SummerSnapshot(ok.Seed, 3, stats, ok.Flags.ToArray(), ok.Done.ToArray(), Array.Empty<LogEntry>()), out _);
            Assert.That(r!.Forced, Is.True);
        }

        // ---- 実行器は構文解析の型を使わない ----

        [Test]
        public void Runtime_DoesNotReferenceCompilerTypes()
        {
            var compilerTypes = typeof(SummerCompiler).GetNestedTypes(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                .Append(typeof(SummerCompiler)).ToList();
            const System.Reflection.BindingFlags All = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
            foreach (var t in new[] { typeof(Summer), typeof(SummerSnapshot) })
            {
                foreach (var f in t.GetFields(All)) { Assert.That(compilerTypes, Has.No.Member(f.FieldType), $"{t.Name}.{f.Name}"); }
                foreach (var m in t.GetMethods(All))
                {
                    Assert.That(compilerTypes, Has.No.Member(m.ReturnType), $"{t.Name}.{m.Name}");
                    foreach (var p in m.GetParameters()) { Assert.That(compilerTypes, Has.No.Member(p.ParameterType), $"{t.Name}.{m.Name}"); }
                }
            }
        }
    }
}
