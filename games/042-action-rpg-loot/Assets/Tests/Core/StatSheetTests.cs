using System;
using System.Collections.Generic;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>題材固有テスト「装備入替後の能力値が正しい」。</summary>
    public sealed class StatSheetTests
    {
        [Test]
        public void NoEquipmentGivesHeroBase()
        {
            var data = Lab.Loot();
            Assert.That(StatSheet.Compute(data.Hero, new Equipment()), Is.EqualTo(data.Hero));
        }

        [Test]
        public void FlatIsAddedBeforeIncreasedAndTruncated()
        {
            var hero = Block(life: 100);
            var mods = new[]
            {
                new StatMod(StatKind.Life, ModKind.Flat, 5),
                new StatMod(StatKind.Life, ModKind.Increased, 10),
                new StatMod(StatKind.Life, ModKind.Increased, 3),
            };

            // (100 + 5) × 113 / 100 = 118.65 → 118
            Assert.That(StatSheet.Compute(hero, mods).Life, Is.EqualTo(118));
        }

        [Test]
        public void ClampsToStatLimits()
        {
            var hero = Block(life: 100);
            var mods = new[]
            {
                new StatMod(StatKind.Life, ModKind.Increased, -150),
                new StatMod(StatKind.Leech, ModKind.Flat, 50),
                new StatMod(StatKind.AttackSpeed, ModKind.Flat, -100),
                new StatMod(StatKind.FireResist, ModKind.Flat, 90),
                new StatMod(StatKind.Armor, ModKind.Flat, -10),
                new StatMod(StatKind.DamageMin, ModKind.Flat, 50),
            };
            var s = StatSheet.Compute(hero, mods);
            Assert.That(s.Life, Is.EqualTo(1), "負の Increased でも 0 未満にならず下限 1");
            Assert.That(s.Leech, Is.EqualTo(20));
            Assert.That(s.AttackSpeed, Is.EqualTo(25));
            Assert.That(s.FireResist, Is.EqualTo(75));
            Assert.That(s.Armor, Is.EqualTo(0));
            Assert.That(s.DamageMax, Is.EqualTo(s.DamageMin), "最大攻撃力は最小を下回らない");
        }

        [Test]
        public void HugeValuesSaturateInsteadOfOverflowing()
        {
            var hero = Block(life: 99999);
            var mods = new List<StatMod>();
            for (int i = 0; i < 100; i++)
            {
                mods.Add(new StatMod(StatKind.Life, ModKind.Flat, LootData.MaxModValue));
                mods.Add(new StatMod(StatKind.Life, ModKind.Increased, LootData.MaxModValue));
            }

            Assert.That(StatSheet.Compute(hero, mods).Life, Is.EqualTo(StatRules.Max(StatKind.Life)));
        }

        [Test]
        public void EquipSwapAndRestoreMatchesFreshComputation()
        {
            var data = Lab.Loot();
            var gen = new LootGenerator(data);
            var rng = new SeededRandom(42);
            var a = gen.Roll(rng, 3, Rarity.Rare);
            Item b;
            do { b = gen.Roll(rng, 3, Rarity.Rare); } while (b.Slot != a.Slot);

            var eq = new Equipment();
            Assert.That(eq.Equip(a), Is.Null);
            var withA = StatSheet.Compute(data.Hero, eq);
            Assert.That(withA, Is.EqualTo(Naive(data.Hero, a)));

            Assert.That(eq.Equip(b), Is.SameAs(a), "同じ枠の付け替えは前の品を返す");
            Assert.That(StatSheet.Compute(data.Hero, eq), Is.EqualTo(Naive(data.Hero, b)));

            Assert.That(eq.Equip(a), Is.SameAs(b));
            Assert.That(StatSheet.Compute(data.Hero, eq), Is.EqualTo(withA), "A→B→A で元に戻る");

            Assert.That(eq.Unequip(a.Slot), Is.SameAs(a));
            Assert.That(StatSheet.Compute(data.Hero, eq), Is.EqualTo(data.Hero), "全部外すと基礎値");
        }

        [Test]
        public void RandomLoadoutsMatchNaiveRecomputationInAnyOrder()
        {
            var data = Lab.Loot();
            var gen = new LootGenerator(data);
            var rng = new SeededRandom(7);
            for (int trial = 0; trial < 300; trial++)
            {
                var items = new List<Item>();
                var eq = new Equipment();
                int changes = rng.Range(1, 12);
                for (int c = 0; c < changes; c++)
                {
                    var item = gen.Roll(rng, rng.Range(1, 3), (Rarity)rng.Next(3));
                    if (rng.Next(4) == 0)
                    {
                        eq.Unequip(item.Slot);
                        continue;
                    }

                    eq.Equip(item);
                }

                for (int s = 0; s < SlotInfo.Count; s++)
                {
                    var item = eq[(Slot)s];
                    if (item != null) { items.Add(item); }
                }

                var expected = Naive(data.Hero, items.ToArray());
                Assert.That(StatSheet.Compute(data.Hero, eq), Is.EqualTo(expected));

                // 修飾の順序を入れ替えても同じ（可換）。
                var mods = new List<StatMod>(eq.AllMods());
                for (int i = mods.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (mods[i], mods[j]) = (mods[j], mods[i]);
                }

                Assert.That(StatSheet.Compute(data.Hero, mods), Is.EqualTo(expected));
            }
        }

        [Test]
        public void CloneIsIndependent()
        {
            var data = Lab.Loot();
            var gen = new LootGenerator(data);
            var item = gen.Roll(new SeededRandom(3), 1, Rarity.Magic);
            var eq = new Equipment();
            var copy = eq.Clone();
            copy.Equip(item);
            Assert.That(eq[item.Slot], Is.Null);
            Assert.That(copy[item.Slot], Is.SameAs(item));
        }

        /// <summary>素朴な参照実装：能力ごとに全ての品の全ての修飾を数え直す。</summary>
        private static StatBlock Naive(StatBlock hero, params Item[] items)
        {
            var values = new int[StatRules.Count];
            for (int i = 0; i < StatRules.Count; i++)
            {
                var stat = (StatKind)i;
                long flat = hero[stat];
                long inc = 0;
                foreach (var item in items)
                {
                    foreach (var m in item.Mods)
                    {
                        if (m.Stat != stat) { continue; }
                        if (m.Kind == ModKind.Flat) { flat += m.Value; } else { inc += m.Value; }
                    }
                }

                decimal v = Math.Truncate(flat * Math.Max(0m, 100 + inc) / 100m);
                values[i] = (int)Math.Clamp(v, StatRules.Min(stat), StatRules.Max(stat));
            }

            values[(int)StatKind.DamageMax] = Math.Max(values[(int)StatKind.DamageMax], values[(int)StatKind.DamageMin]);
            return new StatBlock(values);
        }

        private static StatBlock Block(int life) =>
            new(new[] { life, 2, 4, 100, 100, 0, 5, 150, 0, 0, 0 });
    }
}
