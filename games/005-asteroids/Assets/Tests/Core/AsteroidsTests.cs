using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Core;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class AsteroidsTests
    {
        private static T Get<T>(AsteroidsState game, string name) => (T)typeof(AsteroidsState).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!;
        private static void Set(AsteroidsState game, string name, object value) => typeof(AsteroidsState).GetProperty(name)!.SetValue(game, value);
        private static AsteroidsState Empty(AsteroidsConfig? config = null)
        {
            var game = new AsteroidsState(config ?? new AsteroidsConfig(), 1);
            Get<List<Asteroid>>(game, "_asteroids").Clear(); Set(game, "InvincibleTicks", 0);
            return game;
        }
        private static void Rock(AsteroidsState game, int id, int size, Point position, Point velocity = default)
            => Get<List<Asteroid>>(game, "_asteroids").Add(new Asteroid(id, size, position, velocity, 0, 1));
        private static void Shot(AsteroidsState game, int id, Point position, Point velocity = default, int ticks = 60)
            => Get<List<Bullet>>(game, "_bullets").Add(new Bullet(id, position, velocity, ticks));

        [Test]
        public void InitialState_HasSafeDistinctPositions()
        {
            for (var seed = 0; seed < 100; seed++)
            {
                var game = new AsteroidsState(new AsteroidsConfig { WaveCount = 1, FirstWaveCount = 8 }, seed);
                Assert.That(game.AsteroidCount, Is.EqualTo(8)); Assert.That(game.Lives, Is.EqualTo(3));
                for (var a = 0; a < 8; a++)
                {
                    Assert.That(Torus.Difference(game.AsteroidAt(a).Position, default).Length, Is.GreaterThan(5));
                    for (var b = 0; b < a; b++) { Assert.That(Torus.Difference(game.AsteroidAt(a).Position, game.AsteroidAt(b).Position).Length, Is.GreaterThan(2.4)); }
                }
            }
        }
        [TestCase(16f, -16f)]
        [TestCase(-16.5f, 15.5f)]
        [TestCase(112.5f, -15.5f)]
        public void Wrap_NormalizesMultiplePeriods(float value, float expected) => Assert.That(Torus.Wrap(value, 32), Is.EqualTo(expected));

        [TestCase(15.99f, 0f, 8f, 0f)]
        [TestCase(-15.99f, 0f, -8f, 0f)]
        [TestCase(0f, 8.99f, 0f, 8f)]
        [TestCase(0f, -8.99f, 0f, -8f)]
        [TestCase(15.99f, 8.99f, 4f, 4f)]
        public void ShipWrap_PreservesVelocity(float x, float y, float vx, float vy)
        {
            var game = Empty(); Rock(game, 1, 0, new Point(8, 4));
            Set(game, "ShipPosition", new Point(x, y)); Set(game, "ShipVelocity", new Point(vx, vy));
            game.Step(default);
            Assert.That(game.ShipVelocity.X, Is.EqualTo(vx)); Assert.That(game.ShipVelocity.Y, Is.EqualTo(vy));
            Assert.That(game.ShipPosition.X, Is.InRange(-16, 16)); Assert.That(game.ShipPosition.Y, Is.InRange(-9, 9));
        }
        [Test]
        public void ThrustAndTurn_KeepInertiaAndCapSpeed()
        {
            var game = Empty(); Rock(game, 1, 0, new Point(8, 4)); Set(game, "InvincibleTicks", 10000);
            for (var i = 0; i < 1000; i++) { game.Step(new PlayerCommand(0, true, false)); }
            Assert.That(game.ShipVelocity.Length, Is.EqualTo(8).Within(.001));
            game.Step(new PlayerCommand(10, false, false)); var angle = game.ShipAngle; var velocity = game.ShipVelocity;
            game.Step(default);
            Assert.That(game.ShipAngularVelocity, Is.Zero); Assert.That(game.ShipAngle, Is.EqualTo(angle)); Assert.That(game.ShipVelocity.X, Is.EqualTo(velocity.X));
        }
        [TestCase(15.5f, 0f, -15.5f, 0f, 120f, 0f)]
        [TestCase(0f, 8.5f, 0f, -8.5f, 0f, 120f)]
        [TestCase(15.5f, 8.5f, -15.5f, -8.5f, 120f, 120f)]
        public void PeriodicSweep_HitsAcrossEdgesAndCorners(float x, float y, float rx, float ry, float vx, float vy)
        {
            var time = Torus.Contact(new Point(x, y), new Point(vx / 60, vy / 60), new Point(rx, ry), default, .2f);
            Assert.That(time.HasValue, Is.True); Assert.That(time!.Value, Is.InRange(0f, 1f));
        }
        [Test]
        public void Sweep_DetectsMovingTargetAndRejectsMiss()
        {
            Assert.That(Torus.Contact(new Point(-1, 0), new Point(2, 0), new Point(1, 0), new Point(-2, 0), .1f), Is.EqualTo(.475f).Within(.0001));
            Assert.That(Torus.Contact(new Point(-1, 1), new Point(2, 0), default, default, .1f), Is.Null);
            Assert.That(Torus.Contact(default, default, default, default, .1f), Is.EqualTo(0));
        }
        [Test]
        public void BulletAndAsteroidWrap_PreserveLinearAndAngularVelocity()
        {
            var game = Empty(); Rock(game, 10, 0, new Point(15.99f, 8.99f), new Point(2, 2));
            Shot(game, 11, new Point(-15.99f, -6), new Point(-18, 0)); game.Step(default);
            Assert.That(game.AsteroidAt(0).Position.X, Is.LessThan(0)); Assert.That(game.AsteroidAt(0).Position.Y, Is.LessThan(0));
            Assert.That(game.AsteroidAt(0).Velocity.X, Is.EqualTo(2)); Assert.That(game.AsteroidAt(0).AngularVelocity, Is.EqualTo(1));
            Assert.That(game.BulletAt(0).Position.X, Is.GreaterThan(0)); Assert.That(game.BulletAt(0).Velocity.X, Is.EqualTo(-18));
        }
        [Test]
        public void FullSplitTree_HasBoundedCapacityAndExactScore()
        {
            var game = Empty(new AsteroidsConfig { WaveCount = 1, FirstWaveCount = 8 });
            for (var i = 0; i < 8; i++) { Rock(game, 100 + i, 2, new Point(i * 3 - 12, 4)); }
            Set(game, "InvincibleTicks", 1000);
            for (var tick = 0; tick < 100 && game.Phase == GamePhase.Playing; tick++)
            {
                Shot(game, 200 + tick, game.AsteroidAt(0).Position); game.Step(default);
                var potential = 0;
                for (var i = 0; i < game.AsteroidCount; i++) { potential += 1 << game.AsteroidAt(i).Size; }
                Assert.That(potential, Is.LessThanOrEqualTo(32));
            }
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Won)); Assert.That(game.Score, Is.EqualTo(4160));
        }
        [Test]
        public void EveryFloatSetting_RejectsNonFiniteValues()
        {
            foreach (var field in typeof(AsteroidsConfig).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.FieldType != typeof(float)) { continue; }
                foreach (var value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                {
                    var config = new AsteroidsConfig(); field.SetValue(config, value);
                    Assert.Throws<ArgumentException>(() => config.Validate(), field.Name);
                }
            }
        }
        [Test]
        public void NextWaveGeneration_DoesNotConsumeSplitRandomness()
        {
            var config = new AsteroidsConfig { TransitionSeconds = 1f / 60 };
            var a = Empty(config); var b = Empty(config);
            Rock(b, 100, 2, new Point(5, 0)); Shot(b, 101, new Point(5, 0)); b.Step(default);
            Get<List<Asteroid>>(b, "_asteroids").Clear(); a.Step(default); b.Step(default); a.Step(default); b.Step(default);
            Assert.That(a.Wave, Is.EqualTo(2)); Assert.That(b.Wave, Is.EqualTo(2));
            for (var i = 0; i < a.AsteroidCount; i++)
            {
                Assert.That(a.AsteroidAt(i).Position.X, Is.EqualTo(b.AsteroidAt(i).Position.X));
                Assert.That(a.AsteroidAt(i).Velocity.X, Is.EqualTo(b.AsteroidAt(i).Velocity.X));
                Assert.That(a.AsteroidAt(i).AngularVelocity, Is.EqualTo(b.AsteroidAt(i).AngularVelocity));
            }
        }
        [TestCase(2, 1, 20)]
        [TestCase(1, 0, 50)]
        [TestCase(0, -1, 100)]
        public void Hit_SplitsExactlyTwoAtContactPosition(int size, int childSize, int score)
        {
            var game = Empty(); Set(game, "InvincibleTicks", 100);
            Rock(game, 10, size, new Point(5, 0), new Point(1, 0)); Shot(game, 11, new Point(5, 0));
            game.Step(default);
            Assert.That(game.Score, Is.EqualTo(score)); Assert.That(game.BulletCount, Is.Zero);
            Assert.That(game.AsteroidCount, Is.EqualTo(size == 0 ? 0 : 2));
            for (var i = 0; i < game.AsteroidCount; i++)
            {
                Assert.That(game.AsteroidAt(i).Size, Is.EqualTo(childSize)); Assert.That(game.AsteroidAt(i).Position.X, Is.EqualTo(5));
            }
            if (size > 0)
            {
                Assert.That(game.AsteroidAt(0).Velocity.Plus(game.AsteroidAt(1).Velocity).X, Is.EqualTo(2).Within(.0001));
            }
        }
        [Test]
        public void OneBullet_ConsumesOnlyFirstTarget()
        {
            var game = Empty(); Rock(game, 10, 0, new Point(2, 0)); Rock(game, 11, 0, new Point(1, 0));
            Shot(game, 12, default, new Point(180, 0)); Set(game, "InvincibleTicks", 10);
            game.Step(default); Assert.That(game.AsteroidCount, Is.EqualTo(1)); Assert.That(game.AsteroidAt(0).Id, Is.EqualTo(10)); Assert.That(game.Score, Is.EqualTo(100));
        }
        [Test]
        public void DuplicateHits_DoNotHitNewChildren()
        {
            var game = Empty(); Rock(game, 10, 2, new Point(5, 0)); Shot(game, 11, new Point(5, 0)); Shot(game, 12, new Point(5, 0));
            game.Step(default); Assert.That(game.Score, Is.EqualTo(20)); Assert.That(game.AsteroidCount, Is.EqualTo(2)); Assert.That(game.BulletCount, Is.EqualTo(1));
        }
        [Test]
        public void Damage_OnceThenRespawnAndExactInvincibility()
        {
            var config = new AsteroidsConfig { RespawnSeconds = 1f / 60, InvincibleSeconds = 2f / 60 };
            var game = Empty(config); Rock(game, 1, 0, default); Rock(game, 2, 0, default);
            game.Step(default); Assert.That(game.Lives, Is.EqualTo(2)); Assert.That(game.Phase, Is.EqualTo(GamePhase.Respawning));
            game.Step(default); Assert.That(game.InvincibleTicks, Is.EqualTo(2));
            game.Step(default); game.Step(default); Assert.That(game.Lives, Is.EqualTo(2)); Assert.That(game.InvincibleTicks, Is.Zero);
            game.Step(default); Assert.That(game.Lives, Is.EqualTo(1));
        }
        [Test]
        public void EqualTime_BulletPreventsShipHit()
        {
            var game = Empty(); Rock(game, 1, 0, default); Shot(game, 2, default);
            game.Step(default); Assert.That(game.Lives, Is.EqualTo(3)); Assert.That(game.Score, Is.EqualTo(100)); Assert.That(game.Phase, Is.EqualTo(GamePhase.WaveTransition));
        }
        [Test]
        public void EarlierShipContact_CancelsLaterBullet()
        {
            var game = Empty(); Rock(game, 1, 0, default); Shot(game, 2, new Point(-1, 0), new Point(60, 0));
            game.Step(default); Assert.That(game.Score, Is.Zero); Assert.That(game.BulletCount, Is.Zero); Assert.That(game.Lives, Is.EqualTo(2));
        }
        [Test]
        public void BulletLifetime_AndCapacityNeverQueueShot()
        {
            var config = new AsteroidsConfig { BulletLimit = 1, BulletSeconds = 2f / 60, FireSeconds = 1f / 60 };
            var game = Empty(config); Rock(game, 1, 0, new Point(8, 4));
            game.Step(new PlayerCommand(0, false, true)); Assert.That(game.BulletCount, Is.EqualTo(1));
            game.Step(new PlayerCommand(0, false, true)); Assert.That(game.BulletCount, Is.Zero);
            game.Step(default); Assert.That(game.BulletCount, Is.Zero);
        }
        [Test]
        public void AllWaves_KeepScoreAndLivesThenIgnoreInput()
        {
            var game = Empty(new AsteroidsConfig { TransitionSeconds = 1f / 60 });
            for (var wave = 1; wave <= 3; wave++)
            {
                Get<List<Asteroid>>(game, "_asteroids").Clear(); game.Step(default);
                if (wave < 3) { Assert.That(game.Phase, Is.EqualTo(GamePhase.WaveTransition)); game.Step(default); Assert.That(game.AsteroidCount, Is.EqualTo(3 + wave)); }
            }
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Won)); var tick = game.TickCount;
            game.Step(new PlayerCommand(1, true, true)); Assert.That(game.TickCount, Is.EqualTo(tick));
        }
        [Test]
        public void LastLife_EndsAndRejectsFurtherDamage()
        {
            var game = Empty(new AsteroidsConfig { Lives = 1 }); Rock(game, 1, 0, default);
            game.Step(default); game.Step(default); Assert.That(game.Lives, Is.Zero); Assert.That(game.Phase, Is.EqualTo(GamePhase.Lost)); Assert.That(game.TickCount, Is.EqualTo(1));
        }
        [Test]
        public void Configuration_IsCopiedAndInvalidValuesRejected()
        {
            var config = new AsteroidsConfig(); var game = new AsteroidsState(config, 1); config.Acceleration = float.NaN;
            Assert.Throws<ArgumentException>(() => config.Validate()); Assert.DoesNotThrow(() => game.Step(new PlayerCommand(0, true, true)));
            Assert.Throws<ArgumentException>(() => new AsteroidsConfig { BulletSpeed = float.PositiveInfinity }.Validate());
            Assert.Throws<ArgumentException>(() => new AsteroidsConfig { WaveCount = 5, WaveIncrease = 2 }.Validate());
            Assert.Throws<ArgumentException>(() => new AsteroidsConfig { SmallRadius = -.1f }.Validate());
            Assert.Throws<ArgumentNullException>(() => new AsteroidsState(null!, 1));
        }
        [TestCase(.1f, 6)]
        [TestCase(1f / 60, 1)]
        [TestCase(1.2f, 72)]
        public void TickConversion_RecognizesBoundaryAndAdjacentFloats(float seconds, int ticks)
        {
            Assert.That(AsteroidsConfig.Ticks(seconds), Is.EqualTo(ticks));
            var bits = BitConverter.SingleToInt32Bits(seconds);
            Assert.That(AsteroidsConfig.Ticks(BitConverter.Int32BitsToSingle(bits + 1)), Is.EqualTo(ticks + 1));
            Assert.That(AsteroidsConfig.Ticks(BitConverter.Int32BitsToSingle(bits - 1)), Is.EqualTo(ticks));
        }
        [Test]
        public void FixedSeedAndMaximumSettings_PreserveBoundsAndCapacity()
        {
            var config = new AsteroidsConfig { FirstWaveCount = 8, WaveIncrease = 0, WaveCount = 5, BulletLimit = 16, FireSeconds = 1f / 60, BulletSpeed = 120, MaxSpeed = 16, SplitSpeed = 3, AsteroidMaxSpeed = 6 };
            var a = new AsteroidsState(config, 32); var b = new AsteroidsState(config, 32);
            uint random = 7;
            for (var i = 0; i < 20000; i++)
            {
                random ^= random << 13; random ^= random >> 17; random ^= random << 5;
                var command = new PlayerCommand((int)(random % 3) - 1, random % 5 != 0, true); a.Step(command); b.Step(command);
                Assert.That(a.ShipPosition.X, Is.EqualTo(b.ShipPosition.X)); Assert.That(a.Score, Is.EqualTo(b.Score));
                Assert.That(a.AsteroidCount, Is.LessThanOrEqualTo(32)); Assert.That(a.BulletCount, Is.LessThanOrEqualTo(16));
                for (var j = 0; j < a.AsteroidCount; j++)
                {
                    var asteroid = a.AsteroidAt(j); Assert.That(asteroid.Position.X, Is.InRange(-16f, 16f)); Assert.That(asteroid.Position.Y, Is.InRange(-9f, 9f));
                    Assert.That(asteroid.Velocity.Length, Is.LessThanOrEqualTo(12.001f));
                }
                if (a.Phase == GamePhase.Lost || a.Phase == GamePhase.Won) { a = new AsteroidsState(config, i + 1); b = new AsteroidsState(config, i + 1); }
            }
        }
    }
}
