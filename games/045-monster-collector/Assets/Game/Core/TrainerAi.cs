using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>トレーナーの技の選び方：相性・一致補正・命中を掛けた期待値の高い技。状態異常の相手に状態異常の技は使わない。</summary>
    public static class TrainerAi
    {
        /// <summary>使える技の枠から最も点の高いものを返す（同点なら番号の小さい方）。乱数は使わない。</summary>
        public static int Best(MonsterData data, Monster self, Monster target, IReadOnlyList<int> selfStages, IReadOnlyList<int> targetStages, IReadOnlyList<int> usable)
        {
            var best = usable[0];
            var bestScore = long.MinValue;
            foreach (var slot in usable)
            {
                var score = Score(data, self, target, selfStages, targetStages, self.Moves[slot].Move);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = slot;
                }
            }

            return best;
        }

        /// <summary>技の点（威力 × 相性 × 一致補正 × 命中 を 100 万で割った程度の大きさ）。</summary>
        public static long Score(MonsterData data, Monster self, Monster target, IReadOnlyList<int> selfStages, IReadOnlyList<int> targetStages, MoveDef move)
        {
            long score = 0;
            foreach (var e in move.Effects)
            {
                switch (e.Kind)
                {
                    case EffectKind.Damage:
                        long eff = data.Effectiveness(move.Type, target.Species.Type);
                        long stab = move.Type != null && ReferenceEquals(move.Type, self.Species.Type) ? Formula.StabPercent : 100;
                        score += move.Power * eff * stab / 10000;
                        break;
                    case EffectKind.Inflict:
                        if (target.Status == null) { score += 45 * e.Percent / 100; }
                        break;
                    case EffectKind.Heal:
                        if (self.Hp * 2 < self.MaxHp) { score += 40; }
                        break;
                    case EffectKind.Stat:
                        var stages = e.Target == EffectTarget.Self ? selfStages : targetStages;
                        var room = e.Stages > 0 ? stages[(int)e.Stat] < MonsterDataValidator.MaxStage : stages[(int)e.Stat] > -MonsterDataValidator.MaxStage;
                        if (room) { score += 20; }
                        break;
                }
            }

            return score * move.Accuracy / 100;
        }
    }
}
