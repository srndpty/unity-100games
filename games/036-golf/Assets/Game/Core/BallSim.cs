using System;
using IntGeometry;
using TileMotion;

namespace Game.Core
{
    public enum BallPhase
    {
        /// <summary>止まっている（打つ前・止まった後）。</summary>
        Resting,

        Flying,

        Rolling,

        /// <summary>カップに入った。</summary>
        Holed,

        /// <summary>池・OB の地面に触れた。</summary>
        Out,
    }

    /// <summary>1 Tick に球に起きたこと。</summary>
    [Flags]
    public enum BallEvents
    {
        None = 0,

        /// <summary>打ってから最初に地面に触れた。</summary>
        Landed = 1,

        /// <summary>地面で跳ねた（最初の着地を含む）。</summary>
        Bounced = 1 << 1,

        TreeHit = 1 << 2,

        Holed = 1 << 3,

        Out = 1 << 4,

        Stopped = 1 << 5,

        /// <summary>カップの上を速すぎて通り過ぎた。</summary>
        LipOut = 1 << 6,

        /// <summary>1 打の Tick 数の上限で止めた。</summary>
        TimedOut = 1 << 7,
    }

    /// <summary>球の状態。値型なので、代入で複製して予測に使える。</summary>
    public struct BallState
    {
        public BallPhase Phase;

        /// <summary>水平の位置（µm）。</summary>
        public Vec2i Position;

        /// <summary>高さ（µm、地面は 0）。</summary>
        public int Z;

        /// <summary>水平の速度（µm/Tick）。</summary>
        public Vec2i Velocity;

        public int VelocityZ;

        /// <summary>横の加速（µm/Tick²。正なら進む向きの右へ曲がる）。最初の着地で 0 になる。</summary>
        public int Spin;

        /// <summary>最初の着地で水平の速さに掛ける割合（/4096、クラブと打点で決まる）。</summary>
        public int FirstBite;

        /// <summary>打ってからの Tick 数。</summary>
        public int Ticks;

        /// <summary>最初の着地をしたか。</summary>
        public bool Landed;

        /// <summary>最初に地面に触れた位置（<see cref="Landed"/> のときだけ意味がある）。</summary>
        public Vec2i Landing;

        /// <summary>池・OB に触れた地面（<see cref="BallPhase.Out"/> のとき）。</summary>
        public Surface OutSurface;

        /// <summary>速すぎてカップの上を通っている途中（カップの円から出るまで、カップには入らない）。</summary>
        public bool OverCup;

        public bool IsMoving => Phase == BallPhase.Flying || Phase == BallPhase.Rolling;

        public static BallState RestingAt(Vec2i position) => new() { Phase = BallPhase.Resting, Position = position, FirstBite = Units.Ratio };

        public void MixInto(ref StateHash hash)
        {
            hash.Mix((int)Phase);
            hash.Mix(Position.X);
            hash.Mix(Position.Y);
            hash.Mix(Z);
            hash.Mix(Velocity.X);
            hash.Mix(Velocity.Y);
            hash.Mix(VelocityZ);
            hash.Mix(Spin);
            hash.Mix(FirstBite);
            hash.Mix(Ticks);
            hash.Mix(Landed);
            hash.Mix(Landing.X);
            hash.Mix(Landing.Y);
            hash.Mix((int)OutSurface);
            hash.Mix(OverCup);
        }
    }

    /// <summary>
    /// 球を 1 Tick 進める。予測線と実際の球の両方がこれを使う（同じ関数なので、同じ打ち出しなら軌跡は全 Tick で一致する）。
    /// <list type="bullet">
    /// <item>飛行：抵抗 → 風 → 横の曲がり → 重力の順に速度を変え、経路の点の列で木・地面との最初の接触を探す。</item>
    /// <item>木の横に当たったら、触れる直前の点で水平の速度を反射して減らし、その Tick の残りの点を新しい速度で進め直す（同じ木には 1 Tick に 1 回）。
    /// 木の上から入ったら、水平の速度を減らし鉛直の速度を 0 にして真下へ落とす。</item>
    /// <item>着地：池・OB なら終わり。他は鉛直の速さを反発で返し、水平の速さを減らす。反発が小さければ転がりへ。着地した Tick の残りは進めない。</item>
    /// <item>転がり：傾きの加速を足し、地面の減速で遅くし、止まる条件を見てから、木・カップとの接触を探して進む。地面の種類は Tick の始めと終わりの位置で決める。</item>
    /// </list>
    /// </summary>
    public static class BallSim
    {
        /// <summary>1 ホールの木の数の上限（1 Tick に当たった木をビットで持つ）。</summary>
        public const int MaxTrees = 64;

