using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>
    /// カプセルとパドルの接触を、Tick 中の軌跡で判定することを確かめる。既定の設定では、横の取得範囲は 1 + 0.4 = 1.4、
    /// 縦は (0.3 + 0.3) / 2 = 0.3、1 Tick の落下は 0.05、パドルの移動は 0.2。
    /// </summary>
    public sealed class CapsuleTests
    {
        private BreakoutConfig _config = null!;
        private Paddle _paddle = null!;

        [SetUp]
        public void SetUp()
        {
            _config = new BreakoutConfig();
            _paddle = new Paddle(_config);
        }

        [Test]
        public void PaddleTouchingOnlyMidTick_CatchesCapsule()
        {
            // 縦に重なるのは Tick 中盤から。その時刻のパドル（x = 0.1）とは横 1.35 で届くが、終了時（x = 0.2）は 1.45 で届かない。
            var capsule = new Capsule(EffectKind.Expand, new Vec2(-1.25f, _config.PaddleY + 0.325f));
            _paddle.Move(1f);

            Assert.That(capsule.FallAndCatch(_paddle, _config), Is.True);
        }

        [Test]
        public void PaddleMovingAway_DoesNotCatch()
        {
            var capsule = new Capsule(EffectKind.Expand, new Vec2(-1.45f, _config.PaddleY + 0.325f));
            _paddle.Move(1f);

            Assert.That(capsule.FallAndCatch(_paddle, _config), Is.False);
        }

        [Test]
        public void PaddleStoppedAtWall_IsComparedWhereItStopped()
        {
            // 右の壁（x = 5）の手前 0.1 から右へ動き、Tick の半ばで止まる。カプセルは縦に Tick の終わりにだけ重なる。
            for (var i = 0; i < 120; i++)
            {
                _paddle.Move(1f);
            }

            _paddle.Move(-1f);
            _paddle.Move(-0.5f); // x = 4.7 まで戻す
            _paddle.Move(1f); // 4.7 → 4.9
            _paddle.Move(1f); // 4.9 → 5（途中で止まる）
            Assert.That(_paddle.Motion.StopAt, Is.LessThan(1f));
            var reach = _paddle.HalfWidth + (_config.CapsuleWidth / 2f);
            var inside = new Capsule(EffectKind.Pierce, new Vec2(5f - reach + 0.01f, _config.PaddleY + 0.345f));
            var outside = new Capsule(EffectKind.Pierce, new Vec2(5f - reach - 0.01f - 0.1f, _config.PaddleY + 0.345f));

            Assert.That(inside.FallAndCatch(_paddle, _config), Is.True);
            Assert.That(outside.FallAndCatch(_paddle, _config), Is.False);
        }

        [Test]
        public void CapsuleOutOfVerticalReach_IsNotCaught()
        {
            var above = new Capsule(EffectKind.Expand, new Vec2(0f, _config.PaddleY + 0.36f));
            var below = new Capsule(EffectKind.Expand, new Vec2(0f, _config.PaddleY - 0.31f));

            Assert.That(above.FallAndCatch(_paddle, _config), Is.False);
            Assert.That(below.FallAndCatch(_paddle, _config), Is.False);
            Assert.That(above.Position.Y, Is.EqualTo(_config.PaddleY + 0.31f).Within(1e-5f), "1 Tick 分落ちる");
        }
    }
}
