using System;
using System.Linq;
using Game.Core.Data;
using Game.Core.Play;
using Game.Core.Save;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class SaveCodecTests
    {
        private static SummerBook B => Lab.SmallBook;

        private static Summer Morning(int day, int seed = 7)
        {
            var s = new Summer(B, seed);
            while (s.Day < day) { Lab.Step(s, Lab.Prefer("meet", "study")); }
            return s;
        }

        private static string Text(Summer s, int playMs = 12345) => SummerSaveCodec.Encode(B, s.Snapshot(), playMs);

        private static string Body(string text) => text.Substring(0, text.IndexOf("checksum=", StringComparison.Ordinal));

        private static bool Decode(string text, out Summer? s, out SaveError? error) => SummerSaveCodec.TryDecode(B, text, out s, out _, out error);

        /// <summary>本文の 1 行を置き換えて署名し直す（チェックサムは合うが中身が違うセーブ）。</summary>
        private static string Resigned(string text, string key, string replacement)
        {
            var lines = Body(text).Split('\n').ToList();
            var i = lines.FindIndex(l => l.StartsWith(key, StringComparison.Ordinal));
            Assert.That(i, Is.GreaterThanOrEqualTo(0), key);
            lines[i] = replacement;
            return SaveText.Sign(string.Join("\n", lines));
        }

        [Test]
        public void RoundTrip_RestoresTheSameMorning_AndPlayTime()
        {
            for (int day = 1; day <= B.Days; day++)
            {
                var s = Morning(day);
                Assert.That(SummerSaveCodec.TryDecode(B, Text(s, day * 1000), out var r, out var ms, out var error), Is.True, error?.ToString());
                Assert.That(r!.Hash(), Is.EqualTo(s.Hash()), $"day {day}");
                Assert.That(r.Log, Is.EqualTo(s.Log));
                Assert.That(ms, Is.EqualTo(day * 1000));
            }
        }

        [Test]
        public void EveryTruncation_IsRejected()
        {
            var text = Text(Morning(5));
            for (int n = 0; n < text.Length; n++)
            {
                Assert.That(Decode(text.Substring(0, n), out var r, out var error), Is.False, $"{n} 文字で切った");
                Assert.That(r, Is.Null);
                Assert.That(error, Is.Not.Null);
            }
        }

        [Test]
        public void EveryOneCharChange_IsRejected()
        {
            var text = Text(Morning(5));
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n') { continue; }
                var c = text[i] == '1' ? '2' : '1';
                var bad = text.Substring(0, i) + c + text.Substring(i + 1);
                Assert.That(Decode(bad, out _, out _), Is.False, $"{i} 文字目を変えた");
            }
        }

        [Test]
        public void OtherVersionOrBook_IsRejectedWithReason()
        {
            var text = Text(Morning(3));
            var other = Lab.Compile(Lab.Small + "\n# 別の台本\n");
            Assert.That(SummerSaveCodec.TryDecode(other, text, out _, out _, out var e1), Is.False);
            Assert.That(e1!.Kind, Is.EqualTo(SaveErrorKind.Fingerprint));

            Assert.That(Decode(Resigned(text, "version=", "version=1"), out _, out var e2), Is.False, "版 1（遊んだ時間のない形式）は読まない");
            Assert.That(e2!.Kind, Is.EqualTo(SaveErrorKind.Version));
        }

        [TestCase("stat.study=", "stat.study=101", "範囲外")]
        [TestCase("flag.met=", "flag.met=2", "0 か 1")]
        [TestCase("day=", "day=99", "範囲外")]
        [TestCase("playms=", "playms=-1", "遊んだ時間")]
        [TestCase("playms=", "playms=359999001", "遊んだ時間")]
        [TestCase("log.0=", "log.0=1:action:nothing:0", "不正")]
        [TestCase("log.0=", "log.0=9:action:meet:0", "記録の日")]
        [TestCase("log.0=", "log.0=1:action:meet:7", "記録の値")]
        public void SignedButInvalidValues_AreRejected(string key, string replacement, string message)
        {
            Assert.That(Decode(Resigned(Text(Morning(3)), key, replacement), out _, out var error), Is.False);
            Assert.That(error!.Message.Contains(message, StringComparison.Ordinal), Is.True, error.Message);
        }

        /// <summary>範囲の中だが、記録を再生した状態と食い違うセーブ（レビュー P2-2）。</summary>
        [TestCase("stat.study=", "stat.study=99", "一致しません", TestName = "Stat_NotReachedByTheLog")]
        [TestCase("flag.met=", "flag.met=0", "一致しません", TestName = "Flag_DiffersFromTheLog")]
        [TestCase("done.e_first=", "done.e_first=0", "一致しません", TestName = "Done_DiffersFromTheLog")]
        [TestCase("day=", "day=6", "行動がありません", TestName = "Day_WithoutTheActionsOfThePreviousDays")]
        [TestCase("seed=", "seed=8", "", TestName = "OtherSeed_GivesOtherOutcomes")]
        public void SignedAndInRange_ButInconsistentWithTheLog_IsRejected(string key, string replacement, string message)
        {
            var s = Morning(4);
            Assert.That(s.Flag(B.FlagIndex("met")), Is.True, "前提：出会いの選択でフラグが立っている");
            Assert.That(Decode(Resigned(Text(s), key, replacement), out var r, out var error), Is.False);
            Assert.That(r, Is.Null);
            Assert.That(error!.Message.Contains(message, StringComparison.Ordinal), Is.True, error.Message);
        }

        [Test]
        public void LogWithAMissingDay_OrADoubledAction_IsRejected()
        {
            var text = Text(Morning(4));
            var body = Body(text);
            var lines = body.Split('\n').ToList();
            var actions = lines.Where(l => l.StartsWith("log.", StringComparison.Ordinal) && l.Contains(":action:", StringComparison.Ordinal)).ToList();
            Assert.That(actions.Count, Is.EqualTo(3));

            // 2 日目の行動を 1 日目の行動に書き換える（同じ日に 2 回行動した記録）。
            var i = lines.IndexOf(actions[1]);
            var parts = actions[1].Split('=');
            lines[i] = parts[0] + "=1" + parts[1].Remove(0, parts[1].IndexOf(':', StringComparison.Ordinal));
            Assert.That(Decode(SaveText.Sign(string.Join("\n", lines)), out _, out var error), Is.False);
            Assert.That(error!.Message, Does.Contain("2 日目の行動がありません").Or.Contain("一致しません"));
        }

        [Test]
        public void ChoiceWithoutTheEvent_IsRejected()
        {
            // 出会いのイベントの選択を、選んでいない値に変える（イベントの選択の記録と状態が合わない）。
            var text = Text(Morning(4));
            var line = Body(text).Split('\n').First(l => l.Contains(":choice:e_first:", StringComparison.Ordinal));
            var key = line.Substring(0, line.IndexOf('=') + 1);
            var changed = line.Remove(line.Length - 1) + (line.EndsWith(":0", StringComparison.Ordinal) ? "1" : "0");
            Assert.That(Decode(Resigned(text, key, changed), out _, out var error), Is.False);
            Assert.That(error!.Message, Does.Contain("一致しません"));
        }

        [Test]
        public void ExtraOrMissingKeys_AreRejected()
        {
            var body = Body(Text(Morning(2)));
            Assert.That(Decode(SaveText.Sign(body + "extra=1\n"), out _, out _), Is.False);
            var missing = string.Join("\n", body.Split('\n').Where(l => !l.StartsWith("flag.met=", StringComparison.Ordinal)));
            Assert.That(Decode(SaveText.Sign(missing), out _, out _), Is.False);
        }

        [Test]
        public void PlayTime_OutOfRange_CannotBeEncoded() =>
            Assert.Throws<ArgumentOutOfRangeException>(() => SummerSaveCodec.Encode(B, Morning(1).Snapshot(), SummerSaveCodec.MaxPlayMilliseconds + 1));
    }
}
