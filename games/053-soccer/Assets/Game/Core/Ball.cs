using System;
using IntGeometry;

namespace Game.Core
{
    [Flags]
    public enum BallHit
    {
        None = 0,

        /// <summary>地面で弾んだ。</summary>
        Bounce = 1,

        /// <summary>ポストに当たった。</summary>
        Post = 2,

        /// <summary>弾みが小さくなり、転がりに変わった。</summary>
        Landed = 4,

        /// <summary>壁で跳ね返った。</summary>
        Wall = 8,
    }

    /// <summary>Tick の中の時刻（0 が始め、1 が終わり）。有理数で持ち、比べるときは交差乗算する。</summary>
    public readonly struct TickTime : IComparable<TickTime>, IEquatable<TickTime>
    {
        private TickTime(long numerator, long denominator)
        {
            var g = Gcd(Math.Abs(numerator), denominator);
            Numerator = g > 1 ? numerator / g : numerator;
            Denominator = g > 1 ? denominator / g : denominator;
        }

        public static TickTime Zero => new(0, 1);

        public static TickTime One => new(1, 1);

        public long Numerator { get; }

        public long Denominator { get; }

        public static TickTime Of(long numerator, long denominator)
        {
            if (denominator <= 0) { throw new ArgumentOutOfRangeException(nameof(denominator), denominator, "分母は正の値にしてください。"); }
            return new TickTime(numerator, denominator);
        }

        /// <summary>この時刻から終わりまでの長さ。</summary>
        public TickTime Remaining => new(Denominator - Numerator, Denominator);

        /// <summary>この時刻から終わりまでの残りのうち、割合 <paramref name="part"/> だけ進んだ時刻。</summary>
        public TickTime Advance(TickTime part) =>
            new((Numerator * part.Denominator) + ((Denominator - Numerator) * part.Numerator), Denominator * part.Denominator);

        /// <summary><paramref name="from"/> から <paramref name="to"/> へ直線で動くものの、この時刻の位置（0 に向けて切り捨て）。</summary>
        public Vec2i Lerp(Vec2i from, Vec2i to) => from + Scale(to - from);

        /// <summary>ベクトルにこの割合を掛ける（0 に向けて切り捨て）。</summary>
        public Vec2i Scale(Vec2i v) => new((int)(v.X * Numerator / Denominator), (int)(v.Y * Numerator / Denominator));

        public int CompareTo(TickTime other) => (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);

        public bool Equals(TickTime other) => CompareTo(other) == 0;

        public override bool Equals(object? obj) => obj is TickTime other && Equals(other);

        public override int GetHashCode() => unchecked((Numerator.GetHashCode() * 397) ^ Denominator.GetHashCode());

        public override string ToString() => $"{Numerator}/{Denominator}";

        public static bool operator ==(TickTime left, TickTime right) => left.Equals(right);

        public static bool operator !=(TickTime left, TickTime right) => !left.Equals(right);

        public static bool operator <(TickTime left, TickTime right) => left.CompareTo(right) < 0;

        public static bool operator >(TickTime left, TickTime right) => left.CompareTo(right) > 0;

        public static bool operator <=(TickTime left, TickTime right) => left.CompareTo(right) <= 0;

        public static bool operator >=(TickTime left, TickTime right) => left.CompareTo(right) >= 0;

        private static long Gcd(long a, long b)
        {
            while (b != 0)
            {
                var t = a % b;
                a = b;
                b = t;
            }

            return a;
        }
    }

    /// <summary>Tick の中で球が直線で動いた 1 区間（壁で跳ね返ると区間が分かれる）。保持の判定は区間ごとに掃引する。</summary>
    public readonly struct BallSegment
    {
        public BallSegment(Vec2i from, Vec2i to, Vec2i velocity, TickTime start, TickTime end)
        {
            From = from;
            To = to;
            Velocity = velocity;
            Start = start;
            End = end;
        }

        public Vec2i From { get; }
        public Vec2i To { get; }

        /// <summary>この区間の速度（1 Tick あたり）。</summary>
        public Vec2i Velocity { get; }

        public TickTime Start { get; }
        public TickTime End { get; }
    }

