using System;
using System.Collections.Generic;
using System.Globalization;

namespace Game.Core
{
    /// <summary>能力値の 1 つの差（装備を付け替えたら before → after）。</summary>
    public readonly struct StatDiff
    {
        public StatDiff(StatKind stat, int before, int after)
        {
            Stat = stat;
            Before = before;
            After = after;
        }

        public StatKind Stat { get; }

        public int Before { get; }

        public int After { get; }

        public int Delta => After - Before;
    }

    /// <summary>装備の説明文と、装備中の品との比較。画面はこの文をそのまま出す。</summary>
    public static class ItemText
    {
        /// <summary>修飾 1 つの文。例：「最大HP +12」「最大HP +10%」「攻撃速度 +15%」。</summary>
        public static string Mod(StatMod mod)
        {
            bool percent = mod.Kind == ModKind.Increased || StatRules.IsPercentValue(mod.Stat);
            return StatRules.Name(mod.Stat) + " " + Signed(mod.Value) + (percent ? "%" : string.Empty);
        }

        /// <summary>品の説明の行。1 行目は名前、2 行目は希少度と枠、次に基礎の性能、最後に Affix ごとの行。</summary>
        public static List<string> Describe(Item item)
        {
            if (item == null) { throw new ArgumentNullException(nameof(item)); }
            var lines = new List<string>
            {
                item.Name,
                RarityInfo.Name(item.Rarity) + "・" + SlotInfo.Name(item.Slot),
            };
            foreach (var mod in item.Base.Mods) { lines.Add(Mod(mod)); }
            foreach (var affix in item.Affixes)
            {
                var label = affix.Affix.IsPrefix ? "《" + affix.Affix.Name + "》" : "〔" + affix.Affix.Name + "〕";
                foreach (var mod in affix.Mods) { lines.Add(label + " " + Mod(mod)); }
            }

            return lines;
        }

        /// <summary>
        /// <paramref name="candidate"/> を装備したら能力値がどう変わるか。変わる能力だけを表示順に返す。
        /// 付け替えた装備で <see cref="StatSheet"/> を計算し直した差なので、上下限・切り捨ても含めて実際の値と一致する。
        /// </summary>
        public static List<StatDiff> Compare(StatBlock heroBase, Equipment equipment, Item candidate)
        {
            if (equipment == null) { throw new ArgumentNullException(nameof(equipment)); }
            if (candidate == null) { throw new ArgumentNullException(nameof(candidate)); }
            var before = StatSheet.Compute(heroBase, equipment);
            var trial = equipment.Clone();
            trial.Equip(candidate);
            var after = StatSheet.Compute(heroBase, trial);
            return Diff(before, after);
        }

        public static List<StatDiff> Diff(StatBlock before, StatBlock after)
        {
            if (before == null) { throw new ArgumentNullException(nameof(before)); }
            if (after == null) { throw new ArgumentNullException(nameof(after)); }
            var diffs = new List<StatDiff>();
            for (int i = 0; i < StatRules.Count; i++)
            {
                var stat = (StatKind)i;
                if (before[stat] != after[stat]) { diffs.Add(new StatDiff(stat, before[stat], after[stat])); }
            }

            return diffs;
        }

        /// <summary>差の文。例：「攻撃速度 100% → 115%（+15）」。</summary>
        public static string Diff(StatDiff diff)
        {
            string unit = StatRules.IsPercentValue(diff.Stat) ? "%" : string.Empty;
            return StatRules.Name(diff.Stat) + " " + Num(diff.Before) + unit + " → " + Num(diff.After) + unit + "（" + Signed(diff.Delta) + "）";
        }

        /// <summary>能力値の一覧の文（持ち物の画面の左の欄）。</summary>
        public static List<string> Stats(StatBlock stats)
        {
            if (stats == null) { throw new ArgumentNullException(nameof(stats)); }
            var lines = new List<string>();
            for (int i = 0; i < StatRules.Count; i++)
            {
                var stat = (StatKind)i;
                lines.Add(StatRules.Name(stat) + " " + Num(stats[stat]) + (StatRules.IsPercentValue(stat) ? "%" : string.Empty));
            }

            return lines;
        }

        private static string Signed(int value) => (value >= 0 ? "+" : string.Empty) + Num(value);

        private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
