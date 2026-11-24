using System;

namespace Game.Core
{
    /// <summary>
    /// 引き（発射台から離した位置へのベクトル）から初速を決める。初速は引きだけで決まり、ドラッグの経路・弾の種類（質量）に依らない。
    /// </summary>
    public static class Launch
    {
        /// <summary>
        /// 引き <paramref name="pull"/> の初速。引いた向きの逆向きで、速さは <see cref="SiegeConfig.MaxSpeed"/> × min(長さ, MaxPull) / MaxPull。
        /// 長さが <see cref="SiegeConfig.MinPull"/> 未満、または有限でなければ取り消し（null）。
        /// </summary>
        public static V2? Velocity(V2 pull, SiegeConfig config)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            if (!pull.IsFinite) { return null; }
            var length = pull.Length;
            if (!float.IsFinite(length) || length < config.MinPull) { return null; }
            var speed = config.MaxSpeed * Math.Min(length, config.MaxPull) / config.MaxPull;
            return pull * (-speed / length);
        }

        /// <summary>描く引き：長さを <see cref="SiegeConfig.MaxPull"/> で頭打ちにする。</summary>
        public static V2 Clamp(V2 pull, SiegeConfig config)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            if (!pull.IsFinite) { return V2.Zero; }
            var length = pull.Length;
            return length <= config.MaxPull ? pull : pull * (config.MaxPull / length);
        }
    }

    /// <summary>
    /// 弾道プレビュー。Physics2D と同じ半陰的オイラー（v += g·dt、x += v·dt、弾の減衰は 0）で、1 Tick ごとの位置を求める。
    /// 何にも触れない間は、Script モードで 1 Tick に 1 回 <c>Physics2D.Simulate(1/60)</c> した弾の位置と一致する。
    /// </summary>
    public static class Trajectory
    {
        public const float TickSeconds = 1f / SiegeRun.TickRate;

        /// <summary>
        /// <paramref name="buffer"/>[i] に、射出から i + 1 Tick 後の位置を書く（配列を使い回し、割り当てない）。
        /// </summary>
        public static void Fill(V2 start, V2 velocity, float gravity, V2[] buffer)
        {
            if (buffer == null) { throw new ArgumentNullException(nameof(buffer)); }
            var x = start.X;
            var y = start.Y;
            var vx = velocity.X;
            var vy = velocity.Y;
            var dv = gravity * TickSeconds;
            for (var i = 0; i < buffer.Length; i++)
            {
                vy += dv;
                x += vx * TickSeconds;
                y += vy * TickSeconds;
                buffer[i] = new V2(x, y);
            }
        }
    }
}
