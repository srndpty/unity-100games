using System;
using System.Collections.Generic;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>完了判定「各装備の効果を説明表示」。説明文は Core が作り、画面はそれを出すだけ。</summary>
    public sealed class ItemTextTests
    {
        [Test]
        public void EveryBaseHasADescriptionWithItsMods()
        {
            var data = Lab.Loot();
            foreach (var b in data.Bases)
            {
                var item = new Item(b, Rarity.Normal, Array.Empty<RolledAffix>());
                var lines = ItemText.Describe(item);
                Assert.That(lines[0], Is.EqualTo(b.Name));
                Assert.That(lines[1], Is.EqualTo("普通・" + SlotInfo.Name(b.Slot)));
                Assert.That(lines.Count, Is.EqualTo(2 + b.Mods.Count), b.Id);
                foreach (var m in b.Mods) { Assert.That(lines, Has.Member(ItemText.Mod(m))); }
            }
        }

        [Test]
        public void EveryAffixHasLabelledLines()
        {
            var data = Lab.Loot();
            foreach (var a in data.Affixes)
            {
                var slotBase = FirstBaseFor(data, a);
                int tier = a.TierFor(LootData.MaxFloor);
                var values = new List<int>();
                foreach (var m in a.Tiers[tier].Mods) { values.Add(m.Max); }
                var item = new Item(slotBase, Rarity.Magic, new[] { new RolledAffix(a, tier, values) });
                var lines = ItemText.Describe(item);
                var label = a.IsPrefix ? "《" + a.Name + "》" : "〔" + a.Name + "〕";
                int labelled = 0;
                foreach (var line in lines)
                {
                    if (line.StartsWith(label, StringComparison.Ordinal)) { labelled++; }
                }

                Assert.That(labelled, Is.EqualTo(a.Tiers[tier].Mods.Count), a.Id);
                Assert.That(lines[0].Contains(a.Name, StringComparison.Ordinal), Is.True, "名前に Affix が入る");
            }
        }

        [Test]
        public void ModTextUsesPercentForIncreasedAndPercentStats()
        {
            Assert.That(ItemText.Mod(new StatMod(StatKind.Life, ModKind.Flat, 12)), Is.EqualTo("最大HP +12"));
            Assert.That(ItemText.Mod(new StatMod(StatKind.Life, ModKind.Increased, 10)), Is.EqualTo("最大HP +10%"));
            Assert.That(ItemText.Mod(new StatMod(StatKind.AttackSpeed, ModKind.Flat, -15)), Is.EqualTo("攻撃速度 -15%"));
            Assert.That(ItemText.Diff(new StatDiff(StatKind.AttackSpeed, 100, 115)), Is.EqualTo("攻撃速度 100% → 115%（+15）"));
        }

        [Test]
        public void CompareMatchesTheActualChangeAfterEquipping()
        {
            var data = Lab.Loot();
            var gen = new LootGenerator(data);
            var rng = new SeededRandom(11);
            var eq = new Equipment();
            for (int i = 0; i < 200; i++)
            {
                var candidate = gen.Roll(rng, rng.Range(1, 3), (Rarity)rng.Next(3));
                var diffs = ItemText.Compare(data.Hero, eq, candidate);
                var before = StatSheet.Compute(data.Hero, eq);
                eq.Equip(candidate);
                var after = StatSheet.Compute(data.Hero, eq);
                Assert.That(diffs.Count, Is.EqualTo(ItemText.Diff(before, after).Count));
                foreach (var d in diffs)
                {
                    Assert.That(d.Before, Is.EqualTo(before[d.Stat]));
                    Assert.That(d.After, Is.EqualTo(after[d.Stat]));
                    Assert.That(d.Delta, Is.Not.Zero);
                }
            }
        }

        [Test]
        public void CompareDoesNotChangeTheEquipment()
        {
            var data = Lab.Loot();
            var gen = new LootGenerator(data);
            var eq = new Equipment();
            var item = gen.Roll(new SeededRandom(1), 1, Rarity.Rare);
            ItemText.Compare(data.Hero, eq, item);
            Assert.That(eq[item.Slot], Is.Null);
        }

        private static ItemBaseDef FirstBaseFor(LootData data, AffixDef a)
        {
            foreach (var b in data.Bases)
            {
                if (a.Fits(b.Slot)) { return b; }
            }

            throw new InvalidOperationException(a.Id);
        }
    }
}
