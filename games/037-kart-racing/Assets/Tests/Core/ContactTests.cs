using System;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class ContactTests
    {
        private const int All = 0b11;

        [Test]
        public void ARearEndSlowsTheChaserAndPushesTheLeader()
        {
            var config = new KartConfig();
            var karts = Pair(new Vec2i(0, 0), 300, new Vec2i(1300, 0), 100);
            var deltas = new[] { new Vec2i(300, 0), new Vec2i(100, 0) };
            var bumped = KartContact.Resolve(karts, deltas, All, config);
            Assert.That(bumped, Is.EqualTo(All));
            Assert.That(karts[0].Speed, Is.LessThan(300));
            Assert.That(karts[1].Speed, Is.GreaterThan(100));
            Assert.That(karts[0].Speed, Is.LessThan(karts[1].Speed), "反発があれば追突した側が遅くなる");
            Assert.That(karts[0].Speed + karts[1].Speed, Is.InRange(398, 400), "等しい質量で運動量を保つ");
            AssertApart(karts, deltas, config);
        }

        [Test]
        public void NoBounceOnlyEqualizesTheNormalSpeed()
        {
            var config = new KartConfig { BumpBounce = 0 };
            var karts = Pair(new Vec2i(0, 0), 300, new Vec2i(1300, 0), 100);
            var deltas = new[] { new Vec2i(300, 0), new Vec2i(100, 0) };
            KartContact.Resolve(karts, deltas, All, config);
            Assert.That(karts[0].Speed, Is.EqualTo(200));
            Assert.That(karts[1].Speed, Is.EqualTo(200));
        }

        [Test]
        public void AGrazeInsideTheTickIsFoundEvenWhenBothEndsAreApart()
        {
            // 始めも終わりも直径より離れているが、すれ違う途中で触れる。端点だけの判定では見逃す。
            var config = new KartConfig();
            var a = new Vec2i(0, 0);
            var b = new Vec2i(500, 1100);
            var deltaA = new Vec2i(500, 0);
            var deltaB = new Vec2i(-500, 0);
            var diameter = config.Radius * 2;
            Assert.That(Vec2i.DistanceSquared(a, b), Is.GreaterThan((long)diameter * diameter));
            Assert.That(Vec2i.DistanceSquared(a + deltaA, b + deltaB), Is.GreaterThan((long)diameter * diameter));

            var karts = Pair(a, 500, b, 500);
            karts[1].Heading = Trig.FullTurn / 2;
            var deltas = new[] { deltaA, deltaB };
            Assert.That(KartContact.Resolve(karts, deltas, All, config), Is.EqualTo(All));
            AssertApart(karts, deltas, config);
        }

        [Test]
        public void TouchingExactlyIsNotAContact()
        {
            var config = new KartConfig();
            var karts = Pair(new Vec2i(0, 0), 0, new Vec2i(config.Radius * 2, 0), 0);
            var deltas = new[] { Vec2i.Zero, Vec2i.Zero };
            Assert.That(KartContact.Resolve(karts, deltas, All, config), Is.EqualTo(0));
            Assert.That(deltas[0], Is.EqualTo(Vec2i.Zero));

            var closer = Pair(new Vec2i(0, 0), 0, new Vec2i((config.Radius * 2) - 1, 0), 0);
            Assert.That(KartContact.Resolve(closer, deltas, All, config), Is.EqualTo(All));
        }

        [Test]
        public void OverlappingKartsSeparateWithoutGainingSpeed()
        {
            var config = new KartConfig();
            var karts = Pair(new Vec2i(0, 0), 0, new Vec2i(800, 0), 0);
            var deltas = new[] { Vec2i.Zero, Vec2i.Zero };
            Assert.That(KartContact.Resolve(karts, deltas, All, config), Is.EqualTo(All));
            Assert.That(karts[0].Speed, Is.EqualTo(0));
            Assert.That(karts[1].Speed, Is.EqualTo(0));
            AssertApart(karts, deltas, config);
        }

        [Test]
        public void SideBySideKartsKeepTheirTravel()
        {
            // 横に食い込んだまま同じ速さで走る 2 台は、押し離されるだけで前へ進み続ける（Tick の残りを捨てない）。
            var config = new KartConfig();
            var karts = Pair(new Vec2i(0, 0), 300, new Vec2i(0, 1100), 300);
            var deltas = new[] { new Vec2i(300, 0), new Vec2i(300, 0) };
            Assert.That(KartContact.Resolve(karts, deltas, All, config), Is.EqualTo(All));
            Assert.That(deltas[0].X, Is.EqualTo(300));
            Assert.That(deltas[1].X, Is.EqualTo(300));
            Assert.That(karts[0].Speed, Is.EqualTo(300));
            Assert.That(karts[1].Speed, Is.EqualTo(300));
            AssertApart(karts, deltas, config);
        }

        [Test]
        public void InactiveKartsAreGhosts()
        {
            var config = new KartConfig();
            var karts = Pair(new Vec2i(0, 0), 300, new Vec2i(800, 0), 0);
            var deltas = new[] { new Vec2i(300, 0), Vec2i.Zero };
            Assert.That(KartContact.Resolve(karts, deltas, 0b01, config), Is.EqualTo(0));
            Assert.That(deltas[0], Is.EqualTo(new Vec2i(300, 0)));
            Assert.That(karts[0].Speed, Is.EqualTo(300));
        }

        [Test]
        public void TheEarliestPairResolvesFirst()
        {
            // 0 番は 1 番に先に触れ、2 番には触れる前に跳ね返される。
            var config = new KartConfig();
            var karts = new[]
            {
                new Kart { Position = new Vec2i(0, 0), Speed = 400 },
                new Kart { Position = new Vec2i(1300, 0), Speed = 0 },
                new Kart { Position = new Vec2i(1600, 2000), Speed = 0, Heading = Trig.FullTurn * 3 / 4 },
            };
            var deltas = new[] { new Vec2i(400, 0), Vec2i.Zero, Vec2i.Zero };
            var bumped = KartContact.Resolve(karts, deltas, 0b111, config);
            Assert.That(bumped, Is.EqualTo(0b011));
        }

        [Test]
        public void RandomCrowdsStayInsideTheSweepLimit([Values(1, 2, 3)] int seed)
        {
            // 検証を通る最大の速さと半径で、4 台の乱数の配置と移動。掃引が上限を超えて例外にならない。
            var random = new Random(seed);
            var config = new KartConfig { BoostSpeed = 2000, Radius = 1100, MaxSpeed = 2000 }.Validate();
            Assert.That(KartConfig.ContactSweepBound(config.BoostSpeed, config.Radius), Is.LessThanOrEqualTo(Sweep.FirstLimit));
            for (var round = 0; round < 2000; round++)
            {
                var karts = new Kart[KartConfig.KartCount];
                var deltas = new Vec2i[KartConfig.KartCount];
                for (var i = 0; i < karts.Length; i++)
                {
                    var heading = random.Next(Trig.FullTurn);
                    var speed = random.Next(-config.ReverseSpeed, config.BoostSpeed + 1);
                    karts[i] = new Kart { Position = new Vec2i(random.Next(-3000, 3000), random.Next(-3000, 3000)), Heading = heading, Speed = speed };
                    deltas[i] = new Vec2i((int)(((long)speed * Trig.Cos(heading)) / Trig.One), (int)(((long)speed * Trig.Sin(heading)) / Trig.One));
                }

                Assert.DoesNotThrow(() => KartContact.Resolve(karts, deltas, 0b1111, config), $"round {round}");
                for (var i = 0; i < karts.Length; i++)
                {
                    Assert.That(karts[i].Speed, Is.InRange(-config.ReverseSpeed, config.BoostSpeed));
                }
            }
        }

        [Test]
        public void TheConfigRejectsASweepBeyondTheLimit()
        {
            var config = new KartConfig { MaxSpeed = 2000, BoostSpeed = 2500, Radius = 2000 };
            Assert.That(KartConfig.ContactSweepBound(config.BoostSpeed, config.Radius), Is.GreaterThan(Sweep.FirstLimit));
            Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());
            Assert.DoesNotThrow(() => new KartConfig().Validate());
        }

        private static Kart[] Pair(Vec2i a, int speedA, Vec2i b, int speedB) => new[]
        {
            new Kart { Position = a, Speed = speedA },
            new Kart { Position = b, Speed = speedB },
        };

        private static void AssertApart(Kart[] karts, Vec2i[] deltas, KartConfig config)
        {
            var diameter = (long)config.Radius * 2;
            var end = Vec2i.DistanceSquared(karts[0].Position + deltas[0], karts[1].Position + deltas[1]);
            Assert.That(end, Is.GreaterThanOrEqualTo(diameter * diameter), "Tick の終わりに重なっている");
        }
    }
}
