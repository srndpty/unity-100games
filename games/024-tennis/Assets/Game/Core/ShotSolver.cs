using System;

namespace Game.Core
{
    public enum ShotKind
    {
        /// <summary>ストローク：速く、ネットすれすれを越える低い弾道。</summary>
        Stroke,

        /// <summary>ロブ：遅く、ネットの高く上を越える弾道。</summary>
        Lob,

        Serve,
    }

    /// <summary>
    /// 狙った着地点へ飛ぶ初速を整数で解く。
    /// <para>
    /// 飛行 Tick 数 T が決まれば、離散の積分（速さを先に更新してから位置）で T Tick 後の高さが 0 になる縦の初速は
    /// vz = (g・T(T+1)/2 − z0) / T、横は (目標 − 始点) / T。
    /// T は「最高速を超えない」最小の Tick 数から始めて、ネットの面を通る高さが「ネット＋余裕」以上になるまで 1 ずつ伸ばす
    /// （余裕を満たす中でいちばん平らな弾道）。余裕が負なら、ネットより低く飛ぶ（ネットにかかる）。
    /// </para>
    /// </summary>
    public static class ShotSolver
    {
        /// <summary>飛行 Tick 数の上限（4 秒）。これでも余裕を満たせなければ上限の弾道で打つ。</summary>
        public const int MaxFlightTicks = 240;

        public static Vec3 Solve(Vec3 from, int targetX, int targetY, int maxSpeed, int clearance, int gravity)
        {
            if (maxSpeed <= 0) { throw new ArgumentOutOfRangeException(nameof(maxSpeed), maxSpeed, "正の値にしてください。"); }
            if (gravity <= 0) { throw new ArgumentOutOfRangeException(nameof(gravity), gravity, "正の値にしてください。"); }

            long dx = (long)targetX - from.X;
            long dy = (long)targetY - from.Y;
            var distance = Units.Sqrt((dx * dx) + (dy * dy));
            var ticks = (int)Math.Max(1, Math.Min(MaxFlightTicks, Units.CeilDiv(distance, maxSpeed)));
            while (ticks < MaxFlightTicks && !Clears(from, dy, ticks, clearance, gravity)) { ticks++; }

            return Velocity(from, dx, dy, ticks, gravity);
        }

        /// <summary>T Tick で着地する初速。</summary>
        public static Vec3 Velocity(Vec3 from, long dx, long dy, int ticks, int gravity)
        {
            long t = ticks;
            var vz = ((gravity * t * (t + 1) / 2) - from.Z) / t;
            return new Vec3((int)(dx / t), (int)(dy / t), (int)vz);
        }

        /// <summary>T Tick の弾道で、ネットの面を通るときの高さがネット＋余裕以上か（ネットを横切らなければ真）。</summary>
        private static bool Clears(Vec3 from, long dy, int ticks, int clearance, int gravity)
        {
            if (from.Y == 0 || dy == 0 || Math.Sign(dy) == Math.Sign(from.Y) || Math.Abs(dy) < Math.Abs((long)from.Y)) { return true; }

            // ネットを通る Tick（実数）を k = T・|y0| / |dy| とし、その前後の Tick の高さを線形に補間する。
            long t = ticks;
            var vz = ((gravity * t * (t + 1) / 2) - from.Z) / t;
            var num = t * Math.Abs((long)from.Y);
            var den = Math.Abs(dy);
            var k = num / den;
            var z0 = Height(from.Z, vz, gravity, k);
            var z1 = Height(from.Z, vz, gravity, k + 1);
            var z = z0 + ((z1 - z0) * (num - (k * den)) / den);
            return z >= Court.NetHeight + (long)clearance;
        }

        private static long Height(long z0, long vz, long gravity, long k) => z0 + (vz * k) - (gravity * k * (k + 1) / 2);
    }

    /// <summary>
    /// 打球の狙い：ショットの種類・左右の狙い・早打ち/遅打ちのずれから、着地点とネットの余裕を決めて初速を解く。
    /// <list type="bullet">
    /// <item>横：狙い × <see cref="TennisConfig.AimWide"/> に、ずれ × <see cref="TennisConfig.TimingShift"/> を打つ人の左（早打ち＝引っ張り）へ足す。</item>
    /// <item>深さ：早打ち（正）ほど長く（<see cref="TennisConfig.LongError"/>）、アウトしうる。</item>
    /// <item>ネットの余裕：遅打ち（負）ほど小さく（<see cref="TennisConfig.NetError"/>）、負になるとネットにかかる。</item>
    /// <item>サーブは高い打点から打つので余裕を減らすだけではネットにかからない。遅打ちほど短く（<see cref="TennisConfig.ServeShortError"/>）、急に落ちる。</item>
    /// </list>
    /// ずれが 0 なら狙いどおり（設定の検証で、コートの中に入ることを実際に飛ばして確かめる）。
    /// </summary>
    public static class Shots
    {
        public static Vec3 Plan(Side hitter, ShotKind kind, int aim, int timing, Vec3 contact, ServeCourt serveCourt, TennisConfig config)
        {
            if (aim < -1 || aim > 1) { throw new ArgumentOutOfRangeException(nameof(aim), aim, "-1・0・1 のどれかにしてください。"); }
            if (timing < -HitWindow.MaxTiming || timing > HitWindow.MaxTiming) { throw new ArgumentOutOfRangeException(nameof(timing), timing, "-1000〜1000 にしてください。"); }

            var target = Target(hitter, kind, aim, timing, serveCourt, config);
            var late = Math.Max(-timing, 0);
            int speed, clearance;
            switch (kind)
            {
                case ShotKind.Stroke:
                    speed = config.StrokeSpeed;
                    clearance = config.StrokeClearance - (late * config.NetError / HitWindow.MaxTiming);
                    break;
                case ShotKind.Lob:
                    speed = config.LobSpeed;
                    clearance = config.LobClearance - (late * config.NetError / HitWindow.MaxTiming);
                    break;
                case ShotKind.Serve:
                    speed = config.ServeSpeed;
                    clearance = config.ServeClearance - (late * config.ServeNetError / HitWindow.MaxTiming);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "未定義のショットです。");
            }

            return ShotSolver.Solve(contact, target.X, target.Y, speed, clearance, config.Gravity);
        }

        /// <summary>狙う着地点（z は 0）。</summary>
        public static Vec3 Target(Side hitter, ShotKind kind, int aim, int timing, ServeCourt serveCourt, TennisConfig config)
        {
            var forward = Court.Forward(hitter);
            var right = Court.RightOf(hitter);
            var early = Math.Max(timing, 0);
            int x, depth;
            switch (kind)
            {
                case ShotKind.Stroke:
                case ShotKind.Lob:
                    x = (aim * config.AimWide * right) - (timing * config.TimingShift / HitWindow.MaxTiming * right);
                    depth = (kind == ShotKind.Stroke ? config.StrokeDepth : config.LobDepth) + (early * config.LongError / HitWindow.MaxTiming);
                    break;
                case ShotKind.Serve:
                    var receiver = Court.Other(hitter);
                    var boxCenter = Court.CourtSign(receiver, serveCourt) * Court.HalfWidth / 2;
                    x = boxCenter + (aim * config.ServeAim * right) - (timing * config.ServeShift / HitWindow.MaxTiming * right);
                    var late = Math.Max(-timing, 0);
                    depth = config.ServeDepth + (early * config.ServeLongError / HitWindow.MaxTiming) - (late * config.ServeShortError / HitWindow.MaxTiming);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "未定義のショットです。");
            }

            return new Vec3(x, depth * forward, 0);
        }
    }
}
