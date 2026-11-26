using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Core
{
    /// <summary>カードの説明文。データの効果から作るので、効果と食い違わない。</summary>
    public static class CardText
    {
        public static string StatusName(StatusKind kind) => kind switch
        {
            StatusKind.Strength => "筋力",
            StatusKind.Weak => "脱力",
            StatusKind.Vulnerable => "弱体",
            StatusKind.Poison => "毒",
            StatusKind.Bulwark => "守りの火",
            StatusKind.Miasma => "煤煙",
            _ => kind.ToString(),
        };

        public static string TypeName(CardType type) => type switch
        {
            CardType.Attack => "攻撃",
            CardType.Skill => "技能",
            _ => "持続",
        };

        public static string RarityName(Rarity rarity) => rarity switch
        {
            Rarity.Starter => "初期",
            Rarity.Common => "普通",
            Rarity.Uncommon => "上等",
            _ => "希少",
        };

        /// <summary>README のカード表。Cards.txt から作る。</summary>
        public static string Table(GameData data)
        {
            if (data == null) { throw new ArgumentNullException(nameof(data)); }
            var sb = new StringBuilder();
            sb.Append("| ID | 名前 | 種類 | 希少度 | コスト | 効果 |\n");
            sb.Append("| --- | --- | --- | --- | ---: | --- |\n");
            foreach (var c in data.Cards)
            {
                sb.Append($"| {c.Id} | {c.Name} | {TypeName(c.Type)} | {RarityName(c.Rarity)} | {c.Cost} | {Describe(c).Replace("\n", " ")} |\n");
            }

            return sb.ToString();
        }

        public static string Describe(CardDef card)
        {
            if (card == null) { throw new ArgumentNullException(nameof(card)); }
            var lines = new List<string>();
            var effects = card.Effects;
            for (int i = 0; i < effects.Count; i++)
            {
                var e = effects[i];
                var times = 1;
                while (i + times < effects.Count && Same(effects[i + times], e)) { times++; }
                lines.Add(Line(e, times));
                i += times - 1;
            }

            if (card.Type == CardType.Power) { lines.Add("持続。"); }
            else if (card.Exhaust) { lines.Add("廃棄。"); }
            return string.Join("\n", lines);
        }

        private static bool Same(EffectDef a, EffectDef b) =>
            a.Kind == b.Kind && a.Amount == b.Amount && a.Target == b.Target && a.Status == b.Status && a.Kind is EffectKind.Damage or EffectKind.DamageByBlock;

        private static string Who(TargetMode target) => target switch
        {
            TargetMode.AllEnemies => "全ての敵に",
            TargetMode.RandomEnemy => "無作為な敵に",
            _ => string.Empty,
        };

        private static string Line(EffectDef e, int times)
        {
            var sb = new StringBuilder();
            switch (e.Kind)
            {
                case EffectKind.Damage:
                case EffectKind.DamageByBlock:
                    sb.Append(Who(e.Target));
                    if (sb.Length > 0) { sb.Append(' '); }
                    sb.Append(e.Kind == EffectKind.Damage ? $"{e.Amount} ダメージ" : "ブロックと同じ量のダメージ");
                    sb.Append(times > 1 ? $"を {times} 回。" : "。");
                    break;
                case EffectKind.Block:
                    sb.Append($"{e.Amount} ブロック。");
                    break;
                case EffectKind.DoubleBlock:
                    sb.Append("ブロックを 2 倍にする。");
                    break;
                case EffectKind.Draw:
                    sb.Append($"{e.Amount} 枚引く。");
                    break;
                case EffectKind.GainEnergy:
                    sb.Append($"エネルギー +{e.Amount}。");
                    break;
                case EffectKind.ExhaustRandom:
                    sb.Append($"手札から無作為に {e.Amount} 枚廃棄。");
                    break;
                case EffectKind.Status:
                    sb.Append(StatusLine(e));
                    break;
                case EffectKind.Summon:
                    sb.Append("仲間を呼ぶ。");
                    break;
            }

            return sb.ToString();
        }

        private static string StatusLine(EffectDef e)
        {
            if (e.Target == TargetMode.Self)
            {
                return e.Status switch
                {
                    StatusKind.Bulwark => $"自分の番の初めに {e.Amount} ブロック。",
                    StatusKind.Miasma => $"自分の番の初めに全ての敵へ毒 {e.Amount}。",
                    _ => $"{StatusName(e.Status)} +{e.Amount}。",
                };
            }

            var who = Who(e.Target);
            return $"{who}{StatusName(e.Status)} {e.Amount} を与える。";
        }
    }
}