    /// <summary>
    /// 球。位置（x, y）・高さ z（球の下端）・速度（1 Tick あたり）。全て整数で、Mono と .NET で同じ軌跡になる。
    /// <para>
    /// 1 Tick：高さ 0 で縦の速さ 0 なら転がる（横の速さを ‰ で減らし、遅くなったら止まる）。それ以外は空中（縦の速さから重力を引く）。
    /// 高さが 0 以下になったら弾む（縦は反転して反発の割合、横は摩擦の割合。弾みが小さければ転がりへ）。高さは Tick の中で直線に変わるとみなす。
    /// </para>
    /// <para>
    /// 横の移動は、Tick の中で最初に触れた物で区切る（<see cref="Segment"/>）。
    /// 壁：球の端が壁に触れた時刻の位置で、壁に垂直な速さを反転して割合を掛け、Tick の残りはその速度で進む（角では両方）。
    /// ゴールラインの壁は、触れた時刻の横の位置と高さがゴールの口（ポストの間・全体がクロスバーの下）なら通り抜ける。
    /// ポスト：クロスバーより下で触れたら、触れた位置で止めて速さを法線に対して反射する（Tick の残りは捨てる）。
    /// 同じ時刻ならポストを先にする。
    /// </para>
    /// 予測（<see cref="BallPredictor"/>）も同じ <see cref="Step"/> を使うので、予測と本番の軌跡は Tick 単位で一致する（誰も触らなければ）。
    /// </summary>
    public sealed class Ball
    {
        /// <summary>1 Tick の区間の上限（壁 2 つ・ポスト 1 つで足りる）。</summary>
        public const int MaxSegments = 4;

        private BallSegment[] _segments = new BallSegment[MaxSegments];

        public Vec2i Position { get; private set; }

        public int Height { get; private set; }

        public Vec2i Velocity { get; private set; }

        public int VerticalSpeed { get; private set; }

        public bool IsRolling => Height == 0 && VerticalSpeed == 0;

        public bool IsStopped => IsRolling && Velocity.IsZero;

        /// <summary>最後の <see cref="Step"/> で動いた区間の数（置いた・運んだ直後は 0）。</summary>
        public int SegmentCount { get; private set; }

        public BallSegment Segment(int index) =>
            index >= 0 && index < SegmentCount ? _segments[index] : throw new ArgumentOutOfRangeException(nameof(index), index, $"区間は {SegmentCount} 個です");

        public Ball Clone()
        {
            var c = (Ball)MemberwiseClone();
            c._segments = (BallSegment[])_segments.Clone();
            return c;
        }

        public void Place(Vec2i position, int height = 0)
        {
            Position = position;
            Height = Math.Max(0, height);
            Velocity = Vec2i.Zero;
            VerticalSpeed = 0;
            SegmentCount = 0;
        }

        /// <summary>蹴る（今の位置から、この速度で）。</summary>
        public void Launch(Vec2i velocity, int verticalSpeed)
        {
            Velocity = velocity;
            VerticalSpeed = Math.Max(0, verticalSpeed);
        }

        /// <summary>選手が足元で運ぶ（位置と速度を選手に合わせ、地面に置く）。</summary>
        public void Carry(Vec2i position, Vec2i velocity)
        {
            Position = position;
            Velocity = velocity;
            Height = 0;
            VerticalSpeed = 0;
            SegmentCount = 0;
        }

        public BallHit Step(SoccerConfig config)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            var hit = BallHit.None;
            var v = Velocity;
            var vz = VerticalSpeed;
            if (IsRolling)
            {
                v = new Vec2i(Decay(v.X, config.RollFrictionPerMille), Decay(v.Y, config.RollFrictionPerMille));
                if (Math.Abs(v.X) + Math.Abs(v.Y) <= config.StopSpeed) { v = Vec2i.Zero; }
            }
            else
            {
                vz -= config.Gravity;
            }

