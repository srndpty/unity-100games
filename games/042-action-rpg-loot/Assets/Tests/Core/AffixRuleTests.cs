using System.Collections.Generic;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>題材固有テスト「禁止 Affix の組合せを出さない」。</summary>
    public sealed class AffixRuleTests
    {
        [Test]
        public void GeneratedItemsNeverBreakTheRules()
        {
            var data = Lab.Loot();
            int checkedItems = 0;
            foreach (var (item, floor) in Lab.ManyItems(data, 400))
            {
                Assert.That(AffixRules.IsAllowed(item, floor, out var reason), Is.True, reason);
                Assert.That(NaiveAllowed(item), Is.True, item.Name);
                checkedItems++;
            }

            Assert.That(checkedItems, Is.EqualTo(400 * LootData.MaxFloor * RarityInfo.Count));
        }

        [Test]
        public void ExclusivePairsActuallyCompeteOnSharedSlots()
        {
            // 排他の組の両方が付きうる枠で、片方は実際に出る（規則が空振りしていない）。
            var data = Lab.Loot();
            var seen = new HashSet<string>();
            foreach (var (item, _) in Lab.ManyItems(data, 400))
            {
                foreach (var a in item.Affixes) { seen.Add(item.Slot + ":" + a.Affix.Id); }
            }

            Assert.That(seen, Has.Member("Weapon:heavy"));
            Assert.That(seen, Has.Member("Weapon:swift"));
            Assert.That(seen, Has.Member("Ring:ember"));
            Assert.That(seen, Has.Member("Ring:frost"));
        }

        [Test]
        public void ExhaustedCandidatesReduceTheAffixCountInsteadOfBreakingRules()
        {
            // 武器に付く Affix は排他の 2 つだけ。希少（3〜4 個）を引いても 1 つにしかならない。
            var extra = "affix p1 prefix a 10 weapon 甲\ntier 1 life flat 1 2\n"
                + "affix s1 suffix b 10 weapon 乙\ntier 1 crit flat 1 2\n"
                + "exclusive p1 s1\n";
            var data = LootData.Parse(Lab.MinimalText(extra));
            var gen = new LootGenerator(data);
            for (int seed = 1; seed <= 200; seed++)
            {
                var item = gen.Roll(new SeededRandom(seed), 1, Rarity.Rare);
                Assert.That(item.Affixes.Count, Is.EqualTo(1));
                Assert.That(AffixRules.IsAllowed(item, 1, out var reason), Is.True, reason);
            }
        }

        [Test]
        public void SameGroupIsUsedOnce()
        {
            // 接頭は同じ群の 3 つだけ。何回引いても接頭は 1 つ。
            var extra = "affix a prefix same 10 weapon 甲\ntier 1 life flat 1 2\n"
                + "affix b prefix same 10 weapon 乙\ntier 1 armor flat 1 2\n"
                + "affix c prefix same 10 weapon 丙\ntier 1 crit flat 1 2\n";
            var data = LootData.Parse(Lab.MinimalText(extra));
            var gen = new LootGenerator(data);
            for (int seed = 1; seed <= 200; seed++)
            {
                var item = gen.Roll(new SeededRandom(seed), 1, Rarity.Rare);
                Assert.That(item.Affixes.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public void PrefixAndSuffixLimitsHold()
        {
            var extra = string.Empty;
            for (int i = 0; i < 4; i++)
            {
                extra += $"affix p{i} prefix gp{i} 10 weapon 接{i}\ntier 1 life flat 1 2\n";
                extra += $"affix s{i} suffix gs{i} 10 weapon 尾{i}\ntier 1 crit flat 1 2\n";
            }

            var data = LootData.Parse(Lab.MinimalText(extra));
            var gen = new LootGenerator(data);
            for (int seed = 1; seed <= 200; seed++)
            {
                var item = gen.Roll(new SeededRandom(seed), 1, Rarity.Rare);
                int prefixes = 0;
                foreach (var a in item.Affixes) { if (a.Affix.IsPrefix) { prefixes++; } }
                Assert.That(prefixes, Is.LessThanOrEqualTo(2));
                Assert.That(item.Affixes.Count - prefixes, Is.LessThanOrEqualTo(2));
                Assert.That(item.Affixes.Count, Is.InRange(3, 4));
            }
        }

        [Test]
        public void IsAllowedRejectsEachForbiddenCombination()
        {
            var data = Lab.Loot();
            var sword = data.Base("shortsword");
            var ring = data.Base("copperring");
            RolledAffix Roll(string id, int floor = 1)
            {
                var a = data.Affix(id);
                int tier = a.TierFor(floor);
                var values = new List<int>();
                foreach (var m in a.Tiers[tier].Mods) { values.Add(m.Min); }
                return new RolledAffix(a, tier, values);
            }

            void Reject(Item item, string why, bool naive = true)
            {
                Assert.That(AffixRules.IsAllowed(item, 1, out var reason), Is.False, why);
                Assert.That(reason, Is.Not.Empty);
                if (naive) { Assert.That(NaiveAllowed(item), Is.False, why); }
            }

            Reject(new Item(sword, Rarity.Magic, new[] { Roll("sharp"), Roll("cruel") }), "同じ群");
            Reject(new Item(sword, Rarity.Magic, new[] { Roll("heavy"), Roll("swift") }), "排他");
            Reject(new Item(ring, Rarity.Magic, new[] { Roll("ember"), Roll("frost") }), "排他");
            Reject(new Item(ring, Rarity.Magic, new[] { Roll("cruel") }), "枠違い");
            Reject(new Item(sword, Rarity.Normal, new[] { Roll("sharp") }), "普通に Affix");
            Reject(new Item(sword, Rarity.Magic, new[] { Roll("sharp"), Roll("swift"), Roll("keen") }), "魔法に 3 つ");
            Reject(new Item(sword, Rarity.Rare, new[] { Roll("keen"), Roll("fury"), Roll("thirst") }), "接尾 3 つ");
            Reject(new Item(sword, Rarity.Magic, new[] { Roll("sharp", 3) }), "1 階に 3 階の段", naive: false);
            Reject(new Item(sword, Rarity.Magic, new[] { new RolledAffix(data.Affix("keen"), 0, new List<int> { 99 }) }), "範囲外の値");

            var ok = new Item(sword, Rarity.Rare, new[] { Roll("sharp"), Roll("heavy"), Roll("keen"), Roll("fury") });
            Assert.That(AffixRules.IsAllowed(ok, 1, out var okReason), Is.True, okReason);
            Assert.That(NaiveAllowed(ok), Is.True);
        }

        [Test]
        public void CanAddAgreesWithIsAllowedForEveryPairOnEverySlot()
        {
            var data = Lab.Loot();
            foreach (var b in data.Bases)
            {
                foreach (var x in data.Affixes)
                {
                    foreach (var y in data.Affixes)
                    {
                        if (ReferenceEquals(x, y) || !x.Fits(b.Slot)) { continue; }
                        bool add = AffixRules.CanAdd(b.Slot, new[] { x }, y);
                        var item = new Item(b, Rarity.Magic, new[] { Min(x), Min(y) });
                        Assert.That(add, Is.EqualTo(AffixRules.IsAllowed(item, LootData.MaxFloor, out _)), $"{b.Id} {x.Id} {y.Id}");
                        Assert.That(add, Is.EqualTo(NaiveAllowed(item)), $"{b.Id} {x.Id} {y.Id}");
                    }
                }
            }
        }

        private static RolledAffix Min(AffixDef a)
        {
            int tier = a.TierFor(LootData.MaxFloor);
            var values = new List<int>();
            foreach (var m in a.Tiers[tier].Mods) { values.Add(m.Min); }
            return new RolledAffix(a, tier, values);
        }

        /// <summary>素朴な参照実装：全ての組を数え直す（値の範囲と段は見ない）。</summary>
        private static bool NaiveAllowed(Item item)
        {
            var list = item.Affixes;
            int max = item.Rarity == Rarity.Normal ? 0 : item.Rarity == Rarity.Magic ? 2 : 4;
            if (list.Count > max) { return false; }
            int pre = 0;
            int suf = 0;
            foreach (var a in list)
            {
                if (a.Affix.IsPrefix) { pre++; } else { suf++; }
                if ((a.Affix.SlotMask & (1 << (int)item.Slot)) == 0) { return false; }
            }

            if (pre > 2 || suf > 2) { return false; }
            for (int i = 0; i < list.Count; i++)
            {
                for (int j = 0; j < list.Count; j++)
                {
                    if (i == j) { continue; }
                    if (list[i].Affix.Group == list[j].Affix.Group) { return false; }
                    foreach (var e in list[i].Affix.Exclusive)
                    {
                        if (e.Id == list[j].Affix.Id) { return false; }
                    }
                }
            }

            foreach (var a in list)
            {
                var tier = a.Affix.Tiers[a.Tier];
                for (int k = 0; k < tier.Mods.Count; k++)
                {
                    if (a.Values[k] < tier.Mods[k].Min || a.Values[k] > tier.Mods[k].Max) { return false; }
                }
            }

            return true;
        }
    }
}
