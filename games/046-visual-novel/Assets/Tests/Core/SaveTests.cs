using System;
using System.Linq;
using Game.Core.Runtime;
using Game.Core.Save;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>クイックセーブと既読の記録の直列化：往復・選択前への復帰・破損と版違いの拒否。</summary>
    public sealed class SaveTests
    {
        private static CompiledScript Script => ScenarioTests.Script;

        private static ScriptRunner AtChoice(int skipChoices = 0)
        {
            var r = new ScriptRunner(Script);
            var seen = 0;
            while (true)
            {
                while (r.Phase == RunnerPhase.Text) { r.Advance(); }
                if (seen++ == skipChoices) { return r; }
                r.Choose(0);
            }
        }

        // ---- 題材固有：ロードで選択前の状態に戻る ----

        [Test]
        public void QuickSaveAtChoiceRestoresBeforeTheChoice()
        {
            var r = AtChoice(1);
            var text = NovelSaveCodec.EncodeQuick(r);
            var before = r.Hash();
            var clue = r.Value("clue");
            r.Choose(0);
            while (r.Phase == RunnerPhase.Text) { r.Advance(); }
            Assert.That(r.Value("clue"), Is.Not.EqualTo(clue), "選んだ後は変数が変わる");

            Assert.That(NovelSaveCodec.TryDecodeQuick(Script, text, out var back, out var error), Is.True, error?.Message);
            Assert.That(back!.Phase, Is.EqualTo(RunnerPhase.Choice));
            Assert.That(back.Hash(), Is.EqualTo(before));
            Assert.That(back.Value("clue"), Is.EqualTo(clue));
            Assert.That(back.Background, Is.EqualTo("corridor"));
            Assert.That(back.Slot(StageSlot.Left).Character, Is.EqualTo(Script.Characters.ToList().FindIndex(c => c.Id == "mio")));
        }

        [Test]
        public void QuickSaveRoundTripsEveryWaitOfOnePath()
        {
            var r = new ScriptRunner(Script);
            var choices = Scenario.RouteTo(ScenarioTests.Compiled, "truth");
            var next = 0;
            while (r.Phase != RunnerPhase.Ended)
            {
                Assert.That(NovelSaveCodec.TryDecodeQuick(Script, NovelSaveCodec.EncodeQuick(r), out var back, out var error), Is.True, error?.Message);
                Assert.That(back!.Hash(), Is.EqualTo(r.Hash()));
                if (r.Phase == RunnerPhase.Text) { r.Advance(); }
                else { r.Choose(choices[next++]); }
            }
        }

        [Test]
        public void LoadedRunnerFollowsTheSameFuture()
        {
            var a = AtChoice(0);
            Assert.That(NovelSaveCodec.TryDecodeQuick(Script, NovelSaveCodec.EncodeQuick(a), out var b, out _), Is.True);
            var rest = new[] { 1, 1, 0 };
            Assert.That(Lab.Play(Script, rest).Hash(), Is.Not.EqualTo(0));
            foreach (var runner in new[] { a, b! })
            {
                var next = 0;
                while (runner.Phase != RunnerPhase.Ended)
                {
                    if (runner.Phase == RunnerPhase.Text) { runner.Advance(); }
                    else { runner.Choose(rest[next++]); }
                }
            }

            Assert.That(b!.Hash(), Is.EqualTo(a.Hash()));
            Assert.That(Script.Endings[a.EndingIndex].Id, Is.EqualTo("truth"));
        }

        [Test]
        public void SavingAfterEndingIsNotAllowed()
        {
            var r = Lab.Play(Script, Scenario.RouteTo(ScenarioTests.Compiled, "bad").ToArray());
            Assert.Throws<InvalidOperationException>(() => NovelSaveCodec.EncodeQuick(r));
        }

        // ---- 破損・部分データ・版の違い ----

        [Test]
        public void EveryTruncationIsRejected()
        {
            var text = NovelSaveCodec.EncodeQuick(AtChoice(1));
            for (int len = 0; len < text.Length; len++)
            {
                Assert.That(NovelSaveCodec.TryDecodeQuick(Script, text.Substring(0, len), out var r, out var error), Is.False, $"{len} 文字で切った");
                Assert.That(r, Is.Null);
                Assert.That(error, Is.Not.Null);
            }
        }

        [Test]
        public void EverySingleCharacterChangeIsRejected()
        {
            var text = NovelSaveCodec.EncodeQuick(AtChoice(1));
            for (int i = 0; i < text.Length; i++)
            {
                var c = text[i] == '0' ? '1' : '0';
                var broken = text.Substring(0, i) + c + text.Substring(i + 1);
                Assert.That(NovelSaveCodec.TryDecodeQuick(Script, broken, out _, out _), Is.False, $"{i} 文字目を変えた");
            }
        }

        [Test]
        public void VersionAndFingerprintMismatchAreRejected()
        {
            var text = NovelSaveCodec.EncodeQuick(AtChoice(1));
            var v2 = SaveText.Sign(Body(text).Replace("version=1\n", "version=2\n", StringComparison.Ordinal));
            AssertReject(v2, SaveErrorKind.Version);

            var other = Lab.Script();
            Assert.That(NovelSaveCodec.TryDecodeQuick(other, text, out _, out var error), Is.False);
            Assert.That(error!.Kind, Is.EqualTo(SaveErrorKind.Fingerprint));
            Assert.That(error.Message, Does.Contain("シナリオが保存したときと違います"));
        }

        [Test]
        public void SemanticallyWrongContentIsRejected()
        {
            var body = Body(NovelSaveCodec.EncodeQuick(AtChoice(1)));
            var pcLine = body.Split('\n').First(l => l.StartsWith("pc=", StringComparison.Ordinal));
            AssertReject(SaveText.Sign(body.Replace(pcLine + "\n", "pc=0\n", StringComparison.Ordinal)), SaveErrorKind.Invalid, "文でも選択肢でもありません");
            AssertReject(SaveText.Sign(body.Replace(pcLine + "\n", "pc=99999\n", StringComparison.Ordinal)), SaveErrorKind.Invalid, "スクリプトの外");
            AssertReject(SaveText.Sign(body.Replace("var.tracks=", "var.tracks=7\nx=", StringComparison.Ordinal)), SaveErrorKind.Invalid, "キーの数");
            AssertReject(SaveText.Sign(body.Replace("var.clue=", "var.clue=9999", StringComparison.Ordinal)), SaveErrorKind.Invalid, "clue");
            AssertReject(SaveText.Sign(body.Replace("var.diary=", "var.diary=2", StringComparison.Ordinal)), SaveErrorKind.Invalid, "diary");
            AssertReject(SaveText.Sign(body.Replace("slot.left=mio:", "slot.left=kai:", StringComparison.Ordinal)), SaveErrorKind.Invalid, "立ち絵 kai");
            AssertReject(SaveText.Sign(body.Replace("bg=corridor", "bg=cellar", StringComparison.Ordinal)), SaveErrorKind.Invalid, "背景 cellar");
            AssertReject(SaveText.Sign(body.Replace("pc=", "pc=+", StringComparison.Ordinal)), SaveErrorKind.Syntax);
            AssertReject(SaveText.Sign(body + "pc=1\n"), SaveErrorKind.DuplicateKey);
            AssertReject(SaveText.Sign(body.Replace("kind=quick", "kind=read", StringComparison.Ordinal)), SaveErrorKind.Invalid);
            AssertReject("", SaveErrorKind.Empty);
        }

        // ---- 既読の記録 ----

        [Test]
        public void ReadLogRoundTrips()
        {
            var log = new ReadLog(Script);
            log.MarkRead(Script.TextIds[0]);
            log.MarkRead(Script.TextIds[5]);
            log.MarkEnding(Script.EndingIndex("bad"));
            Assert.That(NovelSaveCodec.TryDecodeRead(Script, NovelSaveCodec.EncodeRead(log), out var back, out var error), Is.True, error?.Message);
            Assert.That(back!.ReadCount, Is.EqualTo(2));
            Assert.That(back.IsRead(Script.TextIds[5]), Is.True);
            Assert.That(back.IsRead(Script.TextIds[1]), Is.False);
            Assert.That(back.Reached(Script.EndingIndex("bad")), Is.True);
            Assert.That(back.Reached(Script.EndingIndex("truth")), Is.False);
        }

        [Test]
        public void ReadLogRejectsUnknownOrDuplicateIdsAndCorruption()
        {
            var log = new ReadLog(Script);
            log.MarkRead(Script.TextIds[0]);
            var text = NovelSaveCodec.EncodeRead(log);
            var body = Body(text);
            AssertReadReject(SaveText.Sign(body.Replace("read.0=" + Script.TextIds[0], "read.0=nowhere#1", StringComparison.Ordinal)), "nowhere#1");
            AssertReadReject(SaveText.Sign(body.Replace("read.count=1", "read.count=2", StringComparison.Ordinal) + "read.1=" + Script.TextIds[0] + "\n"), "重複");
            AssertReadReject(SaveText.Sign(body.Replace("ending.bad=0", "ending.bad=2", StringComparison.Ordinal)), "0 か 1");
            AssertReadReject(text.Substring(0, text.Length - 3), "切れて");
            Assert.That(NovelSaveCodec.TryDecodeRead(Lab.Script(), text, out _, out var e), Is.False);
            Assert.That(e!.Kind, Is.EqualTo(SaveErrorKind.Fingerprint));
            Assert.Throws<ArgumentException>(() => log.MarkRead("nowhere#1"));
        }

        private static string Body(string signed) => signed.Substring(0, signed.LastIndexOf("checksum=", StringComparison.Ordinal));

        private static void AssertReject(string text, SaveErrorKind kind, string fragment = "")
        {
            Assert.That(NovelSaveCodec.TryDecodeQuick(Script, text, out var r, out var error), Is.False, fragment);
            Assert.That(r, Is.Null);
            Assert.That(error!.Kind, Is.EqualTo(kind), error.Message);
            Assert.That(error.Message, Does.Contain(fragment));
        }

        private static void AssertReadReject(string text, string fragment)
        {
            Assert.That(NovelSaveCodec.TryDecodeRead(Script, text, out var log, out var error), Is.False, fragment);
            Assert.That(log, Is.Null);
            Assert.That(error!.Message, Does.Contain(fragment));
        }
    }
}
