using System;

namespace Game.Core
{
    /// <summary>問題の難しさ。</summary>
    public enum Difficulty
    {
        /// <summary>与えが多く、制約伝播（Naked / Hidden Single）だけで解ける。</summary>
        Easy,

        /// <summary>制約伝播だけで解ける。</summary>
        Normal,

        /// <summary>与えが少なく、解が 1 つであることだけを保証する（仮置きが要ることがある）。</summary>
        Hard,
    }

    /// <summary>問題の生成の設定。</summary>
    [Serializable]
    public sealed class SudokuConfig
    {
        /// <summary>与えの目標数の下限。点対称を保ったまま抜ける数には限りがあり、これより少ない目標は実際には届かない。</summary>
        public const int MinTargetGivens = 22;

        public const int MaxTargetGivens = 60;

        /// <summary>
        /// 難しさごとの与え（初めから入っている数字）の目標数。条件を保ったまま抜ける間は目標まで抜く。
        /// 点対称の 2 マスを組で抜くので、結果は目標か目標 + 1、抜けなくなればそれより多い。
        /// </summary>
        public int EasyGivens = 42;

        public int NormalGivens = 36;

        public int HardGivens = 26;

        public int GivensOf(Difficulty difficulty) => difficulty switch
        {
            Difficulty.Easy => EasyGivens,
            Difficulty.Normal => NormalGivens,
            Difficulty.Hard => HardGivens,
            _ => throw new ArgumentOutOfRangeException(nameof(difficulty), difficulty, "未定義の難しさです。"),
        };

        /// <summary>制約伝播だけで解ける問題に限るか。</summary>
        public static bool IsLogicOnly(Difficulty difficulty) => difficulty switch
        {
            Difficulty.Easy => true,
            Difficulty.Normal => true,
            Difficulty.Hard => false,
            _ => throw new ArgumentOutOfRangeException(nameof(difficulty), difficulty, "未定義の難しさです。"),
        };

        public void Validate()
        {
            RequireRange(EasyGivens, nameof(EasyGivens));
            RequireRange(NormalGivens, nameof(NormalGivens));
            RequireRange(HardGivens, nameof(HardGivens));
        }

        /// <summary>進行中の試合が設定の書き換えの影響を受けないよう複製する。</summary>
        public SudokuConfig Copy() => (SudokuConfig)MemberwiseClone();

        private static void RequireRange(int value, string name)
        {
            if (value < MinTargetGivens || value > MaxTargetGivens)
            {
                throw new ArgumentOutOfRangeException(name, value, $"{MinTargetGivens}〜{MaxTargetGivens} の範囲で指定してください。");
            }
        }
    }
}
