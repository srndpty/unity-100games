using System;
using Game.Core;

namespace Game.App
{
    /// <summary>敵の意図の印（優先の高い順に 1 つ選ぶ）。</summary>
    public enum IntentIcon
    {
        Attack,
        Block,
        Buff,
        Debuff,
        Summon,
    }

    /// <summary>敵の意図の表示。攻撃の量は今の卓の状態（筋力・脱力・弱体）で見積もる（Core と同じ式）。</summary>
    public readonly struct IntentSummary
    {
        private IntentSummary(IntentIcon icon, int damage, int hits, string text)
        {
            Icon = icon;
            Damage = damage;
            Hits = hits;
            Text = text;
        }

        public IntentIcon Icon { get; }

        /// <summary>1 回の攻撃の見積もり（攻撃しないなら 0）。</summary>
        public int Damage { get; }

        public int Hits { get; }

        /// <summary>印の横に出す短い文（「12」「4×3」「強化」など）。</summary>
        public string Text { get; }

        public static IntentSummary Of(Seat enemy, Seat player, DeckConfig config)
        {
            if (enemy == null) { throw new ArgumentNullException(nameof(enemy)); }
            if (player == null) { throw new ArgumentNullException(nameof(player)); }
            var move = enemy.Intent ?? throw new ArgumentException("意図のない敵です。", nameof(enemy));
            int damage = 0, hits = 0;
            bool block = false, buff = false, debuff = false, summon = false;
            foreach (var e in move.Effects)
            {
                switch (e.Kind)
                {
                    case EffectKind.Damage:
                        damage = DamageCalc.Attack(
                            e.Amount,
                            enemy.Status(StatusKind.Strength),
                            enemy.Status(StatusKind.Weak) > 0,
                            player.Status(StatusKind.Vulnerable) > 0,
                            config);
                        hits++;
                        break;
                    case EffectKind.Block:
                    case EffectKind.DoubleBlock:
                        block = true;
                        break;
                    case EffectKind.Status:
                        if (e.Target == TargetMode.Player) { debuff = true; }
                        else { buff = true; }
                        break;
                    case EffectKind.Summon:
                        summon = true;
                        break;
                }
            }

            if (hits > 0) { return new IntentSummary(IntentIcon.Attack, damage, hits, hits > 1 ? $"{damage}×{hits}" : damage.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
            if (summon) { return new IntentSummary(IntentIcon.Summon, 0, 0, "呼ぶ"); }
            if (debuff) { return new IntentSummary(IntentIcon.Debuff, 0, 0, "妨害"); }
            if (buff) { return new IntentSummary(IntentIcon.Buff, 0, 0, "強化"); }
            return new IntentSummary(IntentIcon.Block, 0, 0, block ? "防御" : string.Empty);
        }
    }
}
