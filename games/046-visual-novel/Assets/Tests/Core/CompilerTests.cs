using System.Linq;
using System.Text;
using Game.Core.Runtime;
using Game.Core.Script;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>コンパイラの検証（分岐・変数・テキスト）。誤りは行番号と理由付き。</summary>
    public sealed class CompilerTests
    {
        private static readonly int _jumpLine = Lab.LineOf("-> verdict");
        private static readonly int _optionLine = Lab.LineOf("- 鍵を調べる -> look_key { clue += 1 }");
        private static readonly int _ifLine = Lab.LineOf("$if clue >= 1 -> good");

        [Test]
        public void SmallScriptCompiles()
        {
            var s = Lab.Script();
            Assert.That(s.Endings.Select(e => e.Id), Is.EqualTo(Lab.Of("bad", "good", "normal")));
            Assert.That(s.ChoiceCount, Is.EqualTo(1));
            Assert.That(s.TextIds, Is.EqualTo(Lab.Of("start#1", "start#2", "look_key#1", "trust#1")));
            Assert.That(s.Labels["verdict"], Is.EqualTo(s.Instructions.ToList().FindIndex(i => i.Kind == InstructionKind.JumpIf)));
        }

        // ---- 題材固有：存在しないラベル参照の検出 ----

        [Test]
        public void MissingLabelInJumpIsReportedWithLine()
        {
            Lab.AssertError(Lab.Replace(_jumpLine, "-> nowhere"), _jumpLine, "存在しないラベル nowhere");
        }

        [Test]
        public void MissingLabelInChoiceIsReportedWithLine()
        {
            Lab.AssertError(Lab.Replace(_optionLine, "- 鍵を調べる -> look_kye { clue += 1 }"), _optionLine, "存在しないラベル look_kye");
        }

        [Test]
        public void MissingLabelInConditionIsReportedWithLine()
        {
            Lab.AssertError(Lab.Replace(_ifLine, "$if clue >= 1 -> god"), _ifLine, "存在しないラベル god");
        }

        [Test]
        public void EveryMissingLabelIsReported()
        {
            var src = Lab.Replace(_ifLine, "$if clue >= 1 -> god", Lab.Replace(_jumpLine, "-> nowhere"));
            var r = Lab.Compile(src);
            Assert.That(r.Errors.Count(e => e.Message.Contains("存在しないラベル", System.StringComparison.Ordinal)), Is.EqualTo(2), r.Report());
        }

        // ---- 構文 ----

        [Test]
        public void UnknownCommandIsRejected()
        {
            Lab.AssertError(Lab.Replace(_jumpLine, "@wait 10"), _jumpLine, "未知の命令 @wait");
            Lab.AssertError(Lab.Replace(_jumpLine, "$add clue 1"), _jumpLine, "未知の命令 $add");
        }

        [Test]
        public void UnclosedChoiceIsRejected()
        {
            var end = Lab.LineOf("?end");
            var choice = Lab.LineOf("?choice");
            Lab.AssertError(Lab.Replace(end, "# 閉じ忘れ"), choice, "?end で閉じられていません");
        }

        [Test]
        public void EndWithoutChoiceIsRejected()
        {
            Lab.AssertError(Lab.Replace(_jumpLine, "?end"), _jumpLine, "対応する ?choice");
        }

        [Test]
        public void OptionOutsideChoiceIsRejected()
        {
            Lab.AssertError(Lab.Replace(_jumpLine, "- 逃げる -> verdict"), _jumpLine, "?choice と ?end の間");
        }

        [Test]
        public void UnclosedSpeechIsRejected()
        {
            var line = Lab.LineOf("mio「こんばんは」");
            Lab.AssertError(Lab.Replace(line, "mio「こんばんは"), line, "」 で閉じます");
        }

        [Test]
        public void TooFewOrTooManyOptionsAreRejected()
        {
            var choice = Lab.LineOf("?choice");
            var second = Lab.LineOf("- 澪を信じる -> trust { trusted = true }");
            var third = Lab.LineOf("- 何もしない -> verdict");
            var one = Lab.Replace(third, "#", Lab.Replace(second, "#"));
            Lab.AssertError(one, choice, "選択肢は 2〜3 個（1）");
            var four = Lab.Replace(third, "- 何もしない -> verdict\n- 眠る -> verdict");
            Lab.AssertError(four, choice, "選択肢は 2〜3 個（4）");
        }

        // ---- 参照と宣言 ----

        [Test]
        public void DuplicateLabelIsRejected()
        {
            var line = Lab.LineOf("*trust");
            Lab.AssertError(Lab.Replace(line, "*look_key"), line, "ラベル look_key が重複");
        }

        [Test]
        public void UndefinedVariableIsRejected()
        {
            Lab.AssertError(Lab.Replace(_ifLine, "$if clues >= 1 -> good"), _ifLine, "変数 clues は $var で宣言されていません");
            Lab.AssertError(Lab.Replace(_optionLine, "- 鍵を調べる -> look_key { clu += 1 }"), _optionLine, "変数 clu");
        }

        [Test]
        public void UndefinedSpeakerIsRejected()
        {
            var line = Lab.LineOf("mio「ありがとう」");
            Lab.AssertError(Lab.Replace(line, "rin「ありがとう」"), line, "話者 rin は @char で定義されていません");
        }

        [Test]
        public void UnknownAssetsAreRejected()
        {
            var bg = Lab.LineOf("@bg hall");
            Lab.AssertError(Lab.Replace(bg, "@bg cellar"), bg, "背景 cellar は素材にありません");
            var show = Lab.LineOf("@show mio calm left");
            Lab.AssertError(Lab.Replace(show, "@show mio angry left"), show, "mio の表情 angry は素材にありません");
            Lab.AssertError(Lab.Replace(show, "@show mio calm top"), show, "位置 top");
            Lab.AssertError(Lab.Replace(show, "@show kai calm left"), show, "人物 kai は @char で定義されていません");
        }

        [Test]
        public void CharacterWithoutPortraitCannotBeShown()
        {
            var src = Lab.Replace(4, "@char mio 澪 #8fc3e0\n@char kai 海 #ffffff");
            var show = Lab.LineOf("@show mio calm left", src);
            Lab.AssertError(Lab.Replace(show, "@show kai calm left", src), show, "人物 kai の立ち絵は素材にありません");
        }

        [Test]
        public void TypeMismatchIsRejected()
        {
            Lab.AssertError(Lab.Replace(_optionLine, "- 鍵を調べる -> look_key { trusted += 1 }"), _optionLine, "bool の変数 trusted");
            Lab.AssertError(Lab.Replace(_optionLine, "- 鍵を調べる -> look_key { clue = true }"), _optionLine, "int の変数 clue に true/false");
            Lab.AssertError(Lab.Replace(_ifLine, "$if clue -> good"), _ifLine, "int の変数 clue は比較");
            Lab.AssertError(Lab.Replace(_ifLine, "$if trusted >= 1 -> good"), _ifLine, "bool の変数 trusted");
        }

        [Test]
        public void DeclarationsAfterFirstLabelAreRejected()
        {
            var line = Lab.LineOf("*look_key");
            Lab.AssertError(Lab.Replace(line, "$var late = 0\n*look_key"), line, "$var は最初のラベルより前");
        }

        [Test]
        public void StatementBeforeFirstLabelIsRejected()
        {
            Lab.AssertError(Lab.Replace(1, "ラベルより前の文"), 1, "最初のラベル");
        }

        [Test]
        public void DuplicateDeclarationsAreRejected()
        {
            Lab.AssertError(Lab.Replace(1, "$var clue = 1"), 2, "変数 clue が重複");
            var normal = Lab.LineOf("@ending normal 灰");
            Lab.AssertError(Lab.Replace(normal, "@ending good 灰"), normal, "結末 good が重複");
        }

        [Test]
        public void ValueOutOfRangeIsRejected()
        {
            Lab.AssertError(Lab.Replace(_optionLine, "- 鍵を調べる -> look_key { clue += 10000 }"), _optionLine, "±9999");
            Lab.AssertError(Lab.Replace(_optionLine, "- 鍵を調べる -> look_key { clue += 99999999999 }"), _optionLine, "整数か true/false");
        }

        [TestCase("9999", true)]
        [TestCase("-9999", true)]
        [TestCase("10000", false)]
        [TestCase("-10000", false)]
        [TestCase("2147483647", false)]
        public void ConditionLiteralRangeMatchesAssignments(string value, bool ok)
        {
            // 条件の数値も代入と同じ ±9999（レビュー P3-1）。
            var src = Lab.Replace(_ifLine, "$if clue >= 1 -> good\n$if clue == " + value + " -> good");
            if (ok) { Assert.That(Lab.Compile(src).Ok, Is.True, Lab.Compile(src).Report()); }
            else { Lab.AssertError(src, _ifLine + 1, "値は ±9999 まで"); }
        }

        // ---- 到達性と終了性 ----

        [Test]
        public void UnreachableLabelIsRejected()
        {
            var src = Lab.Small + "*orphan\n誰も来ない部屋。\n@ending orphan 孤\n";
            Lab.AssertError(src, Lab.LineOf("誰も来ない部屋。", src), "どこからも到達しません");
        }

        [Test]
        public void FallingOffTheEndIsRejected()
        {
            var normal = Lab.LineOf("@ending normal 灰");
            var src = Lab.Replace(normal, "@ending normal 灰\n*tail\n終わらない話。") + "\n";
            src = Lab.Replace(Lab.LineOf("$if trusted -> normal", src), "$if trusted -> tail", src);
            Lab.AssertError(src, Lab.LineOf("終わらない話。", src), "結末（@ending）も飛び先もありません");
        }

        [Test]
        public void LabelAtTheEndIsRejected()
        {
            var src = Lab.Small + "*empty\n";
            Lab.AssertError(src, Lab.LineOf("*empty", src), "ラベル empty の後に文がありません");
        }

        [Test]
        public void LoopWithTextIsRejected()
        {
            // 文を挟んでも同じ状態に戻るなら終わらない。
            var line = Lab.LineOf("-> verdict");
            Lab.AssertError(Lab.Replace(line, "-> look_key"), Lab.LineOf("鍵は冷たかった。"), "同じ状態に戻る経路");
        }

        [Test]
        public void LoopWithoutTextHitsFlowLimit()
        {
            var line = Lab.LineOf("*verdict");
            var src = Lab.Replace(line, "*verdict\n$set clue += 0\n$if clue < 5 -> verdict");
            var r = Lab.Compile(src);
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Report(), Does.Contain("終わらない流れ"));
        }

        [Test]
        public void CountedLoopTerminates()
        {
            // 変数が進むループは同じ状態に戻らないので受け付ける。
            var line = Lab.LineOf("*verdict");
            var src = Lab.Replace(line, "*verdict\n$set n += 1\n$if n < 5 -> verdict", Lab.Replace(1, "$var n = 0"));
            Assert.That(Lab.Compile(src).Ok, Is.True, Lab.Compile(src).Report());
        }

        [Test]
        public void EndingNoPathReachesIsRejected()
        {
            // 「何もしない」を外すと bad に届かない。
            var third = Lab.LineOf("- 何もしない -> verdict");
            Lab.AssertError(Lab.Replace(third, "#"), Lab.LineOf("@ending bad 闇"), "結末 bad にはどの選び方でも届きません");
        }

        [Test]
        public void ChoiceAndEndingCountsFollowRules()
        {
            var rules = Lab.Rules();
            rules.ChoiceCount = 3;
            rules.EndingCount = 3;
            Lab.AssertError(Lab.Small, 0, "選択肢は 3 箇所（1）", rules);
            rules.ChoiceCount = 1;
            Assert.That(Lab.Compile(Lab.Small, rules).Ok, Is.True);
            rules.EndingCount = 2;
            Lab.AssertError(Lab.Small, 0, "結末は 2 つ（3）", rules);
        }

        // ---- テキスト ----

        [Test]
        public void TextLengthIsBounded()
        {
            var line = Lab.LineOf("扉が閉まる音がした。");
            var rules = Lab.Rules();
            rules.MaxTextLength = 10;
            Assert.That(Lab.Compile(Lab.Small, rules).Ok, Is.True, "ちょうど 10 文字は通る");
            Lab.AssertError(Lab.Replace(line, "扉が閉まる音がした。。"), line, "文は 10 文字まで（11）", rules);
        }

        [Test]
        public void TagsAndUnbalancedBracketsAreRejected()
        {
            var line = Lab.LineOf("扉が閉まる音がした。");
            Lab.AssertError(Lab.Replace(line, "<b>扉</b>が閉まった。"), line, "< > は使えません");
            Lab.AssertError(Lab.Replace(line, "「扉が閉まった。"), line, "「」が対応していません");
            Lab.AssertError(Lab.Replace(line, "扉が\u0007閉まった。"), line, "制御文字");
        }

        [Test]
        public void OptionTextAndNamesAreBounded()
        {
            Lab.AssertError(Lab.Replace(_optionLine, "- " + new string('あ', 25) + " -> look_key"), _optionLine, "選択肢は 24 文字まで");
            Lab.AssertError(Lab.Replace(_optionLine, "-  -> look_key"), _optionLine, "選択肢が空");
            Lab.AssertError(Lab.Replace(4, "@char mio 長い長い長い名前の人物 #8fc3e0"), 4, "話者名は 10 文字まで");
        }

        [Test]
        public void LineCountIsCheckedBeforeParsing()
        {
            var rules = Lab.Rules();
            rules.MaxLines = 5;
            var r = Lab.Compile(Lab.Small, rules);
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Report(), Does.Contain("5 行まで"));
            Assert.That(r.Errors.Count, Is.EqualTo(1), "構文の誤りを数える前に止まる");
        }

        [Test]
        public void InvalidRulesAreRejected()
        {
            var rules = Lab.Rules();
            rules.MaxOptions = 1;
            Assert.That(Lab.Compile(Lab.Small, rules).Report(), Does.Contain("規則"));
        }

        // ---- 指紋 ----

        [Test]
        public void FingerprintIgnoresCommentsButNotText()
        {
            var a = Lab.Script().Fingerprint;
            var b = Lab.Script("# 前置き\n\n" + Lab.Small.Replace("\n*good", "\n# 注\n*good", System.StringComparison.Ordinal)).Fingerprint;
            var line = Lab.LineOf("扉が閉まる音がした。");
            var c = Lab.Script(Lab.Replace(line, "扉が閉まる音がする。")).Fingerprint;
            Assert.That(b, Is.EqualTo(a));
            Assert.That(c, Is.Not.EqualTo(a));
        }

        [Test]
        public void ManyErrorsAreAllReported()
        {
            var sb = new StringBuilder(Lab.Small);
            sb.Append("*extra\nrin「誰？」\n@bg cellar\n-> void\n");
            var r = Lab.Compile(sb.ToString());
            Assert.That(r.Errors.Count, Is.GreaterThanOrEqualTo(3), r.Report());
        }
    }
}
