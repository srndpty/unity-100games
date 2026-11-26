using System;
using System.Collections.Generic;
using IntGeometry;
using NUnit.Framework;
using TileMotion;

namespace Game.Core.Tests
{
    /// <summary>戦利品の生成はシード駆動で、範囲内のどの値も出うる。</summary>
    public sealed class LootGenerationTests
    {
        [Test]
        public void SameSeedGivesSameItems()
        {
            var gen = new LootGenerator(Lab.Loot());
            for (int seed = 1; seed <= 50; seed++)
            {
                var a = gen.Roll(new SeededRandom(seed), 2, Rarity.Rare);
                var b = gen.Roll(new SeededRandom(seed), 2, Rarity.Rare);
                Assert.That(Key(a), Is.EqualTo(Key(b)));
            }
        }

        [Test]
        public void DropStreamDependsOnlyOnSeedFloorAndSpawn()
        {
            var data = Lab.Loot();
            var gen = new LootGenerator(data);
            var table = data.Drop("charger");

            // 倒す順を変えても、配置ごとの系列は同じ品を出す。
            var forward = new Dictionary<int, string>();
            for (int spawn = 0; spawn < 30; spawn++) { forward[spawn] = DropKey(gen, LootRng.For(9, 2, spawn), table); }
            for (int spawn = 29; spawn >= 0; spawn--)
            {
                Assert.That(DropKey(gen, LootRng.For(9, 2, spawn), table), Is.EqualTo(forward[spawn]));
            }

            // 配置・階・シードが違えば系列が違う。
            Assert.That(LootRng.For(9, 2, 0).State, Is.Not.EqualTo(LootRng.For(9, 2, 1).State));
            Assert.That(LootRng.For(9, 2, 0).State, Is.Not.EqualTo(LootRng.For(9, 3, 0).State));
            Assert.That(LootRng.For(9, 2, 0).State, Is.Not.EqualTo(LootRng.For(10, 2, 0).State));
        }

        [Test]
        public void EveryBaseAndAffixCanAppearOnFloorThree()
        {
            var data = Lab.Loot();
            var bases = new HashSet<string>();
            var affixes = new HashSet<string>();
            foreach (var (item, floor) in Lab.ManyItems(data, 400))
            {
                if (floor != 3) { continue; }
                bases.Add(item.Base.Id);
                foreach (var a in item.Affixes) { affixes.Add(a.Affix.Id); }
            }

            Assert.That(bases.Count, Is.EqualTo(data.Bases.Count));
            Assert.That(affixes.Count, Is.EqualTo(data.Affixes.Count));
        }

        [Test]
        public void BasesRespectTheirFloorAndDeeperFloorsUseHigherTiers()
        {
            var data = Lab.Loot();
            var tierSum = new long[LootData.MaxFloor + 1];
            var tierCount = new long[LootData.MaxFloor + 1];
            foreach (var (item, floor) in Lab.ManyItems(data, 300))
            {
                Assert.That(item.Base.MinFloor, Is.LessThanOrEqualTo(floor));
                foreach (var a in item.Affixes)
                {
                    Assert.That(a.Tier, Is.EqualTo(a.Affix.TierFor(floor)));
                    tierSum[floor] += a.Tier;
                    tierCount[floor]++;
                }
            }

            double Mean(int f) => (double)tierSum[f] / tierCount[f];
            Assert.That(Mean(2), Is.GreaterThan(Mean(1)));
            Assert.That(Mean(3), Is.GreaterThan(Mean(2)));
        }

        [Test]
        public void EveryValueInAnAffixRangeCanBeRolled()
        {
            // 019 の教訓：範囲内のどの値もシードで出る（範囲の端を含む）。
            var data = Lab.Loot();
            var gen = new LootGenerator(data);
            var seen = new Dictionary<string, HashSet<int>>();
            for (int seed = 1; seed <= 3000; seed++)
            {
                var item = gen.Roll(new SeededRandom(seed), 1, Rarity.Rare);
                foreach (var a in item.Affixes)
                {
                    if (!seen.TryGetValue(a.Affix.Id, out var set)) { seen[a.Affix.Id] = set = new HashSet<int>(); }
                    set.Add(a.Values[0]);
                }
            }

            foreach (var a in data.Affixes)
            {
                int tier = a.TierFor(1);
                if (tier < 0) { continue; }
                var range = a.Tiers[tier].Mods[0];
                for (int v = range.Min; v <= range.Max; v++)
                {
                    Assert.That(seen[a.Id], Has.Member(v), $"{a.Id} の {v}");
                }
            }
        }

