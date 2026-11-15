using System;

namespace Game.Core
{
    /// <summary>
    /// 1 Tick 中のパドルの軌跡（x 方向）。<see cref="From"/> から等速で動き、Tick 中の時刻 <see cref="StopAt"/> で <see cref="To"/> に着いたら
    /// 以降は止まる（途中で壁に着いた場合）。ボールとの衝突判定と描画の補間が同じ軌跡を使う。
    /// </summary>
    public readonly struct PaddleMotion
    {
        private PaddleMotion(float from, float to, float stopAt)
        {
            From = from;
            To = to;
            StopAt = stopAt;
        }

        /// <summary>Tick 開始時の x。</summary>
        public float From { get; }

        /// <summary>Tick 終了時の x。</summary>
        public float To { get; }

        /// <summary><see cref="To"/> に着く時刻（0 = 開始、1 = 終了）。</summary>
        public float StopAt { get; }

        /// <summary>止まっている（瞬間移動を含む）。</summary>
        public static PaddleMotion At(float x) => new(x, x, 0f);

        /// <summary><paramref name="from"/> から <paramref name="requestedDelta"/> だけ動こうとし、±<paramref name="limit"/> で止まる軌跡。</summary>
        public static PaddleMotion Clamped(float from, float requestedDelta, float limit)
        {
            var to = Math.Clamp(from + requestedDelta, -limit, limit);
            if (to == from)
            {
                return At(from);
            }

            // 端で止まらなければ (to - from) == requestedDelta なので 1 になる。
            return new(from, to, Math.Clamp((to - from) / requestedDelta, 0f, 1f));
        }

        /// <summary>Tick 中の時刻 <paramref name="fraction"/>（0 = 開始、1 = 終了）での x。</summary>
        public float XAt(float fraction) =>
            fraction >= StopAt ? To : From + ((To - From) * (fraction / StopAt));
    }
}
