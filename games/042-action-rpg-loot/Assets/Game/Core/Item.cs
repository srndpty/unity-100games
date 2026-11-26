using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Game.Core
{
    /// <summary>装備の枠。</summary>
    public enum Slot
    {
        Weapon,
        Armor,
        Helm,
        Ring,
        Amulet,
    }

    /// <summary>希少度。Affix の数が決まる。</summary>
    public enum Rarity
    {
        Normal,
        Magic,
        Rare,
    }

    public static class SlotInfo
    {
        public const int Count = 5;

        public static string Name(Slot slot) => slot switch
        {
            Slot.Weapon => "武器",
            Slot.Armor => "鎧",
            Slot.Helm => "兜",
            Slot.Ring => "指輪",
            Slot.Amulet => "首飾り",
            _ => throw new ArgumentOutOfRangeException(nameof(slot)),
        };

        public static bool TryParse(string token, out Slot slot)
        {
            switch (token)
            {
                case "weapon": slot = Slot.Weapon; return true;
                case "armor": slot = Slot.Armor; return true;
                case "helm": slot = Slot.Helm; return true;
                case "ring": slot = Slot.Ring; return true;
                case "amulet": slot = Slot.Amulet; return true;
                default: slot = default; return false;
            }
        }
    }

    public static class RarityInfo
    {
        public const int Count = 3;

        public static string Name(Rarity rarity) => rarity switch
        {
            Rarity.Normal => "普通",
            Rarity.Magic => "魔法",
            Rarity.Rare => "希少",
            _ => throw new ArgumentOutOfRangeException(nameof(rarity)),
        };

        /// <summary>希少度ごとの Affix の数の範囲。</summary>
        public static int MinAffixes(Rarity rarity) => rarity switch
        {
            Rarity.Normal => 0,
            Rarity.Magic => 1,
            Rarity.Rare => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(rarity)),
        };

        public static int MaxAffixes(Rarity rarity) => rarity switch
        {
            Rarity.Normal => 0,
            Rarity.Magic => 2,
            Rarity.Rare => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(rarity)),
        };
    }

    /// <summary>値を引いた Affix。<see cref="Values"/> は段の修飾ごとに 1 つ。</summary>
    public sealed class RolledAffix
    {
        private readonly StatMod[] _mods;

        public RolledAffix(AffixDef affix, int tier, IReadOnlyList<int> values)
        {
            Affix = affix ?? throw new ArgumentNullException(nameof(affix));
            if (tier < 0 || tier >= affix.Tiers.Count) { throw new ArgumentOutOfRangeException(nameof(tier)); }
            if (values == null) { throw new ArgumentNullException(nameof(values)); }
            Tier = tier;
            var ranges = affix.Tiers[tier].Mods;
            if (values.Count != ranges.Count) { throw new ArgumentException("値の数が段の修飾の数と違います。", nameof(values)); }
            _mods = new StatMod[ranges.Count];
            var copy = new int[ranges.Count];
            for (int i = 0; i < ranges.Count; i++)
            {
                copy[i] = values[i];
                _mods[i] = new StatMod(ranges[i].Stat, ranges[i].Kind, values[i]);
            }

            Values = Array.AsReadOnly(copy);
            Mods = Array.AsReadOnly(_mods);
        }

        public AffixDef Affix { get; }

        public int Tier { get; }

        public IReadOnlyList<int> Values { get; }

        public IReadOnlyList<StatMod> Mods { get; }
    }

    /// <summary>1 つの装備品（不変）。</summary>
    public sealed class Item
    {
        public Item(ItemBaseDef baseDef, Rarity rarity, IReadOnlyList<RolledAffix> affixes)
        {
            Base = baseDef ?? throw new ArgumentNullException(nameof(baseDef));
            if (affixes == null) { throw new ArgumentNullException(nameof(affixes)); }
            Rarity = rarity;
            var copy = new RolledAffix[affixes.Count];
            for (int i = 0; i < copy.Length; i++) { copy[i] = affixes[i] ?? throw new ArgumentException("Affix が null です。", nameof(affixes)); }
            Affixes = Array.AsReadOnly(copy);
            var mods = new List<StatMod>(baseDef.Mods);
            foreach (var affix in copy) { mods.AddRange(affix.Mods); }
            Mods = mods.AsReadOnly();
            Name = BuildName();
        }

        public ItemBaseDef Base { get; }

        public Slot Slot => Base.Slot;

        public Rarity Rarity { get; }

        public ReadOnlyCollection<RolledAffix> Affixes { get; }

        /// <summary>基礎品と全 Affix の修飾。</summary>
        public ReadOnlyCollection<StatMod> Mods { get; }

        public string Name { get; }

        private string BuildName()
        {
            string prefix = string.Empty;
            string suffix = string.Empty;
            foreach (var affix in Affixes)
            {
                if (affix.Affix.IsPrefix && prefix.Length == 0) { prefix = affix.Affix.Name; }
                if (!affix.Affix.IsPrefix && suffix.Length == 0) { suffix = affix.Affix.Name; }
            }

            return prefix + Base.Name + (suffix.Length > 0 ? "〔" + suffix + "〕" : string.Empty);
        }
    }

    /// <summary>5 つの枠の装備。能力値はいつでも <see cref="StatSheet"/> で集合から計算し直す。</summary>
    public sealed class Equipment
    {
        private readonly Item?[] _slots = new Item?[SlotInfo.Count];

        public Item? this[Slot slot] => _slots[(int)slot];

        /// <summary>装備し、その枠に前から付けていた品を返す。</summary>
        public Item? Equip(Item item)
        {
            if (item == null) { throw new ArgumentNullException(nameof(item)); }
            var previous = _slots[(int)item.Slot];
            _slots[(int)item.Slot] = item;
            return previous;
        }

        /// <summary>外して返す。空なら null。</summary>
        public Item? Unequip(Slot slot)
        {
            var previous = _slots[(int)slot];
            _slots[(int)slot] = null;
            return previous;
        }

        public IEnumerable<StatMod> AllMods()
        {
            foreach (var item in _slots)
            {
                if (item == null) { continue; }
                foreach (var mod in item.Mods) { yield return mod; }
            }
        }

        public Equipment Clone()
        {
            var copy = new Equipment();
            Array.Copy(_slots, copy._slots, _slots.Length);
            return copy;
        }
    }
}
