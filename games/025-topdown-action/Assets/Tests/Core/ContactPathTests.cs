using NUnit.Framework;
using TileMotion;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>
    /// 被弾の接触は、地形の解決と同じ横 → 縦の経路で判定する（始めと終わりを結ぶ直線ではない）。
    /// 斜めの移動量は既定の速さ 22 の斜め成分 15（2 軸とも 15 なら Tick の半分ずつ）。箱は自機と同じ 192 単位。
    /// </summary>
    public sealed class ContactPathTests
    {
        private const int S = Hero.Size;

        private static Motion RightThenUp(int step) => new(new Box(0, 0, S, S), new Box(step, 0, S, S), new Box(step, step, S, S));

        private static void AssertAt(Touch touch, long numerator, long denominator)
        {
            Assert.That(touch.Hit, Is.True);
            Assert.That(touch.Numerator * denominator, Is.EqualTo(numerator * touch.Denominator), $"{touch.Numerator}/{touch.Denominator}");
        }

        [Test]
        public void DefaultDiagonal_IsFifteenPerAxis() => Assert.That(VaultConfig.DiagonalOf(Config().HeroSpeed), Is.EqualTo(15));

        [Test]
        public void TouchOnTheHorizontalLeg_IsDetected_ThoughTheStraightLineMisses()
        {
            var motion = RightThenUp(15);
            var enemy = new Box(204, -190, S, S);

            Assert.That(Sweep.First(motion.Start, motion.End, enemy, enemy).Hit, Is.False, "直線では横に届く前に縦の重なりがなくなる");

            // 右端が 204 を越えるのは横の区間の 12/15、Tick 全体では 12/30。
            AssertAt(Sweep.Along(motion, enemy, enemy), 12, 30);
        }

        [Test]
        public void OnlyTheStraightLineTouches_IsNotAHit()
        {
            var motion = RightThenUp(15);
            var enemy = new Box(-191, 192, S, S);

            Assert.That(Sweep.First(motion.Start, motion.End, enemy, enemy).Hit, Is.True, "直線は左上の角をかすめる");
            Assert.That(Sweep.Along(motion, enemy, enemy).Hit, Is.False);
        }

        [Test]
        public void MovingEnemy_IsInterpolatedToTheSameTimeAsEachLeg()
        {
            // 自機は右へ 16（Tick の前半）→ 上へ 16（後半）。敵は Tick 全体で下へ 32。
            var motion = RightThenUp(16);
            var start = new Box(200, 210, S, S);
            var end = new Box(200, 178, S, S);

            // 前半は縦に重ならない（自機の上端 192・敵の下端 210〜194）。後半は 192 + 32(t - 1/2) > 210 - 32t で t > 17/32。
            AssertAt(Sweep.Along(motion, start, end), 17, 32);

            // 直線とみなすと t = 1/2 になる（時刻が食い違う）。
            AssertAt(Sweep.First(motion.Start, motion.End, start, end), 1, 2);
        }

        [Test]
        public void EnemyOnTheHorizontalLeg_IsEarlierThanAShotOnTheVerticalLeg()
        {
            var motion = RightThenUp(16);
            var enemy = new Box(200, -190, S, S);
            var shot = new Box(100, 200, Projectile.Size, Projectile.Size);

            var enemyTouch = Sweep.Along(motion, enemy, enemy);
            var shotTouch = Sweep.Along(motion, shot, shot);
            AssertAt(enemyTouch, 1, 4);
            AssertAt(shotTouch, 3, 4);
            Assert.That(enemyTouch.IsEarlierThan(shotTouch), Is.True);

            // 直線では敵に触れず、弾だけが先に当たる（選ぶ相手が変わる）。
            Assert.That(Sweep.First(motion.Start, motion.End, enemy, enemy).Hit, Is.False);
            AssertAt(Sweep.First(motion.Start, motion.End, shot, shot), 1, 2);
        }

        [Test]
        public void SingleAxisMotion_IsTheSameAsAStraightLine()
        {
            var right = new Motion(new Box(0, 0, S, S), new Box(22, 0, S, S), new Box(22, 0, S, S));
            var up = new Motion(new Box(0, 0, S, S), new Box(0, 0, S, S), new Box(0, 22, S, S));
            var start = new Box(200, 100, S, S);
            var end = new Box(190, 120, S, S);

            foreach (var motion in new[] { right, up })
            {
                var along = Sweep.Along(motion, start, end);
                var straight = Sweep.First(motion.Start, motion.End, start, end);
                Assert.That(along.Hit, Is.EqualTo(straight.Hit));
                Assert.That(along.Numerator * straight.Denominator, Is.EqualTo(straight.Numerator * along.Denominator));
            }
        }

        [Test]
        public void AxisStoppedByAWall_UsesTheStoppedPath()
        {
            // 左の壁（地図の外）まで 5 単位。左上へ歩くと横は 5 で止まり、縦だけ 15 進む。
            var hero = new Hero(Config(), new Box(5, 2 * T, S, S));
            hero.Step(Open(), In(-1, 1));
            var motion = hero.LastMotion;
            Assert.That(motion.Corner, Is.EqualTo(new Box(0, 2 * T, S, S)));
            Assert.That(motion.End, Is.EqualTo(new Box(0, (2 * T) + 15, S, S)));

            // 止まらずに横へ 15 進んでいれば触れた位置（左端 -8 より左）。実際の経路は左端 0 までなので触れない。
            var beyond = new Box(-200, (2 * T) + 8, S, S);
            Assert.That(Sweep.First(new Box(5, 2 * T, S, S), new Box(-10, (2 * T) + 15, S, S), beyond, beyond).Hit, Is.True);
            Assert.That(Sweep.Along(motion, beyond, beyond).Hit, Is.False);

            // 壁の手前の敵には、止まった後の縦の区間で触れる。
            var above = new Box(-100, (2 * T) + S + 10, S, S);
            AssertAt(Sweep.Along(motion, above, above), 5 + 10, 5 + 15);
        }
    }
}
