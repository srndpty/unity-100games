using System;
using NUnit.Framework;
using TileMotion;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>題材固有：弾数上限で破綻しない。固定容量の置き場は満杯の発射を捨てて数え、例外にも拡張にもしない。</summary>
    public sealed class BulletPoolTests
    {
        private static readonly Vec2i _center = Px(192, 224);

        [Test]
        public void Capacity_ExactlyFull_ThenOneMoreIsDropped()
        {
            var pool = new BulletPool(10);
            for (var i = 0; i < 10; i++) { Assert.That(pool.Spawn(Bullet(_center, 0, Speed(1))), Is.True); }
            Assert.That(pool.Count, Is.EqualTo(10));
            Assert.That(pool.Dropped, Is.EqualTo(0));

            Assert.That(pool.Spawn(Bullet(_center, 0, Speed(1))), Is.False);
            Assert.That(pool.Count, Is.EqualTo(10));
            Assert.That(pool.Dropped, Is.EqualTo(1));
            Assert.That(pool.Spawned, Is.EqualTo(10));
        }

        [Test]
        public void FreedSlots_AreReusedOnNextTick()
        {
            var pool = new BulletPool(4);
            for (var i = 0; i < 4; i++) { pool.Spawn(Bullet(_center, 0, Speed(1), life: i < 2 ? 1 : 0)); }
            pool.Step(); // 寿命 1 の 2 発は 1 回動いた
            Assert.That(pool.Spawn(Bullet(_center, 0, Speed(1))), Is.False, "まだ消えていない");
            pool.Step(); // Tick の初めに 2 発が消える
            Assert.That(pool.Count, Is.EqualTo(2));
            Assert.That(pool.Spawn(Bullet(_center, 0, Speed(1))), Is.True);
            Assert.That(pool.Spawn(Bullet(_center, 0, Speed(1))), Is.True);
            Assert.That(pool.Spawn(Bullet(_center, 0, Speed(1))), Is.False);
        }

        /// <summary>容量 64 に毎 Tick 24 発を 3000 Tick 撃ち続けても、数は容量を超えず、捨てた数も正しく数える。</summary>
        [Test]
        public void SustainedOverflow_NeverExceedsCapacityAndCountsDrops()
        {
            var pool = new BulletPool(64);
            long attempts = 0;
            var sawFull = 0;
            for (var t = 0; t < 3000; t++)
            {
                for (var i = 0; i < 24; i++)
                {
                    pool.Spawn(Bullet(_center, (Trig.FullTurn / 24 * i) + (t * 97), Speed(3), life: 90));
                    attempts++;
                }

                Assert.That(pool.Count, Is.LessThanOrEqualTo(64));
                if (pool.Count == 64) { sawFull++; }
                pool.Step();
                Assert.That(pool.Count, Is.LessThanOrEqualTo(64));
            }

            Assert.That(sawFull, Is.EqualTo(2998), "3 Tick 目からは毎 Tick 満杯に張り付く（24 → 48 → 64）");
            Assert.That(pool.Dropped, Is.GreaterThan(0));
            Assert.That(pool.Spawned + pool.Dropped, Is.EqualTo(attempts));
        }

        [Test]
        public void Life_MovesExactlyThatManyTimes()
        {
            var pool = new BulletPool(1);
            pool.Spawn(Bullet(_center, Trig.Right, Speed(1), life: 3));
            for (var t = 0; t < 3; t++) { pool.Step(); }
            Assert.That(pool.Count, Is.EqualTo(1));
            Assert.That(pool.AgeOf(0), Is.EqualTo(3));
            Assert.That(pool.PositionOf(0), Is.EqualTo(_center + Px(3, 0)));
            pool.Step();
            Assert.That(pool.Count, Is.EqualTo(0));
        }

        [Test]
        public void LeavingField_IsRemovedOnTheTickAfterCrossingMargin()
        {
            // 余白の外（x > 384 + 32 px）へ 1 Tick で出る位置から撃つ。
            var pool = new BulletPool(1);
            pool.Spawn(Bullet(Px(384 + 30, 100), Trig.Right, Speed(4)));
            pool.Step();
            Assert.That(pool.Count, Is.EqualTo(1), "出た Tick にはまだ居る（通り道の当たりを残す）");
            Assert.That(pool.PositionOf(0).X, Is.GreaterThan(Field.BulletBounds.MaxX));
            pool.Step();
            Assert.That(pool.Count, Is.EqualTo(0));
        }

        [Test]
        public void OnMarginEdge_StillAlive()
        {
            var pool = new BulletPool(1);
            pool.Spawn(Bullet(Px(384 + 31, 100), Trig.Right, Speed(1)));
            pool.Step(); // ちょうど余白の端
            pool.Step();
            Assert.That(pool.Count, Is.EqualTo(1));
            pool.Step();
            Assert.That(pool.Count, Is.EqualTo(0));
        }

        [Test]
        public void Removal_KeepsOrderOfRemainingBullets()
        {
            var pool = new BulletPool(8);
            pool.Spawn(Bullet(_center, 0, Speed(1), life: 1, shape: BulletShape.Small));
            pool.Spawn(Bullet(_center, 0, Speed(1), shape: BulletShape.Rice));
            pool.Spawn(Bullet(_center, 0, Speed(1), life: 1, shape: BulletShape.Orb));
            pool.Spawn(Bullet(_center, 0, Speed(1), shape: BulletShape.Large));
            pool.Step();
            pool.Step();
            Assert.That(pool.Count, Is.EqualTo(2));
            Assert.That(pool.ShapeOf(0), Is.EqualTo(BulletShape.Rice));
            Assert.That(pool.ShapeOf(1), Is.EqualTo(BulletShape.Large));
        }

        [Test]
        public void Clear_RemovesAllButKeepsCounters()
        {
            var pool = new BulletPool(2);
            pool.Spawn(Bullet(_center, 0, Speed(1)));
            pool.Spawn(Bullet(_center, 0, Speed(1)));
            pool.Spawn(Bullet(_center, 0, Speed(1)));
            pool.Clear();
            Assert.That(pool.Count, Is.EqualTo(0));
            Assert.That(pool.Dropped, Is.EqualTo(1));
            Assert.That(pool.Spawn(Bullet(_center, 0, Speed(1))), Is.True);
        }

        [Test]
        public void Clone_IsIndependentAndHashesEqual()
        {
            var pool = new BulletPool(8);
            pool.Spawn(Bullet(_center, 100, Speed(2), curve: 64));
            pool.Step();
            var copy = pool.Clone();
            Assert.That(HashOf(copy), Is.EqualTo(HashOf(pool)));

            copy.Step();
            copy.Spawn(Bullet(_center, 0, Speed(1)));
            Assert.That(pool.Count, Is.EqualTo(1));
            Assert.That(pool.AgeOf(0), Is.EqualTo(1));
            Assert.That(HashOf(copy), Is.Not.EqualTo(HashOf(pool)));
        }

        [Test]
        public void Hash_DistinguishesStatesThatLookTheSameNow()
        {
            // 今は同じ位置・速さでも、加速・角速度・寿命が違えば以後の動きが分かれる。
            static uint With(int accel, int curve, int life)
            {
                var pool = new BulletPool(1);
                pool.Spawn(Bullet(_center, 0, Speed(1), accel: accel, maxSpeed: Speed(4), curve: curve, life: life));
                return HashOf(pool);
            }

            var plain = With(0, 0, 0);
            Assert.That(With(1, 0, 0), Is.Not.EqualTo(plain));
            Assert.That(With(0, 1, 0), Is.Not.EqualTo(plain));
            Assert.That(With(0, 0, 5), Is.Not.EqualTo(plain));
        }

        [Test]
        public void Step_DoesNotAllocate()
        {
            var pool = new BulletPool(2048);
            for (var i = 0; i < 2048; i++) { pool.Spawn(Bullet(_center, i * 32, Speed(0.5), curve: 16, life: i % 7 == 0 ? 50 : 0)); }
            pool.Step();

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var t = 0; t < 100; t++) { pool.Step(); }
            var after = GC.GetAllocatedBytesForCurrentThread();
            Assert.That(after - before, Is.EqualTo(0));
            Assert.That(pool.Count, Is.LessThan(2048), "寿命の弾は消えた");
        }

        [Test]
        public void InvalidArguments_AreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new BulletPool(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new BulletPool(BulletPool.MaxCapacity + 1));

            var pool = new BulletPool(2);
            Assert.Throws<ArgumentOutOfRangeException>(() => pool.Spawn(Bullet(_center, 0, Speed(1), shape: (BulletShape)99)));
            Assert.Throws<ArgumentOutOfRangeException>(() => pool.Spawn(Bullet(_center, 0, Speed(3), maxSpeed: Speed(2))));
            Assert.Throws<ArgumentOutOfRangeException>(() => pool.Spawn(Bullet(_center, 0, -1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => pool.Spawn(Bullet(_center, 0, Speed(1), life: -1)));
            Assert.That(pool.Count, Is.EqualTo(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => pool.PositionOf(0));
        }

        private static uint HashOf(BulletPool pool)
        {
            var hash = StateHash.Start();
            pool.MixInto(ref hash);
            return hash.Value;
        }
    }
}