            var z0 = Height;
            var z1 = Height + vz;
            if (z1 <= 0 && vz < 0)
            {
                hit |= BallHit.Bounce;
                vz = -vz * config.BounceRestitutionPercent / 100;
                v = new Vec2i(v.X * config.BounceFrictionPercent / 100, v.Y * config.BounceFrictionPercent / 100);
                if (vz < config.MinBounceSpeed)
                {
                    vz = 0;
                    hit |= BallHit.Landed;
                }

                z1 = 0;
            }

            var zEnd = Math.Max(0, z1);
            hit |= Move(config, v, z0, zEnd, out var position, out var velocity, out var post);
            Position = position;
            Velocity = velocity;
            if (post.HasValue)
            {
                // ポストで止まった時刻の高さ。
                Height = HeightAt(z0, zEnd, post.Value);
                VerticalSpeed = Height == 0 ? 0 : vz;
            }
            else
            {
                Height = zEnd;
                VerticalSpeed = vz;
            }

            return hit;
        }

        /// <summary>横の移動を区間に分けて進める。ポストで止まったらその時刻を <paramref name="post"/> に返す。</summary>
        private BallHit Move(SoccerConfig config, Vec2i v, int z0, int zEnd, out Vec2i position, out Vec2i velocity, out TickTime? post)
        {
            var hit = BallHit.None;
            var r = config.BallRadius;
            var wallX = Pitch.HalfLength - r;
            var wallY = Pitch.HalfWidth - r;
            var pos = Position;
            var start = TickTime.Zero;
            SegmentCount = 0;
            post = null;
            while (true)
            {
                var disp = start.Remaining.Scale(v);
                var end = pos + disp;
                if (disp.IsZero || SegmentCount == MaxSegments - 1)
                {
                    _segments[SegmentCount++] = new BallSegment(pos, end, v, start, TickTime.One);
                    position = end;
                    velocity = v;
                    return hit;
                }

                // ポスト（クロスバーより下の球だけ）。
                var postIndex = -1;
                TickTime? postAt = null;
                if (Math.Min(z0, zEnd) < Pitch.CrossbarHeight)
                {
                    var first = Touch.None;
                    for (int i = 0; i < Pitch.Posts.Count; i++)
                    {
                        var t = Sweep.First(pos, end, Pitch.Posts[i], Pitch.Posts[i], r + Pitch.PostRadius);
                        if (t.IsEarlierThan(first))
                        {
                            first = t;
                            postIndex = i;
                        }
                    }

                    if (postIndex >= 0) { postAt = TickTime.Of(first.Numerator, first.Denominator); }
                }

                // 横の壁：球の端が y = ±HalfWidth に触れる時刻。
                var yAt = Crossing(pos.Y, disp.Y, wallY);

                // ゴールラインの壁：触れた時刻の横の位置と高さがゴールの口なら通り抜ける。
                var xAt = Crossing(pos.X, disp.X, wallX);
                if (xAt.HasValue && InMouth(pos, disp, xAt.Value, start.Advance(xAt.Value), z0, zEnd, r)) { xAt = null; }

                var part = Earliest(postAt, Earliest(xAt, yAt));
                if (!part.HasValue)
                {
                    _segments[SegmentCount++] = new BallSegment(pos, end, v, start, TickTime.One);
                    position = end;
                    velocity = v;
                    return hit;
                }

                var at = start.Advance(part.Value);
                var contact = pos + part.Value.Scale(disp);
                if (postAt.HasValue && postAt.Value <= part.Value)
                {
                    _segments[SegmentCount++] = new BallSegment(pos, contact, v, start, at);
                    position = contact;
                    velocity = Reflect(v, contact - Pitch.Posts[postIndex], config.PostRestitutionPercent);
                    post = at;
                    return hit | BallHit.Post;
                }

                var reflectX = xAt.HasValue && xAt.Value == part.Value;
                var reflectY = yAt.HasValue && yAt.Value == part.Value;
                if (reflectX) { contact = new Vec2i(Math.Sign(disp.X) * wallX, contact.Y); }
                if (reflectY) { contact = new Vec2i(contact.X, Math.Sign(disp.Y) * wallY); }
                _segments[SegmentCount++] = new BallSegment(pos, contact, v, start, at);
                var percent = config.WallRestitutionPercent;
                v = new Vec2i(reflectX ? -v.X * percent / 100 : v.X, reflectY ? -v.Y * percent / 100 : v.Y);
                hit |= BallHit.Wall;
                pos = contact;
                start = at;
            }
        }

