using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Core;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class InvadersTests
    {
        private static InvadersState Create() => new(new InvadersConfig(), 123);
        private static void Field(InvadersState game, string name, object value) => typeof(InvadersState).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(game, value);
        private static T Get<T>(InvadersState game, string name) => (T)typeof(InvadersState).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(game)!;
        private static void Property(InvadersState game, string name, object value) => typeof(InvadersState).GetProperty(name)!.SetValue(game, value);
        private static void BulletAt(InvadersState game, int id, BulletOwner owner, float x, float y) => Get<List<Bullet>>(game, "_bullets").Add(new Bullet(id, owner, new Point(x, y)));

        [Test]
        public void InitialState_HasFormationShieldsAndLives()
        {
            var game = Create();
            Assert.That(game.EnemyCount, Is.EqualTo(32)); Assert.That(game.Lives, Is.EqualTo(3));
            Assert.That(game.Wave, Is.EqualTo(1)); Assert.That(game.ShieldCapacity, Is.EqualTo(216));
        }
        [TestCase(1)]
        [TestCase(-1)]
        public void Boundary_DescendsAndReversesExactlyOnce(int direction)
        {
            var game = Create();
            Property(game, "Direction", direction); Field(game, "_formationX", direction * 4.25f);
            var y = game.EnemyAt(0).Y;
            game.Step(default);
            Assert.That(game.Direction, Is.EqualTo(direction));
            Assert.That(game.EnemyAt(0).Y, Is.EqualTo(y));
            game.Step(default);
            Assert.That(game.Direction, Is.EqualTo(-direction));
            Assert.That(game.EnemyAt(0).Y, Is.EqualTo(y - .5f));
            game.Step(default);
            Assert.That(game.EnemyAt(0).Y, Is.EqualTo(y - .5f));
        }
        [Test]
        public void DestroyedOuterColumns_NoLongerDefineBoundary()
        {
            var game = Create();
            var enemies = Get<bool[]>(game, "_enemies");
            for (var i = 0; i < enemies.Length; i++) { enemies[i] = i % 8 == 0; }
            Field(game, "_formationX", 4.25f);
            for (var i = 0; i < 3; i++) { game.Step(default); }
            Assert.That(game.Direction, Is.EqualTo(1)); Assert.That(game.EnemyAt(0).Y, Is.EqualTo(6.5f));
        }
        [TestCase(BulletOwner.Player, -3.9f)]
        [TestCase(BulletOwner.Enemy, -1.7f)]
        public void Shield_ConsumesBulletAndOnlyOneCell(BulletOwner owner, float y)
        {
            var game = Create();
            var x = InvadersState.ShieldAt(0).X;
            BulletAt(game, 0, owner, x, y);
            for (var i = 0; i < 12; i++) { game.Step(default); }
            var destroyed = 0;
            for (var s = 0; s < game.ShieldCapacity; s++) { if (!game.ShieldAlive(s)) { destroyed++; } }
            Assert.That(destroyed, Is.EqualTo(1)); Assert.That(game.BulletCount, Is.Zero);
        }
        [Test]
        public void TwoBullets_AlreadyDestroyedCellDoesNotConsumeSecondBullet()
        {
            var game = Create();
            var p = InvadersState.ShieldAt(0);
            BulletAt(game, 0, BulletOwner.Player, p.X, p.Y);
            BulletAt(game, 1, BulletOwner.Player, p.X, p.Y);
            game.Step(default);
            Assert.That(game.ShieldAlive(0), Is.False);
            Assert.That(game.ShieldAlive(12), Is.False);
            Assert.That(game.BulletCount, Is.Zero);
        }
        private static InvadersState DescendingEnemyWithOneShield()
        {
            var game = Create(); var enemies = Get<bool[]>(game, "_enemies"); var shields = Get<bool[]>(game, "_shields");
            Array.Clear(enemies, 0, enemies.Length); enemies[0] = true; Property(game, "EnemyCount", 1);
            Array.Clear(shields, 0, shields.Length); shields[67] = true;
            Field(game, "_formationY", -1.6f); Field(game, "_descendPending", true);
            return game;
        }
        [TestCase(-2.6f, false)]
        [TestCase(-2.2f, true)]
        public void EnemyShieldContact_RespectsKillTime(float bulletY, bool shieldSurvives)
        {
            var game = DescendingEnemyWithOneShield();
            var enemy = new Motion(game.EnemyAt(0), 0, -.5f);
            var shieldTime = Motion.Contact(enemy, new Motion(InvadersState.ShieldAt(67), 0, 0), .625f, .475f);
            var killTime = Motion.Contact(new Motion(new Point(-4.9f, bulletY), 0, 20f / 60), enemy, .56f, .5f);
            Assert.That(shieldTime, Is.Not.Null); Assert.That(killTime, Is.Not.Null);
            Assert.That(killTime!.Value < shieldTime!.Value, Is.EqualTo(shieldSurvives));
            BulletAt(game, 0, BulletOwner.Player, -4.9f, bulletY);
            game.Step(default);
            Assert.That(game.EnemyAlive(0), Is.False); Assert.That(game.Score, Is.EqualTo(100));
            Assert.That(game.ShieldAlive(67), Is.EqualTo(shieldSurvives));
        }
        [TestCase(BulletOwner.Player, -2.775f)]
        [TestCase(BulletOwner.Enemy, -1.9f)]
        public void EnemyDestroysShield_First_LaterBulletPassesThrough(BulletOwner owner, float bulletY)
        {
            var game = DescendingEnemyWithOneShield();
            Field(game, "_formationX", .05f);
            var shield = InvadersState.ShieldAt(67);
            // 盾には当たるが敵の水平範囲からは外れる位置。
            var bulletX = shield.X - .175f;
            var enemy = new Motion(game.EnemyAt(0), 0, -.5f);
            var shieldTime = Motion.Contact(enemy, new Motion(shield, 0, 0), .625f, .475f);
            var dy = owner == BulletOwner.Player ? 20f / 60 : -6f / 60;
            var bulletTime = Motion.Contact(new Motion(new Point(bulletX, bulletY), 0, dy), new Motion(shield, 0, 0), .185f, .275f);
            Assert.That(bulletTime, Is.Not.Null); Assert.That(bulletTime!.Value, Is.GreaterThan(shieldTime!.Value));
            BulletAt(game, 0, owner, bulletX, bulletY);
            game.Step(default);
            Assert.That(game.ShieldAlive(67), Is.False); Assert.That(game.EnemyAlive(0), Is.True);
            Assert.That(game.BulletCount, Is.EqualTo(1)); Assert.That(game.BulletAt(0).Position.Y, Is.EqualTo(bulletY + dy));
        }
        [Test]
        public void EnemyKilledAtContactTime_DoesNotDestroyShield()
        {
            var game = DescendingEnemyWithOneShield(); Field(game, "_formationY", -2f);
            BulletAt(game, 0, BulletOwner.Player, -4.9f, -2.1f);
            game.Step(default);
            Assert.That(game.EnemyAlive(0), Is.False); Assert.That(game.ShieldAlive(67), Is.True);
        }
        [TestCase(BulletOwner.Player, BulletOwner.Player, false)]
        [TestCase(BulletOwner.Player, BulletOwner.Enemy, true)]
        [TestCase(BulletOwner.Enemy, BulletOwner.Player, true)]
        [TestCase(BulletOwner.Enemy, BulletOwner.Enemy, false)]
        public void OwnershipMask(BulletOwner owner, BulletOwner target, bool expected) => Assert.That(InvadersState.CanHit(owner, target), Is.EqualTo(expected));
        [Test]
        public void FriendlyBullets_DoNotHitFriendlyActorsOrEachOther()
        {
            var game = Create(); var enemy = game.EnemyAt(0);
            BulletAt(game, 0, BulletOwner.Player, 0, InvadersConfig.PlayerY);
            BulletAt(game, 1, BulletOwner.Enemy, enemy.X, enemy.Y);
            BulletAt(game, 2, BulletOwner.Player, 3, 0);
            BulletAt(game, 3, BulletOwner.Enemy, 3, .2f);
            game.Step(default);
            Assert.That(game.Lives, Is.EqualTo(3)); Assert.That(game.EnemyCount, Is.EqualTo(32)); Assert.That(game.BulletCount, Is.EqualTo(4));
        }
        [Test]
        public void LastEnemy_StartsNextWaveOnceAndKeepsDamageScoreLives()
        {
            var game = Create(); var enemies = Get<bool[]>(game, "_enemies");
            Array.Clear(enemies, 0, enemies.Length); enemies[0] = true; Property(game, "EnemyCount", 1);
            Get<bool[]>(game, "_shields")[0] = false;
            var p = game.EnemyAt(0); BulletAt(game, 0, BulletOwner.Player, p.X, p.Y);
            game.Step(default);
            Assert.That(game.Phase, Is.EqualTo(GamePhase.WaveTransition)); Assert.That(game.BulletCount, Is.Zero);
            for (var i = 0; i < 60; i++) { game.Step(default); }
            Assert.That(game.Wave, Is.EqualTo(2)); Assert.That(game.EnemyCount, Is.EqualTo(32));
            Assert.That(game.ShieldAlive(0), Is.False); Assert.That(game.Score, Is.EqualTo(100)); Assert.That(game.Lives, Is.EqualTo(3));
        }
        [Test]
        public void ThirdWave_WinsAndIgnoresFurtherCommands()
        {
            var game = Create(); Property(game, "Wave", 3);
            var enemies = Get<bool[]>(game, "_enemies"); Array.Clear(enemies, 0, enemies.Length); Property(game, "EnemyCount", 0);
            game.Step(default); var tick = game.TickCount;
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Won));
            game.Step(new PlayerCommand(99, true)); Assert.That(game.TickCount, Is.EqualTo(tick));
        }
        [Test]
        public void SimultaneousLastKillAndFatalDamage_Loses()
        {
            var game = Create(); var enemies = Get<bool[]>(game, "_enemies");
            Array.Clear(enemies, 0, enemies.Length); enemies[0] = true; Property(game, "EnemyCount", 1); Property(game, "Lives", 1);
            var p = game.EnemyAt(0); BulletAt(game, 0, BulletOwner.Player, p.X, p.Y);
            BulletAt(game, 1, BulletOwner.Enemy, 0, InvadersConfig.PlayerY);
            game.Step(default); Assert.That(game.Phase, Is.EqualTo(GamePhase.Lost));
        }
        [Test]
        public void LastKillAndNonfatalDamage_PreservesRespawnProtectionAcrossWave()
        {
            var game = Create(); var enemies = Get<bool[]>(game, "_enemies");
            Array.Clear(enemies, 0, enemies.Length); enemies[0] = true; Property(game, "EnemyCount", 1);
            var p = game.EnemyAt(0); BulletAt(game, 0, BulletOwner.Player, p.X, p.Y);
            BulletAt(game, 1, BulletOwner.Enemy, 0, -6);
            game.Step(default); Assert.That(game.Lives, Is.EqualTo(2)); Assert.That(game.Phase, Is.EqualTo(GamePhase.WaveTransition));
            for (var i = 0; i < 60; i++) { game.Step(default); }
            Assert.That(game.Wave, Is.EqualTo(2)); Assert.That(game.InvincibleTicks, Is.EqualTo(60));
        }
        [Test]
        public void MultipleHits_CostOneLifeAndRespawnClearsBullets()
        {
            var game = Create();
            BulletAt(game, 0, BulletOwner.Enemy, 0, -6); BulletAt(game, 1, BulletOwner.Enemy, 0, -6);
            game.Step(default);
            Assert.That(game.Lives, Is.EqualTo(2)); Assert.That(game.Phase, Is.EqualTo(GamePhase.Respawning)); Assert.That(game.BulletCount, Is.Zero);
            var p = game.EnemyAt(0);
            for (var i = 0; i < 60; i++) { game.Step(new PlayerCommand(1, true)); }
            Assert.That(game.EnemyAt(0).X, Is.EqualTo(p.X)); Assert.That(game.PlayerX, Is.Zero);
            Assert.That(game.InvincibleTicks, Is.EqualTo(60));
            BulletAt(game, 2, BulletOwner.Enemy, 0, -6); game.Step(default); Assert.That(game.Lives, Is.EqualTo(2));
        }
        [Test]
        public void Invasion_LosesAndDestroysTouchedShield()
        {
            var game = Create(); Field(game, "_formationY", -2f); game.Step(default);
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Lost));
            var count = 0; for (var i = 0; i < game.ShieldCapacity; i++) { if (!game.ShieldAlive(i)) { count++; } }
            Assert.That(count, Is.GreaterThan(0));
        }
        [Test]
        public void HoldingFire_RespectsCooldownAndCapacity()
        {
            var config = new InvadersConfig { PlayerBulletLimit = 1, EnemyBulletLimit = 1 };
            var game = new InvadersState(config, 9);
            for (var tick = 0; tick < 5000 && game.Phase != GamePhase.Lost; tick++)
            {
                game.Step(new PlayerCommand(1, true));
                var own = 0; var enemy = 0;
                for (var b = 0; b < game.BulletCount; b++) { if (game.BulletAt(b).Owner == BulletOwner.Player) { own++; } else { enemy++; } }
                Assert.That(own, Is.LessThanOrEqualTo(1)); Assert.That(enemy, Is.LessThanOrEqualTo(1));
                Assert.That(Math.Abs(game.PlayerX), Is.LessThanOrEqualTo(9.5f));
            }
        }
        [Test]
        public void MovingPlayer_IsHitAtContactTime()
        {
            var game = new InvadersState(new InvadersConfig { PlayerSpeed = 30 }, 3);
            BulletAt(game, 0, BulletOwner.Enemy, -.55f, -5.5f);
            game.Step(new PlayerCommand(1, false));
            Assert.That(game.Lives, Is.EqualTo(3));
            BulletAt(game, 1, BulletOwner.Enemy, .5f, -5.6f);
            game.Step(new PlayerCommand(1, false)); Assert.That(game.Lives, Is.EqualTo(2));
        }
        [Test]
        public void SweptMotion_DetectsCrossingAndUsesStopTime()
        {
            var bullet = new Motion(new Point(0, -3), 0, 6);
            Assert.That(Motion.Contact(bullet, new Motion(new Point(0, 0), 0, 0), .1f, .1f), Is.Not.Null);
            var stopped = new Motion(new Point(0, 0), 10, 0, .1f);
            Assert.That(stopped.At(.5f).X, Is.EqualTo(1));
            Assert.That(Motion.Contact(new Motion(new Point(1, -2), 0, 4), stopped, .1f, .1f), Is.Not.Null);
        }
        [Test]
        public void InvalidConfigAndCommand_AreRejected()
        {
            foreach (var bad in new[] { float.NaN, float.PositiveInfinity, -1f, 0f, 121f })
            {
                Assert.Throws<ArgumentException>(() => new InvadersConfig { PlayerBulletSpeed = bad }.Validate());
            }
            Assert.Throws<ArgumentException>(() => new InvadersConfig { Columns = 11 }.Validate());
            Assert.Throws<ArgumentOutOfRangeException>(() => Create().Step(new PlayerCommand(2, false)));
        }
        [TestCase(1f / 60, 1)]
        [TestCase(.1f, 6)]
        [TestCase(.1001f, 7)]
        [TestCase(.25f, 15)]
        [TestCase(1f, 60)]
        public void DurationConversion_UsesWholeTickBoundaries(float seconds, int ticks) => Assert.That(InvadersConfig.Ticks(seconds), Is.EqualTo(ticks));
        [Test]
        public void DurationConversion_DistinguishesAdjacentFloatsAtEveryConfiguredBoundary()
        {
            for (var tick = 1; tick <= 180; tick++)
            {
                var boundary = (float)((double)tick / InvadersConfig.TickRate);
                var bits = BitConverter.SingleToInt32Bits(boundary);
                var below = BitConverter.Int32BitsToSingle(bits - 1);
                var above = BitConverter.Int32BitsToSingle(bits + 1);
                Assert.That(InvadersConfig.Ticks(boundary), Is.EqualTo(tick), "boundary " + tick);
                Assert.That(InvadersConfig.Ticks(below), Is.EqualTo(tick), "below " + tick);
                Assert.That(InvadersConfig.Ticks(above), Is.EqualTo(tick + 1), "above " + tick);
            }
        }
        [Test]
        public void EveryFloatSetting_RejectsNonFiniteValues()
        {
            foreach (var field in typeof(InvadersConfig).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.FieldType != typeof(float)) { continue; }
                foreach (var bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                {
                    var config = new InvadersConfig(); field.SetValue(config, bad);
                    Assert.Throws<ArgumentException>(config.Validate, field.Name);
                }
            }
        }
        [Test]
        public void LastInvincibleTick_IsProtectedThenExpires()
        {
            var game = Create(); Property(game, "InvincibleTicks", 1);
            BulletAt(game, 0, BulletOwner.Enemy, 0, -6); game.Step(default);
            Assert.That(game.Lives, Is.EqualTo(3)); Assert.That(game.InvincibleTicks, Is.Zero);
            BulletAt(game, 1, BulletOwner.Enemy, 0, -6); game.Step(default);
            Assert.That(game.Lives, Is.EqualTo(2));
        }
        [Test]
        public void RandomizedMaximumConfig_StaysWithinCapacityAndField()
        {
            var config = new InvadersConfig { Columns = 10, Rows = 5, PlayerBulletLimit = 8, EnemyBulletLimit = 24, PlayerSpeed = 30, PlayerBulletSpeed = 120, EnemyBulletSpeed = 60, FormationSpeed = 5, WaveSpeedIncrease = 2, Descent = 1, PlayerFireSeconds = 1f / 60, EnemyFireSeconds = .1f, WaveFireMultiplier = .5f };
            uint random = 99;
            for (var seed = 1; seed <= 8; seed++)
            {
                var game = new InvadersState(config, seed);
                for (var tick = 0; tick < 10000 && game.Phase != GamePhase.Lost && game.Phase != GamePhase.Won; tick++)
                {
                    random ^= random << 13; random ^= random >> 17; random ^= random << 5;
                    game.Step(new PlayerCommand((int)(random % 3) - 1, true));
                    Assert.That(game.BulletCount, Is.LessThanOrEqualTo(32)); Assert.That(Math.Abs(game.PlayerX), Is.LessThanOrEqualTo(9.5f));
                    for (var e = 0; e < game.EnemyCapacity; e++) { if (game.EnemyAlive(e)) { Assert.That(Math.Abs(game.EnemyAt(e).X), Is.LessThanOrEqualTo(9.50001f)); } }
                }
            }
        }
        [Test]
        public void SameSeedAndInputs_ReproduceState()
        {
            var a = Create(); var b = Create();
            for (var i = 0; i < 4000; i++)
            {
                var input = new PlayerCommand(i % 120 < 60 ? 1 : -1, true); a.Step(input); b.Step(input);
                Assert.That(a.Score, Is.EqualTo(b.Score)); Assert.That(a.Lives, Is.EqualTo(b.Lives)); Assert.That(a.BulletCount, Is.EqualTo(b.BulletCount));
                for (var j = 0; j < a.BulletCount; j++) { Assert.That(a.BulletAt(j).Position.X, Is.EqualTo(b.BulletAt(j).Position.X)); Assert.That(a.BulletAt(j).Position.Y, Is.EqualTo(b.BulletAt(j).Position.Y)); }
            }
        }
    }
}
