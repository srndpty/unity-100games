using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class CollisionResolverTests
    {
        private const float Tolerance = 1e-4f;
        private const float Speed = 7f;

        // 列 5 と 6 の最上段に並んだ 2 個。継ぎ目は x = 0、下面は y = 6。
        private const string Pair = ".....NN.....";

        [Test]
        public void AdjacentBricks_HitAtSeam_OnlyOneBreaks()
        {
            var rig = new TestStages.Rig(Pair);
            rig.Ball.Launch(new Vec2(0f, 5f), new Vec2(0f, Speed));

            rig.StepUntil(r => r.Ball.Velocity.Y < 0f);
            rig.StepUntil(r => r.Ball.Position.Y < 4f);

            Assert.That(rig.Count(GameEventKind.BrickDestroyed), Is.EqualTo(1));
            Assert.That(rig.Field.AliveCount, Is.EqualTo(1));
            Assert.That(rig.Field.Bricks[0].IsAlive, Is.False, "同じ距離なら番号の小さい方");
            Assert.That(rig.Ball.Velocity.X, Is.EqualTo(0f), "継ぎ目で横に跳ね返らない");
        }

        [Test]
        public void AdjacentBricks_HitNearSeam_CloserOneBreaks()
        {
            var rig = new TestStages.Rig(Pair);
            rig.Ball.Launch(new Vec2(0.05f, 5f), new Vec2(0f, Speed));

            rig.StepUntil(r => r.Ball.Velocity.Y < 0f);

            Assert.That(rig.Field.Bricks[0].IsAlive, Is.True);
            Assert.That(rig.Field.Bricks[1].IsAlive, Is.False);
            Assert.That(rig.Count(GameEventKind.BrickDestroyed), Is.EqualTo(1));
        }

        [Test]
        public void AdjacentHardBricks_HitAtSeam_OnlyOneIsDamaged()
        {
            var rig = new TestStages.Rig(".....HH.....");
            rig.Ball.Launch(new Vec2(0f, 5f), new Vec2(0f, Speed));

            rig.StepUntil(r => r.Ball.Velocity.Y < 0f);
            rig.StepUntil(r => r.Ball.Position.Y < 4f);

            Assert.That(rig.Events, Is.EqualTo(new[] { new GameEvent(GameEventKind.BrickHit, 0) }));
            Assert.That(rig.Field.Bricks[0].HitsRemaining, Is.EqualTo(1));
            Assert.That(rig.Field.Bricks[1].HitsRemaining, Is.EqualTo(2));
        }

        [Test]
        public void DiagonalHitAtSeamCorner_BouncesVerticallyOnly()
        {
            // 右の列 6 の左下の角を狙う。左に列 5 があるので、実際に当たるのは塊の下面。
            var rig = new TestStages.Rig(Pair);
            var corner = new Vec2(-rig.Ball.HalfSize, 6f - rig.Ball.HalfSize);
            rig.Ball.Launch(corner - new Vec2(0.5f, 0.5f), new Vec2(Speed, Speed));

            rig.StepUntil(r => r.Ball.Velocity.Y < 0f);

            Assert.That(rig.Count(GameEventKind.BrickDestroyed), Is.EqualTo(1));
            Assert.That(rig.Field.Bricks[0].IsAlive, Is.False, "接触位置に近い左の列 5");
            Assert.That(rig.Ball.Velocity.X, Is.GreaterThan(0f), "横には跳ね返らない");
        }

        [Test]
        public void DiagonalHitAtIsolatedCorner_BouncesBothWays()
        {
            var rig = new TestStages.Rig("......N.....");
            var corner = new Vec2(-rig.Ball.HalfSize, 6f - rig.Ball.HalfSize);
            rig.Ball.Launch(corner - new Vec2(0.5f, 0.5f), new Vec2(Speed, Speed));

            rig.StepUntil(r => r.Ball.Velocity.Y < 0f);

            Assert.That(rig.Ball.Velocity.X, Is.LessThan(0f));
            Assert.That(rig.Count(GameEventKind.BrickDestroyed), Is.EqualTo(1));
        }

        [Test]
        public void SideHit_BouncesHorizontally()
        {
            var rig = new TestStages.Rig("......N.....");
            rig.Ball.Launch(new Vec2(-2f, 6.25f), new Vec2(Speed, 0f));

            rig.StepUntil(r => r.Ball.Velocity.X < 0f);

            Assert.That(rig.Ball.Velocity.Y, Is.EqualTo(0f));
            Assert.That(rig.Count(GameEventKind.BrickDestroyed), Is.EqualTo(1));
        }

        [Test]
        public void Pierce_DoesNotReflectOnBricks()
        {
            var rig = new TestStages.Rig("......N.....", "......H.....", "......N.....");
            rig.Ball.Launch(new Vec2(0.5f, 3f), new Vec2(0f, Speed));

            rig.StepUntil(r => r.Ball.Position.Y > 6.6f, pierce: true);

            Assert.That(rig.Ball.Velocity.Y, Is.GreaterThan(0f));
            Assert.That(rig.Field.AliveCount, Is.EqualTo(0), "硬いブロックも 1 発で壊す");
            Assert.That(rig.Count(GameEventKind.BrickDestroyed), Is.EqualTo(3));
            Assert.That(rig.Count(GameEventKind.BrickHit), Is.EqualTo(0));
        }

        [Test]
        public void Pierce_StillReflectsOnWallsAndPaddle()
        {
            var rig = new TestStages.Rig("N...........");
            rig.Ball.Launch(new Vec2(3f, 6f), new Vec2(0f, Speed));

            rig.StepUntil(r => r.Ball.Velocity.Y < 0f, pierce: true);
            Assert.That(rig.Count(GameEventKind.WallHit), Is.EqualTo(1));

            rig.Ball.Launch(new Vec2(0f, -5f), new Vec2(0f, -Speed));
            rig.StepUntil(r => r.Ball.Velocity.Y > 0f, pierce: true);
            Assert.That(rig.Count(GameEventKind.PaddleHit), Is.EqualTo(1));
        }

        [Test]
        public void WithoutPierce_SameColumn_ReflectsOnFirstBrick()
        {
            var rig = new TestStages.Rig("......N.....", "......H.....", "......N.....");
            rig.Ball.Launch(new Vec2(0.5f, 3f), new Vec2(0f, Speed));

            rig.StepUntil(r => r.Ball.Velocity.Y < 0f);

            Assert.That(rig.Field.AliveCount, Is.EqualTo(2));
            Assert.That(rig.Field.Bricks[2].IsAlive, Is.False);
        }

        [Test]
        public void Walls_ReflectOnLeftRightAndTop()
        {
            var rig = new TestStages.Rig("N...........");
            rig.Ball.Launch(new Vec2(4f, 0f), new Vec2(Speed, 0f));
            rig.StepUntil(r => r.Ball.Velocity.X < 0f);
            Assert.That(rig.Ball.Position.X, Is.LessThanOrEqualTo(6f - rig.Ball.HalfSize));

            rig.Ball.Launch(new Vec2(-4f, 0f), new Vec2(-Speed, 0f));
            rig.StepUntil(r => r.Ball.Velocity.X > 0f);

            rig.Ball.Launch(new Vec2(3f, 7f), new Vec2(0f, Speed));
            rig.StepUntil(r => r.Ball.Velocity.Y < 0f);

            Assert.That(rig.Count(GameEventKind.WallHit), Is.EqualTo(3));
        }

        [Test]
        public void PaddleCenterHit_ReturnsStraightUp()
        {
            var rig = new TestStages.Rig("N...........");
            rig.Ball.Launch(new Vec2(0f, -5f), new Vec2(0f, -Speed));

            rig.StepUntil(r => r.Ball.Velocity.Y > 0f);

            Assert.That(rig.Ball.Velocity.X, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(rig.Ball.Velocity.Length, Is.EqualTo(Speed).Within(Tolerance), "パドルでは加速しない");
        }

        [Test]
        public void PaddleEdgeHit_ReturnsAtMaxAngle()
        {
            var rig = new TestStages.Rig("N...........");
            var edge = rig.Paddle.HalfWidth + rig.Ball.HalfSize;
            rig.Ball.Launch(new Vec2(edge, -5f), new Vec2(0f, -Speed));

            rig.StepUntil(r => r.Ball.Velocity.Y > 0f);

            var angle = MathF.Atan2(rig.Ball.Velocity.X, rig.Ball.Velocity.Y) * 180f / MathF.PI;
            Assert.That(angle, Is.EqualTo(rig.Config.MaxBounceAngleDegrees).Within(0.01f));
        }

        [Test]
        public void BallOutsidePaddle_FallsThrough()
        {
            var rig = new TestStages.Rig("N...........");
            var outside = rig.Paddle.HalfWidth + rig.Ball.HalfSize + 0.01f;
            rig.Ball.Launch(new Vec2(outside, -5f), new Vec2(0f, -Speed));

            rig.StepUntil(r => r.Ball.Position.Y < -8.5f);

            Assert.That(rig.Count(GameEventKind.PaddleHit), Is.EqualTo(0));
        }

        [Test]
        public void MovingPaddle_IsComparedAtContactTime()
        {
            // Tick 開始時には届かない位置にいるが、接触時刻には下に来ているパドルで打ち返す。
            var rig = new TestStages.Rig("N...........");
            var reach = rig.Paddle.HalfWidth + rig.Ball.HalfSize;
            var contactY = rig.Paddle.Top + rig.Ball.HalfSize;
            var travel = rig.Config.PaddleSpeed * rig.Config.TickDuration;
            rig.Ball.Launch(new Vec2(reach + (travel * 0.25f), contactY + 0.01f), new Vec2(0f, -0.02f / rig.Config.TickDuration));

            rig.Step(paddleMove: 1f);

            Assert.That(rig.Count(GameEventKind.PaddleHit), Is.EqualTo(1));
        }

        [Test]
        public void DestroyingBrick_SpeedsUpUntilMaximum()
        {
            var rig = new TestStages.Rig("......N.....", "......N.....");
            rig.Ball.Launch(new Vec2(0.5f, 4f), new Vec2(0f, Speed));
            rig.StepUntil(r => r.Ball.Velocity.Y < 0f);
            Assert.That(rig.Ball.Velocity.Length, Is.EqualTo(Speed + rig.Config.SpeedIncreasePerBrick).Within(Tolerance));

            rig.Ball.Launch(new Vec2(0.5f, 4f), new Vec2(0f, rig.Config.MaxBallSpeed));
            rig.StepUntil(r => r.Ball.Velocity.Y < 0f);
            Assert.That(rig.Ball.Velocity.Length, Is.EqualTo(rig.Config.MaxBallSpeed).Within(Tolerance));
        }

        [Test]
        public void HardBrick_BreaksOnSecondHit()
        {
            var rig = new TestStages.Rig("......H.....");
            rig.Ball.Launch(new Vec2(0.5f, 4f), new Vec2(0f, Speed));
            rig.StepUntil(r => r.Ball.Velocity.Y < 0f);
            rig.Ball.Launch(new Vec2(0.5f, 4f), new Vec2(0f, Speed));
            rig.StepUntil(r => r.Ball.Velocity.Y < 0f);

            Assert.That(rig.Events, Is.EqualTo(new[] { new GameEvent(GameEventKind.BrickHit, 0), new GameEvent(GameEventKind.BrickDestroyed, 0) }));
        }

        [Test]
        public void HeldBall_DoesNotMove()
        {
            var rig = new TestStages.Rig("N...........");
            rig.Ball.Hold(new Vec2(1f, 1f));

            rig.Step();

            Assert.That(rig.Ball.Position, Is.EqualTo(new Vec2(1f, 1f)));
        }

        /// <summary>
        /// 最高速度・乱数の面・乱数の向きで動かし続け、1 Tick の接触回数が理屈どおりの上限（跳ね返り 2 回、合計
        /// <see cref="CollisionResolver.MaxContactsPerStep"/> 回）に収まり、壊れていないブロックや壁にめり込まないことを確かめる。
        /// </summary>
        [Test]
        public void RandomPlayAtMaxSpeed_StaysWithinContactLimitAndNeverTunnels([Range(0, 39)] int seed)
        {
            var random = new SeededRandom(seed);
            var rig = new TestStages.Rig(RandomRows(random));
            var pierce = seed % 2 == 0;
            var steps = new List<GameEvent>();
            LaunchRandomly(rig, random);
            for (var tick = 0; tick < 3000; tick++)
            {
                if (rig.Ball.Position.Y < -8.5f)
                {
                    LaunchRandomly(rig, random);
                }

                steps.Clear();
                rig.Paddle.Move(random.Range(-1f, 1f));
                rig.Resolver.Step(rig.Ball, rig.Paddle, rig.Field, pierce, steps);

                var bricks = steps.FindAll(e => e.Kind is GameEventKind.BrickHit or GameEventKind.BrickDestroyed).Count;
                var reflections = steps.FindAll(e => e.Kind is GameEventKind.WallHit or GameEventKind.PaddleHit).Count + (pierce ? 0 : bricks);
                Assert.That(reflections, Is.LessThanOrEqualTo(2), $"tick {tick}");
                Assert.That(steps.Count, Is.LessThanOrEqualTo(CollisionResolver.MaxContactsPerStep), $"tick {tick}");
                AssertNotEmbedded(rig, tick);
            }
        }

        [Test]
        public void TooManyContacts_Throws()
        {
            // 検証を通らない速さで、1 Tick に何度も壁の間を往復させる。
            var rig = new TestStages.Rig("N...........");
            rig.Ball.Launch(new Vec2(0f, 0f), new Vec2(12f * 2f * 60f * 6f, 0f));

            Assert.Throws<InvalidOperationException>(() => rig.Step());
        }

        private static string[] RandomRows(SeededRandom random)
        {
            const string symbols = "NNNHHPE...";
            var rows = new string[4 + (int)(random.NextFloat() * 5f)];
            for (var row = 0; row < rows.Length; row++)
            {
                var chars = new char[12];
                for (var column = 0; column < chars.Length; column++)
                {
                    chars[column] = symbols[(int)(random.NextFloat() * symbols.Length)];
                }

                rows[row] = new string(chars);
            }

            // 少なくとも 1 個はブロックを置く。
            rows[0] = "N" + rows[0].Remove(0, 1);
            return rows;
        }

        // 最下段より下・パドルより上から、最高速度で上半分のどこかへ向けて打ち出す。
        private static void LaunchRandomly(TestStages.Rig rig, SeededRandom random)
        {
            var angle = random.Range(-80f, 80f) * MathF.PI / 180f;
            var position = new Vec2(random.Range(-5.8f, 5.8f), random.Range(-5f, 2f));
            rig.Ball.Launch(position, new Vec2(MathF.Sin(angle), MathF.Cos(angle)) * rig.Config.MaxBallSpeed);
        }

        private static void AssertNotEmbedded(TestStages.Rig rig, int tick)
        {
            const float margin = 1e-4f;
            var ball = rig.Ball.Position;
            var r = rig.Ball.HalfSize;
            Assert.That(MathF.Abs(ball.X), Is.LessThanOrEqualTo(6f - r + margin), $"tick {tick}: 横の壁");
            Assert.That(ball.Y, Is.LessThanOrEqualTo(8f - r + margin), $"tick {tick}: 上の壁");
            foreach (var brick in rig.Field.Bricks)
            {
                var inside = brick.IsAlive
                    && ball.X > brick.MinX - r + margin && ball.X < brick.MaxX + r - margin
                    && ball.Y > brick.MinY - r + margin && ball.Y < brick.MaxY + r - margin;
                Assert.That(inside, Is.False, $"tick {tick}: ブロック {brick.Index} にめり込んだ");
            }
        }
    }
}
