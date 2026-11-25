using System;
using System.Collections.Generic;
using IntGeometry;

namespace Game.Core
{
    /// <summary>
    /// 戦利品の乱数の系列。敵（配置）ごとに試合シード・階・配置番号から独立に派生するので、
    /// 倒す順・倒した Tick・他の敵のドロップに依らず同じ品が出る。
    /// </summary>
    public static class LootRng
    {
        public static SeededRandom For(int matchSeed, int floor, int spawnIndex)
        {
            unchecked
            {
                uint mixed = (uint)matchSeed;
                mixed = (mixed * 0x9E3779B1u) ^ (uint)(floor * 0x85EBCA77);
                mixed = (mixed * 0xC2B2AE3Du) ^ (uint)((spawnIndex + 1) * 0x27D4EB2F);
                return new SeededRandom((int)mixed);
            }
        }
    }

    /// <summary>1 品に付けてよい Affix の組み合わせの規則。生成とテストの両方で使う。</summary>
    public static class AffixRules
    {
        /// <summary>
        /// 品が規則を満たすか。①同じ群は 1 つ ②排他の組を同時に持たない ③枠に合う ④接頭・接尾の数
        /// ⑤希少度の Affix の数の上限 ⑥値が段の範囲内。理由は <paramref name="reason"/> に入る。
        /// </summary>
        public static bool IsAllowed(Item item, int floor, out string reason)
        {
            if (item == null) { throw new ArgumentNullException(nameof(item)); }
            var affixes = item.Affixes;
            if (affixes.Count > RarityInfo.MaxAffixes(item.Rarity))
            {
                reason = $"{RarityInfo.Name(item.Rarity)}の品に Affix が {affixes.Count} 個あります。";
                return false;
            }

            int prefixes = 0;
            int suffixes = 0;
            for (int i = 0; i < affixes.Count; i++)
            {
                var a = affixes[i].Affix;
                if (a.IsPrefix) { prefixes++; } else { suffixes++; }
                if (!a.Fits(item.Slot))
                {
                    reason = $"{a.Id} は{SlotInfo.Name(item.Slot)}に付きません。";
                    return false;
                }

                if (!InTier(affixes[i], floor, out reason)) { return false; }
                for (int j = 0; j < i; j++)
                {
                    var b = affixes[j].Affix;
                    if (a.Group == b.Group)
                    {
                        reason = $"{b.Id} と {a.Id} は同じ群 {a.Group} です。";
                        return false;
                    }

                    if (a.Excludes(b))
                    {
                        reason = $"{b.Id} と {a.Id} は排他です。";
                        return false;
                    }
                }
            }

            if (prefixes > LootData.MaxPrefixes || suffixes > LootData.MaxSuffixes)
            {
                reason = $"接頭 {prefixes}・接尾 {suffixes} は多すぎます。";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        /// <summary>既に付いた Affix の列に <paramref name="candidate"/> を足せるか。</summary>
        public static bool CanAdd(Slot slot, IReadOnlyList<AffixDef> chosen, AffixDef candidate)
        {
            if (chosen == null) { throw new ArgumentNullException(nameof(chosen)); }
            if (candidate == null) { throw new ArgumentNullException(nameof(candidate)); }
            if (!candidate.Fits(slot)) { return false; }
            int sameSide = 0;
            foreach (var a in chosen)
            {
                if (a.Group == candidate.Group || a.Excludes(candidate)) { return false; }
                if (a.IsPrefix == candidate.IsPrefix) { sameSide++; }
            }

            return sameSide < (candidate.IsPrefix ? LootData.MaxPrefixes : LootData.MaxSuffixes);
        }

        private static bool InTier(RolledAffix rolled, int floor, out string reason)
        {
            int best = rolled.Affix.TierFor(floor);
            if (rolled.Tier != best)
            {
                reason = $"{rolled.Affix.Id} の段 {rolled.Tier} は {floor} 階の段 {best} と違います。";
                return false;
            }

            var ranges = rolled.Affix.Tiers[rolled.Tier].Mods;
            for (int k = 0; k < ranges.Count; k++)
            {
                int v = rolled.Values[k];
                if (v < ranges[k].Min || v > ranges[k].Max)
                {
                    reason = $"{rolled.Affix.Id} の値 {v} が範囲外です。";
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }
    }

    /// <summary>シード駆動の戦利品の生成。同じ乱数の状態からは同じ品が出る。</summary>
    public sealed class LootGenerator
    {
        private readonly LootData _data;

        public LootGenerator(LootData data)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
        }

        /// <summary>ドロップ表に従って落とす品を引く（装備品 0〜Count 個）。回復薬は <paramref name="potion"/>。</summary>
        public List<Item> RollDrop(SeededRandom rng, int floor, DropTable table, out bool potion)
        {
            if (rng == null) { throw new ArgumentNullException(nameof(rng)); }
            if (table == null) { throw new ArgumentNullException(nameof(table)); }
            var items = new List<Item>();
            if (rng.Next(100) < table.ChancePercent)
            {
                for (int i = 0; i < table.Count; i++)
                {
                    items.Add(Roll(rng, floor, PickRarity(rng, table)));
                }
            }

            potion = rng.Next(100) < table.PotionPercent;
            return items;
        }

        /// <summary>希少度を指定して 1 品を引く。</summary>
        public Item Roll(SeededRandom rng, int floor, Rarity rarity)
        {
            if (rng == null) { throw new ArgumentNullException(nameof(rng)); }
            if (floor < 1 || floor > LootData.MaxFloor) { throw new ArgumentOutOfRangeException(nameof(floor)); }
            var baseDef = PickBase(rng, floor);
            int count = rarity == Rarity.Normal ? 0 : rng.Range(RarityInfo.MinAffixes(rarity), RarityInfo.MaxAffixes(rarity));
            var chosen = new List<AffixDef>();
            var rolled = new List<RolledAffix>();
            var candidates = new List<AffixDef>();
            for (int n = 0; n < count; n++)
            {
                candidates.Clear();
                long total = 0;
                foreach (var a in _data.Affixes)
                {
                    if (a.TierFor(floor) < 0 || !AffixRules.CanAdd(baseDef.Slot, chosen, a)) { continue; }
                    candidates.Add(a);
                    total += a.Weight;
                }

                // 候補が尽きたら Affix を減らす（禁止を破って埋めない）。
                if (candidates.Count == 0) { break; }
                int pick = rng.Next((int)total);
                AffixDef? affix = null;
                foreach (var a in candidates)
                {
                    if (pick < a.Weight) { affix = a; break; }
                    pick -= a.Weight;
                }

                affix ??= candidates[candidates.Count - 1];
                chosen.Add(affix);
                int tier = affix.TierFor(floor);
                var ranges = affix.Tiers[tier].Mods;
                var values = new int[ranges.Count];
                for (int k = 0; k < values.Length; k++) { values[k] = rng.Range(ranges[k].Min, ranges[k].Max); }
                rolled.Add(new RolledAffix(affix, tier, values));
            }

            // 表示を安定させるため、接頭 → 接尾、それぞれ定義の順に並べる。
            rolled.Sort((x, y) =>
            {
                int side = (x.Affix.IsPrefix ? 0 : 1).CompareTo(y.Affix.IsPrefix ? 0 : 1);
                return side != 0 ? side : x.Affix.Index.CompareTo(y.Affix.Index);
            });
            return new Item(baseDef, rarity, rolled);
        }

        private ItemBaseDef PickBase(SeededRandom rng, int floor)
        {
            long total = 0;
            foreach (var b in _data.Bases)
            {
                if (b.MinFloor <= floor) { total += b.Weight; }
            }

            int pick = rng.Next((int)total);
            ItemBaseDef? last = null;
            foreach (var b in _data.Bases)
            {
                if (b.MinFloor > floor) { continue; }
                last = b;
                if (pick < b.Weight) { return b; }
                pick -= b.Weight;
            }

            return last ?? throw new InvalidOperationException("基礎品がありません。");
        }

        private static Rarity PickRarity(SeededRandom rng, DropTable table)
        {
            int pick = rng.Next(table.NormalWeight + table.MagicWeight + table.RareWeight);
            if (pick < table.NormalWeight) { return Rarity.Normal; }
            pick -= table.NormalWeight;
            return pick < table.MagicWeight ? Rarity.Magic : Rarity.Rare;
        }
    }
}
