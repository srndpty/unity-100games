using System;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>
    /// レビュー：ショットの同時存在数は設定の検証で上限（<see cref="VeilConfig.MaxShots"/>）以下に保証する。
    /// 遅いショットを毎 Tick 撃つ設定は、ボスに届かないショットが溜まり、1 発ごとの描画物も増え続けるので拒否する。
    /// </summary>
    public sealed class ShotCapacityTests
    {
        [Test]
        public void SlowestShotEveryTick_IsRejected()
        {
            var config = new VeilConfig { ShotSpeed = 1, ShotIntervalTicks = 1 };
            Assert.That(config.ShotCapacity, Is.GreaterThan(100000));
            Assert.Throws<ArgumentOutOfRangeException>(config.Validate);
        }

        [Test]
        public void Default_HoldsThirtyShots()
        {
            var config = new VeilConfig();
            Assert.That(config.ShotCapacity, Is.EqualTo(30));
            Assert.DoesNotThrow(config.Validate);
        }

        [Test]
        public void Boundary_LastAcceptedSpeedAndOneSlower()
        {
            // 毎 Tick 撃つとき、検証を通るいちばん遅い速さを探し、1 単位遅くすると拒否されることを確かめる。
            var config = new VeilConfig { ShotIntervalTicks = 1 };
            var speed = 1;
            while (true)
            {
                config.ShotSpeed = speed;
                if (config.ShotCapacity <= VeilConfig.MaxShots) { break; }
                speed++;
            }

            Assert.DoesNotThrow(config.Validate);
            Assert.That(config.ShotCapacity, Is.LessThanOrEqualTo(VeilConfig.MaxShots));
            config.ShotSpeed = speed - 1;
            Assert.That(config.ShotCapacity, Is.GreaterThan(VeilConfig.MaxShots));
            Assert.Throws<ArgumentOutOfRangeException>(config.Validate);
        }

        /// <summary>
        /// 乱数の設定（検証を通るもの）で、自機を左下の隅（いちばん低く、ボスに当たらない位置）に置いて撃ち続けても、
        /// 数は <see cref="VeilConfig.ShotCapacity"/> を超えない。ちょうどその数に届く設定もある（見積もりが緩すぎない）。
        /// </summary>
        [Test]
        public void ObservedShots_NeverExceedTheCapacity_AndReachItSometimes()
        {
            var random = new SeededRandom(97);
            var tested = 0;
            var reached = 0;
            while (tested < 60)
            {
                var config = QuickConfig();
                config.ShotIntervalTicks = random.Range(1, 12);
                config.ShotSpeed = random.Range(2 * Field.Unit, 32 * Field.Unit);
                config.ShotRadius = random.Range(Field.Unit, 8 * Field.Unit);
                config.PlayerMargin = random.Range(config.PlayerRadius, 40 * Field.Unit);
                config.PlayerStartY = Math.Max(config.PlayerStartY, config.PlayerMargin);
                if (config.ShotCapacity > VeilConfig.MaxShots) { continue; }
                tested++;

                var run = new VeilRun(config, Bosses(Quiet, radius: 8), 1);
                var corner = new PlayerCommand(-1, -1, false, false, false);
                StepUntil(run, r => r.Player == new Vec2i(config.PlayerMargin, config.PlayerMargin), corner);
                var shoot = new PlayerCommand(-1, -1, false, true, false);
                var most = 0;
                var ticks = (int)(config.ShotCapacity / VeilConfig.ShotsPerVolley * config.ShotIntervalTicks) + (3 * config.ShotIntervalTicks) + 5;
                for (var t = 0; t < ticks; t++)
                {
                    run.Step(shoot);
                    Assert.That(run.Shots.Count, Is.LessThanOrEqualTo(config.ShotCapacity), $"速さ {config.ShotSpeed}・間隔 {config.ShotIntervalTicks}");
                    most = Math.Max(most, run.Shots.Count);
                }

                if (most == config.ShotCapacity) { reached++; }
            }

            Assert.That(reached, Is.GreaterThan(10), "見積もりの上限にちょうど届く設定がある");
        }
    }
}