        public static BallEvents Step(ref BallState ball, Hole hole, Vec2i wind, GolfConfig config)
        {
            if (hole == null) { throw new ArgumentNullException(nameof(hole)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            if (!ball.IsMoving) { return BallEvents.None; }

            ball.Ticks++;
            var events = ball.Phase == BallPhase.Flying ? Fly(ref ball, hole, wind, config) : Roll(ref ball, hole, config);
            if (ball.IsMoving && ball.Ticks >= config.MaxShotTicks)
            {
                // 空中のまま時間切れでも、置いた位置の地面は着地・転がりと同じく判定する。池・OBで静止させると、次の打ち出しが例外になる。
                ball.Z = 0;
                ball.Velocity = Vec2i.Zero;
                ball.VelocityZ = 0;
                var surface = hole.SurfaceAt(ball.Position);
                if (surface == Surface.Water || surface == Surface.OutOfBounds)
                {
                    events |= GoOut(ref ball, surface) | BallEvents.TimedOut;
                }
                else
                {
                    ball.Phase = BallPhase.Resting;
                    events |= BallEvents.Stopped | BallEvents.TimedOut;
                }
            }

            return events;
        }

        /// <summary>傾き（‰）による転がりの加速（µm/Tick²）。斜面を転がる球の加速 g·sinθ·5/7 を、傾き ≈ sinθ として整数で。</summary>
        public static Vec2i SlopeAcceleration(Vec2i grade, GolfConfig config) =>
            new((int)((long)grade.X * config.Gravity * 5 / 7000), (int)((long)grade.Y * config.Gravity * 5 / 7000));

        private static BallEvents Fly(ref BallState ball, Hole hole, Vec2i wind, GolfConfig config)
        {
            var v = ball.Velocity;
            var vz = ball.VelocityZ;
            v = new Vec2i(v.X - (int)((long)v.X * config.Drag / Units.Fine), v.Y - (int)((long)v.Y * config.Drag / Units.Fine));
            vz -= (int)((long)vz * config.Drag / Units.Fine);
            v += new Vec2i((int)((long)wind.X * config.WindPush / Units.Fine), (int)((long)wind.Y * config.WindPush / Units.Fine));
            if (ball.Spin != 0 && !v.IsZero)
            {
                // 進む向きの右（(x, y) の右は (y, −x)）。
                var lateral = IntMath.Scale(new Vec2i(v.Y, -v.X), Math.Abs(ball.Spin));
                v = ball.Spin > 0 ? v + lateral : v - lateral;
            }

            vz -= config.Gravity;

            var events = BallEvents.None;
            var position = ball.Position;
            var z = ball.Z;
            var remaining = Units.Samples;
            long hitMask = 0;
            while (true)
            {
                var best = remaining + 1;
                var kind = Contact.None;
                var treeIndex = -1;
                for (var i = 0; i < hole.Trees.Count; i++)
                {
                    if ((hitMask & (1L << i)) != 0) { continue; }
                    var tree = hole.Trees[i];
                    var side = CircleSweep.First(position, v, tree.Center, tree.Radius, remaining);
                    if (side >= 1 && side < best && ZAt(z, vz, side) <= tree.Height)
                    {
                        best = side;
                        kind = Contact.Side;
                        treeIndex = i;
                    }

                    if (z > tree.Height && vz < 0)
                    {
                        var top = CeilDiv(((long)z - tree.Height) * Units.Samples, -vz);
                        if (top <= remaining && top < best && IsInside(CircleSweep.Point(position, v, (int)top), tree))
                        {
                            best = (int)top;
                            kind = Contact.Top;
                            treeIndex = i;
                        }
                    }
                }

                if (vz < 0)
                {
                    var ground = CeilDiv((long)z * Units.Samples, -vz);
                    if (ground <= remaining && ground < best)
                    {
                        best = (int)ground;
                        kind = Contact.Ground;
                    }
                }

                switch (kind)
                {
                    case Contact.None:
                        ball.Position = CircleSweep.Point(position, v, remaining);
                        ball.Z = ZAt(z, vz, remaining);
                        ball.Velocity = v;
                        ball.VelocityZ = vz;
                        return events;

                    case Contact.Ground:
                        ball.Position = CircleSweep.Point(position, v, best);
                        ball.Z = 0;
                        ball.Velocity = v;
                        ball.VelocityZ = vz;
                        return events | Land(ref ball, hole, config);

                    case Contact.Side:
                        {
                            var tree = hole.Trees[treeIndex];
                            var contact = CircleSweep.Point(position, v, best);
                            position = CircleSweep.Point(position, v, best - 1);
                            z = ZAt(z, vz, best - 1);
                            v = Reflect(v, contact - tree.Center, config.TreeRestitution);
                            remaining -= best - 1;
                            hitMask |= 1L << treeIndex;
                            events |= BallEvents.TreeHit;
                            break;
                        }

                    case Contact.Top:
                        position = CircleSweep.Point(position, v, best);
                        z = ZAt(z, vz, best);
                        v = Scale(v, config.TreeRestitution);
                        vz = 0;
                        remaining -= best;
                        hitMask |= 1L << treeIndex;
                        events |= BallEvents.TreeHit;
                        break;
                }
            }
        }

        private static BallEvents Land(ref BallState ball, Hole hole, GolfConfig config)
        {
            var surface = hole.SurfaceAt(ball.Position);
            if (surface == Surface.Water || surface == Surface.OutOfBounds)
            {
                if (!ball.Landed)
                {
                    ball.Landed = true;
                    ball.Landing = ball.Position;
                }

                return GoOut(ref ball, surface) | BallEvents.Landed;
            }

            var events = BallEvents.Bounced;
            var keep = config.BounceKeep[(int)surface];
            if (!ball.Landed)
            {
                ball.Landed = true;
                ball.Landing = ball.Position;
                keep = keep * ball.FirstBite / Units.Ratio;
                events |= BallEvents.Landed;
            }

            ball.Velocity = Scale(ball.Velocity, keep);
            ball.Spin = 0;
            var bounce = (int)(-(long)ball.VelocityZ * config.BounceRestitution[(int)surface] / Units.Ratio);
            if (InCup(ball.Position, hole, config))
            {
                if (ball.Velocity.LengthSquared <= (long)config.CupMaxSpeed * config.CupMaxSpeed) { return events | Drop(ref ball, hole); }
                ball.OverCup = true;
                events |= BallEvents.LipOut;
            }

            if (bounce <= config.RollThreshold)
            {
                ball.Phase = BallPhase.Rolling;
                ball.VelocityZ = 0;
            }
            else
            {
                ball.VelocityZ = bounce;
            }

            return events;
        }

        private static BallEvents Roll(ref BallState ball, Hole hole, GolfConfig config)
        {
            var surface = hole.SurfaceAt(ball.Position);
            var decel = config.RollDecel[(int)surface];
            var slope = SlopeAcceleration(hole.GradeAt(ball.Position), config);
            var v = ball.Velocity + slope;
            var speed = IntMath.Sqrt(v.LengthSquared);
            v = speed <= decel ? Vec2i.Zero : IntMath.Scale(v, (int)(speed - decel));
            if (v.LengthSquared <= (long)config.StopSpeed * config.StopSpeed && slope.LengthSquared <= (long)decel * decel)
            {
                // カップの上で止まったら落ちる。
                if (InCup(ball.Position, hole, config)) { return Drop(ref ball, hole); }
                ball.Velocity = Vec2i.Zero;
                ball.Phase = BallPhase.Resting;
                ball.OverCup = false;
                return BallEvents.Stopped;
            }

            var events = BallEvents.None;
            var position = ball.Position;
            var remaining = Units.Samples;
            var canDrop = v.LengthSquared <= (long)config.CupMaxSpeed * config.CupMaxSpeed;
            long hitMask = 0;
            while (true)
            {
                var best = remaining + 1;
                var treeIndex = -1;
                for (var i = 0; i < hole.Trees.Count; i++)
                {
                    if ((hitMask & (1L << i)) != 0) { continue; }
                    var tree = hole.Trees[i];
                    var side = CircleSweep.First(position, v, tree.Center, tree.Radius, remaining);
                    if (side >= 1 && side < best)
                    {
                        best = side;
                        treeIndex = i;
                    }
                }

                // カップ：円の外から入った点（または、通り過ぎている途中でない球が円の中にいる）で速さを比べる。
                var cup = CircleSweep.First(position, v, hole.Cup, config.CupRadius, remaining);
                if (cup >= 0 && cup < best && !(cup == 0 && ball.OverCup))
                {
                    if (canDrop)
                    {
                        ball.Position = CircleSweep.Point(position, v, cup);
                        return events | Drop(ref ball, hole);
                    }

                    ball.OverCup = true;
                    events |= BallEvents.LipOut;
                }

                if (treeIndex < 0)
                {
                    ball.Position = CircleSweep.Point(position, v, remaining);
                    ball.Velocity = v;
                    break;
                }

                var hit = hole.Trees[treeIndex];
                var contact = CircleSweep.Point(position, v, best);
                position = CircleSweep.Point(position, v, best - 1);
                v = Reflect(v, contact - hit.Center, config.TreeRestitution);
                remaining -= best - 1;
                hitMask |= 1L << treeIndex;
                events |= BallEvents.TreeHit;
            }

            if (ball.OverCup && !InCup(ball.Position, hole, config)) { ball.OverCup = false; }
            var after = hole.SurfaceAt(ball.Position);
            if (after == Surface.Water || after == Surface.OutOfBounds) { events |= GoOut(ref ball, after); }
            return events;
        }

        private static bool InCup(Vec2i position, Hole hole, GolfConfig config) =>
            Vec2i.DistanceSquared(position, hole.Cup) < (long)config.CupRadius * config.CupRadius;

        private static BallEvents Drop(ref BallState ball, Hole hole)
        {
            ball.Phase = BallPhase.Holed;
            ball.Position = hole.Cup;
            ball.Z = 0;
            ball.Velocity = Vec2i.Zero;
            ball.VelocityZ = 0;
            return BallEvents.Holed;
        }

        private static BallEvents GoOut(ref BallState ball, Surface surface)
        {
            ball.Phase = BallPhase.Out;
            ball.OutSurface = surface;
            ball.Z = 0;
            ball.Velocity = Vec2i.Zero;
            ball.VelocityZ = 0;
            return BallEvents.Out;
        }

        /// <summary>法線 <paramref name="normal"/>（円の中心から接点へ）で水平の速度を反射し、割合を掛ける。</summary>
        internal static Vec2i Reflect(Vec2i v, Vec2i normal, int restitution)
        {
            if (normal.IsZero) { normal = -v; }
            var unit = IntMath.Scale(normal, Units.Fine);
            var into = ((long)v.X * unit.X) + ((long)v.Y * unit.Y);
            if (into < 0)
            {
                // v' = v − 2 (v·n) n（n は Q16 の単位ベクトル）。
                v = new Vec2i(v.X - (int)(2 * into * unit.X / ((long)Units.Fine * Units.Fine)), v.Y - (int)(2 * into * unit.Y / ((long)Units.Fine * Units.Fine)));
            }

            return Scale(v, restitution);
        }

        private static Vec2i Scale(Vec2i v, int ratio) => new((int)((long)v.X * ratio / Units.Ratio), (int)((long)v.Y * ratio / Units.Ratio));

        /// <summary>経路の点 k の高さ（端数は床へ。接触の点を求める切り上げと合わせ、その点で必ず高さ以下になる）。</summary>
        private static int ZAt(int z, int vz, int k) => z + (int)FloorDiv((long)vz * k, Units.Samples);

        private static long FloorDiv(long numerator, long denominator) => numerator >= 0 ? numerator / denominator : -((-numerator + denominator - 1) / denominator);

        private static long CeilDiv(long numerator, long denominator) => numerator <= 0 ? 0 : (numerator + denominator - 1) / denominator;

        private static bool IsInside(Vec2i point, Tree tree) => Vec2i.DistanceSquared(point, tree.Center) < (long)tree.Radius * tree.Radius;

        private enum Contact
        {
            None,
            Ground,
            Side,
            Top,
        }
    }
}
