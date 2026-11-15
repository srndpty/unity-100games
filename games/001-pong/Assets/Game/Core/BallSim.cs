using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// ボールの移動と衝突。1 Tick の移動を線分として扱い（掃引判定）、最も早い接触から順に処理するため、
    /// 高速でもパドルをすり抜けない。パドルとの衝突は「ボールがパドルへ向かって面を越えるとき」だけ起きる。
    /// パドルも Tick 中に等速で動いたとみなし、接触した時刻でのパドル位置と比べる。
    /// </summary>
    public sealed class BallSim
    {
        private const int MaxContactsPerStep = 4;
        private const float DegreesToRadians = MathF.PI / 180f;

        private readonly PongConfig _config;

        public BallSim(PongConfig config)
        {
            _config = config;
            HalfSize = config.BallSize / 2f;
        }

        public float HalfSize { get; }

        public Vec2 Position { get; private set; }

        public Vec2 Velocity { get; private set; }

        public bool InPlay { get; private set; }

        public void ResetToCenter()
        {
            Position = Vec2.Zero;
            Velocity = Vec2.Zero;
            InPlay = false;
        }

        public void Launch(Vec2 position, Vec2 velocity)
        {
            Position = position;
            Velocity = velocity;
            InPlay = true;
        }

        /// <summary>
        /// 1 Tick 進める。パドルはこの Tick の移動（<see cref="Paddle.PreviousY"/> → <see cref="Paddle.Y"/>）を済ませておくこと。
        /// 接触が <see cref="MaxContactsPerStep"/> 回を超えたら、時間を取りこぼさないよう例外にする（設定の検証で起きない範囲に抑えている）。
        /// </summary>
        public void Step(float deltaTime, Paddle left, Paddle right, ICollection<MatchEvent> events)
        {
            if (!InPlay)
            {
                return;
            }

            var position = Position;
            var velocity = Velocity;
            var elapsed = 0f; // Tick 内で処理し終えた割合（0〜1）
            for (var i = 0; i <= MaxContactsPerStep; i++)
            {
                var span = 1f - elapsed;
                var delta = velocity * (deltaTime * span);
                var contact = FindEarliestContact(position, delta, elapsed, span, left, right);
                if (contact.Paddle is null && !contact.IsWall)
                {
                    Position = position + delta;
                    Velocity = velocity;
                    return;
                }

                if (i == MaxContactsPerStep)
                {
                    break;
                }

                position += delta * contact.Time;
                elapsed += span * contact.Time;
                if (contact.Paddle is Paddle paddle)
                {
                    velocity = Bounce(paddle, paddle.YAt(elapsed), position, velocity);
                    events.Add(new MatchEvent(MatchEventKind.PaddleHit, paddle.Side));
                }
                else
                {
                    velocity = new Vec2(velocity.X, -velocity.Y);
                    events.Add(new MatchEvent(MatchEventKind.WallHit, default));
                }
            }

            throw new InvalidOperationException($"1 Tick の接触が {MaxContactsPerStep} 回を超えました。ボール速度と Tick レートの設定を見直してください");
        }

        /// <summary>ボールが完全にゴールラインを越えた陣地。越えていなければ null。</summary>
        public Side? GoalReached()
        {
            var goalX = _config.FieldWidth / 2f;
            if (Position.X + HalfSize < -goalX)
            {
                return Side.Left;
            }

            if (Position.X - HalfSize > goalX)
            {
                return Side.Right;
            }

            return null;
        }

        // delta は Tick の残り（開始 tickStart、長さ span の区間）の移動量。返す Time はこの区間内の割合（0〜1）。
        private Contact FindEarliestContact(Vec2 position, Vec2 delta, float tickStart, float span, Paddle left, Paddle right)
        {
            var earliest = Contact.None;

            var wallY = (_config.FieldHeight / 2f) - HalfSize;
            if (delta.Y > 0f && position.Y + delta.Y > wallY)
            {
                earliest = Contact.Wall((wallY - position.Y) / delta.Y);
            }
            else if (delta.Y < 0f && position.Y + delta.Y < -wallY)
            {
                earliest = Contact.Wall((-wallY - position.Y) / delta.Y);
            }

            // 向かっている側のパドルだけを判定する（離れていく側とは衝突しない）。
            var paddle = delta.X < 0f ? left : delta.X > 0f ? right : null;
            if (paddle is null)
            {
                return earliest;
            }

            var direction = paddle.Side.Direction();
            var contactX = paddle.FaceX - (direction * HalfSize);
            var startsInFront = (contactX - position.X) * direction >= 0f;
            var endsBeyond = (contactX - (position.X + delta.X)) * direction < 0f;
            if (!startsInFront || !endsBeyond)
            {
                return earliest;
            }

            var time = (contactX - position.X) / delta.X;
            var yAtContact = position.Y + (delta.Y * time);
            var paddleY = paddle.YAt(tickStart + (span * time));
            var reach = paddle.HalfHeight + HalfSize;
            if (MathF.Abs(yAtContact - paddleY) <= reach && time < earliest.Time)
            {
                earliest = Contact.With(paddle, time);
            }

            return earliest;
        }

        // 当たった位置（接触時刻のパドル中央からのずれ）が大きいほど反射角を大きくし、打つたびに加速する。
        private Vec2 Bounce(Paddle paddle, float paddleY, Vec2 position, Vec2 velocity)
        {
            var reach = paddle.HalfHeight + HalfSize;
            var offset = Math.Clamp((position.Y - paddleY) / reach, -1f, 1f);
            var angle = offset * _config.MaxBounceAngleDegrees * DegreesToRadians;
            var speed = MathF.Min(velocity.Length + _config.SpeedIncreasePerHit, _config.MaxBallSpeed);
            var outward = -paddle.Side.Direction();
            return new Vec2(outward * speed * MathF.Cos(angle), speed * MathF.Sin(angle));
        }

        private readonly struct Contact
        {
            private Contact(float time, Paddle? paddle, bool isWall)
            {
                Time = time;
                Paddle = paddle;
                IsWall = isWall;
            }

            public static Contact None => new(float.MaxValue, null, false);

            public float Time { get; }

            public Paddle? Paddle { get; }

            public bool IsWall { get; }

            public static Contact Wall(float time) => new(time, null, true);

            public static Contact With(Paddle paddle, float time) => new(time, paddle, false);
        }
    }
}
