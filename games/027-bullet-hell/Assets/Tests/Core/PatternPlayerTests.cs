using System;
using System.Linq;
using NUnit.Framework;
using TileMotion;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>台本の発射器とボスの動き：撃つ Tick・弾の向き・乱数の系列・動きの補間。</summary>
    public sealed class PatternPlayerTests
    {
        private static readonly Vec2i _boss = Px(192, 360);
        private static readonly Vec2i _player = Px(192, 60);
        private static readonly int[] _expectedFireTicks = { 4, 9, 14, 19 };

        [Test]
        public void Emitter_FiresOnDelayThenEvery()
        {
            var player = Play("emit ring every=5 delay=3 count=1 speed=1");
            var pool = new BulletPool(64);
            var fired = new int[20];
            for (var t = 1; t <= 20; t++)
            {
                var before = pool.Spawned;
                player.Step(_player, pool);
                fired[t - 1] = (int)(pool.Spawned - before);
            }

            // 1 Tick 目が t = 0。delay=3 なので 4, 9, 14, 19 Tick 目に撃つ。
            var ticks = Enumerable.Range(1, 20).Where(t => fired[t - 1] > 0).ToArray();
            Assert.That(ticks, Is.EqualTo(_expectedFireTicks));
        }

        [Test]
        public void Ring_IsEvenlySpacedAndSpinRotatesEachVolley()
        {
            var player = Play("emit ring every=10 count=24 speed=1 angle=90 spin=7.5");
            var pool = new BulletPool(64);
            player.Step(_player, pool);
            var first = Enumerable.Range(0, 24).Select(i => AngleOfSpawn(pool, i)).ToArray();

            for (var i = 0; i < 24; i++)
            {
                Assert.That(first[i], Is.EqualTo(Trig.Normalize(Trig.Up + IntMath.RoundDiv((long)Trig.FullTurn * i, 24))), $"#{i}");
            }

            for (var t = 0; t < 10; t++) { player.Step(_player, pool); }
            Assert.That(pool.Count, Is.EqualTo(48));
            var spin = Degrees(7.5);
            for (var i = 0; i < 24; i++)
            {
                Assert.That(AngleOfSpawn(pool, 24 + i), Is.EqualTo(Trig.Normalize(first[i] + spin)), $"2 回目 #{i}");
            }
        }

        [Test]
        public void Aimed_CentersOnPlayerAndFanIsSymmetric()
        {
            var player = Play("emit aimed every=60 count=5 spread=40 speed=2");
            var pool = new BulletPool(8);
            var target = _boss + Px(-100, -100); // 左下 45 度
            player.Step(target, pool);

            var angles = Enumerable.Range(0, 5).Select(i => AngleOfSpawn(pool, i)).ToArray();
            var aim = Trig.FullTurn * 5 / 8;
            Assert.That(angles[2], Is.EqualTo(aim), "中央は自機の向き");
            Assert.That(Trig.Normalize(angles[0] - aim + Trig.FullTurn), Is.EqualTo(Trig.Normalize(-Degrees(20))));
            Assert.That(Trig.Normalize(angles[4] - aim), Is.EqualTo(Degrees(20)));
            Assert.That(Trig.Normalize(angles[1] - aim + Trig.FullTurn), Is.EqualTo(Trig.Normalize(aim - angles[3] + Trig.FullTurn)), "中心について対称");
        }

        [Test]
        public void Aimed_AtPlayerOnTopOfOrigin_FiresDown()
        {
            var player = Play("emit aimed every=60 count=1 speed=2");
            var pool = new BulletPool(2);
            player.Step(_boss, pool);
            Assert.That(AngleOfSpawn(pool, 0), Is.EqualTo(Trig.Down));
        }

        [Test]
        public void Fan_WithOneBullet_UsesCenter()
        {
            var emitter = ScriptOf("emit fan every=1 count=1 spread=90 speed=1").Emitters[0];
            Assert.That(PatternPlayer.OffsetOf(emitter, 0), Is.EqualTo(0));
        }

        [Test]
        public void Bullets_StartAtBossPlusOffsetAfterThisTicksMove()
        {
            var player = Play("move to at=192,300 over=4\nemit ring every=1 count=1 speed=1 offset=10,-8");
            var pool = new BulletPool(8);
            player.Step(_player, pool);
            Assert.That(player.Boss, Is.EqualTo(Px(192, 345)));
            pool.Step();
            Assert.That(pool.StartOf(0), Is.EqualTo(Px(192 + 10, 345 - 8)));
        }

        [Test]
        public void MoveTo_ReachesTargetOnLastTickAndSequenceRepeats()
        {
            var player = Play("move to at=100,400 over=3\nmove to at=192,360 over=2\nemit ring every=99 speed=1");
            var pool = new BulletPool(8);
            var path = new Vec2i[7];
            for (var t = 0; t < 7; t++)
            {
                player.Step(_player, pool);
                path[t] = player.Boss;
            }

            Assert.That(path[2], Is.EqualTo(Px(100, 400)), "3 Tick 目に着く");
            Assert.That(path[4], Is.EqualTo(Px(192, 360)), "2 つ目の動きで戻る");
            Assert.That(path[5], Is.EqualTo(path[0]), "列を繰り返す");
            Assert.That(path[0], Is.EqualTo(new Vec2i(_boss.X + IntMath.RoundDiv((100 - 192) * Field.Unit, 3), _boss.Y + IntMath.RoundDiv(40 * Field.Unit, 3))));
        }

        [Test]
        public void Wander_StaysInAreaAndDependsOnSeed()
        {
            const string text = "move wander area=96,280,288,400 over=7\nemit ring every=99 speed=1";
            var a = Play(text, seed: 1);
            var b = Play(text, seed: 1);
            var c = Play(text, seed: 2);
            var pool = new BulletPool(64);
            var differs = false;
            for (var t = 0; t < 700; t++)
            {
                a.Step(_player, pool);
                b.Step(_player, pool);
                c.Step(_player, pool);
                Assert.That(a.Boss, Is.EqualTo(b.Boss));
                differs |= a.Boss != c.Boss;
                if (t % 7 == 6)
                {
                    Assert.That(new Rect(96 * Field.Unit, 280 * Field.Unit, 288 * Field.Unit, 400 * Field.Unit).Contains(a.Boss), Is.True);
                }
            }

            Assert.That(differs, Is.True);
        }

        [Test]
        public void Rand_IsDeterministicPerSeedAndVariesAcrossSeeds()
        {
            const string text = "emit ring every=1 count=4 angle=rand(0,360) speed=rand(1,3)\nemit ring every=1 count=1 speed=2";
            static int[] Run(int seed)
            {
                var player = Play(text, seed);
                var pool = new BulletPool(512);
                for (var t = 0; t < 50; t++) { player.Step(_player, pool); }
                return Enumerable.Range(0, pool.Count).SelectMany(i => new[] { pool.AngleOf(i), pool.SpeedOf(i) }).ToArray();
            }

            Assert.That(Run(5), Is.EqualTo(Run(5)));
            Assert.That(Run(5), Is.Not.EqualTo(Run(6)));
            var values = Run(5);
            for (var i = 1; i < values.Length; i += 2) { Assert.That(values[i], Is.InRange(Speed(1), Speed(3))); }
        }

        [Test]
        public void AddingRandToOneEmitter_DoesNotChangeAnother()
        {
            static int[] SecondEmitterAngles(string first)
            {
                var player = Play(first + "\nemit ring every=3 count=2 angle=rand(0,90) speed=1 shape=large");
                var pool = new BulletPool(512);
                for (var t = 0; t < 30; t++) { player.Step(_player, pool); }
                return Enumerable.Range(0, pool.Count).Where(i => pool.ShapeOf(i) == BulletShape.Large).Select(i => AngleOfSpawn(pool, i)).ToArray();
            }

            Assert.That(
                SecondEmitterAngles("emit ring every=2 count=3 speed=1"),
                Is.EqualTo(SecondEmitterAngles("emit ring every=2 count=3 speed=rand(1,2) angle=rand(0,45)")));
        }

        [Test]
        public void Clone_ContinuesIdentically()
        {
            var player = Play("move wander area=0,0,384,448 over=5\nemit ring every=2 count=3 angle=rand(0,360) speed=1");
            var pool = new BulletPool(1024);
            for (var t = 0; t < 13; t++) { player.Step(_player, pool); }
            var copy = player.Clone();
            var copyPool = pool.Clone();
            for (var t = 0; t < 40; t++)
            {
                player.Step(_player, pool);
                copy.Step(_player, copyPool);
            }

            Assert.That(HashOf(copy, copyPool), Is.EqualTo(HashOf(player, pool)));
        }

        [Test]
        public void FullPool_DropsWithoutThrowing()
        {
            var player = Play("emit ring every=1 count=100 speed=1");
            var pool = new BulletPool(150);
            for (var t = 0; t < 5; t++) { player.Step(_player, pool); }
            Assert.That(pool.Count, Is.EqualTo(150));
            Assert.That(pool.Dropped, Is.EqualTo(350));
        }

        private static PatternSpec ScriptOf(string body) =>
            ScriptParser.ParseOrThrow("boss \"B\" radius=16\npattern \"P\" hp=10 time=600\n" + body, "test").Patterns[0];

        private static PatternPlayer Play(string body, int seed = 1) => new(ScriptOf(body), _boss, seed);

        /// <summary>まだ動いていない弾の向き（置き場の向きは Step の前は出た向きのまま）。</summary>
        private static int AngleOfSpawn(BulletPool pool, int index) => pool.AngleOf(index);

        private static uint HashOf(PatternPlayer player, BulletPool pool)
        {
            var hash = StateHash.Start();
            player.MixInto(ref hash);
            pool.MixInto(ref hash);
            return hash.Value;
        }
    }
}
