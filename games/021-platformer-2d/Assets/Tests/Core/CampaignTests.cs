using System;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>3 面を通した試合：残機・スコア・面の切り替え。</summary>
    public sealed class CampaignTests
    {
        private readonly PlatformerConfig _config = new();

        /// <summary>右へ歩けばすぐゴールに着く面。</summary>
        private static Level Short() => Map(
            "...G",
            "S..G",
            "####");

        /// <summary>右へ歩くと穴に落ちる面。</summary>
        private static Level Pit() => Map(
            "G...",
            "S...",
            "#...");

        private static Campaign Create(PlatformerConfig? config = null, params Level[] levels)
        {
            return new Campaign(config ?? new PlatformerConfig(), levels.Length == 0 ? new[] { Short(), Short(), Short() } : levels);
        }

        private static void TickUntilStop(Campaign campaign, PlayerInput input)
        {
            for (var i = 0; i < 1000 && campaign.Status == CampaignStatus.Playing; i++)
            {
                campaign.Tick(input);
            }
        }

        [Test]
        public void Clear_WaitsThenAdvancesExactlyOnce()
        {
            var campaign = Create();
            TickUntilStop(campaign, Right);
            Assert.That(campaign.Status, Is.EqualTo(CampaignStatus.LevelCleared));
            Assert.That(campaign.TakeEvents().Has(RunEventKind.Cleared), Is.True);
            Assert.That(campaign.Tick(Right), Is.False, "待っている間は進まない");

            Assert.That(campaign.Continue(), Is.True);
            Assert.That(campaign.LevelIndex, Is.EqualTo(1));
            Assert.That(campaign.Continue(), Is.False, "2 回目の Continue で面を飛ばさない");
            Assert.That(campaign.LevelIndex, Is.EqualTo(1));
            Assert.That(campaign.Status, Is.EqualTo(CampaignStatus.Playing));
        }

        [Test]
        public void Clear_AddsTimeBonusOnce()
        {
            var campaign = Create();
            TickUntilStop(campaign, Right);
            var remaining = campaign.Run.RemainingTicks / Units.TickRate;
            Assert.That(campaign.LastTimeBonus, Is.EqualTo(remaining * _config.TimeBonusPerSecond));
            Assert.That(campaign.Score, Is.EqualTo(campaign.LastTimeBonus));

            for (var i = 0; i < 10; i++) { campaign.Tick(Right); }
            Assert.That(campaign.Score, Is.EqualTo(campaign.LastTimeBonus), "クリア後の Tick で加算されない");
        }

        [Test]
        public void ClearingLastLevel_Wins()
        {
            var campaign = Create();
            for (var level = 0; level < 3; level++)
            {
                TickUntilStop(campaign, Right);
                if (level < 2) { Assert.That(campaign.Continue(), Is.True); }
            }

            Assert.That(campaign.Status, Is.EqualTo(CampaignStatus.Won));
            Assert.That(campaign.IsOver, Is.True);
            Assert.That(campaign.Continue(), Is.False);
            Assert.That(campaign.Tick(Right), Is.False);
            Assert.That(campaign.LevelIndex, Is.EqualTo(2));
        }

        [Test]
        public void Miss_LosesLifeAndRetriesSameLevelFromStart()
        {
            var campaign = Create(null, Pit(), Short(), Short());
            TickUntilStop(campaign, Right);
            Assert.That(campaign.Status, Is.EqualTo(CampaignStatus.Missed));
            Assert.That(campaign.Lives, Is.EqualTo(_config.Lives - 1));
            Assert.That(campaign.TakeEvents().Has(RunEventKind.Missed), Is.True);

            Assert.That(campaign.Continue(), Is.True);
            Assert.That(campaign.LevelIndex, Is.Zero);
            Assert.That(campaign.Run.Ticks, Is.Zero, "面の最初からやり直す");
            Assert.That(campaign.Continue(), Is.False);
        }

        [Test]
        public void LosingAllLives_Loses()
        {
            var campaign = Create(new PlatformerConfig { Lives = 2 }, Pit(), Short(), Short());
            TickUntilStop(campaign, Right);
            campaign.Continue();
            TickUntilStop(campaign, Right);
            Assert.That(campaign.Status, Is.EqualTo(CampaignStatus.Lost));
            Assert.That(campaign.Lives, Is.Zero);
            Assert.That(campaign.Continue(), Is.False);
            Assert.That(campaign.Tick(Right), Is.False);
        }

        [Test]
        public void Stomp_AddsScore()
        {
            var stomp = Map(
                "G.......",
                "....S...",
                "........",
                "........",
                "........",
                "....h...",
                "########");
            var campaign = Create(null, stomp, Short(), Short());
            TickUntilStop(campaign, Idle);
            Assert.That(campaign.Score, Is.EqualTo(_config.StompScore));
            Assert.That(campaign.Status, Is.EqualTo(CampaignStatus.Playing), "踏んでも面は続く");
        }

        [Test]
        public void Continue_WhilePlayingIsIgnored()
        {
            var campaign = Create();
            Assert.That(campaign.Continue(), Is.False);
            Assert.That(campaign.LevelIndex, Is.Zero);
        }

        [Test]
        public void Constructor_RejectsInvalidArguments()
        {
            Assert.Throws<ArgumentException>(() => _ = new Campaign(_config, Array.Empty<Level>()));
            Assert.Throws<ArgumentException>(() => _ = new Campaign(_config, new Level[] { null! }));
            Assert.Throws<ArgumentNullException>(() => _ = new Campaign(null!, new[] { Short() }));
        }
    }
}
