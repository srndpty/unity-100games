using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class LootDataTests
    {
        private static readonly int[] _slotCounts = { 6, 4, 3, 4, 3 };

        [Test]
        public void DefaultDataHasTwentyBasesAcrossFiveSlots()
        {
            var data = Lab.Loot();
            Assert.That(data.Bases.Count, Is.EqualTo(20));
            var counts = new int[SlotInfo.Count];
            foreach (var b in data.Bases) { counts[(int)b.Slot]++; }
            Assert.That(counts, Is.EqualTo(_slotCounts));
        }

        [Test]
        public void EveryAffixFitsSomeBaseAndAppearsOnFloorThree()
        {
            var data = Lab.Loot();
            foreach (var a in data.Affixes)
            {
                Assert.That(a.TierFor(LootData.MaxFloor), Is.GreaterThanOrEqualTo(0), a.Id);
                bool fits = false;
                foreach (var b in data.Bases) { fits |= a.Fits(b.Slot); }
                Assert.That(fits, Is.True, a.Id);
            }
        }

        [Test]
        public void ExclusivePairsAreSymmetric()
        {
            var data = Lab.Loot();
            Assert.That(data.Affix("heavy").Excludes(data.Affix("swift")), Is.True);
            Assert.That(data.Affix("swift").Excludes(data.Affix("heavy")), Is.True);
            Assert.That(data.Affix("ember").Excludes(data.Affix("frost")), Is.True);
            Assert.That(data.Affix("sharp").Excludes(data.Affix("swift")), Is.False);
        }

        [Test]
        public void MinimalTextParses()
        {
            var data = LootData.Parse(Lab.MinimalText(string.Empty));
            Assert.That(data.Bases.Count, Is.EqualTo(1));
            Assert.That(data.Hero.Life, Is.EqualTo(100));
        }

        [TestCase("base stick weapon 1 10 棒2 dmgmin flat 1", "重複")]
        [TestCase("affix a prefix g 1 weapon A\ntier 1 life flat 1 2\naffix a suffix h 1 weapon B\ntier 1 life flat 1 2", "重複")]
        [TestCase("affix a prefix g 1 weapon A\ntier 1 life flat 1 2\nexclusive a missing", "未定義")]
        [TestCase("affix a prefix g 1 weapon A\ntier 1 life flat 1 2\nexclusive a a", "自分自身")]
        [TestCase("affix a prefix g 1 weapon A\ntier 1 life flat 1 2\naffix b prefix h 1 weapon B\ntier 1 life flat 1 2\nexclusive a b\nexclusive b a", "重複")]
        [TestCase("affix a prefix g 1 , A\ntier 1 life flat 1 2", "付く枠がありません")]
        [TestCase("affix a prefix g 1 weapon A", "段がありません")]
        [TestCase("affix a prefix g 0 weapon A\ntier 1 life flat 1 2", "重みは正")]
        [TestCase("affix a prefix g 1 weapon A\ntier 1 life flat 5 2", "逆転")]
        [TestCase("affix a prefix g 1 weapon A\ntier 2 life flat 1 2\ntier 1 life flat 2 3", "増える順")]
        [TestCase("affix a prefix g 1 weapon A\ntier 1 speed inc 1 2", "％で増やせません")]
        [TestCase("affix a prefix g 1 weapon A\ntier 4 life flat 1 2", "階は")]
        [TestCase("affix a prefix g 1 weapon A\ntier 1 life flat 1 100001", "±")]
        [TestCase("tier 1 life flat 1 2", "affix の後")]
        [TestCase("base bad shield 1 10 盾 armor flat 1", "不明な枠")]
        [TestCase("base bad weapon 1 10 X power flat 1", "不明な能力")]
        [TestCase("hero life 1 dmgmin 1 dmgmax 1 speed 100 move 100 armor 0 crit 0 critdmg 150 leech 0 fireres 0 coldres 0", "2 つ")]
        [TestCase("drop grunt chance 50 count 1 normal 1 magic 1 rare 1 potion 10", "2 回")]
        [TestCase("unknown 1", "不明な行")]
        public void RejectsBrokenData(string extra, string reason)
        {
            var ex = Assert.Throws<LootDataException>(() => LootData.Parse(Lab.MinimalText(extra)));
            Assert.That(ex!.Message.Contains(reason, StringComparison.Ordinal), Is.True, ex.Message);
        }

        [Test]
        public void RejectsMissingDropTable()
        {
            var text = Lab.MinimalText(string.Empty).Replace("drop boss chance 100 count 2 normal 0 magic 0 rare 1 potion 0\n", string.Empty, StringComparison.Ordinal);
            var ex = Assert.Throws<LootDataException>(() => LootData.Parse(text));
            Assert.That(ex!.Message.Contains("boss", StringComparison.Ordinal), Is.True, ex.Message);
        }

        [Test]
        public void RejectsDropTableWithZeroRarityWeights()
        {
            var text = Lab.MinimalText(string.Empty).Replace("drop grunt chance 50 count 1 normal 1 magic 1 rare 1", "drop grunt chance 50 count 1 normal 0 magic 0 rare 0", StringComparison.Ordinal);
            var ex = Assert.Throws<LootDataException>(() => LootData.Parse(text));
            Assert.That(ex!.Message.Contains("全て 0", StringComparison.Ordinal), Is.True, ex.Message);
        }

        [Test]
        public void RarityWeightSumExactlyIntMaxIsAcceptedAndOneMoreIsRejected()
        {
            string Text(int rare) => Lab.MinimalText(string.Empty).Replace(
                "drop grunt chance 50 count 1 normal 1 magic 1 rare 1",
                $"drop grunt chance 50 count 1 normal 1 magic 1 rare {rare}",
                StringComparison.Ordinal);
            Assert.DoesNotThrow(() => LootData.Parse(Text(int.MaxValue - 2)));
            var ex = Assert.Throws<LootDataException>(() => LootData.Parse(Text(int.MaxValue - 1)));
            Assert.That(ex!.Message.Contains("int を超えます", StringComparison.Ordinal), Is.True, ex.Message);
        }

        [Test]
        public void AffixWeightSumBeyondIntIsRejected()
        {
            int big = int.MaxValue / 2 + 1;
            var extra = $"affix a prefix g {big} weapon A\ntier 1 life flat 1 2\naffix b prefix h {big} weapon B\ntier 1 life flat 1 2";
            var ex = Assert.Throws<LootDataException>(() => LootData.Parse(Lab.MinimalText(extra)));
            Assert.That(ex!.Message.Contains("int を超えます", StringComparison.Ordinal), Is.True, ex.Message);
        }

        [Test]
        public void RejectsFloorWithoutBases()
        {
            var text = Lab.MinimalText(string.Empty).Replace("base stick weapon 1 10", "base stick weapon 2 10", StringComparison.Ordinal);
            var ex = Assert.Throws<LootDataException>(() => LootData.Parse(text));
            Assert.That(ex!.Message.Contains("1 階で出る基礎品がありません", StringComparison.Ordinal), Is.True, ex.Message);
        }

        [Test]
        public void FingerprintChangesWithAnyValue()
        {
            var text = Lab.DataText("Loot.txt");
            var original = LootData.Parse(text).Fingerprint;
            Assert.That(LootData.Parse(text).Fingerprint, Is.EqualTo(original));
            var changed = new[]
            {
                text.Replace("tier 3 leech flat 3 5", "tier 3 leech flat 3 6", StringComparison.Ordinal),
                text.Replace("exclusive ember frost", "exclusive ember stride", StringComparison.Ordinal),
                text.Replace("drop grunt   chance 35", "drop grunt   chance 36", StringComparison.Ordinal),
                text.Replace("hero life 100", "hero life 101", StringComparison.Ordinal),
                text.Replace("base spear       weapon 1 8", "base spear       weapon 1 9", StringComparison.Ordinal),
            };
            foreach (var variant in changed)
            {
                Assert.That(variant, Is.Not.EqualTo(text));
                Assert.That(LootData.Parse(variant).Fingerprint, Is.Not.EqualTo(original));
            }
        }
    }
}
