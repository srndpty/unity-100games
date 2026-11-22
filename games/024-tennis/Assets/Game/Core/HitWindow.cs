using System;

namespace Game.Core
{
    /// <summary>打点の判定の結果。<see cref="Ok"/> 以外は打てない理由。</summary>
    public enum HitCheck
    {
        Ok,

        /// <summary>水平の距離が届く範囲（<see cref="TennisConfig.Reach"/>）より遠い。</summary>
        TooFar,

        /// <summary>打てる高さ（<see cref="TennisConfig.MaxHitZ"/>）より高い。</summary>
        TooHigh,

        /// <summary>打てる高さ（<see cref="TennisConfig.MinHitZ"/>）より低い。</summary>
        TooLow,

        /// <summary>球が自分の側にないか、自分のほうへ向かっていない。</summary>
        NotIncoming,

        /// <summary>サーブはバウンドする前に打てない。</summary>
        UnbouncedServe,

        /// <summary>打ち合いの球ではない（トス中・死に球・ポイントの間）。</summary>
        NotInPlay,
    }

    /// <summary>
    /// 打点ウィンドウ：球を打てるかと、早打ち・遅打ちのずれ。
    /// 理想の打点は選手の前方 <see cref="TennisConfig.ContactAhead"/>。打点で球がそれよりネット寄りなら早打ち（ずれが正）、後ろなら遅打ち（負）。
    /// ずれは <see cref="TennisConfig.TimingSpan"/> で -1000〜1000（千分率）に正規化する。
    /// </summary>
    public static class HitWindow
    {
        public const int MaxTiming = 1000;

        /// <param name="incomingToHitter">球が打つ側へ向かう打ち合いの球か（相手が最後に打った・サーブがバウンドした後を含む）。</param>
        /// <param name="unbouncedServe">バウンド前のサーブか。</param>
        public static HitCheck Check(Athlete athlete, Ball ball, bool incomingToHitter, bool unbouncedServe, TennisConfig config)
        {
            if (!incomingToHitter || ball.IsDead) { return HitCheck.NotInPlay; }
            if (unbouncedServe) { return HitCheck.UnbouncedServe; }

            var p = ball.Position;
            var forward = Court.Forward(athlete.Side);
            if (p.Y * forward >= 0 || ball.Velocity.Y * forward > 0) { return HitCheck.NotIncoming; }
            if (p.FlatDistanceSquared(athlete.Position) > (long)config.Reach * config.Reach) { return HitCheck.TooFar; }
            if (p.Z > config.MaxHitZ) { return HitCheck.TooHigh; }
            if (p.Z < config.MinHitZ) { return HitCheck.TooLow; }
            return HitCheck.Ok;
        }

        /// <summary>早打ち（正）・遅打ち（負）のずれ（千分率）。</summary>
        public static int Timing(Athlete athlete, Vec3 ballPosition, TennisConfig config)
        {
            var forward = Court.Forward(athlete.Side);
            long ahead = ((long)ballPosition.Y - athlete.Position.Y) * forward;
            var offset = (ahead - config.ContactAhead) * MaxTiming / config.TimingSpan;
            return (int)Math.Max(-MaxTiming, Math.Min(MaxTiming, offset));
        }

        /// <summary>理想の打点（選手の前方 <see cref="TennisConfig.ContactAhead"/>）。</summary>
        public static Vec3 IdealContact(Side side, Vec3 athletePosition, TennisConfig config) =>
            new(athletePosition.X, athletePosition.Y + (config.ContactAhead * Court.Forward(side)), 0);
    }
}
