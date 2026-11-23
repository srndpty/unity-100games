using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>施設の警戒レベル。題材固有テスト「見失った後に警戒が減衰」の施設側。</summary>
    public sealed class AlertTests
    {
        [Test]
        public void ChaseRaisesToAlertOnce()
        {
            var config = Steady();
            var alert = default(AlertState);
            Assert.That(alert.Update(true, config), Is.True);
            Assert.That(alert.Level, Is.EqualTo(AlertLevel.Alert));
            Assert.That(alert.Update(true, config), Is.False, "追跡が続く間は 1 回と数える");
        }

        [Test]
        public void DecaysAtExactBoundaries()
        {
            var config = new WardConfig { AlertTicks = 5, CautionTicks = 7 };
            config.Validate();
            var alert = default(AlertState);
            alert.Update(true, config);

            // 追跡のない Tick が 4 回までは厳戒、5 回目で警戒。
            for (var t = 1; t <= 4; t++)
            {
                alert.Update(false, config);
                Assert.That(alert.Level, Is.EqualTo(AlertLevel.Alert), $"{t}");
            }

            alert.Update(false, config);
            Assert.That(alert.Level, Is.EqualTo(AlertLevel.Caution));

            for (var t = 1; t <= 6; t++)
            {
                alert.Update(false, config);
                Assert.That(alert.Level, Is.EqualTo(AlertLevel.Caution), $"{t}");
            }

            alert.Update(false, config);
            Assert.That(alert.Level, Is.EqualTo(AlertLevel.Normal));
            alert.Update(false, config);
            Assert.That(alert.Level, Is.EqualTo(AlertLevel.Normal));
            Assert.That(alert.Remaining, Is.Zero);
        }

        [Test]
        public void ChaseDuringCautionCountsAsANewDiscovery()
        {
            var config = new WardConfig { AlertTicks = 1, CautionTicks = 100 };
            config.Validate();
            var alert = default(AlertState);
            alert.Update(true, config);
            alert.Update(false, config);
            Assert.That(alert.Level, Is.EqualTo(AlertLevel.Caution));
            Assert.That(alert.Update(true, config), Is.True);
        }

        [Test]
        public void CautionWidensVision()
        {
            var config = Steady();
            Assert.That(config.VisionAt(AlertLevel.Normal).Range, Is.EqualTo(config.VisionRange));
            Assert.That(config.VisionAt(AlertLevel.Caution).Range, Is.EqualTo(config.AlertVisionRange));
            Assert.That(config.VisionAt(AlertLevel.Alert).Range, Is.EqualTo(config.AlertVisionRange));
        }
    }
}
