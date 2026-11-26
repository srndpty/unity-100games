using System;
using System.Linq;
using Game.Core.Analysis;
using Game.Core.Play;
using Game.Core.Rules;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>本番のルール表（Data/Rules.txt）と解答手順（Data/Walkthrough.txt）。</summary>
    public sealed class ProductionTests
    {
        private static CompileResult? _compiled;

        internal static CompileResult Compiled => _compiled ??= Adventure.Compile(Lab.DataText("Rules.txt"));

        internal static RuleBook Book => Compiled.Book ?? throw new InvalidOperationException(Lab.Dump(Compiled));

        internal static Move[] Walkthrough => Adventure.ParseWalkthrough(Book, Lab.DataText("Walkthrough.txt")).ToArray();

        [Test]
        public void Rules_CompileWithProductionLimits()
        {
            Assert.That(Compiled.Ok, Is.True, Lab.Dump(Compiled));
            Assert.That(Book.Rooms.Select(r => r.Id), Is.EqualTo(Lab.Of("cottage", "dock", "lighthouse")));
            Assert.That(Book.Puzzles.Count, Is.EqualTo(5));
            Assert.That(Book.Hotspots.Count(h => h.Kind == HotspotKind.Npc), Is.GreaterThanOrEqualTo(1));
            Assert.That(Book.Topics.Count, Is.GreaterThanOrEqualTo(3));
            Assert.That(Book.Rules.Any(r => r.Verb == Verb.Use && r.Target.Kind == TargetKind.Item), Is.True, "持ち物どうしの組み合わせがある");
            TestContext.WriteLine($"状態 {Compiled.Report!.States}・遷移 {Compiled.Report.Transitions}");
        }

        // ---- 完了判定：全 5 謎を解ける ----

        [Test]
        public void Walkthrough_SolvesAllFivePuzzlesAndEscapes()
        {
            var w = Lab.PlayWalking(Book, Walkthrough);
            Assert.That(w.Escaped, Is.True);
            Assert.That(w.SolvedCount, Is.EqualTo(5));
            foreach (var p in Book.Puzzles) { Assert.That(w.Solved(p.Index), Is.True, p.Id); }
            Assert.That(w.Room, Is.EqualTo(Book.RoomIndex("lighthouse")));
        }

        [Test]
        public void Walkthrough_FinalHash_IsFixed()
        {
            var w = Lab.PlayWalking(Book, Walkthrough);
            TestContext.WriteLine($"Tick {w.Tick}・行為 {w.Actions}・ハッシュ 0x{w.Hash():X8}");
            Assert.That(w.Hash(), Is.EqualTo(0x1AB9ED74u), "整数と Tick だけの規則なので、.NET と Mono で同じ値になる");
        }

        [Test]
        public void Walkthrough_InstantAndWalkingReachSameAbstractState()
        {
            var instant = StateSpace.Replay(Book, Walkthrough);
            var walking = Lab.PlayWalking(Book, Walkthrough);
            Assert.That(walking.AbstractKey(), Is.EqualTo(instant.AbstractKey()), "探索（歩かない）と試合（歩く）の規則が同じ");
        }

        // ---- 題材固有：依存関係から詰みが発生しない（本番） ----

        [Test]
        public void Production_HasNoDeadEnd()
        {
            Assert.That(Compiled.Report!.Errors, Is.Empty);
            Assert.That(Lab.StuckStates(Book), Is.Empty);
        }

        [Test]
        public void Production_DependencyGraph_MatchesReadme()
        {
            var mermaid = PuzzleGraph.ToMermaid(Book);
            var readme = Lab.ReadmeText().Replace("\r\n", "\n");
            Assert.That(readme.Contains("```mermaid\n" + mermaid + "```", StringComparison.Ordinal), Is.True, "README の依存グラフを次に直す：\n" + mermaid);
        }

        [Test]
        public void Production_PuzzlesCanBeSolvedInOtherOrders()
        {
            // 依存のない謎（鍵・油・お茶）は、他の謎を 1 つも解かずに解ける（一本道にしない）。
            var p = Compiled.Report!.Precedes;
            Assert.That(p[Book.PuzzleIndex("free_key")], Is.EqualTo(0));
            Assert.That(p[Book.PuzzleIndex("draw_oil")], Is.EqualTo(0));
            Assert.That(p[Book.PuzzleIndex("warm_captain")], Is.EqualTo(0));
        }

        // ---- 解答手順の読み込み ----

        [TestCase("dance cottage.knife")]
        [TestCase("take cottage.spoon")]
        [TestCase("go cottage.knife")]
        [TestCase("use knife on")]
        [TestCase("topic dock.captain weather")]
        [TestCase("bye now")]
        public void Walkthrough_RejectsBadLinesWithLineNumber(string line)
        {
            var e = Assert.Throws<FormatException>(() => Adventure.ParseWalkthrough(Book, "# c\nlook cottage.knife\n" + line));
            Assert.That(e!.Message.Contains("行 3", StringComparison.Ordinal), Is.True, e.Message);
        }

        [Test]
        public void Walkthrough_NormalizesCombination()
        {
            var a = Adventure.ParseWalkthrough(Book, "use tea_leaves on hot_water").Single();
            var b = Adventure.ParseWalkthrough(Book, "use hot_water on tea_leaves").Single();
            Assert.That((a.Target, a.Tool), Is.EqualTo((b.Target, b.Tool)));
        }
    }
}
