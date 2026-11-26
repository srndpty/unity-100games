using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>設定の検証（実行時に計算する値の桁あふれ）と、試合が使う設定を外から変えられないこと。</summary>
    public sealed class ConfigTests
    {
        private static bool Valid(CrawlConfig c) => c.Problems().Count == 0;

        [Test]
        public void MonsterCountIsCheckedWithoutOverflow()
        {
            Assert.That(Valid(new CrawlConfig { MonstersBase = 40, MonstersPerFloor = 0 }), Is.True, "最も深い階でちょうど 40");
            Assert.That(Valid(new CrawlConfig { MonstersBase = 35, MonstersPerFloor = 1 }), Is.True, "35 + 1 × 5 = 40");
            Assert.That(Valid(new CrawlConfig { MonstersBase = 36, MonstersPerFloor = 1 }), Is.False, "41");
            Assert.That(Valid(new CrawlConfig { MonstersBase = 1, MonstersPerFloor = 858_993_459 }), Is.False, "int では 1 + 858993459 × 5 が 0 に折り返す");
            Assert.That(Valid(new CrawlConfig { MonstersBase = int.MaxValue, MonstersPerFloor = int.MaxValue }), Is.False);
            Assert.That(Valid(new CrawlConfig { MonstersBase = -1, MonstersPerFloor = 0 }), Is.False);
            Assert.That(Valid(new CrawlConfig { MonstersBase = 0, MonstersPerFloor = -1 }), Is.False);
        }

        [Test]
        public void ItemCountIsCheckedWithoutOverflow()
        {
            Assert.That(Valid(new CrawlConfig { ItemsBase = 40, ItemsPerFloor = 0 }), Is.True, "最も深い階でちょうど 40");
            Assert.That(Valid(new CrawlConfig { ItemsBase = 35, ItemsPerFloor = 1 }), Is.True);
            Assert.That(Valid(new CrawlConfig { ItemsBase = 36, ItemsPerFloor = 1 }), Is.False, "41");
            Assert.That(Valid(new CrawlConfig { ItemsBase = 1, ItemsPerFloor = 858_993_459 }), Is.False, "int では 0 に折り返して FloorItemCap も通る");
            Assert.That(Valid(new CrawlConfig { ItemsBase = int.MaxValue, ItemsPerFloor = int.MaxValue }), Is.False);
            Assert.That(Valid(new CrawlConfig { ItemsBase = -1, ItemsPerFloor = 0 }), Is.False);
            Assert.That(Valid(new CrawlConfig { ItemsBase = 0, ItemsPerFloor = -1 }), Is.False);
            Assert.That(Valid(new CrawlConfig { ItemsBase = 10, ItemsPerFloor = 0, FloorItemCap = 10 }), Is.True, "FloorItemCap ちょうど");
            Assert.That(Valid(new CrawlConfig { ItemsBase = 11, ItemsPerFloor = 0, FloorItemCap = 10 }), Is.False, "FloorItemCap を 1 超える");
        }

        [Test]
        public void ChangingThePublishedConfigDoesNotChangeTheRun()
        {
            var changed = new CrawlRun(new CrawlConfig(), 5);
            var untouched = new CrawlRun(new CrawlConfig(), 5);
            var before = changed.Hash();

            var published = changed.Config;
            published.HitPercent = 1;
            published.Monsters[(int)MonsterKind.Skitter].ActionCost = 0;
            published.LevelExp[0] = 1;
            changed.Config.ViewRadius = 1;
            Assert.That(changed.Hash(), Is.EqualTo(before), "公開した設定を変えても状態は変わらない");
            Assert.That(changed.Config.HitPercent, Is.EqualTo(new CrawlConfig().HitPercent), "公開するのは毎回新しい複製");
            Assert.That(changed.Config.Monsters[(int)MonsterKind.Skitter].ActionCost, Is.EqualTo(new CrawlConfig().Monsters[(int)MonsterKind.Skitter].ActionCost));

            // 命中率・速さ・視界が元のままなら、同じ操作列で同じ結果になる。
            foreach (var command in ReplayTests.RandomCommands(5, 600))
            {
                if (changed.State != RunState.Playing) { break; }
                Assert.That(changed.Submit(command).Accepted, Is.EqualTo(untouched.Submit(command).Accepted));
                Assert.That(changed.Hash(), Is.EqualTo(untouched.Hash()));
            }
        }
    }
}
