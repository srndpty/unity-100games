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

        private static string Text(Summer s) => SummerSaveCodec.Encode(B, s.Snapshot());

        private static string Body(string text) => text.Substring(0, text.IndexOf("checksum=", StringComparison.Ordinal));

        [Test]
        public void RoundTrip_RestoresTheSameMorning()
        {
            for (int day = 1; day <= B.Days; day++)
            {
                var s = Morning(day);
                Assert.That(SummerSaveCodec.TryDecode(B, Text(s), out var r, out var error), Is.True, error?.ToString());
                Assert.That(r!.Hash(), Is.EqualTo(s.Hash()), $"day {day}");
                Assert.That(r.Log, Is.EqualTo(s.Log));
            }
        }

        [Test]
        public void EveryTruncation_IsRejected()
        {
            var text = Text(Morning(5));
            for (int n = 0; n < text.Length; n++)
            {
                Assert.That(SummerSaveCodec.TryDecode(B, text.Substring(0, n), out var r, out var error), Is.False, $"{n} 文字で切った");
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
                Assert.That(SummerSaveCodec.TryDecode(B, bad, out _, out _), Is.False, $"{i} 文字目を変えた");
            }
        }

        [Test]
        public void OtherVersionOrBook_IsRejectedWithReason()
        {
            var text = Text(Morning(3));
            var other = Lab.Compile(Lab.Small + "\n# 別の台本\n");
            Assert.That(SummerSaveCodec.TryDecode(other, text, out _, out var e1), Is.False);
            Assert.That(e1!.Kind, Is.EqualTo(SaveErrorKind.Fingerprint));

            var body = Body(text).Replace("version=1", "version=2", StringComparison.Ordinal);
            Assert.That(SummerSaveCodec.TryDecode(B, SaveText.Sign(body), out _, out var e2), Is.False);
            Assert.That(e2!.Kind, Is.EqualTo(SaveErrorKind.Version));
        }

        [TestCase("stat.study=", "stat.study=101", "範囲外")]
        [TestCase("flag.met=", "flag.met=2", "0 か 1")]
        [TestCase("day=", "day=99", "範囲外")]
        [TestCase("log.0=", "log.0=1:action:nothing:0", "不正")]
        [TestCase("log.0=", "log.0=9:action:meet:0", "記録の日")]
        [TestCase("log.0=", "log.0=1:action:meet:7", "記録の値")]
        public void SignedButInvalidValues_AreRejected(string key, string replacement, string message)
        {
            var lines = Body(Text(Morning(3))).Split('\n').ToList();
            var i = lines.FindIndex(l => l.StartsWith(key, StringComparison.Ordinal));
            Assert.That(i, Is.GreaterThanOrEqualTo(0), key);
            lines[i] = replacement;
            Assert.That(SummerSaveCodec.TryDecode(B, SaveText.Sign(string.Join("\n", lines)), out _, out var error), Is.False);
            Assert.That(error!.Message.Contains(message, StringComparison.Ordinal), Is.True, error.Message);
        }

        [Test]
        public void ExtraOrMissingKeys_AreRejected()
        {
            var body = Body(Text(Morning(2)));
            Assert.That(SummerSaveCodec.TryDecode(B, SaveText.Sign(body + "extra=1\n"), out _, out _), Is.False);
            var missing = string.Join("\n", body.Split('\n').Where(l => !l.StartsWith("flag.met=", StringComparison.Ordinal)));
            Assert.That(SummerSaveCodec.TryDecode(B, SaveText.Sign(missing), out _, out _), Is.False);
        }
    }
}
