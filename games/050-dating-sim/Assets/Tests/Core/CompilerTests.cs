using System;
using System.Linq;
using Game.Core.Data;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class CompilerTests
    {
        private static readonly string[] _statIds = { "study", "fatigue", "aoi" };
        private static readonly string[] _hints = { "一つ目", "二つ目" };

        [Test]
        public void Small_Compiles_WithDeclaredTables()
        {
            var b = Lab.SmallBook;
            Assert.That(b.Days, Is.EqualTo(8));
            Assert.That(b.Stats.Select(s => s.Id), Is.EqualTo(_statIds));
            Assert.That(b.Stats[b.StatIndex("aoi")].IsHeart, Is.True);
            Assert.That(b.Characters[0].Heart, Is.EqualTo(b.StatIndex("aoi")));
            Assert.That(b.Stats[b.StatIndex("fatigue")].Cost, Is.True);
            Assert.That(b.Actions.Count, Is.EqualTo(3));
            Assert.That(b.Events.Count, Is.EqualTo(4));
            Assert.That(b.Exclusive(b.EventIndex("e_c"), b.EventIndex("e_a")), Is.True);
            Assert.That(b.Exclusive(b.EventIndex("e_a"), b.EventIndex("e_c")), Is.True, "排他は対称");
            Assert.That(b.Exclusive(b.EventIndex("e_a"), b.EventIndex("e_b")), Is.False);
            Assert.That(b.EndingsByPriority.Last().IsDefault, Is.True);
            var first = b.Events[b.EventIndex("e_first")];
            Assert.That(first.Choices.Count, Is.EqualTo(2));
            Assert.That(first.Lines[1].Face, Is.EqualTo(1), "aoi:smile");
            Assert.That(b.Events[b.EventIndex("e_c")].Lines[0].Background, Is.EqualTo(1), "@bg park は次の行から");
            Assert.That(b.WeekdayOf(7), Is.EqualTo(Weekday.Sun));
            Assert.That(b.Actions[b.ActionIndex("meet")].AvailableOn(Weekday.Sun), Is.False);
        }

        [Test]
        public void Fingerprint_ChangesWithSource()
        {
            Assert.That(Lab.Compile(Lab.Small + "\n# コメント\n").Fingerprint, Is.Not.EqualTo(Lab.SmallBook.Fingerprint));
            Assert.That(Lab.Compile(Lab.Small).Fingerprint, Is.EqualTo(Lab.SmallBook.Fingerprint));
        }

        // ---- 構文 ----

        [TestCase("@frobnicate x", "未知の文")]
        [TestCase("aoi「浮いた台詞」", "塊の中に書きます")]
        public void Syntax_TopLevel(string extra, string message) => Lab.AssertError(Lab.Small + "\n" + extra + "\n", message);

        [Test]
        public void Syntax_UnclosedBlock() => Lab.AssertError(Lab.Small + "\n@event e_x slot night day 1..8 priority 1 chance 100\nなにか\n", "@end で閉じていません");

        [Test]
        public void Syntax_ChoiceCount()
        {
            Lab.AssertError(Lab.Replace("- …… : aoi += 1\n", ""), "選択肢は 2〜3 個です（1 個）");
            Lab.AssertError(Lab.Replace("- …… : aoi += 1\n", "- a\n- b\n- c\n"), "選択肢は 2〜3 個です（4 個）");
        }

        [Test]
        public void Syntax_TextAfterChoice() => Lab.AssertError(Lab.Replace("- …… : aoi += 1\n", "- …… : aoi += 1\naoi「あとから」\n"), "選択肢の後に会話は書けません");

        [Test]
        public void Syntax_TextTooLong() => Lab.AssertError(Lab.Replace("ひとりの夏だった。", new string('あ', SummerLimits.MaxTextLength + 1)), "字です");

        // ---- 名前と型 ----

        [TestCase("when aoi >= 1", "when aio >= 1", "宣言されていない能力値")]
        [TestCase("when aoi >= 30 and met", "when aoi >= 30 and mett", "宣言されていないフラグ")]
        [TestCase("when aoi >= 30 and met", "when aoi >= 30 and met >= 1", "フラグ met は数と比べられません")]
        [TestCase("when aoi >= 30 and met", "when aoi >= 30 and aoi", "比較が要ります")]
        [TestCase("when action == meet", "when action == study_x", "行動 study_x がありません")]
        [TestCase("when action == meet", "when action >= meet", "== か != で比べます")]
        [TestCase("when action == meet", "when weekday == funday", "曜日（mon〜sun）ではありません")]
        [TestCase("when aoi >= 1", "when (aoi >= 1", "括弧が閉じていません")]
        [TestCase("aoi:smile「よろしくね」", "aoi:angry「よろしくね」", "表情 angry はありません")]
        [TestCase("aoi「はじめまして」", "ren「はじめまして」", "話者 ren が宣言されていません")]
        [TestCase("@bg park\n散歩", "@bg forest\n散歩", "背景 forest が宣言されていません")]
        [TestCase("- よろしく : aoi += 5 ; set met", "- よろしく : aoi += 5 ; set mte", "フラグ mte が宣言されていません")]
        [TestCase("- よろしく : aoi += 5 ; set met", "- よろしく : aoi += 3..5", "幅（a..b）は @action の効果だけ")]
        [TestCase("@flag met", "@flag study", "名前 study が重複しています")]
        [TestCase("@flag met", "@flag day", "予約語")]
        [TestCase("@exclusive e_c e_a", "@exclusive e_c e_z", "イベント e_z がありません")]
        public void Names_AndTypes(string from, string to, string message) => Lab.AssertError(Lab.Replace(from, to), message);

        [Test]
        public void ActionId_CanShareNameWithStat()
        {
            // 行動の ID は能力値と同じでもよい（action と比べる側だけ行動として解決する）。
            var b = Lab.Compile(Lab.Replace("when action == meet", "when action == study"));
            var e = b.Events[b.EventIndex("e_first")];
            Assert.That(e.When!.Right.Value, Is.EqualTo(b.ActionIndex("study")));
        }

        // ---- 範囲・数 ----

        [TestCase("@stat study 学力 0..100 = 10", "@stat study 学力 0..100 = 101", "初期値 101 が範囲 0..100 の外")]
        [TestCase("@stat study 学力 0..100 = 10", "@stat study 学力 100..0 = 10", "最小 < 最大")]
        [TestCase("@action study 勉強 : study += 3..5", "@action study 勉強 : study += 5..3", "逆向き")]
        [TestCase("@action study 勉強 : study += 3..5", "@action study 勉強 : study += -3", "-= を使います")]
        [TestCase("priority 10 chance 100 when aoi >= 1\n葵から", "priority 10 chance 0 when aoi >= 1\n葵から", "chance は 1〜100")]
        [TestCase("@event e_c slot after day 1..8", "@event e_c slot after day 1..9", "day は 1..8 の中")]
        [TestCase("@event e_c slot after day 1..8", "@event e_c slot noon day 1..8", "slot は after か night")]
        [TestCase("@ending alone ひとりの夏 default", "@ending alone ひとりの夏 priority 10 when met", "priority 10 が重複")]
        [TestCase("@ending alone ひとりの夏 default", "@ending alone ひとりの夏 priority 5 when met", "既定の ED（default）はちょうど 1 つです（0 個）")]
        [TestCase("@outcome great 大成功 x200 weight 20", "@outcome great 大成功 x500 weight 20", "倍率は 0〜400")]
        [TestCase("@calendar 8 mon", "@calendar 8 noday", "@calendar は")]
        public void Ranges(string from, string to, string message) => Lab.AssertError(Lab.Replace(from, to), message);

        [Test]
        public void Hints_AreKeptInOrder_AndChecked()
        {
            var b = Lab.Compile(Lab.Small + "\n@hint 一つ目\n@hint 二つ目 # コメント\n");
            Assert.That(b.Hints, Is.EqualTo(_hints));
            Assert.That(Lab.SmallBook.Hints, Is.Empty);
            Lab.AssertError(Lab.Small + "\n@hint\n", "@hint の文がありません");
            Lab.AssertError(Lab.Small + "\n@hint " + new string('あ', SummerLimits.MaxTextLength + 1) + "\n", "字です");
            Lab.AssertError(Lab.Small + string.Concat(Enumerable.Repeat("\n@hint ひとこと", SummerLimits.MaxHints + 1)) + "\n", $"@hint は {SummerLimits.MaxHints} 個まで");
        }

        [Test]
        public void Forced_IsRejectedOnlyWhenItHoldsOnEveryMorning()
        {
            // 最終日（8 日目）は月曜。最終日だけで判定すると、月曜だけ休む台本まで拒否してしまう。
            Lab.Compile(Lab.Replace("@forced rest when fatigue >= 100", "@forced rest when weekday == mon"));
            Lab.Compile(Lab.Replace("@forced rest when fatigue >= 100", "@forced rest when day >= 5"));

            // 全ての朝で必ず成り立つ条件は拒否する。
            Lab.AssertError(Lab.Replace("@forced rest when fatigue >= 100", "@forced rest when day >= 1"), "全ての朝で必ず成り立ち");
            Lab.AssertError(Lab.Replace("@forced rest when fatigue >= 100", "@forced rest when fatigue >= 0"), "全ての朝で必ず成り立ち");
        }

        [Test]
        public void DayWithoutAnySelectableAction_IsRejected()
        {
            // 全ての行動を月〜土にすると、7 日目（日曜）に選べる行動がなく先へ進めない。
            var s = Lab.Replace("@action study 勉強 :", "@action study 勉強 week mon..sat :")
                .Replace("@action rest 休む bg room", "@action rest 休む week mon..sat bg room", StringComparison.Ordinal);
            Lab.AssertError(s, "7 日目（sun）に選べる行動がありません");

            // 休むだけを毎日選べるなら通る（強制の行動には頼らない）。
            Lab.Compile(Lab.Replace("@action study 勉強 :", "@action study 勉強 week mon..sat :"));

            // 6 日しかない暦なら日曜は来ないので通る。
            Lab.Compile(s.Replace("@calendar 8 mon", "@calendar 6 mon", StringComparison.Ordinal).Replace("day 1..8", "day 1..6", StringComparison.Ordinal));
        }

        [Test]
        public void TwoDefaultEndings_AreRejected() =>
            Lab.AssertError(Lab.Small + "\n@ending other 別 default\n別\n@end\n", "ちょうど 1 つです（2 個）");

        [Test]
        public void Weights_ThatCanAllBeZero_AreRejected()
        {
            var s = Lab.Replace("@weight bad 80 when fatigue >= 80", "@weight bad 0 when fatigue >= 80\n@weight good 0 when fatigue >= 80\n@weight great 0 when fatigue >= 80");
            Lab.AssertError(s, "重みの合計が 0 になりうります");
        }

        [Test]
        public void LineLimit_IsCheckedBeforeExpanding()
        {
            var huge = string.Concat(Enumerable.Repeat("# 行\n", SummerLimits.MaxLines + 1));
            Lab.AssertError(huge, "行まで");
        }

        // ---- 区間の検証 ----

        [Test]
        public void Range_RejectsEventConditionNeverTrue()
        {
            // 学力は 1 日に高々 5 × 2 = 10 増える。3 日目までに 90 以上にはならない。
            Lab.AssertError(Lab.Replace("@event e_c slot after day 1..8 priority 5 chance 50", "@event e_c slot after day 1..3 priority 5 chance 50 when study >= 90"),
                "e_c の条件は、取りうる能力値の範囲ではどの日にも成り立ちません");
            Lab.Compile(Lab.Replace("@event e_c slot after day 1..8 priority 5 chance 50", "@event e_c slot after day 1..8 priority 5 chance 50 when study >= 90"));
        }

        [Test]
        public void Range_RejectsEventWithNoOpenDay() =>
            Lab.AssertError(Lab.Replace("@event e_c slot after day 1..8", "@event e_c slot after day 1..6 week sun"), "出る日がありません");

        [Test]
        public void Range_RejectsEndingNeverTrue_AndEndingAlwaysTrue()
        {
            Lab.AssertError(Lab.Replace("when aoi >= 30 and met", "when aoi >= 30 and study >= 100 and fatigue < 0"), "ED aoi の条件は、取りうる能力値の範囲では成り立ちません");
            Lab.AssertError(Lab.Replace("when aoi >= 30 and met", "when day >= 1"), "必ず成り立つので、優先度の低い ED に届きません");
        }

        [Test]
        public void Range_FlagNeverSet_IsAlwaysFalse()
        {
            var s = Lab.Replace("@flag met", "@flag met\n@flag never");
            Lab.AssertError(s.Replace("when aoi >= 30 and met", "when aoi >= 30 and never", StringComparison.Ordinal), "成り立ちません");
        }

        [Test]
        public void StatBounds_ContainTheRealRange()
        {
            var b = Lab.SmallBook;
            var bounds = new StatBounds(b);
            var study = b.StatIndex("study");
            Assert.That(bounds.StatOn(study, 0), Is.EqualTo((10, 10)));
            Assert.That(bounds.StatOn(study, 1), Is.EqualTo((10, 20)), "勉強 5 × 大成功 200 %");
            Assert.That(bounds.StatOn(b.StatIndex("fatigue"), 2), Is.EqualTo((0, 60)), "疲労は費用なので倍率を掛けない");
            Assert.That(bounds.StatOn(b.StatIndex("aoi"), 8), Is.EqualTo((0, 100)), "宣言の範囲で切る");
            Assert.That(bounds.FlagPossible(b.FlagIndex("met")), Is.True);
        }
    }
}
