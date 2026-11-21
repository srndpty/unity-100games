using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>踏みつけと横衝突の区別（相対軌跡の掃引）。</summary>
    public sealed class EnemyContactTests
    {
        private static readonly Box _enemy = new(0, 0, 16, 16);

        private static ContactKind Classify(Box playerStart, int dx, int dy, bool stompable = true, int enemyDx = 0, int enemyDy = 0)
        {
            return EnemyContact.Classify(playerStart, playerStart.Offset(dx, dy), _enemy, _enemy.Offset(enemyDx, enemyDy), stompable).Kind;
        }

        [Test]
        public void FallingOntoTop_IsStomp()
        {
            Assert.That(Classify(new Box(0, 20, 16, 16), 0, -10), Is.EqualTo(ContactKind.Stomp));
        }

        [Test]
        public void WalkingIntoSide_IsHurt()
        {
            Assert.That(Classify(new Box(-20, 0, 16, 16), 10, 0), Is.EqualTo(ContactKind.Hurt));
            Assert.That(Classify(new Box(20, 0, 16, 16), -10, 0), Is.EqualTo(ContactKind.Hurt));
        }

        [Test]
        public void FallingIntoSideWhileMovingDown_IsHurt()
        {
            // 下へ動いていても、先に触れたのが横の面なら横衝突。
            Assert.That(Classify(new Box(-20, 4, 16, 16), 10, -2), Is.EqualTo(ContactKind.Hurt));
        }

        [Test]
        public void RisingIntoBottom_IsHurt()
        {
            Assert.That(Classify(new Box(0, -20, 16, 16), 0, 10), Is.EqualTo(ContactKind.Hurt));
        }

        [Test]
        public void UnstompableFromAbove_IsHurt()
        {
            Assert.That(Classify(new Box(0, 20, 16, 16), 0, -10, stompable: false), Is.EqualTo(ContactKind.Hurt));
        }

        [Test]
        public void TouchingWithoutOverlap_IsNone()
        {
            Assert.That(Classify(new Box(0, 20, 16, 16), 0, -4), Is.EqualTo(ContactKind.None), "足元が上面にちょうど着くだけ");
            Assert.That(Classify(new Box(-20, 0, 16, 16), 4, 0), Is.EqualTo(ContactKind.None), "横の面にちょうど着くだけ");
            Assert.That(Classify(new Box(-20, 0, 16, 16), 0, 0), Is.EqualTo(ContactKind.None), "止まっている");
        }

        [Test]
        public void OverlappingAtStart_IsHurt()
        {
            Assert.That(Classify(new Box(4, 4, 16, 16), 0, 0), Is.EqualTo(ContactKind.Hurt));
            Assert.That(Classify(new Box(4, 4, 16, 16), 0, 30), Is.EqualTo(ContactKind.Hurt));
        }

        [Test]
        public void ExactCorner_PrefersVertical()
        {
            // 横と縦が同じ時刻に触れ始める。
            Assert.That(Classify(new Box(-20, 20, 16, 16), 8, -8), Is.EqualTo(ContactKind.Stomp));
            Assert.That(Classify(new Box(-20, -20, 16, 16), 8, 8), Is.EqualTo(ContactKind.Hurt), "下からの角は踏みつけではない");
        }

        /// <summary>始めも終わりも重なっていないが、途中で角をかすめる。端点だけの判定では見逃す。</summary>
        [Test]
        public void DiagonalGraze_IsDetectedFromTrajectory()
        {
            var side = new Box(-20, 10, 16, 16);
            Assert.That(side.Overlaps(_enemy) || side.Offset(40, -40).Overlaps(_enemy), Is.False);
            Assert.That(Classify(side, 40, -40), Is.EqualTo(ContactKind.Hurt), "横の面が先");

            var top = new Box(-10, 20, 16, 16);
            Assert.That(top.Overlaps(_enemy) || top.Offset(40, -40).Overlaps(_enemy), Is.False);
            Assert.That(Classify(top, 40, -40), Is.EqualTo(ContactKind.Stomp), "上の面が先");
        }

        [Test]
        public void EnemyRisingIntoStandingPlayer_IsStompByRelativeMotion()
        {
            // 自機は止まっていても、敵が下から上がってくれば相対的には自機が下へ動いている。
            Assert.That(Classify(new Box(0, 20, 16, 16), 0, 0, enemyDy: 10), Is.EqualTo(ContactKind.Stomp));
        }

        [Test]
        public void EnemyWalkingIntoStandingPlayer_IsHurt()
        {
            Assert.That(Classify(new Box(20, 0, 16, 16), 0, 0, enemyDx: 10), Is.EqualTo(ContactKind.Hurt));
        }

        [Test]
        public void MovingTogether_NoContact()
        {
            Assert.That(Classify(new Box(-20, 0, 16, 16), 30, 0, enemyDx: 30), Is.EqualTo(ContactKind.None));
        }

        [Test]
        public void PassingFarAway_NoContact()
        {
            Assert.That(Classify(new Box(-50, 30, 16, 16), 100, 0), Is.EqualTo(ContactKind.None));
            Assert.That(Classify(new Box(-50, 0, 16, 16), 20, 0), Is.EqualTo(ContactKind.None), "届かない");
        }

        [Test]
        public void Contact_TimeIsComparable()
        {
            var early = EnemyContact.Classify(new Box(0, 18, 16, 16), new Box(0, 8, 16, 16), _enemy, _enemy, true);
            var late = EnemyContact.Classify(new Box(0, 24, 16, 16), new Box(0, 14, 16, 16), _enemy, _enemy, true);
            Assert.That(early.CompareTime(late), Is.LessThan(0));
            Assert.That(late.CompareTime(early), Is.GreaterThan(0));
            Assert.That(early.CompareTime(early), Is.Zero);
        }
    }
}
