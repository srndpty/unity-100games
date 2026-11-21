using System;

namespace Game.Core
{
    /// <summary>試合の設定。盤は 5×5 固定（大きさは設定に出さない）。</summary>
    [Serializable]
    public sealed class LightsOutConfig
    {
        public const int MinScramblePresses = 1;
        public const int MaxScramblePresses = LightsBoard.CellCount;

        /// <summary>問題を作るときに押す、重複のないマスの数。</summary>
        public int ScramblePresses = 10;

        public static int RequireScramblePresses(int presses)
        {
            if (presses < MinScramblePresses || presses > MaxScramblePresses)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(presses), presses, $"{MinScramblePresses}〜{MaxScramblePresses} の範囲で指定してください。");
            }

            return presses;
        }

        public void Validate() => RequireScramblePresses(ScramblePresses);

        /// <summary>進行中の試合が設定の書き換えの影響を受けないよう複製する。</summary>
        public LightsOutConfig Copy() => (LightsOutConfig)MemberwiseClone();
    }
}
