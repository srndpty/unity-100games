using System;
using Game.Core;

namespace Game.App
{
    /// <summary>
    /// 手前の選手の自動操縦（撮影・テスト用）。CPU と同じ判断（<see cref="CpuBrain"/>）を手前の側で使う。
    /// 試合が変わったら判断の状態を捨てて作り直す（前の試合の計画を持ち越さない）。乱数は試合のシードから決めるので、試合ごとに再現できる。
    /// </summary>
    public sealed class Autopilot : IPilot
    {
        /// <summary>試合のシードと混ぜて、CPU（奥）と違う系列にする値。</summary>
        public const int SeedSalt = 0x0A770000;

        private TennisMatch? _match;
        private CpuBrain? _brain;

        public AthleteCommand CommandFor(TennisMatch match)
        {
            if (match == null) { throw new ArgumentNullException(nameof(match)); }
            if (!ReferenceEquals(match, _match) || _brain == null)
            {
                _match = match;
                _brain = new CpuBrain(Side.Near, unchecked(match.Seed ^ SeedSalt));
            }

            return _brain.Decide(match);
        }
    }
}