        [Test]
        public void RarityControlsAffixCount()
        {
            var data = Lab.Loot();
            foreach (var (item, _) in Lab.ManyItems(data, 200))
            {
                Assert.That(item.Affixes.Count, Is.LessThanOrEqualTo(RarityInfo.MaxAffixes(item.Rarity)));
                if (item.Rarity == Rarity.Normal) { Assert.That(item.Affixes, Is.Empty); }
                if (item.Rarity == Rarity.Magic) { Assert.That(item.Affixes.Count, Is.InRange(1, 2)); }
            }
        }

        [Test]
        public void BossDropTableAlwaysGivesTwoRares()
        {
            var data = Lab.Loot();
            var gen = new LootGenerator(data);
            for (int seed = 1; seed <= 50; seed++)
            {
                var items = gen.RollDrop(LootRng.For(seed, 3, 0), 3, data.Drop("boss"), out var potion);
                Assert.That(items.Count, Is.EqualTo(2));
                Assert.That(potion, Is.False);
                foreach (var item in items) { Assert.That(item.Rarity, Is.EqualTo(Rarity.Rare)); }
            }
        }

        [Test]
        public void DropRatesFollowTheTable()
        {
            var data = Lab.Loot();
            var gen = new LootGenerator(data);
            var table = data.Drop("grunt");
            int drops = 0;
            int potions = 0;
            const int N = 4000;
            for (int i = 0; i < N; i++)
            {
                if (gen.RollDrop(LootRng.For(1, 1, i), 1, table, out var potion).Count > 0) { drops++; }
                if (potion) { potions++; }
            }

            Assert.That(drops * 100.0 / N, Is.EqualTo(table.ChancePercent).Within(3));
            Assert.That(potions * 100.0 / N, Is.EqualTo(table.PotionPercent).Within(3));
        }

        [Test]
        public void FixedLootHashMatchesAcrossRuntimes()
        {
            // 整数だけの生成なので .NET と Mono で同じ値になる。
            var data = Lab.Loot();
            var gen = new LootGenerator(data);
            var hash = StateHash.Start();
            hash.Mix(data.Fingerprint);
            foreach (var key in new[] { "grunt", "archer", "charger", "boss" })
            {
                for (int spawn = 0; spawn < 40; spawn++)
                {
                    var items = gen.RollDrop(LootRng.For(1, 1 + (spawn % 3), spawn), 1 + (spawn % 3), data.Drop(key), out var potion);
                    foreach (var item in items)
                    {
                        hash.Mix(item.Base.Index);
                        hash.Mix((int)item.Rarity);
                        foreach (var a in item.Affixes)
                        {
                            hash.Mix(a.Affix.Index);
                            foreach (var v in a.Values) { hash.Mix(v); }
                        }
                    }

                    hash.Mix(potion);
                }
            }

            TestContext.WriteLine($"loot hash 0x{hash.Value:X8}");
            Assert.That(hash.Value, Is.EqualTo(ExpectedHash));
        }

        private const uint ExpectedHash = 0x897152A8u;

        private static string DropKey(LootGenerator gen, SeededRandom rng, DropTable table)
        {
            var items = gen.RollDrop(rng, 2, table, out var potion);
            var parts = new List<string> { potion ? "P" : "-" };
            foreach (var item in items) { parts.Add(Key(item)); }
            return string.Join("|", parts);
        }

        private static string Key(Item item)
        {
            var parts = new List<string> { item.Base.Id, item.Rarity.ToString() };
            foreach (var a in item.Affixes) { parts.Add(a.Affix.Id + ":" + string.Join(",", a.Values)); }
            return string.Join(";", parts);
        }

        [Test]
        public void ItemListsCannotBeMutatedThroughCasts()
        {
            var item = new LootGenerator(Lab.Loot()).Roll(new SeededRandom(5), 3, Rarity.Rare);
            Assert.That(item.Affixes, Is.Not.InstanceOf<RolledAffix[]>());
            Assert.That(item.Mods is IList<StatMod> list && !list.IsReadOnly, Is.False);
            Assert.That(item.Affixes[0].Values is int[], Is.False);
            Assert.Throws<NotSupportedException>(() => ((IList<RolledAffix>)item.Affixes)[0] = item.Affixes[0]);
        }
    }
}
