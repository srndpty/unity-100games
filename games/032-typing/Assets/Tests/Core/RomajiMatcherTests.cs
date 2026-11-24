using System.Linq;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class RomajiMatcherTests
    {
        private static RomajiMatcher Of(string kana) => new(Reading.Parse(kana));

        [Test]
        public void 全50語で照合器の打ち方が参照実装と一致する()
        {
            foreach (var entry in TestKit.Production().Entries)
            {
                AssertSameLanguage(entry.Reading.Kana);
            }
        }

        [TestCase("かんい")]
        [TestCase("かんな")]
        [TestCase("ほんや")]
        [TestCase("きっぷ")]
        [TestCase("まっちゃ")]
        [TestCase("えっあ")]
        [TestCase("んっか")]
        [TestCase("っっか")]
        [TestCase("ちぇっくいん")]
        [TestCase("きゃっしゅ")]
        [TestCase("らーめん")]
        [TestCase("ふぁん")]
        [TestCase("ゃゅょ")]
        [TestCase("んんん")]
        public void 境界の読みでも参照実装と一致する(string kana) => AssertSameLanguage(kana);

        [TestCase("かんたん", "kantann")]
        [TestCase("かんたん", "kanntann")]
        [TestCase("かんたん", "kaxntaxn")]
        [TestCase("かんい", "kanni")]
        [TestCase("かんい", "kannyi")]
        [TestCase("かんな", "kannna")]
        [TestCase("ほんや", "honnya")]
        [TestCase("きって", "kitte")]
        [TestCase("きって", "kixtute")]
        [TestCase("きって", "kiltsute")]
        [TestCase("まっちゃ", "maccha")]
        [TestCase("まっちゃ", "mattya")]
        [TestCase("しゃしん", "shashinn")]
        [TestCase("しゃしん", "syasinn")]
        [TestCase("しゃしん", "shixyacinn")]
        [TestCase("ちゃや", "chilyaya")]
        [TestCase("らーめん", "ra-menn")]
        [TestCase("ふね", "hune")]
        [TestCase("ぎんいろ", "ginniro")]
        public void いろいろな綴りで打ち切れる(string kana, string keys)
        {
            var matcher = Of(kana);
            Assert.That(TestKit.TypeAll(matcher, keys), Is.EqualTo(KeyResult.Completed));
            Assert.That(matcher.IsComplete, Is.True);
            Assert.That(matcher.Typed, Is.EqualTo(keys));
            Assert.That(matcher.Guide, Is.Empty);
        }

        [TestCase("かんい", "kani", 3)]
        [TestCase("かんな", "kanna", 4)]
        [TestCase("ほんや", "honya", 3)]
        [TestCase("えっあ", "eaa", 1)]
        [TestCase("まっちゃ", "matcha", 3)]
        [TestCase("かん", "kan", -1)]
        [TestCase("らーめん", "ramenn", 2)]
        public void 規則で許さない綴りは途中で誤字になる(string kana, string keys, int missAt)
        {
            var matcher = Of(kana);
            for (var i = 0; i < keys.Length; i++)
            {
                var result = matcher.Type(keys[i]);
                if (i == missAt)
                {
                    Assert.That(result, Is.EqualTo(KeyResult.Miss), $"{i} 文字目");
                    Assert.That(matcher.Typed, Is.EqualTo(keys.Substring(0, i)));
                    return;
                }

                Assert.That(result, Is.EqualTo(KeyResult.Correct), $"{i} 文字目");
            }

            // missAt が -1：全て受け付けるが打ち終わらない（語末の「ん」は n 1 つでは終わらない）。
            Assert.That(matcher.IsComplete, Is.False);
            Assert.That(matcher.Type('n'), Is.EqualTo(KeyResult.Completed));
        }

        [Test]
        public void 誤字は道筋も打った文字列も案内も変えない_全50語の全ての位置()
        {
            foreach (var entry in TestKit.Production().Entries)
            {
                var matcher = new RomajiMatcher(entry.Reading);
                foreach (var key in entry.Reading.DefaultSpelling)
                {
                    var typed = matcher.Typed;
                    var guide = matcher.Guide;
                    var accepted = RomajiTable.Keys.Where(matcher.Accepts).ToArray();
                    foreach (var wrong in RomajiTable.Keys.Where(k => !matcher.Accepts(k)))
                    {
                        Assert.That(matcher.Type(wrong), Is.EqualTo(KeyResult.Miss));
                        Assert.That(matcher.Typed, Is.EqualTo(typed));
                        Assert.That(matcher.Guide, Is.EqualTo(guide));
                        Assert.That(RomajiTable.Keys.Where(matcher.Accepts).ToArray(), Is.EqualTo(accepted));
                    }

                    Assert.That(matcher.Type(key), Is.Not.EqualTo(KeyResult.Miss), $"{entry}：{matcher.Typed}+{key}");
                }

                Assert.That(matcher.IsComplete, Is.True, entry.ToString());
            }
        }

        [Test]
        public void 案内は打った綴りに合わせて変わる()
        {
            var matcher = Of("しかく");
            Assert.That(matcher.Guide, Is.EqualTo("shikaku"));
            matcher.Type('s');
            Assert.That(matcher.Guide, Is.EqualTo("hikaku"));
            matcher.Type('i');
            Assert.That(matcher.Guide, Is.EqualTo("kaku"));
            Assert.That(matcher.KanaDone, Is.EqualTo(1));

            var fune = Of("ふね");
            Assert.That(fune.Guide, Is.EqualTo("fune"));
            fune.Type('h');
            Assert.That(fune.Guide, Is.EqualTo("une"));
        }

        [Test]
        public void 案内のんは次の綴りで決まる()
        {
            Assert.That(Of("かんたん").Guide, Is.EqualTo("kantann"));
            Assert.That(Of("かんい").Guide, Is.EqualTo("kanni"));
            Assert.That(Of("きっぷ").Guide, Is.EqualTo("kippu"));
            Assert.That(Of("きんぎょ").Guide, Is.EqualTo("kingyo"));
        }

        [Test]
        public void 打ち終えた後のキーは全て誤字()
        {
            var matcher = Of("つき");
            TestKit.TypeAll(matcher, "tuki");
            foreach (var key in RomajiTable.Keys) { Assert.That(matcher.Type(key), Is.EqualTo(KeyResult.Miss)); }
            Assert.That(matcher.Typed, Is.EqualTo("tuki"));
        }

        [Test]
        public void Resetで最初に戻り複製は独立している()
        {
            var matcher = Of("しゃしん");
            TestKit.TypeAll(matcher, "sy");
            var clone = matcher.Clone();
            TestKit.TypeAll(clone, "a");
            Assert.That(matcher.Typed, Is.EqualTo("sy"));
            Assert.That(clone.Typed, Is.EqualTo("sya"));

            matcher.Reset();
            Assert.That(matcher.Typed, Is.Empty);
            Assert.That(matcher.Guide, Is.EqualTo("shashinn"));
            Assert.That(matcher.Accepts('c'), Is.True);
        }

        [Test]
        public void 最初の1打の候補()
        {
            Assert.That(Reading.Parse("しか").FirstKeys, Is.EqualTo("sc"));
            Assert.That(Reading.Parse("かね").FirstKeys, Is.EqualTo("kc"));
            Assert.That(Reading.Parse("ふね").FirstKeys, Is.EqualTo("fh"));
        }

        [TestCase("")]
        [TestCase("カタカナ")]
        [TestCase("abc")]
        [TestCase("きっ")]
        [TestCase("ゔ")]
        public void 打てない読みは拒否する(string kana)
        {
            Assert.That(() => Reading.Parse(kana), Throws.TypeOf<System.FormatException>());
        }

        private static void AssertSameLanguage(string kana)
        {
            var reference = TestKit.ReferenceSpellings(kana);
            var (complete, prefixes) = TestKit.MatcherLanguage(Reading.Parse(kana));
            Assert.That(reference, Is.Not.Empty, kana);
            Assert.That(complete, Is.EquivalentTo(reference), kana);
            Assert.That(prefixes, Is.EquivalentTo(TestKit.PrefixesOf(reference)), kana);
            Assert.That(reference, Does.Contain(Reading.Parse(kana).DefaultSpelling), kana);
        }
    }
}
