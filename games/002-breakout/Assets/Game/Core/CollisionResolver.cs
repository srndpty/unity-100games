using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// ボールの 1 Tick の移動と衝突。<b>衝突の優先順位はこのクラスだけで決める。</b>
    /// <para>
    /// 1 Tick の移動を線分として扱い（掃引判定）、壁・パドル・ブロックのうち最も早い接触を 1 つ選んで処理し、残りの時間で探し直す。
    /// ボールは正方形として、相手をボールの半分の大きさだけ膨らませた長方形と線分の交差で判定する。
    /// </para>
    /// <para>同時刻（<see cref="TimeEpsilon"/> 以内）の接触は次の順で 1 つに絞る。</para>
    /// <list type="number">
    /// <item>壁 → パドル → ブロック。</item>
    /// <item>ブロック同士は、接触位置がブロックの中心に近い方。同じ距離なら番号（左上から行優先）の小さい方。</item>
    /// </list>
    /// <para>
    /// 1 回の接触でダメージを受けるブロックは 1 個だけ。隣り合うブロックの継ぎ目に当たっても、選ばれた 1 個が跳ね返し、
    /// 跳ね返った後はもう一方へ向かっていないので二重には壊れない。
    /// また、隣に壊れていないブロックがある面（ブロックの塊の内側の継ぎ目）は当たる面として扱わない。継ぎ目で横向きに跳ね返る不自然さを防ぐ。
    /// </para>
    /// <para>貫通弾が有効なら、ブロックとの接触は「壊して通過」になり、反射しない。壁とパドルでは通常どおり反射する。</para>
    /// </summary>
    public sealed class CollisionResolver
    {
        /// <summary>
        /// 1 Tick の接触回数の上限。検証済みの設定（<see cref="BreakoutConfig.MaxTravelPerTick"/>）では、
        /// 跳ね返る接触は x・y 各 1 回の最大 2 回、貫通で壊すブロックは最大 9 個（ボールが 1 Tick に触れうる範囲は 1 辺が 2 マス未満の正方形で、
        /// 各軸 3 マスまでにしか掛からない）なので、合計 11 回を超えない。
        /// </summary>
        public const int MaxContactsPerStep = 11;

        /// <summary>同時刻とみなす Tick 内の時刻の差。</summary>
        public const float TimeEpsilon = 1e-5f;

        // 同じ距離とみなす差（距離の 2 乗）。継ぎ目の真ん中に当たったときの丸め誤差を吸収する。
        private const float DistanceEpsilon = 1e-6f;

        private const float DegreesToRadians = MathF.PI / 180f;

        private readonly BreakoutConfig _config;

        public CollisionResolver(BreakoutConfig config)
        {
            _config = config;
        }

        /// <summary>
        /// ボールを 1 Tick 進める。パドルはこの Tick の移動（<see cref="Paddle.Motion"/>）を済ませておくこと。
        /// 接触が <see cref="MaxContactsPerStep"/> 回を超えたら、時間を取りこぼさないよう例外にする。
        /// </summary>
        public void Step(Ball ball, Paddle paddle, BrickField field, bool pierce, ICollection<GameEvent> events)
        {
            if (!ball.InPlay)
            {
                return;
            }

            var deltaTime = _config.TickDuration;
            var position = ball.Position;
            var velocity = ball.Velocity;
            var elapsed = 0f; // Tick 内で処理し終えた割合（0〜1）
            for (var i = 0; i <= MaxContactsPerStep; i++)
            {
                var span = 1f - elapsed;
                var delta = velocity * (deltaTime * span);
                var contact = FindEarliestContact(ball.HalfSize, position, delta, elapsed, span, paddle, field);
                if (contact.Kind == ContactKind.None)
                {
                    ball.Position = position + delta;
                    ball.Velocity = velocity;
                    return;
                }

                if (i == MaxContactsPerStep)
                {
                    break;
                }

                var time = Math.Clamp(contact.Time, 0f, 1f);
                position += delta * time;
                elapsed += span * time;
                switch (contact.Kind)
                {
                    case ContactKind.Wall:
                        velocity = Reflect(velocity, contact);
                        events.Add(new GameEvent(GameEventKind.WallHit));
                        break;
                    case ContactKind.Paddle:
                        velocity = Bounce(ball.HalfSize, paddle, paddle.XAt(elapsed), position, velocity);
                        events.Add(new GameEvent(GameEventKind.PaddleHit));
                        break;
                    default:
                        velocity = HitBrick(contact, field, pierce, velocity, events);
                        break;
                }
            }

            throw new InvalidOperationException($"1 Tick の接触が {MaxContactsPerStep} 回を超えました。ボール速度と Tick レートの設定を見直してください");
        }

        private Vec2 HitBrick(Contact contact, BrickField field, bool pierce, Vec2 velocity, ICollection<GameEvent> events)
        {
            var brick = contact.Brick!;
            if (pierce)
            {
                field.Destroy(brick);
                events.Add(new GameEvent(GameEventKind.BrickDestroyed, brick.Index));
                return SpeedUp(velocity);
            }

            velocity = Reflect(velocity, contact);
            if (!field.Hit(brick))
            {
                events.Add(new GameEvent(GameEventKind.BrickHit, brick.Index));
                return velocity;
            }

            events.Add(new GameEvent(GameEventKind.BrickDestroyed, brick.Index));
            return SpeedUp(velocity);
        }

        // delta は Tick の残り（開始 tickStart、長さ span の区間）の移動量。返す Time はこの区間内の割合（0〜1）。
        private Contact FindEarliestContact(float halfSize, Vec2 position, Vec2 delta, float tickStart, float span, Paddle paddle, BrickField field)
        {
            var earliest = Contact.None;

            var wallX = (_config.FieldWidth / 2f) - halfSize;
            var wallY = (_config.FieldHeight / 2f) - halfSize;
            if (delta.X < 0f && position.X + delta.X < -wallX)
            {
                earliest = Pick(earliest, Contact.Wall((-wallX - position.X) / delta.X, flipX: true, flipY: false));
            }
            else if (delta.X > 0f && position.X + delta.X > wallX)
            {
                earliest = Pick(earliest, Contact.Wall((wallX - position.X) / delta.X, flipX: true, flipY: false));
            }

            if (delta.Y > 0f && position.Y + delta.Y > wallY)
            {
                earliest = Pick(earliest, Contact.Wall((wallY - position.Y) / delta.Y, flipX: false, flipY: true));
            }

            earliest = Pick(earliest, PaddleContact(halfSize, position, delta, tickStart, span, paddle));

            foreach (var brick in field.Bricks)
            {
                if (brick.IsAlive)
                {
                    earliest = Pick(earliest, BrickContact(halfSize, position, delta, brick, field));
                }
            }

            return earliest;
        }

        // パドルは上面だけ。下へ向かって上面の高さを越えるときに、その時刻のパドルの x と比べる。
        private static Contact PaddleContact(float halfSize, Vec2 position, Vec2 delta, float tickStart, float span, Paddle paddle)
        {
            var contactY = paddle.Top + halfSize;
            if (delta.Y >= 0f || position.Y < contactY || position.Y + delta.Y >= contactY)
            {
                return Contact.None;
            }

            var time = (contactY - position.Y) / delta.Y;
            var xAtContact = position.X + (delta.X * time);
            var paddleX = paddle.XAt(tickStart + (span * time));
            return MathF.Abs(xAtContact - paddleX) <= paddle.HalfWidth + halfSize ? Contact.WithPaddle(time) : Contact.None;
        }

        private static Contact BrickContact(float halfSize, Vec2 position, Vec2 delta, Brick brick, BrickField field)
        {
            if (!Slab(position.X, delta.X, brick.MinX - halfSize, brick.MaxX + halfSize, out var xIn, out var xOut)
                || !Slab(position.Y, delta.Y, brick.MinY - halfSize, brick.MaxY + halfSize, out var yIn, out var yOut))
            {
                return Contact.None;
            }

            var timeIn = MathF.Max(xIn, yIn);
            var timeOut = MathF.Min(xOut, yOut);
            if (timeIn < -TimeEpsilon || timeIn > 1f || timeIn >= timeOut)
            {
                return Contact.None;
            }

            // 最後に入った軸の面に当たった。両方ほぼ同時なら角。
            var flipX = xIn >= yIn - TimeEpsilon;
            var flipY = yIn >= xIn - TimeEpsilon;

            // 隣に壊れていないブロックがある面は塊の内側の継ぎ目なので、当たる面として扱わない。
            var closedX = flipX && field.HasAliveAt(brick.Column - MathF.Sign(delta.X), brick.Row);
            var closedY = flipY && field.HasAliveAt(brick.Column, brick.Row + MathF.Sign(delta.Y)); // 段は下向きに増える
            if (flipX && flipY)
            {
                // 角：内側の面だけを外す（両方とも内側なら角のまま）。
                if (closedX != closedY)
                {
                    flipX = !closedX;
                    flipY = !closedY;
                }
            }
            else if (closedX || closedY)
            {
                // 継ぎ目の面に入った：塊の外側の面（もう一方の軸）で跳ね返る。
                flipX = !flipX;
                flipY = !flipY;
            }

            var at = position + (delta * timeIn);
            var offset = at - brick.Center;
            return Contact.WithBrick(timeIn, brick, flipX, flipY, (offset.X * offset.X) + (offset.Y * offset.Y));
        }

        // 1 軸ぶんの区間 [min, max] に入る時刻と出る時刻。動かない軸で区間外なら交差しない。
        private static bool Slab(float start, float delta, float min, float max, out float timeIn, out float timeOut)
        {
            if (delta == 0f)
            {
                timeIn = float.NegativeInfinity;
                timeOut = float.PositiveInfinity;
                return start > min && start < max;
            }

            var toMin = (min - start) / delta;
            var toMax = (max - start) / delta;
            timeIn = MathF.Min(toMin, toMax);
            timeOut = MathF.Max(toMin, toMax);
            return true;
        }

        // 優先順位の唯一の定義。早い方、同時なら 壁 → パドル → ブロック、ブロック同士は中心に近い方、同じなら番号の小さい方。
        private static Contact Pick(Contact current, Contact candidate)
        {
            if (candidate.Kind == ContactKind.None)
            {
                return current;
            }

            if (current.Kind == ContactKind.None || candidate.Time < current.Time - TimeEpsilon)
            {
                return candidate;
            }

            if (candidate.Time > current.Time + TimeEpsilon)
            {
                return current;
            }

            if (candidate.Kind != current.Kind)
            {
                return candidate.Kind < current.Kind ? candidate : current;
            }

            if (candidate.Kind != ContactKind.Brick)
            {
                return current;
            }

            var closer = candidate.DistanceSquared - current.DistanceSquared;
            if (MathF.Abs(closer) > DistanceEpsilon)
            {
                return closer < 0f ? candidate : current;
            }

            return candidate.Brick!.Index < current.Brick!.Index ? candidate : current;
        }

        private static Vec2 Reflect(Vec2 velocity, Contact contact) =>
            new(contact.FlipX ? -velocity.X : velocity.X, contact.FlipY ? -velocity.Y : velocity.Y);

        // 当たった位置（接触時刻のパドル中央からのずれ）が大きいほど、垂直から大きく傾けて返す。速さは変えない。
        private Vec2 Bounce(float halfSize, Paddle paddle, float paddleX, Vec2 position, Vec2 velocity)
        {
            var offset = Math.Clamp((position.X - paddleX) / (paddle.HalfWidth + halfSize), -1f, 1f);
            var angle = offset * _config.MaxBounceAngleDegrees * DegreesToRadians;
            return new Vec2(MathF.Sin(angle), MathF.Cos(angle)) * velocity.Length;
        }

        private Vec2 SpeedUp(Vec2 velocity)
        {
            var speed = velocity.Length;
            var next = MathF.Min(speed + _config.SpeedIncreasePerBrick, _config.MaxBallSpeed);
            return next > speed ? velocity * (next / speed) : velocity;
        }

        // 値が小さいほど優先（同時刻の比較に使う）。
        private enum ContactKind
        {
            Wall,
            Paddle,
            Brick,
            None,
        }

        private readonly struct Contact
        {
            private Contact(ContactKind kind, float time, bool flipX, bool flipY, Brick? brick, float distanceSquared)
            {
                Kind = kind;
                Time = time;
                FlipX = flipX;
                FlipY = flipY;
                Brick = brick;
                DistanceSquared = distanceSquared;
            }

            public static Contact None => new(ContactKind.None, float.MaxValue, false, false, null, 0f);

            public ContactKind Kind { get; }

            public float Time { get; }

            public bool FlipX { get; }

            public bool FlipY { get; }

            public Brick? Brick { get; }

            /// <summary>接触位置からブロックの中心までの距離の 2 乗。</summary>
            public float DistanceSquared { get; }

            public static Contact Wall(float time, bool flipX, bool flipY) => new(ContactKind.Wall, time, flipX, flipY, null, 0f);

            public static Contact WithPaddle(float time) => new(ContactKind.Paddle, time, false, true, null, 0f);

            public static Contact WithBrick(float time, Brick brick, bool flipX, bool flipY, float distanceSquared) =>
                new(ContactKind.Brick, time, flipX, flipY, brick, distanceSquared);
        }
    }
}
