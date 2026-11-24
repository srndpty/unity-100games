using System;
using System.Linq;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class WordBookTests
    {
        [Test]
        public void 本番の単語帳は50語で全ての段がある()
        {
            var book = TestKit.Production();
            Assert.That(book.Count, Is.EqualTo(50));
            for (var tier = 1; tier <= WordBook.MaxTier; tier++) { Assert.That(book.CountInTiers(tier, tier), Is.Positive); }
            Assert.That(book.Entries.Select(e => e.Index), Is.EqualTo(Enumerable.Range(0, 50)));
        }

        [Test]
        public void 本番の単語帳は表示文と読みを分けて持つ()
        {
            var book = TestKit.Production();
            var word = TestKit.Word(book, "よあけのかね");
            Assert.That(word.Display, Is.EqualTo("夜明けの鐘"));
            Assert.That(word.Tier, Is.EqualTo(3));
            Assert.That(book.Entries.Count(e => e.Display != e.Reading.Kana), Is.GreaterThan(45), "ほとんどの語は表示文が読みと違う");
        }

        [Test]
        public void 本番の単語帳はん_っ_拗音_長音_別綴りのかなを含む()
        {
            var all = string.Concat(TestKit.Production().Entries.Select(e => e.Reading.Kana));
            foreach (var kana in new[] { "ん", "っ", "ゃ", "ゅ", "ょ", "ー", "し", "ち", "つ", "ふ", "じ" })
            {
                Assert.That(all.Contains(kana, StringComparison.Ordinal), Is.True, kana);
            }

            Assert.That(TestKit.Production().Entries.Any(e => e.Reading.Kana.Contains("んい", StringComparison.Ordinal)), Is.True, "「ん」の後に母音");
        }

        [Test]
        public void 空行と注釈は読み飛ばしCRLFも読める()
        {
            var book = WordBook.Parse("# 注釈\r\n\r\n月\tつき\t1\r\n星\tほし\t3\r\n");
            Assert.That(book.Count, Is.EqualTo(2));
            Assert.That(book[1].Display, Is.EqualTo("星"));
            Assert.That(book[1].Tier, Is.EqualTo(3));
        }

        [TestCase("月\tつき", "3 列")]
        [TestCase("月\tつき\t1\t余り", "3 列")]
        [TestCase("\tつき\t1", "表示文が空")]
        [TestCase("月\tつき\t0", "段は")]
        [TestCase("月\tつき\t4", "段は")]
        [TestCase("月\tつき\t一", "段は")]
        [TestCase("月\tツキ\t1", "打てないかな")]
        [TestCase("月\tきっ\t1", "「っ」で終わって")]
        [TestCase("月\tつき\t1\n月\tげつ\t1", "表示文「月」が重複")]
        [TestCase("月\tつき\t1\n槻\tつき\t1", "読み「つき」が重複")]
        [TestCase("# 注釈だけ", "語がありません")]
        public void 不正な行は行番号付きで拒否する(string text, string reason)
        {
            Assert.That(() => WordBook.Parse(text), Throws.TypeOf<FormatException>().With.Message.Contains(reason));
        }

        [Test]
        public void 本番の単語帳は50語ちょうどでなければ拒否する()
        {
            var lines = TestKit.WordsText().Split('\n').Where(l => l.Trim().Length > 0 && !l.StartsWith('#')).ToList();
            Assert.That(() => WordBook.Production(string.Join("\n", lines.Take(49))), Throws.TypeOf<FormatException>().With.Message.Contains("50 語"));
            Assert.That(
                () => WordBook.Production(string.Join("\n", lines.Append("新しい語\tあたらしいご\t1"))),
                Throws.TypeOf<FormatException>().With.Message.Contains("50 語"));
        }

        [Test]
        public void 段のない本番の単語帳は拒否する()
        {
            var lines = Enumerable.Range(0, 50).Select(i => $"語{i}\t{Kana(i)}\t1");
            Assert.That(() => WordBook.Production(string.Join("\n", lines)), Throws.TypeOf<FormatException>().With.Message.Contains("段 2"));
        }

        [Test]
        public void 全ての語の綴りを既定の案内どおりに打ち切れる()
        {
            foreach (var entry in TestKit.Production().Entries)
            {
                var matcher = new RomajiMatcher(entry.Reading);
                Assert.That(TestKit.TypeAll(matcher, entry.Reading.DefaultSpelling), Is.EqualTo(KeyResult.Completed), entry.ToString());
            }
        }

        // 重ならない 2 かなの読みを番号から作る。
        private static string Kana(int i)
        {
            const string Chars = "あいうえおかきくけこさしすせそたちつてと";
            return $"{Chars[i / Chars.Length]}{Chars[i % Chars.Length]}";
        }
    }
}
