using System;

namespace Game.Core
{
    /// <summary>試合の設定。</summary>
    [Serializable]
    public sealed class SolitaireConfig
    {
        public const int MaxDeadlockSearchStates = 1_000_000;

        /// <summary>山札から 1 回にめくる枚数。1 または 3。</summary>
        public int DrawCount = 1;

        /// <summary>
        /// 手詰まりの探索で調べる局面数の上限。超えたら手詰まりとはしない（まだ勝てる盤を手詰まりと判定しない側に倒す）。
        /// </summary>
        public int DeadlockSearchStates = 2_000;

        public static int RequireDrawCount(int drawCount)
        {
            if (drawCount != 1 && drawCount != 3)
            {
                throw new ArgumentOutOfRangeException(nameof(drawCount), drawCount, "めくる枚数は 1 または 3 です。");
            }

            return drawCount;
        }

        public void Validate()
        {
            RequireDrawCount(DrawCount);
            if (DeadlockSearchStates < 1 || DeadlockSearchStates > MaxDeadlockSearchStates)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(DeadlockSearchStates), DeadlockSearchStates, $"1〜{MaxDeadlockSearchStates} の範囲で指定してください。");
            }
        }

        /// <summary>進行中の試合が設定の書き換えの影響を受けないよう複製する。</summary>
        public SolitaireConfig Copy() => (SolitaireConfig)MemberwiseClone();
    }
}
