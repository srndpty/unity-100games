using System;
using System.Collections.Generic;
using IntGeometry;

namespace Game.Core
{
    /// <summary>予測した球の 1 Tick（今から何 Tick 後に、どこに、どの高さで）。</summary>
    public readonly struct BallSample
    {
        public BallSample(int tick, Vec2i position, int height)
        {
            Tick = tick;
            Position = position;
            Height = height;
        }

        public int Tick { get; }
        public Vec2i Position { get; }
        public int Height { get; }
    }

    /// <summary>
    /// 球の軌道予測。球を複製して本番と同じ <see cref="Ball.Step"/> で進めるので、予測と本番の軌跡は Tick 単位で一致する（誰も触らなければ）。
    /// 選手が球に届く時刻は、止まった状態からまっすぐ走った距離の表（<see cref="Reach"/>、試合の移動と同じ加速）で求める。
    /// </summary>
    public static class BallPredictor
    {
        /// <summary>今から 1〜<paramref name="ticks"/> Tick 後の球。止まったら、止まった位置を最後に 1 つ入れて打ち切る。</summary>
        public static IReadOnlyList<BallSample> Trajectory(Ball ball, SoccerConfig config, int ticks)
        {
            if (ball == null) { throw new ArgumentNullException(nameof(ball)); }
            var b = ball.Clone();
            var list = new List<BallSample>(ticks);
            for (int t = 1; t <= ticks; t++)
            {
                b.Step(config);
                list.Add(new BallSample(t, b.Position, b.Height));
                if (b.IsStopped) { break; }
            }

            return list.AsReadOnly();
        }

        /// <summary>止まった状態からまっすぐ走って、<paramref name="ticks"/> Tick で進む距離（試合の移動と同じ：毎 Tick 加速して最高速で頭打ち）。</summary>
        public static long Reach(int ticks, int maxSpeed, int acceleration)
        {
            // 加速の間（k ≤ r）は k × 加速度、その後は最高速。和を閉じた式で求める（Σ min(k a, max)）。
            if (ticks <= 0) { return 0; }
            long r = maxSpeed / acceleration;
            if (ticks <= r) { return (long)acceleration * ticks * (ticks + 1) / 2; }
            return ((long)acceleration * r * (r + 1) / 2) + ((ticks - r) * (long)maxSpeed);
        }

        /// <summary>
        /// 選手が軌跡のどこかで球に届く最初の Tick（届く距離 <paramref name="reach"/>・高さの上限 <paramref name="maxHeight"/>）。届かなければ -1。
        /// 軌跡が止まって終わったら、止まった位置へ走って届く Tick も数える。
        /// </summary>
        public static int ArrivalTick(IReadOnlyList<BallSample> trajectory, Vec2i from, int maxSpeed, int acceleration, int reach, int maxHeight, int horizon)
        {
            if (trajectory == null) { throw new ArgumentNullException(nameof(trajectory)); }
            foreach (var s in trajectory)
            {
                if (s.Height > maxHeight) { continue; }
                if (Arrives(from, s.Position, s.Tick, maxSpeed, acceleration, reach)) { return s.Tick; }
            }

            if (trajectory.Count == 0) { return -1; }
            var last = trajectory[trajectory.Count - 1];
            for (int t = last.Tick + 1; t <= horizon; t++)
            {
                if (last.Height <= maxHeight && Arrives(from, last.Position, t, maxSpeed, acceleration, reach)) { return t; }
            }

            return -1;
        }

        private static bool Arrives(Vec2i from, Vec2i to, int ticks, int maxSpeed, int acceleration, int reach)
        {
            // 保持の判定（掃引）と同じく、届く距離より「近い」ときに届いたとする（ちょうどの距離では届かない）。
            var d = IntMath.Sqrt(Vec2i.DistanceSquared(from, to)) - reach;
            return d < 0 || Reach(ticks, maxSpeed, acceleration) > d;
        }
    }
}
