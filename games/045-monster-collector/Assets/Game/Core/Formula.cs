using System;

namespace Game.Core
{
    /// <summary>能力・経験値・ダメージ・捕獲・逃走の式。全て整数で、割り算は 0 方向へ切り捨てる。</summary>
    public static class Formula
    {
        /// <summary>一致補正（技の属性と自分の属性が同じ）の百分率。</summary>
        public const int StabPercent = 150;

        public const int RandomMinPercent = 85;
        public const int RandomMaxPercent = 100;

        /// <summary>捕獲値がこれ以上なら必ず成功する。捕獲・逃走の判定は <c>Next(256)</c>。</summary>
        public const int CertainRoll = 255;

        public static int MaxHp(int baseHp, int level) => baseHp * 2 * level / 100 + level + 10;

        public static int Stat(int baseStat, int level) => baseStat * 2 * level / 100 + 5;

        /// <summary>レベル L に必要な累計経験値 = L^3 × 分子 / 分母。</summary>
        public static int ExpForLevel(Rules rules, int level)
        {
            if (rules == null) { throw new ArgumentNullException(nameof(rules)); }
            long l = level;
            return (int)(l * l * l * rules.ExpCurveNumerator / rules.ExpCurveDenominator);
        }

        /// <summary>倒した相手 1 匹から得る経験値の合計（出た生存者で等分する前）。</summary>
        public static int ExpGain(Rules rules, SpeciesDef foe, int foeLevel, bool trainer)
        {
            if (rules == null) { throw new ArgumentNullException(nameof(rules)); }
            if (foe == null) { throw new ArgumentNullException(nameof(foe)); }
            long gain = (long)foe.ExpYield * foeLevel / rules.ExpYieldDivisor;
            if (trainer) { gain = gain * rules.TrainerExpPercent / 100; }
            return (int)Math.Max(1, Math.Min(int.MaxValue, gain));
        }

        /// <summary>能力の段階（-3〜+3）を掛ける：正なら (2+s)/2、負なら 2/(2-s)。</summary>
        public static int ApplyStage(int value, int stage)
        {
            if (stage >= 0) { return value * (2 + stage) / 2; }
            return value * 2 / (2 - stage);
        }

        /// <summary>
        /// ダメージ。<c>((2×Lv/5+2) × 威力 × 攻撃 / 防御) / 50 + 2</c> → 一致補正 → 相性 → 乱数（85〜100%）→ 最低 1。
        /// </summary>
        public static int Damage(int level, int power, int attack, int defense, bool stab, int effectiveness, int randomPercent)
        {
            if (defense < 1) { throw new ArgumentOutOfRangeException(nameof(defense)); }
            long d = (2L * level / 5 + 2) * power * attack / defense / 50 + 2;
            if (stab) { d = d * StabPercent / 100; }
            d = d * effectiveness / 100;
            d = d * randomPercent / 100;
            return (int)Math.Max(1, Math.Min(int.MaxValue, d));
        }

        /// <summary>捕獲値：捕獲率 × (3×最大HP − 2×HP) / (3×最大HP) × 状態異常の補正。<see cref="CertainRoll"/> 以上なら必ず成功。</summary>
        public static int CaptureValue(int catchRate, int hp, int maxHp, int statusPercent)
        {
            if (maxHp < 1) { throw new ArgumentOutOfRangeException(nameof(maxHp)); }
            long v = (long)catchRate * (3L * maxHp - 2L * hp) / (3L * maxHp);
            v = v * statusPercent / 100;
            return (int)Math.Max(0, Math.Min(CertainRoll, v));
        }

        /// <summary>逃走値：自分の素早さ × 128 / 相手の素早さ + 30 × 試した回数（今回を含まない）。256 以上なら必ず成功。</summary>
        public static int RunOdds(int playerSpeed, int foeSpeed, int previousAttempts)
        {
            long v = (long)playerSpeed * 128 / Math.Max(1, foeSpeed) + 30L * previousAttempts;
            return (int)Math.Min(256, v);
        }
    }
}
