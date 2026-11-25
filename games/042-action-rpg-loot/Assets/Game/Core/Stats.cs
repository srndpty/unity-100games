using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>能力値の種類。並びは表示順と状態のハッシュに使う。</summary>
    public enum StatKind
    {
        Life,
        DamageMin,
        DamageMax,
        AttackSpeed,
        MoveSpeed,
        Armor,
        CritChance,
        CritDamage,
        Leech,
        FireResist,
        ColdResist,
    }

    /// <summary>修飾の種類。Flat は足し算、Increased は％の掛け算（Flat の合計の後に掛ける）。</summary>
    public enum ModKind
    {
        Flat,
        Increased,
    }

    /// <summary>1 つの能力修飾。Affix・基礎品はこれの列でしかない。</summary>
    public readonly struct StatMod : IEquatable<StatMod>
    {
        public StatMod(StatKind stat, ModKind kind, int value)
        {
            Stat = stat;
            Kind = kind;
            Value = value;
        }

        public StatKind Stat { get; }

        public ModKind Kind { get; }

        public int Value { get; }

        public bool Equals(StatMod other) => Stat == other.Stat && Kind == other.Kind && Value == other.Value;

        public override bool Equals(object? obj) => obj is StatMod other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Stat, Kind, Value);

        public override string ToString() => $"{Stat} {Kind} {Value}";

        public static bool operator ==(StatMod left, StatMod right) => left.Equals(right);

        public static bool operator !=(StatMod left, StatMod right) => !left.Equals(right);
    }

    /// <summary>能力値の上下限と表示の性質。</summary>
    public static class StatRules
    {
        public const int Count = 11;

        /// <summary>％で増やせる（Increased を受け付ける）能力。それ以外は％の値そのものなので Flat だけ。</summary>
        public static bool AcceptsIncreased(StatKind stat) =>
            stat == StatKind.Life || stat == StatKind.DamageMin || stat == StatKind.DamageMax || stat == StatKind.Armor;

        /// <summary>値そのものが％の能力（表示に % を付ける）。</summary>
        public static bool IsPercentValue(StatKind stat) => !AcceptsIncreased(stat);

        public static int Min(StatKind stat) => stat switch
        {
            StatKind.Life => 1,
            StatKind.DamageMin => 1,
            StatKind.DamageMax => 1,
            StatKind.AttackSpeed => 25,
            StatKind.MoveSpeed => 50,
            StatKind.CritDamage => 100,
            _ => 0,
        };

        public static int Max(StatKind stat) => stat switch
        {
            StatKind.Life => 99999,
            StatKind.DamageMin => 9999,
            StatKind.DamageMax => 9999,
            StatKind.AttackSpeed => 300,
            StatKind.MoveSpeed => 200,
            StatKind.Armor => 9999,
            StatKind.CritChance => 75,
            StatKind.CritDamage => 500,
            StatKind.Leech => 20,
            StatKind.FireResist => 75,
            StatKind.ColdResist => 75,
            _ => throw new ArgumentOutOfRangeException(nameof(stat)),
        };

        public static string Name(StatKind stat) => stat switch
        {
            StatKind.Life => "最大HP",
            StatKind.DamageMin => "最小攻撃力",
            StatKind.DamageMax => "最大攻撃力",
            StatKind.AttackSpeed => "攻撃速度",
            StatKind.MoveSpeed => "移動速度",
            StatKind.Armor => "防御",
            StatKind.CritChance => "会心率",
            StatKind.CritDamage => "会心倍率",
            StatKind.Leech => "吸収",
            StatKind.FireResist => "火耐性",
            StatKind.ColdResist => "冷気耐性",
            _ => throw new ArgumentOutOfRangeException(nameof(stat)),
        };

        public static bool TryParse(string token, out StatKind stat)
        {
            switch (token)
            {
                case "life": stat = StatKind.Life; return true;
                case "dmgmin": stat = StatKind.DamageMin; return true;
                case "dmgmax": stat = StatKind.DamageMax; return true;
                case "speed": stat = StatKind.AttackSpeed; return true;
                case "move": stat = StatKind.MoveSpeed; return true;
                case "armor": stat = StatKind.Armor; return true;
                case "crit": stat = StatKind.CritChance; return true;
                case "critdmg": stat = StatKind.CritDamage; return true;
                case "leech": stat = StatKind.Leech; return true;
                case "fireres": stat = StatKind.FireResist; return true;
                case "coldres": stat = StatKind.ColdResist; return true;
                default: stat = default; return false;
            }
        }
    }

    /// <summary>能力値の組（不変）。</summary>
    public sealed class StatBlock : IEquatable<StatBlock>
    {
        private readonly int[] _values;

        public StatBlock(IReadOnlyList<int> values)
        {
            if (values == null) { throw new ArgumentNullException(nameof(values)); }
            if (values.Count != StatRules.Count) { throw new ArgumentException($"能力値は {StatRules.Count} 個です。", nameof(values)); }
            _values = new int[StatRules.Count];
            for (int i = 0; i < _values.Length; i++) { _values[i] = values[i]; }
        }

        public int this[StatKind stat] => _values[(int)stat];

        public int Life => this[StatKind.Life];

        public int DamageMin => this[StatKind.DamageMin];

        public int DamageMax => this[StatKind.DamageMax];

        public int AttackSpeed => this[StatKind.AttackSpeed];

        public int MoveSpeed => this[StatKind.MoveSpeed];

        public int Armor => this[StatKind.Armor];

        public int CritChance => this[StatKind.CritChance];

        public int CritDamage => this[StatKind.CritDamage];

        public int Leech => this[StatKind.Leech];

        public int FireResist => this[StatKind.FireResist];

        public int ColdResist => this[StatKind.ColdResist];

        public bool Equals(StatBlock? other)
        {
            if (other is null) { return false; }
            for (int i = 0; i < _values.Length; i++)
            {
                if (_values[i] != other._values[i]) { return false; }
            }

            return true;
        }

        public override bool Equals(object? obj) => obj is StatBlock other && Equals(other);

        public override int GetHashCode()
        {
            int hash = 17;
            foreach (var v in _values) { hash = unchecked((hash * 31) + v); }
            return hash;
        }

        public override string ToString() => string.Join(",", _values);
    }

    /// <summary>
    /// 能力値の算出。基礎 + Σ Flat → × (100 + Σ Increased) / 100（0 方向へ切り捨て）→ 上下限。
    /// 足し算だけなので修飾の順序に依らない（可換）。装備が変わるたびに集合から 0 で計算し直す。
    /// </summary>
    public static class StatSheet
    {
        public static StatBlock Compute(StatBlock baseStats, IEnumerable<StatMod> mods)
        {
            if (baseStats == null) { throw new ArgumentNullException(nameof(baseStats)); }
            if (mods == null) { throw new ArgumentNullException(nameof(mods)); }
            var flat = new long[StatRules.Count];
            var increased = new long[StatRules.Count];
            for (int i = 0; i < StatRules.Count; i++) { flat[i] = baseStats[(StatKind)i]; }
            foreach (var mod in mods)
            {
                int index = (int)mod.Stat;
                if (mod.Kind == ModKind.Flat) { flat[index] += mod.Value; }
                else { increased[index] += mod.Value; }
            }

            var values = new int[StatRules.Count];
            for (int i = 0; i < StatRules.Count; i++)
            {
                var stat = (StatKind)i;
                long percent = Math.Max(0, 100 + increased[i]);
                long scaled = flat[i] * percent / 100;
                values[i] = (int)Math.Max(StatRules.Min(stat), Math.Min(StatRules.Max(stat), scaled));
            }

            // 最大攻撃力は最小攻撃力を下回らない。
            int min = (int)StatKind.DamageMin;
            int max = (int)StatKind.DamageMax;
            if (values[max] < values[min]) { values[max] = values[min]; }
            return new StatBlock(values);
        }

        public static StatBlock Compute(StatBlock baseStats, Equipment equipment)
        {
            if (equipment == null) { throw new ArgumentNullException(nameof(equipment)); }
            return Compute(baseStats, equipment.AllMods());
        }
    }
}
