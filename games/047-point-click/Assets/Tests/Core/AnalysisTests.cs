using System;
using System.Linq;
using Game.Core.Analysis;
using Game.Core.Play;
using Game.Core.Rules;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>状態空間の探索と謎の依存グラフ。</summary>
    public sealed class AnalysisTests
    {
        // ---- 題材固有：依存関係から詰みが発生しない ----

        [Test]
        public void SmallBook_HasNoDeadEnd()
        {
            var report = Lab.SmallResult.Report!;
            Assert.That(report.Errors, Is.Empty);
            // 到達できる全ての状態で、そこから脱出できる手順があることを、手の列で 1 つずつ確かめ直す（探索とは別の経路）。
            var book = Lab.Book;
            var states = Enumerate(book);
            Assert.That(states.Length, Is.EqualTo(report.States));
            foreach (var w in states)
            {
                Assert.That(CanEscape(w), Is.True, w.AbstractKey());
            }
        }

        [Test]
        public void ThrowingAwayTheOnlyBottle_IsADeadEnd()
        {
            var src = Lab.Small + "use bottle on dock.net : lose bottle ; 「瓶を海に投げ捨てた。」\n";
            var r = Lab.Compile(src);
            Assert.That(r.Ok, Is.False);
            var e = r.Errors.Single(x => x.Message.StartsWith("詰み", StringComparison.Ordinal));
            Assert.That(e.Message.Contains("use bottle on dock.net", StringComparison.Ordinal), Is.True, e.Message);
        }

        [Test]
        public void KeyConsumedByAnotherDoor_IsADeadEnd()
        {
            var src = Lab.Small.Replace("@exit shed.out", "@object dock.chest 箱 rect 0 300 50 350 stand 25\n@exit shed.out", StringComparison.Ordinal)
                + "look dock.chest : 「鍵のかかった箱。」\nuse key on dock.chest : lose key ; hide dock.chest ; 「箱を開けたが、鍵が折れた。」\n";
            var r = Lab.Compile(src);
            Assert.That(r.Errors.Any(x => x.Message.StartsWith("詰み", StringComparison.Ordinal) && x.Message.Contains("use key on dock.chest", StringComparison.Ordinal)), Is.True, Lab.Dump(r));
        }

        [Test]
        public void NoWayToEscape_IsRejected()
        {
            var src = Lab.Replace("use torch on shed.lamp when lamp_oiled : solve light ;", "use torch on shed.lamp when lamp_oiled and has key : solve light ;");
            var r = Lab.Compile(src);
            Assert.That(Lab.HasError(r, "どの手順でも脱出"), Is.True, Lab.Dump(r));
        }

        // ---- 使われないルール・曖昧なルール ----

        [Test]
        public void UnreachableRule_IsRejected()
        {
            var src = Lab.Small + "look dock.net when solved light : 「脱出の後にしか成り立たない。」\n";
            var r = Lab.Compile(src);
            Assert.That(Lab.HasError(r, "どの到達状態でも選ばれません", Lab.LineOf(src, "when solved light")), Is.True, Lab.Dump(r));
        }

        [Test]
        public void UnreachableTopic_IsRejected()
        {
            var src = Lab.Small + "topic dock.man secret 「灯台の秘密は？」 when solved light : 「…」\n";
            var r = Lab.Compile(src);
            Assert.That(Lab.HasError(r, "この話題はどの到達状態でも選べません", Lab.LineOf(src, "secret")), Is.True, Lab.Dump(r));
        }

        [Test]
        public void AmbiguousRules_AreRejected()
        {
            var src = Lab.Replace("look dock.net : 「古い漁網に何か絡まっている。」",
                "look dock.net when !asked : 「古い漁網に何か絡まっている。」\nlook dock.net when !has knife : 「網だ。」");
            var r = Lab.Compile(src);
            Assert.That(Lab.HasError(r, $"行 {Lab.LineOf(src, "when !asked : 「古い")} と同じ状況で当たります", Lab.LineOf(src, "「網だ。」")), Is.True, Lab.Dump(r));
        }

        [Test]
        public void DisjointConditions_AreNotAmbiguous()
        {
            var src = Lab.Replace("look dock.net : 「古い漁網に何か絡まっている。」",
                "look dock.net when !solved cut_net : 「古い漁網に何か絡まっている。」\nlook dock.net when solved cut_net : 「切れた網だ。」");
            Assert.That(Lab.Compile(src).Ok, Is.True, Lab.Dump(Lab.Compile(src)));
        }

        // ---- 持ち物の違反 ----

        [Test]
        public void GivingSameItemTwice_IsRejected()
        {
            var src = Lab.Replace("take dock.cloth : give cloth ;", "take dock.cloth : give cloth ; give cloth ;");
            var r = Lab.Compile(src);
            Assert.That(Lab.HasError(r, "持ち物 cloth はすでに手に入れています", Lab.LineOf(src, "take dock.cloth")), Is.True, Lab.Dump(r));
        }

        [Test]
        public void LosingItemNotHeld_IsRejected()
        {
            var src = Lab.Replace("use oil on shed.lamp : lose oil ;", "use oil on shed.lamp : lose oil ; lose key ;");
            var r = Lab.Compile(src);
            Assert.That(Lab.HasError(r, "持っていない持ち物 key を失います", Lab.LineOf(src, "lose oil ; lose key")), Is.True, Lab.Dump(r));
        }

        [Test]
        public void InventoryCapacity_IsEnforced()
        {
            var r = Lab.Compile(Lab.Small, new BookLimits { InventoryCapacity = 2 });
            Assert.That(Lab.HasError(r, "持ち物が 2 個を超えます"), Is.True, Lab.Dump(r));
            Assert.That(Lab.Compile(Lab.Small, new BookLimits { InventoryCapacity = MaxHeld() }).Ok, Is.True);
            Assert.That(Lab.Compile(Lab.Small, new BookLimits { InventoryCapacity = MaxHeld() - 1 }).Ok, Is.False);
        }

        [Test]
        public void StateLimit_IsEnforced()
        {
            var states = Lab.SmallResult.Report!.States;
            Assert.That(Lab.Compile(Lab.Small, new BookLimits { MaxStates = states }).Ok, Is.True);
            var r = Lab.Compile(Lab.Small, new BookLimits { MaxStates = states - 1 });
            Assert.That(Lab.HasError(r, $"上限 {states - 1} を超えました"), Is.True, Lab.Dump(r));
        }

        // ---- 依存グラフ ----

        [Test]
        public void DerivedPrecedence_MatchesDeclaration()
        {
            var b = Lab.Book;
            var p = Lab.SmallResult.Report!.Precedes;
            int Bit(string id) => 1 << b.PuzzleIndex(id);
            Assert.That(p[b.PuzzleIndex("cut_net")], Is.EqualTo(0));
            Assert.That(p[b.PuzzleIndex("open_door")], Is.EqualTo(Bit("cut_net")));
            Assert.That(p[b.PuzzleIndex("light")], Is.EqualTo(Bit("cut_net") | Bit("open_door")));
        }

        [Test]
        public void DeclarationMismatch_IsRejected()
        {
            var src = Lab.Replace("@puzzle open_door 扉を開ける after cut_net", "@puzzle open_door 扉を開ける");
            var r = Lab.Compile(src);
            Assert.That(Lab.HasError(r, "謎 open_door の依存が宣言と食い違います。宣言では [] の後、ルールからは [cut_net] の後", Lab.LineOf(src, "@puzzle open_door")), Is.True, Lab.Dump(r));

            src = Lab.Replace("@puzzle cut_net 網を切る", "@puzzle cut_net 網を切る after open_door");
            r = Lab.Compile(src);
            Assert.That(Lab.HasError(r, "循環"), Is.True, Lab.Dump(r));
        }

        [Test]
        public void GoalMustComeAfterAllPuzzles()
        {
            var src = Lab.Small.Replace("@puzzle light", "@puzzle chat 話す\n@puzzle light", StringComparison.Ordinal)
                .Replace("「嵐が来るぞ。」", "solve chat ; 「嵐が来るぞ。」", StringComparison.Ordinal);
            var r = Lab.Compile(src);
            Assert.That(Lab.HasError(r, "脱出の謎 light は他の全ての謎の後に解けるようにします（今は [chat] を解かずに"), Is.True, Lab.Dump(r));
        }

        [Test]
        public void Mermaid_UsesTransitiveReduction()
        {
            var expected = "graph LR\n" +
                "  cut_net[\"網を切る\"]\n" +
                "  open_door[\"扉を開ける\"]\n" +
                "  light[\"灯をともす\"]\n" +
                "  cut_net --> open_door\n" +
                "  open_door --> light\n" +
                "  light --> escape((脱出))\n";
            Assert.That(PuzzleGraph.ToMermaid(Lab.Book), Is.EqualTo(expected));
            Assert.That(PuzzleGraph.Reduce(Lab.Ints(0, 1, 3)), Is.EqualTo(Lab.Ints(0, 1, 2)));
        }

        // ---- 道具 ----

        /// <summary>探索とは独立に、手の列を深さ優先でたどって到達状態を列挙する。</summary>
        private static World[] Enumerate(RuleBook book)
        {
            var seen = new System.Collections.Generic.Dictionary<string, World>(StringComparer.Ordinal);
            var stack = new System.Collections.Generic.Stack<World>();
            var start = new World(book, new WorldSettings());
            seen[start.AbstractKey()] = start;
            stack.Push(start);
            while (stack.Count > 0)
            {
                var w = stack.Pop();
                foreach (var m in StateSpace.Moves(w))
                {
                    var n = w.Clone();
                    m.ApplyInstantly(n);
                    var k = n.AbstractKey();
                    if (seen.ContainsKey(k)) { continue; }
                    seen[k] = n;
                    stack.Push(n);
                }
            }

            return seen.Values.ToArray();
        }

        private static bool CanEscape(World from)
        {
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal) { from.AbstractKey() };
            var queue = new System.Collections.Generic.Queue<World>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                var w = queue.Dequeue();
                if (w.Escaped) { return true; }
                foreach (var m in StateSpace.Moves(w))
                {
                    var n = w.Clone();
                    m.ApplyInstantly(n);
                    if (seen.Add(n.AbstractKey())) { queue.Enqueue(n); }
                }
            }

            return false;
        }

        private static int MaxHeld() => Enumerate(Lab.Book).Max(w => w.Inventory.Count);
    }
}
