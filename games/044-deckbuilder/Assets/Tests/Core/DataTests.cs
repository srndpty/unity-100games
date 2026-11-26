using System;
using System.Linq;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>データの読み込みと検証。</summary>
    public sealed class DataTests
    {
        private static readonly int[] _zeroWeights = { 0, 0, 0 };

        private static readonly int[] _twoWeights = { 1, 1 };

        private static readonly int[] _rareOnly = { 0, 0, 1 };

        private static string Cards => Lab.DataText("Cards.txt");

        private static string Enemies => Lab.DataText("Enemies.txt");

        private static string Encounters => Lab.DataText("Encounters.txt");

        private static string[] Errors(string? cards = null, string? enemies = null, string? encounters = null, DeckConfig? config = null) =>
            DeckDataValidator.Validate(GameData.Parse(cards ?? Cards, enemies ?? Enemies, encounters ?? Encounters), config ?? new DeckConfig()).ToArray();

        private static void AssertHas(System.Collections.Generic.IEnumerable<string> errors, string part)
        {
            var list = errors.ToList();
            Assert.That(list.Any(e => e.Contains(part, StringComparison.Ordinal)), Is.True, $"「{part}」を含む理由がない：{string.Join(" / ", list)}");
        }

        [Test]
        public void ShippedDataIsValidWithTwentyCardsFiveEnemiesAndABoss()
        {
            var data = Lab.Data;
            Assert.That(data.Cards.Count, Is.EqualTo(20));
            Assert.That(Errors(), Is.Empty);
            var boss = data.Encounters.Where(e => e.Tier == EncounterTier.Boss).SelectMany(e => e.Foes).Distinct().ToList();
            var regular = data.Encounters.Where(e => e.Tier != EncounterTier.Boss).SelectMany(e => e.Foes).Distinct().ToList();
            Assert.That(boss.Count, Is.EqualTo(1));
            Assert.That(regular.Count, Is.EqualTo(5));
            Assert.That(data.Enemies.Count, Is.EqualTo(6));
            Assert.That(data.StarterDeck.Count, Is.EqualTo(10));
        }

        [Test]
        public void EveryCardHasADescription()
        {
            foreach (var card in Lab.Data.Cards)
            {
                var text = CardText.Describe(card);
                Assert.That(text, Is.Not.Empty, card.Id);
                Assert.That(text.IndexOf('{'), Is.EqualTo(-1), card.Id);
            }

            Assert.That(CardText.Describe(Lab.Card("twin")), Is.EqualTo("4 ダメージを 2 回。"));
            Assert.That(CardText.Describe(Lab.Card("finisher")), Is.EqualTo("24 ダメージ。\n廃棄。"));
            Assert.That(CardText.Describe(Lab.Card("sparks")), Is.EqualTo("無作為な敵に 3 ダメージを 3 回。"));
            Assert.That(CardText.Describe(Lab.Card("crush")), Is.EqualTo("9 ダメージ。\n弱体 2 を与える。"));
        }

        [Test]
        public void SyntaxErrorsAndUnknownReferencesAreRejectedWhileParsing()
        {
            Assert.Throws<FormatException>(() => GameData.Parse(Cards.Replace("do damage 6 chosen", "do smash 6 chosen", StringComparison.Ordinal), Enemies, Encounters));
            Assert.Throws<FormatException>(() => GameData.Parse(Cards.Replace("do damage 6 chosen", "do damage six chosen", StringComparison.Ordinal), Enemies, Encounters));
            Assert.Throws<FormatException>(() => GameData.Parse(Cards.Replace("do damage 6 chosen", "do damage 6 player2", StringComparison.Ordinal), Enemies, Encounters));
            Assert.Throws<FormatException>(() => GameData.Parse(Cards.Replace("deck crush 1", "deck missing 1", StringComparison.Ordinal), Enemies, Encounters));
            Assert.Throws<FormatException>(() => GameData.Parse(Cards, Enemies, Encounters.Replace("foe warden", "foe ghost", StringComparison.Ordinal)));
            Assert.Throws<FormatException>(() => GameData.Parse(Cards, Enemies.Replace("onhalf call", "onhalf nothing", StringComparison.Ordinal), Encounters));
            Assert.Throws<FormatException>(() => GameData.Parse(Cards.Replace("type attack\nrarity starter\ncost 1", "rarity starter\ncost 1", StringComparison.Ordinal), Enemies, Encounters));
            Assert.Throws<FormatException>(() => GameData.Parse("type attack\n" + Cards, Enemies, Encounters));
        }

        [Test]
        public void RuleErrorsAreReportedWithReasons()
        {
            AssertHas(Errors(cards: Cards.Replace("card glance 一瞥", "card guard 一瞥", StringComparison.Ordinal)), "重複");
            AssertHas(Errors(cards: Cards.Replace("cost 2\ndo damage 9 chosen", "cost 4\ndo damage 9 chosen", StringComparison.Ordinal)), "コスト");
            AssertHas(Errors(cards: Cards.Replace("cost 1\ndo block 5 self", "cost -1\ndo block 5 self", StringComparison.Ordinal)), "コスト");
            AssertHas(Errors(cards: Cards.Replace("do block 5 self", "do block 5 chosen", StringComparison.Ordinal)), "対象");
            AssertHas(Errors(cards: Cards.Replace("do damage 7 all", "do damage 7 self", StringComparison.Ordinal)), "対象");
            AssertHas(Errors(cards: Cards.Replace("do damage 6 chosen", "do damage 0 chosen", StringComparison.Ordinal)), "量");
            AssertHas(Errors(cards: Cards.Replace("do damage 6 chosen", "do damage 100 chosen", StringComparison.Ordinal)), "量");
            AssertHas(Errors(cards: Cards.Replace("do damage 6 chosen", "do summon imp", StringComparison.Ordinal)), "対象");
            AssertHas(Errors(cards: Cards.Replace("deck strike 5\ndeck guard 4", "deck strike 1\ndeck guard 1", StringComparison.Ordinal)), "初期デッキ");
            var nineteen = Cards.Remove(Cards.IndexOf("card fumes", StringComparison.Ordinal)) + "deck strike 5\ndeck guard 4\ndeck crush 1\n";
            AssertHas(Errors(cards: nineteen), "20 種");
            var tooMany = Cards.Replace("do damage 4 chosen\ndo damage 4 chosen", string.Concat(Enumerable.Repeat("do damage 4 chosen\n", 9)), StringComparison.Ordinal);
            AssertHas(Errors(cards: tooMany), "効果は");
        }

        [Test]
        public void EnemyAndEncounterRuleErrorsAreReported()
        {
            AssertHas(Errors(enemies: Enemies.Replace("do summon imp", "do summon ghost", StringComparison.Ordinal)), "呼ぶ敵");
            AssertHas(Errors(enemies: Enemies.Replace("do damage 6 player", "do damage 6 self", StringComparison.Ordinal)), "対象");
            AssertHas(Errors(enemies: Enemies.Replace("do status weak 2 player", "do status weak 2 chosen", StringComparison.Ordinal)), "対象");
            AssertHas(Errors(enemies: Enemies.Replace("hp 16", "hp 0", StringComparison.Ordinal)), "HP");
            AssertHas(Errors(enemies: Enemies.Replace("move scratch 引っかき 2", "move scratch 引っかき 0", StringComparison.Ordinal)), "重み");
            var single = Enemies.Replace("move jab 二度突き 1\ndo damage 3 player\ndo damage 3 player\n", string.Empty, StringComparison.Ordinal);
            AssertHas(Errors(enemies: single), "普段の行動");
            AssertHas(Errors(encounters: Encounters.Replace("foe warden\nfoe imp", "foe warden\nfoe imp\nfoe imp\nfoe imp", StringComparison.Ordinal)), "敵は");
            AssertHas(Errors(encounters: Encounters.Replace("tier hard", "tier normal", StringComparison.Ordinal)), "Hard");
            AssertHas(Errors(encounters: Encounters.Replace("tier boss", "tier hard", StringComparison.Ordinal)), "Boss");
        }

        [Test]
        public void ConfigErrorsAreReported()
        {
            Assert.That(new DeckConfig().Validate(), Is.Empty);
            AssertHas(new DeckConfig { Energy = 0 }.Validate(), "Energy");
            AssertHas(new DeckConfig { HandLimit = 4 }.Validate(), "HandLimit");
            AssertHas(new DeckConfig { NormalRarityWeights = _zeroWeights }.Validate(), "合計");
            AssertHas(new DeckConfig { HardRarityWeights = _twoWeights }.Validate(), "3 つ");
            AssertHas(new DeckConfig { PlayerMaxHp = 999, RestMaxHpGain = 1 }.Validate(), "PlayerMaxHp + RestMaxHpGain");

            // Rare だけの重みでは報酬に出うるカードが 4 種なので、5 択は組めない。
            AssertHas(Errors(config: new DeckConfig { NormalRarityWeights = _rareOnly, RewardChoices = 5 }), "報酬");
            Assert.That(() => GameData.Load(Cards, Enemies, Encounters, new DeckConfig { Energy = 1 }), Throws.InvalidOperationException);
        }

        [Test]
        public void ConfigCopyIsDeep()
        {
            var config = new DeckConfig();
            var copy = config.Copy();
            copy.NormalRarityWeights[0] = 1;
            Assert.That(config.NormalRarityWeights[0], Is.EqualTo(70));
        }

        [Test]
        public void FingerprintChangesWithAnyRuleValue()
        {
            var baseline = Fingerprint.Of(Lab.Data, new DeckConfig());
            Assert.That(Fingerprint.Of(Lab.Data, new DeckConfig()), Is.EqualTo(baseline));
            Assert.That(Fingerprint.Of(Lab.Data, new DeckConfig { VulnerablePercent = 151 }), Is.Not.EqualTo(baseline));
            var other = GameData.Parse(Cards.Replace("do damage 6 chosen", "do damage 7 chosen", StringComparison.Ordinal), Enemies, Encounters);
            Assert.That(Fingerprint.Of(other, new DeckConfig()), Is.Not.EqualTo(baseline));
        }
    }
}
