using System;
using System.Collections.Generic;
using System.Globalization;
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

    /// <summary>
    /// 敵の意図の表示。行動の効果列を書いた順にたどり、実際の解決と同じ式で攻撃の量を見積もる。
    /// 敵は行動の前にブロックが 0 に戻り、同じ行動の中の防御・強化・弱体の付与は後の攻撃に効く。
    /// プレイヤーの番の途中なら、ターン終了で 1 減るプレイヤーの弱体を差し引く。
    /// </summary>
    public readonly struct IntentSummary
    {
        private IntentSummary(IntentIcon icon, IReadOnlyList<int> hits, string text)
        {
            Icon = icon;
            Hits = hits;
            Text = text;
        }

        public IntentIcon Icon { get; }

        /// <summary>攻撃 1 回ずつの見積もり（書いた順。攻撃しないなら空）。</summary>
        public IReadOnlyList<int> Hits { get; }

        /// <summary>攻撃の合計の見積もり。</summary>
        public int Total
        {
            get
            {
                long sum = 0;
                foreach (var h in Hits) { sum += h; }
                return (int)Math.Min(int.MaxValue, sum);
            }
        }

        /// <summary>印の横に出す短い文（「12」「4×3」「3+8」「強化」など）。</summary>
        public string Text { get; }

        public static IntentSummary Of(Seat enemy, TableModel table, DeckConfig config)
        {
            if (enemy == null) { throw new ArgumentNullException(nameof(enemy)); }
            if (table == null) { throw new ArgumentNullException(nameof(table)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            var move = enemy.Intent ?? throw new ArgumentException("意図のない敵です。", nameof(enemy));
            var player = table.Player;
            var pendingDecay = table.PlayerTurnOpen ? 1 : 0;
            long block = 0;
            int strength = enemy.Status(StatusKind.Strength);
            int weak = enemy.Status(StatusKind.Weak);
            int vulnerable = Math.Max(0, player.Status(StatusKind.Vulnerable) - pendingDecay);
            var hits = new List<int>();
            bool blocks = false, buff = false, debuff = false, summon = false;
            foreach (var e in move.Effects)
            {
                switch (e.Kind)
                {
                    case EffectKind.Damage:
                    case EffectKind.DamageByBlock:
                        var baseAmount = e.Kind == EffectKind.Damage ? e.Amount : (int)block;
                        hits.Add(DamageCalc.Attack(baseAmount, strength, weak > 0, vulnerable > 0, config));
                        break;
                    case EffectKind.Block:
                        block = Math.Min(DamageCalc.Max, block + e.Amount);
                        blocks = true;
                        break;
                    case EffectKind.DoubleBlock:
                        block = Math.Min(DamageCalc.Max, block * 2);
                        blocks = true;
                        break;
                    case EffectKind.Status when e.Target == TargetMode.Player:
                        debuff = true;
                        if (e.Status == StatusKind.Vulnerable) { vulnerable += e.Amount; }
                        break;
                    case EffectKind.Status:
                        buff = true;
                        if (e.Status == StatusKind.Strength) { strength += e.Amount; }
                        else if (e.Status == StatusKind.Weak) { weak += e.Amount; }
                        break;
                    case EffectKind.Summon:
                        summon = true;
                        break;
                }
            }

            if (hits.Count > 0) { return new IntentSummary(IntentIcon.Attack, hits.AsReadOnly(), AttackText(hits)); }
            var none = Array.AsReadOnly(Array.Empty<int>());
            if (summon) { return new IntentSummary(IntentIcon.Summon, none, "呼ぶ"); }
            if (debuff) { return new IntentSummary(IntentIcon.Debuff, none, "妨害"); }
            if (buff) { return new IntentSummary(IntentIcon.Buff, none, "強化"); }
            return new IntentSummary(IntentIcon.Block, none, blocks ? "防御" : string.Empty);
        }

        /// <summary>同じ量なら「量×回数」、違えば書いた順に「+」でつなぐ。</summary>
        private static string AttackText(List<int> hits)
        {
            if (hits.Count == 1) { return hits[0].ToString(CultureInfo.InvariantCulture); }
            if (hits.TrueForAll(h => h == hits[0])) { return $"{hits[0].ToString(CultureInfo.InvariantCulture)}×{hits.Count.ToString(CultureInfo.InvariantCulture)}"; }
            return string.Join("+", hits.ConvertAll(h => h.ToString(CultureInfo.InvariantCulture)));
        }
    }
}
