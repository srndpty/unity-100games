using System;
using System.Linq;
using Game.Core.Analysis;
using Game.Core.Play;
using Game.Core.Rules;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>
    /// 探索の手と、遊ぶときにクリック・命令できることの一致（レビュー P1-1・P2-1）。
    /// 手前の物に全体を覆われた物はクリックで当たらないので、探索でも命令でも扱えない。
    /// </summary>
    public sealed class ReachabilityTests
    {
        private const string KnifeLine = "@object dock.knife 包丁 rect 200 0 260 60 stand 230";

        /// <summary>包丁（200〜260 × 0〜60）の手前に物を足した表。足した物は取ると隠れる。</summary>
        private static CompileResult WithCovers(params string[] rects)
        {
            var decl = string.Join("\n", rects.Select((r, i) => $"@object dock.crate{i} 木箱 rect {r} stand 230"));
            var rules = string.Concat(rects.Select((r, i) => $"look dock.crate{i} : 「木箱だ。」\ntake dock.crate{i} : hide dock.crate{i} ; 「木箱をどけた。」\n"));
            return Lab.Compile(Lab.Small.Replace(KnifeLine, KnifeLine + "\n" + decl, StringComparison.Ordinal) + rules);
        }

        private static World NewWorld(CompileResult r)
        {
            Assert.That(r.Ok, Is.True, Lab.Dump(r));
            return new World(r.Book!, new WorldSettings());
        }

        /// <summary>素朴な参照：矩形の全ての整数の点で HotspotAt を引き、その物に当たる点があるか。</summary>
        private static bool BruteForce(World w, int hotspot)
        {
            var a = w.Book.Hotspots[hotspot].Area;
            for (int y = a.MinY; y <= a.MaxY; y++)
            {
                for (int x = a.MinX; x <= a.MaxX; x++)
                {
                    if (w.HotspotAt(new Vec2i(x, y)) == hotspot) { return true; }
                }
            }

            return false;
        }

        private static void AssertConsistent(World w)
        {
            foreach (var h in w.Book.Hotspots.Where(h => h.Room == w.Room))
            {
                Assert.That(w.IsInteractable(h.Index), Is.EqualTo(BruteForce(w, h.Index)), h.Id);
                var p = w.ClickablePoint(h.Index);
                if (p != null) { Assert.That(w.HotspotAt(p.Value), Is.EqualTo(h.Index), h.Id); }
            }
        }

        [Test]
        public void FullyCoveredObject_CannotBeActedOn_UntilTheCoverIsRemoved()
        {
            var r = WithCovers("190 0 270 70");
            var w = NewWorld(r);
            var knife = r.Book!.HotspotIndex("dock.knife");
            Assert.That(w.IsInteractable(knife), Is.False);
            AssertConsistent(w);
            var hash = w.Hash();
            Assert.That(w.Act(Verb.Take, Target.Hotspot(knife)).Reason, Is.EqualTo(Refusal.NotHere), "覆われた物への命令は受け付けない");
            Assert.That(w.Hash(), Is.EqualTo(hash));
            Assert.That(StateSpace.Moves(w).Any(m => m.Target == Target.Hotspot(knife)), Is.False, "探索も覆われた物を扱わない");

            // 手前の木箱をどけると、包丁をクリック・命令できる。
            w.Act(Verb.Take, Target.Hotspot(r.Book.HotspotIndex("dock.crate0")));
            Lab.Arrive(w);
            Assert.That(w.IsInteractable(knife), Is.True);
            AssertConsistent(w);
            Assert.That(w.Act(Verb.Take, Target.Hotspot(knife)).Accepted, Is.True);
        }

        [Test]
        public void PartiallyCoveredObject_CanBeClickedOnItsVisiblePart()
        {
            var r = WithCovers("190 0 230 70");
            var w = NewWorld(r);
            var knife = r.Book!.HotspotIndex("dock.knife");
            Assert.That(w.IsInteractable(knife), Is.True);
            var p = w.ClickablePoint(knife)!.Value;
            Assert.That(p.X, Is.GreaterThan(230), "覆われていない右側の点");
            AssertConsistent(w);
        }

        [Test]
        public void CoverMadeOfSeveralRects_IsDetected()
        {
            // 両端を含む整数の矩形なので、230 で終わる箱と 231 で始まる箱は隙間なく覆う。
            var full = WithCovers("190 0 230 70", "231 0 270 30", "231 31 270 70");
            var w = NewWorld(full);
            Assert.That(w.IsInteractable(full.Book!.HotspotIndex("dock.knife")), Is.False);
            AssertConsistent(w);

            // 1 列だけ隙間（x = 231）があれば、そこでクリックできる。
            var gap = WithCovers("190 0 230 70", "232 0 270 30", "232 31 270 70");
            var g = NewWorld(gap);
            var knife = gap.Book!.HotspotIndex("dock.knife");
            Assert.That(g.ClickablePoint(knife)!.Value.X, Is.EqualTo(231));
            AssertConsistent(g);

            // 1 点だけ隙間（x = 231..270, y = 31 の行が空く）でも見つける。
            var hole = WithCovers("190 0 230 70", "231 0 270 30", "231 32 270 70");
            var h = NewWorld(hole);
            Assert.That(h.ClickablePoint(hole.Book!.HotspotIndex("dock.knife"))!.Value.Y, Is.EqualTo(31));
            AssertConsistent(h);
        }

        [Test]
        public void CoveredKeyItemThatCanNeverBeUncovered_IsADeadEnd()
        {
            // 手前の物を動かす手段がなければ、包丁が取れず網を切れないので脱出できない。探索がそれを拒否する。
            var src = Lab.Small.Replace(KnifeLine, KnifeLine + "\n@object dock.crate 木箱 rect 190 0 270 70 stand 230", StringComparison.Ordinal)
                + "look dock.crate : 「動かせない木箱だ。」\n";
            var r = Lab.Compile(src);
            Assert.That(r.Ok, Is.False);
            Assert.That(Lab.HasError(r, "どの手順でも脱出"), Is.True, Lab.Dump(r));
        }

        [Test]
        public void HitTestAndInteractable_AgreeInEveryReachableState()
        {
            foreach (var w in Lab.Enumerate(Lab.Book).Where(w => !w.Escaped && w.TalkingTo < 0)) { AssertConsistent(w); }
        }

        [Test]
        public void TakeAndTalkOnExits_AreExploredLikeEveryOtherCommand()
        {
            // 出口への「取る」「話す」も遊ぶときに命じられるので、探索の手に入る（入らなければ使われないルールとして拒否されていた）。
            var src = Lab.Small + "take dock.door : 「扉は持ち運べない。」\ntalk shed.out : 「外は嵐だ。」\n";
            var r = Lab.Compile(src);
            Assert.That(r.Ok, Is.True, Lab.Dump(r));
            var w = new World(r.Book!, new WorldSettings());
            var door = Target.Hotspot(r.Book!.HotspotIndex("dock.door"));
            var moves = StateSpace.Moves(w).ToArray();
            Assert.That(moves.Any(m => m.Verb == Verb.Take && m.Target == door), Is.True);
            Assert.That(moves.Any(m => m.Verb == Verb.Talk && m.Target == door), Is.True);
            Assert.That(Lab.Said(Lab.Do(w, Verb.Take, "dock.door")), Is.EqualTo(Lab.Of("扉は持ち運べない。")));
        }

        [Test]
        public void ExploredMoves_AreExactlyTheAcceptedCommands()
        {
            // 探索の手は、命令の全ての候補のうち World.Check が受け付けるものと一致する（持ち物どうしは正規化した 1 通り）。
            foreach (var w in Lab.Enumerate(Lab.Book).Where(w => !w.Escaped && w.TalkingTo < 0).Take(200))
            {
                var expected = 0;
                var tools = new[] { -1 }.Concat(w.Inventory).ToArray();
                foreach (var verb in new[] { Verb.Walk, Verb.Look, Verb.Take, Verb.Use, Verb.Talk })
                {
                    foreach (var tool in tools)
                    {
                        expected += w.Book.Hotspots.Count(h => w.Clone().Act(verb, Target.Hotspot(h.Index), tool).Accepted);
                        expected += w.Inventory.ToArray().Count(i => tool <= i && w.Clone().Act(verb, Target.Item(i), tool).Accepted);
                    }
                }

                Assert.That(StateSpace.Moves(w).Count(), Is.EqualTo(expected), w.AbstractKey());
            }
        }
    }
}
