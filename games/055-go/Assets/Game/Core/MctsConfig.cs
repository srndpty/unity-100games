using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>CPU の強さ。強さはプレイアウトの総数だけで決まる。</summary>
    public enum CpuLevel
    {
        Weak = 0,
        Normal = 1,
        Strong = 2,
    }

    /// <summary>
    /// MCTS と死石の推定の調整値（全て整数）。Unity の設定アセットから写す。範囲は <see cref="Validate"/> で検証する。
    /// </summary>
    public sealed class MctsConfig
    {
        public int WeakPlayouts = 500;
        public int NormalPlayouts = 2000;
        public int StrongPlayouts = 8000;

        /// <summary>探索項の係数（‰）。探索項 = 係数 × √(親の訪問) ÷ (1 + 子の訪問)。</summary>
        public int ExplorationPermille = 100;

        /// <summary>RAVE の等価の回数 k（β = √(k ÷ (3n + k))）。0 なら AMAF を使わない。</summary>
        public int RaveEquivalence = 1000;

        /// <summary>プレイアウトを打ち切る手数（81 × 3）。</summary>
        public int PlayoutMoveLimit = Points.Count * 3;

        /// <summary>この手数（対局の手数）からは、勝率が <see cref="ResignPermille"/> 未満なら投了する。</summary>
        public int ResignMinMove = 30;

        /// <summary>投了する勝率（‰）。0 なら投了しない。</summary>
        public int ResignPermille = 100;

        /// <summary>死石の推定に回すプレイアウトの数。</summary>
        public int OwnershipPlayouts = 600;

        /// <summary>連の石が相手の持ち物になった割合（%）がこれより大きければ死石と推定する。</summary>
        public int DeadPercent = 50;

        public MctsConfig Copy() => (MctsConfig)MemberwiseClone();

        public int PlayoutsOf(CpuLevel level) => level switch
        {
            CpuLevel.Weak => WeakPlayouts,
            CpuLevel.Normal => NormalPlayouts,
            CpuLevel.Strong => StrongPlayouts,
            _ => throw new System.ArgumentOutOfRangeException(nameof(level), level, "未定義の強さです。"),
        };

        /// <summary>範囲の外の値を全て挙げる（空なら有効）。</summary>
        public IReadOnlyList<string> Validate()
        {
            var e = new List<string>();
            void Range(string name, int value, int min, int max)
            {
                if (value < min || value > max) { e.Add($"{name} は {min}〜{max}（{value}）"); }
            }

            Range(nameof(WeakPlayouts), WeakPlayouts, 1, 100_000);
            Range(nameof(NormalPlayouts), NormalPlayouts, 1, 100_000);
            Range(nameof(StrongPlayouts), StrongPlayouts, 1, 100_000);
            Range(nameof(ExplorationPermille), ExplorationPermille, 0, 10_000);
            Range(nameof(RaveEquivalence), RaveEquivalence, 0, 1_000_000);
            Range(nameof(PlayoutMoveLimit), PlayoutMoveLimit, Points.Count, Points.Count * 10);
            Range(nameof(ResignMinMove), ResignMinMove, 0, 1000);
            Range(nameof(ResignPermille), ResignPermille, 0, 500);
            Range(nameof(OwnershipPlayouts), OwnershipPlayouts, 1, 100_000);
            Range(nameof(DeadPercent), DeadPercent, 1, 99);
            if (WeakPlayouts > NormalPlayouts || NormalPlayouts > StrongPlayouts)
            {
                e.Add($"回数は {nameof(WeakPlayouts)} ≤ {nameof(NormalPlayouts)} ≤ {nameof(StrongPlayouts)}（{WeakPlayouts}・{NormalPlayouts}・{StrongPlayouts}）");
            }
            return e;
        }
    }
}
