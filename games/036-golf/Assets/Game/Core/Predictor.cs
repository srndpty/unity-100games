using System;
using IntGeometry;

namespace Game.Core
{
    /// <summary>予測線の結果。</summary>
    public readonly struct Prediction
    {
        public Prediction(int count, bool landed, Vec2i landing, BallState end)
        {
            Count = count;
            Landed = landed;
            Landing = landing;
            End = end;
        }

        /// <summary>書いた点の数。</summary>
        public int Count { get; }

        /// <summary>最初の着地まで進んだか（パターは転がりの途中で打ち切る）。</summary>
        public bool Landed { get; }

        public Vec2i Landing { get; }

        /// <summary>打ち切った時点の球。</summary>
        public BallState End { get; }
    }

    /// <summary>
    /// 予測：実際と同じ <see cref="BallSim.Step"/> で、打ち出した球の複製を進める。点の列は呼び出し側の配列に書く（割り当てなし）。
    /// </summary>
    public static class Predictor
    {
        /// <summary>
        /// 打ち出しの位置から、最初の着地（または <paramref name="maxTicks"/> Tick・止まる・入る・池/OB）までの各 Tick の位置を書く。
        /// 点は打ち出しの位置を含み、配列に入るだけ書く。高さは <paramref name="heights"/> に書く（null なら書かない）。
        /// </summary>
        public static Prediction Trace(BallState start, Hole hole, Vec2i wind, GolfConfig config, Vec2i[] points, int[]? heights, int maxTicks)
        {
            if (points == null) { throw new ArgumentNullException(nameof(points)); }
            var ball = start;
            var count = 0;
            Write(points, heights, ref count, ball);
            for (var tick = 0; tick < maxTicks && ball.IsMoving; tick++)
            {
                var events = BallSim.Step(ref ball, hole, wind, config);
                Write(points, heights, ref count, ball);
                if ((events & BallEvents.Landed) != 0) { return new Prediction(count, true, ball.Landing, ball); }
            }

            return new Prediction(count, ball.Landed, ball.Landing, ball);
        }

        /// <summary>止まる・入る・池/OB まで進めた球（1 打の Tick 数の上限で必ず終わる）。</summary>
        public static BallState Finish(BallState start, Hole hole, Vec2i wind, GolfConfig config)
        {
            var ball = start;
            while (ball.IsMoving) { BallSim.Step(ref ball, hole, wind, config); }
            return ball;
        }

        private static void Write(Vec2i[] points, int[]? heights, ref int count, BallState ball)
        {
            if (count >= points.Length) { return; }
            points[count] = ball.Position;
            if (heights != null && count < heights.Length) { heights[count] = ball.Z; }
            count++;
        }
    }
}
