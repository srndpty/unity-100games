using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>規則の数値。Unity の設定アセットからそのままシリアライズする。検証は <see cref="Validate"/>。</summary>
    [Serializable]
    public sealed class DeckConfig
    {
        /// <summary>データのカードの数（仕様：20 カード）。</summary>
        public const int CardCount = 20;

        public int PlayerMaxHp = 70;

        /// <summary>毎ターンのエネルギー（持ち越さない）。</summary>
        public int Energy = 3;

        public int DrawPerTurn = 5;

        /// <summary>手札の上限。満杯のときは引かずに山札に残す。</summary>
        public int HandLimit = 10;

        /// <summary>同時に戦う敵の上限（呼ばれた敵を含む）。満杯のときの呼び出しは不発。</summary>
        public int MaxEnemies = 3;

        /// <summary>カード・敵の行動 1 つに書ける効果の数の上限。解決キューの大きさの上界になる。</summary>
        public int MaxEffects = 8;

        /// <summary>効果 1 つの量の上限。</summary>
        public int MaxAmount = 99;

        public int WeakPercent = 75;

        public int VulnerablePercent = 150;

        /// <summary>報酬で選べるカードの数。</summary>
        public int RewardChoices = 3;

        /// <summary>除去でこの枚数より少なくしない。</summary>
        public int MinDeckSize = 5;

        /// <summary>休憩の回復（最大 HP の百分率）。</summary>
        public int RestHealPercent = 30;

        /// <summary>休憩のもう一つの選択：最大 HP を増やす量（同じ量だけ回復もする）。</summary>
        public int RestMaxHpGain = 6;

        /// <summary>通常の遭遇の報酬の希少度の重み（Common・Uncommon・Rare）。</summary>
        public int[] NormalRarityWeights = { 70, 25, 5 };

        /// <summary>手強い遭遇の報酬の希少度の重み（Common・Uncommon・Rare）。</summary>
        public int[] HardRarityWeights = { 40, 40, 20 };

        /// <summary>理由付きの検証。空なら有効。</summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            void Range(string name, long value, long min, long max)
            {
                if (value < min || value > max) { errors.Add($"{name} は {min}〜{max}（{value}）"); }
            }

            Range(nameof(PlayerMaxHp), PlayerMaxHp, 1, 999);
            Range(nameof(Energy), Energy, 1, 10);
            Range(nameof(DrawPerTurn), DrawPerTurn, 1, 10);
            Range(nameof(HandLimit), HandLimit, DrawPerTurn, 10);
            Range(nameof(MaxEnemies), MaxEnemies, 1, 5);
            Range(nameof(MaxEffects), MaxEffects, 1, 16);
            Range(nameof(MaxAmount), MaxAmount, 1, 999);
            Range(nameof(WeakPercent), WeakPercent, 0, 100);
            Range(nameof(VulnerablePercent), VulnerablePercent, 100, 300);
            Range(nameof(RewardChoices), RewardChoices, 1, 5);
            Range(nameof(MinDeckSize), MinDeckSize, 1, 30);
            Range(nameof(RestHealPercent), RestHealPercent, 0, 100);
            Range(nameof(RestMaxHpGain), RestMaxHpGain, 0, 99);
            Range("PlayerMaxHp + RestMaxHpGain", (long)PlayerMaxHp + RestMaxHpGain, 1, 999);
            Weights(nameof(NormalRarityWeights), NormalRarityWeights, errors);
            Weights(nameof(HardRarityWeights), HardRarityWeights, errors);
            return errors;
        }

        private static void Weights(string name, int[]? weights, List<string> errors)
        {
            if (weights == null || weights.Length != 3)
            {
                errors.Add($"{name} は Common・Uncommon・Rare の 3 つ");
                return;
            }

            long total = 0;
            foreach (var w in weights)
            {
                if (w < 0 || w > 10000) { errors.Add($"{name} の重みは 0〜10000（{w}）"); }
                total += w;
            }

            if (total <= 0) { errors.Add($"{name} の重みの合計が 0"); }
        }

        /// <summary>深い複製。</summary>
        public DeckConfig Copy()
        {
            var copy = (DeckConfig)MemberwiseClone();
            copy.NormalRarityWeights = (int[])NormalRarityWeights.Clone();
            copy.HardRarityWeights = (int[])HardRarityWeights.Clone();
            return copy;
        }
    }
}