        /// <summary>
        /// 座標 <paramref name="from"/> から <paramref name="delta"/> 動くとき、|座標| = <paramref name="wall"/> を外へ越える区間の割合（越えなければ null）。
        /// 既に外にいる（ゴールの口を通った）なら null。
        /// </summary>
        private static TickTime? Crossing(int from, int delta, int wall)
        {
            if (delta > 0)
            {
                if (from + delta <= wall || from > wall) { return null; }
                return TickTime.Of(Math.Max(0L, (long)wall - from), delta);
            }

            if (delta < 0)
            {
                if (from + delta >= -wall || from < -wall) { return null; }
                return TickTime.Of(Math.Max(0L, (long)from + wall), -delta);
            }

            return null;
        }

        /// <summary>区間の割合 <paramref name="part"/>（Tick の時刻 <paramref name="at"/>）で、球がゴールの口にあるか（交差乗算で厳密に）。</summary>
        private static bool InMouth(Vec2i pos, Vec2i disp, TickTime part, TickTime at, int z0, int zEnd, int r)
        {
            // |y| < GoalHalfWidth：y = pos.Y + disp.Y × n / d。
            var y = ((long)pos.Y * part.Denominator) + ((long)disp.Y * part.Numerator);
            if (Math.Abs(y) >= (long)Pitch.GoalHalfWidth * part.Denominator) { return false; }

            // 高さ + 直径 ≦ クロスバー：z = z0 + (zEnd − z0) × n / d。
            var z = ((long)z0 * at.Denominator) + ((long)(zEnd - z0) * at.Numerator);
            return z + ((long)2 * r * at.Denominator) <= (long)Pitch.CrossbarHeight * at.Denominator;
        }

        private static TickTime? Earliest(TickTime? a, TickTime? b) => !a.HasValue ? b : !b.HasValue ? a : a.Value <= b.Value ? a : b;

        /// <summary>時刻 <paramref name="at"/> の高さ（0 に向けて切り捨て）。</summary>
        private static int HeightAt(int z0, int zEnd, TickTime at) => Math.Max(0, z0 + (int)((long)(zEnd - z0) * at.Numerator / at.Denominator));

        /// <summary>‰ の摩擦で 0 に向けて減らす（0 に向けて切り捨て）。</summary>
        private static int Decay(int value, int perMille) => (int)((long)value * (1000 - perMille) / 1000);

        /// <summary>速度を法線について反射し、割合を掛ける（法線は長さを問わない）。</summary>
        private static Vec2i Reflect(Vec2i v, Vec2i normal, int percent)
        {
            var nn = normal.LengthSquared;
            if (nn == 0) { return new Vec2i(-v.X * percent / 100, -v.Y * percent / 100); }
            var dot = Vec2i.Dot(v, normal);
            if (dot >= 0) { return new Vec2i(v.X * percent / 100, v.Y * percent / 100); }
            var rx = v.X - (2 * dot * normal.X / nn);
            var ry = v.Y - (2 * dot * normal.Y / nn);
            return new Vec2i((int)(rx * percent / 100), (int)(ry * percent / 100));
        }
    }
}
