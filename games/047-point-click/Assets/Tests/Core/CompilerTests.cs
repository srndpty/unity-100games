using System;
using System.Linq;
using Game.Core.Rules;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>構文解析・名前の解決・意味の検証。誤りは行番号と理由付きで拒否する。</summary>
    public sealed class CompilerTests
    {
        [Test]
        public void SmallBook_Compiles()
        {
            var r = Lab.SmallResult;
            Assert.That(r.Ok, Is.True, Lab.Dump(r));
            var b = r.Book!;
            Assert.That(b.Rooms.Count, Is.EqualTo(2));
            Assert.That(b.Puzzles.Count, Is.EqualTo(3));
            Assert.That(b.Puzzles[b.Goal].Id, Is.EqualTo("light"));
            Assert.That(b.Hotspots[Lab.H("dock.door")].InitiallyLocked, Is.True);
            Assert.That(b.Hotspots[Lab.H("dock.door")].Destination, Is.EqualTo(b.RoomIndex("shed")));
            Assert.That(r.Report!.States, Is.GreaterThan(10));
        }

        [Test]
        public void SolveRule_GetsImplicitUnsolvedGuard()
        {
            var b = Lab.Book;
            var rule = b.RulesFor(Verb.Use, Lab.HT("dock.net"), Lab.I("knife")).Single();
            Assert.That(rule.Conditions.Any(c => c.Kind == ConditionKind.Solved && c.Index == b.PuzzleIndex("cut_net") && c.Negated), Is.True);
        }

        [Test]
        public void ItemCombination_IsNormalizedBothWays()
        {
            var b = Lab.Book;
            var a = b.RulesFor(Verb.Use, Lab.IT("stick"), Lab.I("cloth"));
            var c = b.RulesFor(Verb.Use, Lab.IT("cloth"), Lab.I("stick"));
            Assert.That(a.Count, Is.EqualTo(1));
            Assert.That(c, Is.EqualTo(a));
        }

        [Test]
        public void Fingerprint_IgnoresLineEndings()
        {
            Assert.That(RuleCompiler.Fingerprint("a\r\nb"), Is.EqualTo(RuleCompiler.Fingerprint("a\nb")));
            Assert.That(RuleCompiler.Fingerprint("a\nb"), Is.Not.EqualTo(RuleCompiler.Fingerprint("a\nc")));
        }

        // ---- 構文 ----

        [TestCase("look dock.net 「古い」", "がありません")]
        [TestCase("jump dock.net : 「x」", "未知の動詞 jump")]
        [TestCase("look dock.net : 「閉じていない", "閉じられていません")]
        [TestCase("look dock.net : explode dock.net", "未知の効果 explode")]
        [TestCase("look dock.net : 「a」 ;", "空の効果")]
        [TestCase("look dock.net when : 「a」", "条件がありません")]
        [TestCase("look dock.net when asked or !asked : 「a」", "and でつなぎます")]
        [TestCase("look dock.net : give", "「give 名前」と書きます")]
        [TestCase("@thing x", "未知の宣言 @thing")]
        [TestCase("@room Big 大部屋 width 10 start 0", "@room は")]
        [TestCase("@object dock.x 物 rect 1 2 3 stand 1", "@object は")]
        [TestCase("@exit dock.x 戸 rect 0 0 10 10 stand 5 -> shed", "@exit は")]
        [TestCase("@puzzle p 名前 before q", "after")]
        public void SyntaxErrors_AreReportedWithLine(string line, string fragment)
        {
            var src = Lab.Small + line + "\n";
            var r = Lab.Compile(src);
            Assert.That(r.Ok, Is.False);
            Assert.That(Lab.HasError(r, fragment, Lab.LineOf(src, line)), Is.True, Lab.Dump(r));
        }

        // ---- 参照 ----

        [TestCase("use knife on dock.net :", "use knife on dock.nett :", "対象 dock.nett")]
        [TestCase("use knife on dock.net :", "use knfe on dock.net :", "使う持ち物 knfe")]
        [TestCase("when asked :", "when askd :", "条件のフラグ askd")]
        [TestCase("when lamp_oiled :", "when solved lite :", "条件の謎 lite")]
        [TestCase("give key ;", "give kye ;", "効果の持ち物 kye")]
        [TestCase("unlock dock.door ;", "unlock dock.net ;", "出口ではありません")]
        [TestCase("hide dock.knife ;", "hide dock.knif ;", "dock.knif は物")]
        [TestCase("@goal light", "@goal lite", "@goal の謎 lite")]
        [TestCase("@start dock", "@start pier", "@start の部屋 pier")]
        [TestCase("after open_door", "after opn_door", "after の謎 opn_door")]
        [TestCase("-> shed at 50", "-> shad at 50", "行き先の部屋 shad")]
        [TestCase("topic dock.man oil", "topic dock.knife oil", "人物ではありません")]
        public void UnknownReferences_AreRejected(string from, string to, string fragment)
        {
            var src = Lab.Replace(from, to);
            var r = Lab.Compile(src);
            Assert.That(r.Ok, Is.False);
            Assert.That(Lab.HasError(r, fragment, Lab.LineOf(src, to)), Is.True, Lab.Dump(r));
        }

        [Test]
        public void DuplicateIds_AreRejectedAcrossKinds()
        {
            var src = Lab.Small + "@flag knife\n@room dock 二つ目 width 10 start 0\n";
            var r = Lab.Compile(src);
            Assert.That(Lab.HasError(r, "knife は持ち物としてすでに宣言されています", Lab.LineOf(src, "@flag knife")), Is.True, Lab.Dump(r));
            Assert.That(Lab.HasError(r, "dock は部屋としてすでに宣言されています", Lab.LineOf(src, "二つ目")), Is.True, Lab.Dump(r));
        }

        // ---- 意味 ----

        [TestCase("stand 230", "stand 1001", "stand 1001 が部屋の幅")]
        [TestCase("rect 200 0 260 60", "rect 260 0 200 60", "右 ≥ 左")]
        [TestCase("rect 400 100 600 300", "rect 400 100 600 1081", "部屋（0〜1000 × 0〜1080）の外")]
        [TestCase("-> shed at 50", "-> shed at 801", "行き先の部屋の幅")]
        [TestCase("-> dock at 960", "-> shed at 10", "同じ部屋")]
        [TestCase("start 100", "start 1001", "start 1001")]
        public void Geometry_IsValidated(string from, string to, string fragment)
        {
            var src = Lab.Replace(from, to);
            var r = Lab.Compile(src);
            Assert.That(Lab.HasError(r, fragment), Is.True, Lab.Dump(r));
        }

        [TestCase("take knife : 「x」", "持ち物に書ける規則は look")]
        [TestCase("talk knife : 「x」", "持ち物に書ける規則は look")]
        [TestCase("use knife : 「x」", "持ち物に書ける規則は look")]
        [TestCase("use knife on knife : 「x」", "それ自身")]
        [TestCase("use dock.door : 「x」", "出口を（持ち物なしで）使う")]
        [TestCase("look dock.net when asked and asked : 「x」", "条件 asked が重複")]
        [TestCase("use stick on dock.net : solve cut_net ; solve cut_net", "二度解いて")]
        [TestCase("topic dock.man oil 「もう一度」 : 「x」", "話題 oil が重複")]
        public void InvalidRuleShapes_AreRejected(string line, string fragment)
        {
            var src = Lab.Small + line + "\n";
            var r = Lab.Compile(src);
            Assert.That(Lab.HasError(r, fragment, Lab.LineOf(src, line)), Is.True, Lab.Dump(r));
        }

        [Test]
        public void MissingLook_IsRejected()
        {
            var src = Lab.Replace("look dock.knife : 「錆びた包丁だ。」\n", "");
            var r = Lab.Compile(src);
            Assert.That(Lab.HasError(r, "dock.knife に look の規則がありません", Lab.LineOf(src, "@object dock.knife")), Is.True, Lab.Dump(r));
            src = Lab.Replace("look torch : 「火のついた松明。」\n", "");
            r = Lab.Compile(src);
            Assert.That(Lab.HasError(r, "持ち物 torch に look の規則がありません", Lab.LineOf(src, "@item torch")), Is.True, Lab.Dump(r));
        }

        [Test]
        public void TextLength_IsBoundedByLimit()
        {
            // ほかの文は上限を超えてもよい。確かめるのは書き換えた 1 行の誤りの有無だけ。
            var src = Lab.Replace("「錆びた包丁だ。」", "「" + new string('あ', 10) + "」");
            var line = Lab.LineOf(src, "look dock.knife");
            Assert.That(Lab.HasError(Lab.Compile(src, new BookLimits { MaxTextLength = 10 }), "字で、上限", line), Is.False);
            var r = Lab.Compile(src, new BookLimits { MaxTextLength = 9 });
            Assert.That(Lab.HasError(r, "10 字で、上限 9 字", line), Is.True, Lab.Dump(r));
        }

        [Test]
        public void EmptyText_IsRejected()
        {
            var src = Lab.Replace("「錆びた包丁だ。」", "「 」");
            Assert.That(Lab.HasError(Lab.Compile(src), "空の文"), Is.True);
        }

        [Test]
        public void ExpectedCounts_AreChecked()
        {
            var r = Lab.Compile(Lab.Small, new BookLimits { ExpectedRooms = 3, ExpectedPuzzles = 5 });
            Assert.That(Lab.HasError(r, "部屋は 3 個です（今は 2 個）"), Is.True, Lab.Dump(r));
            Assert.That(Lab.HasError(r, "謎は 5 個です（今は 3 個）"), Is.True, Lab.Dump(r));
            Assert.That(Lab.Compile(Lab.Small, new BookLimits { ExpectedRooms = 2, ExpectedPuzzles = 3 }).Ok, Is.True);
        }

        [Test]
        public void Limits_AreCheckedBeforeExpanding()
        {
            var r = Lab.Compile(Lab.Small, new BookLimits { MaxLines = 10 });
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Errors.Single().Message, Does.StartWith("行数"));
            r = Lab.Compile(Lab.Small, new BookLimits { MaxItems = 6 });
            Assert.That(Lab.HasError(r, "持ち物は 6 種類まで", Lab.LineOf(Lab.Small, "@item torch")), Is.True, Lab.Dump(r));
            r = Lab.Compile(Lab.Small, new BookLimits { MaxRules = 5 });
            Assert.That(Lab.HasError(r, "ルールは 5 個まで"), Is.True, Lab.Dump(r));
        }

        [Test]
        public void InvalidLimits_Throw()
        {
            Assert.Throws<ArgumentException>(() => Lab.Compile(Lab.Small, new BookLimits { MaxStates = 0 }));
            Assert.Throws<ArgumentException>(() => Lab.Compile(Lab.Small, new BookLimits { MaxPuzzles = 31 }));
            Assert.Throws<ArgumentException>(() => Lab.Compile(Lab.Small, new BookLimits { ExpectedRooms = -1 }));
        }

        [Test]
        public void MissingStartOrGoal_IsRejected()
        {
            Assert.That(Lab.HasError(Lab.Compile(Lab.Replace("@goal light\n", "")), "@goal がありません"), Is.True);
            var src = Lab.Small + "@start shed\n";
            Assert.That(Lab.HasError(Lab.Compile(src), "@start は 1 つだけ", Lab.LineOf(src, "@start shed")), Is.True);
        }
    }
}
