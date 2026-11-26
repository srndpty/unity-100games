using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using TileMotion;

namespace Game.Core
{
    /// <summary>戦利品のデータが規則に合わない。理由と場所を文で持つ。</summary>
    public sealed class LootDataException : Exception
    {
        public LootDataException(string message)
            : base(message)
        {
        }

        public LootDataException()
        {
        }

        public LootDataException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    /// <summary>修飾の値の範囲（Affix の段の 1 行）。</summary>
    public readonly struct ModRange
    {
        public ModRange(StatKind stat, ModKind kind, int min, int max)
        {
            Stat = stat;
            Kind = kind;
            Min = min;
            Max = max;
        }

        public StatKind Stat { get; }

        public ModKind Kind { get; }

        public int Min { get; }

        public int Max { get; }
    }

    /// <summary>Affix の段。<see cref="MinFloor"/> 以上の階で出る（深い階ほど上の段）。</summary>
    public sealed class AffixTier
    {
        public AffixTier(int minFloor, IReadOnlyList<ModRange> mods)
        {
            MinFloor = minFloor;
            Mods = new ReadOnlyCollection<ModRange>(new List<ModRange>(mods));
        }

        public int MinFloor { get; }

        public ReadOnlyCollection<ModRange> Mods { get; }
    }

    /// <summary>基礎品。</summary>
    public sealed class ItemBaseDef
    {
        public ItemBaseDef(int index, string id, string name, Slot slot, int minFloor, int weight, IReadOnlyList<StatMod> mods)
        {
            Index = index;
            Id = id;
            Name = name;
            Slot = slot;
            MinFloor = minFloor;
            Weight = weight;
            Mods = new ReadOnlyCollection<StatMod>(new List<StatMod>(mods));
        }

        public int Index { get; }

        public string Id { get; }

        public string Name { get; }

        public Slot Slot { get; }

        public int MinFloor { get; }

        public int Weight { get; }

        public ReadOnlyCollection<StatMod> Mods { get; }
    }

    /// <summary>Affix の定義。同じ <see cref="Group"/> の Affix は 1 品に 1 つまで。</summary>
    public sealed class AffixDef
    {
        private readonly List<AffixTier> _tiers = new();
        private readonly List<AffixDef> _exclusive = new();

        internal AffixDef(int index, string id, string name, bool isPrefix, string group, int weight, int slotMask)
        {
            Index = index;
            Id = id;
            Name = name;
            IsPrefix = isPrefix;
            Group = group;
            Weight = weight;
            SlotMask = slotMask;
            Tiers = _tiers.AsReadOnly();
            Exclusive = _exclusive.AsReadOnly();
        }

        public int Index { get; }

        public string Id { get; }

        public string Name { get; }

        public bool IsPrefix { get; }

        public string Group { get; }

        public int Weight { get; }

        /// <summary>付く枠のビット集合（1 &lt;&lt; Slot）。</summary>
        public int SlotMask { get; }

        public ReadOnlyCollection<AffixTier> Tiers { get; }

        /// <summary>同じ品に付けてはいけない Affix。対称（両方の側に入る）。</summary>
        public ReadOnlyCollection<AffixDef> Exclusive { get; }

        public bool Fits(Slot slot) => (SlotMask & (1 << (int)slot)) != 0;

        /// <summary>その階で出る最も上の段。出ないなら -1。</summary>
        public int TierFor(int floor)
        {
            int result = -1;
            for (int i = 0; i < _tiers.Count; i++)
            {
                if (_tiers[i].MinFloor <= floor) { result = i; }
            }

            return result;
        }

        public bool Excludes(AffixDef other)
        {
            foreach (var e in _exclusive)
            {
                if (ReferenceEquals(e, other)) { return true; }
            }

            return false;
        }

        internal void AddTier(AffixTier tier) => _tiers.Add(tier);

        internal void AddExclusive(AffixDef other) => _exclusive.Add(other);
    }

    /// <summary>敵の種類ごとのドロップ表。</summary>
    public sealed class DropTable
    {
        public DropTable(string id, int chancePercent, int count, int normalWeight, int magicWeight, int rareWeight, int potionPercent)
        {
            Id = id;
            ChancePercent = chancePercent;
            Count = count;
            NormalWeight = normalWeight;
            MagicWeight = magicWeight;
            RareWeight = rareWeight;
            PotionPercent = potionPercent;
        }

        public string Id { get; }

        /// <summary>装備品を落とす確率（％）。落とすなら <see cref="Count"/> 個。</summary>
        public int ChancePercent { get; }

        public int Count { get; }

        public int NormalWeight { get; }

        public int MagicWeight { get; }

        public int RareWeight { get; }

        /// <summary>回復薬を落とす確率（％）。装備品とは別に引く。</summary>
        public int PotionPercent { get; }

        public int RarityWeight(Rarity rarity) => rarity switch
        {
            Rarity.Normal => NormalWeight,
            Rarity.Magic => MagicWeight,
            Rarity.Rare => RareWeight,
            _ => throw new ArgumentOutOfRangeException(nameof(rarity)),
        };
    }

    /// <summary>
    /// 戦利品のデータ（Loot.txt）。<see cref="Parse"/> が読み込みと検証を同時に行い、通ったものだけを不変の束にする。
    /// </summary>
    public sealed class LootData
    {
        public const int MaxFloor = 3;

        public const int MaxPrefixes = 2;

        public const int MaxSuffixes = 2;

        /// <summary>修飾の値の絶対値の上限。能力値の計算を long の中で安全に保つ。</summary>
        public const int MaxModValue = 100000;

        public static readonly ReadOnlyCollection<string> RequiredDrops =
            Array.AsReadOnly(new[] { "grunt", "archer", "charger", "boss" });

        private static readonly char[] _separators = { ' ', '\t' };

        private readonly Dictionary<string, DropTable> _drops;

        private LootData(StatBlock hero, List<ItemBaseDef> bases, List<AffixDef> affixes, Dictionary<string, DropTable> drops)
        {
            Hero = hero;
            Bases = bases.AsReadOnly();
            Affixes = affixes.AsReadOnly();
            _drops = drops;
            Fingerprint = ComputeFingerprint();
        }

        /// <summary>装備なしの自機の能力値。</summary>
        public StatBlock Hero { get; }

        public ReadOnlyCollection<ItemBaseDef> Bases { get; }

        public ReadOnlyCollection<AffixDef> Affixes { get; }

        /// <summary>データの指紋。状態のハッシュに混ぜる。</summary>
        public uint Fingerprint { get; }

        public DropTable Drop(string id) =>
            _drops.TryGetValue(id, out var table) ? table : throw new KeyNotFoundException($"ドロップ表 {id} がありません。");

        public ItemBaseDef Base(string id)
        {
            foreach (var b in Bases)
            {
                if (b.Id == id) { return b; }
            }

            throw new KeyNotFoundException($"基礎品 {id} がありません。");
        }

        public AffixDef Affix(string id)
        {
            foreach (var a in Affixes)
            {
                if (a.Id == id) { return a; }
            }

            throw new KeyNotFoundException($"Affix {id} がありません。");
        }

        public static LootData Parse(string text)
        {
            if (text == null) { throw new ArgumentNullException(nameof(text)); }
            var parser = new Parser();
            var lines = text.Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var raw = lines[i];
                int comment = raw.IndexOf('#', StringComparison.Ordinal);
                if (comment >= 0) { raw = raw.Substring(0, comment); }
                raw = raw.Trim();
                if (raw.Length == 0) { continue; }
                parser.Line(i + 1, raw.Split(_separators, StringSplitOptions.RemoveEmptyEntries));
            }

            return parser.Finish();
        }

        private uint ComputeFingerprint()
        {
            var hash = StateHash.Start();
            for (int i = 0; i < StatRules.Count; i++) { hash.Mix(Hero[(StatKind)i]); }
            foreach (var b in Bases)
            {
                MixText(ref hash, b.Id);
                hash.Mix((int)b.Slot);
                hash.Mix(b.MinFloor);
                hash.Mix(b.Weight);
                foreach (var m in b.Mods)
                {
                    hash.Mix((int)m.Stat);
                    hash.Mix((int)m.Kind);
                    hash.Mix(m.Value);
                }
            }

            foreach (var a in Affixes)
            {
                MixText(ref hash, a.Id);
                MixText(ref hash, a.Group);
                hash.Mix(a.IsPrefix);
                hash.Mix(a.Weight);
                hash.Mix(a.SlotMask);
                foreach (var t in a.Tiers)
                {
                    hash.Mix(t.MinFloor);
                    foreach (var m in t.Mods)
                    {
                        hash.Mix((int)m.Stat);
                        hash.Mix((int)m.Kind);
                        hash.Mix(m.Min);
                        hash.Mix(m.Max);
                    }
                }

                foreach (var e in a.Exclusive) { hash.Mix(e.Index); }
            }

            foreach (var id in RequiredDrops)
            {
                var d = _drops[id];
                hash.Mix(d.ChancePercent);
                hash.Mix(d.Count);
                hash.Mix(d.NormalWeight);
                hash.Mix(d.MagicWeight);
                hash.Mix(d.RareWeight);
                hash.Mix(d.PotionPercent);
            }

            return hash.Value;
        }

        private static void MixText(ref StateHash hash, string text)
        {
            hash.Mix(text.Length);
            foreach (var ch in text) { hash.Mix(ch); }
        }

        private sealed class Parser
        {
            private readonly List<ItemBaseDef> _bases = new();
            private readonly List<AffixDef> _affixes = new();
            private readonly Dictionary<string, DropTable> _drops = new(StringComparer.Ordinal);
            private readonly List<(string A, string B, int Line)> _exclusive = new();
            private int[]? _hero;
            private AffixDef? _current;

            public void Line(int lineNo, string[] parts)
            {
                switch (parts[0])
                {
                    case "hero": Hero(lineNo, parts); break;
                    case "base": Base(lineNo, parts); break;
                    case "affix": Affix(lineNo, parts); break;
                    case "tier": Tier(lineNo, parts); break;
                    case "exclusive":
                        if (parts.Length != 3) { throw Error(lineNo, "exclusive には Affix が 2 つ必要です。"); }
                        _exclusive.Add((parts[1], parts[2], lineNo));
                        break;
                    case "drop": Drop(lineNo, parts); break;
                    default: throw Error(lineNo, $"不明な行 {parts[0]} です。");
                }
            }

            public LootData Finish()
            {
                if (_hero == null) { throw new LootDataException("hero の行がありません。"); }
                foreach (var a in _affixes)
                {
                    if (a.Tiers.Count == 0) { throw new LootDataException($"Affix {a.Id} に段がありません。"); }
                }

                foreach (var (aId, bId, lineNo) in _exclusive)
                {
                    var a = Find(aId) ?? throw Error(lineNo, $"排他の Affix {aId} が未定義です。");
                    var b = Find(bId) ?? throw Error(lineNo, $"排他の Affix {bId} が未定義です。");
                    if (ReferenceEquals(a, b)) { throw Error(lineNo, $"Affix {aId} は自分自身と排他にできません。"); }
                    if (a.Excludes(b)) { throw Error(lineNo, $"排他の組 {aId} と {bId} が重複しています。"); }
                    a.AddExclusive(b);
                    b.AddExclusive(a);
                }

                foreach (var id in RequiredDrops)
                {
                    if (!_drops.ContainsKey(id)) { throw new LootDataException($"ドロップ表 {id} がありません。"); }
                }

                for (int floor = 1; floor <= MaxFloor; floor++)
                {
                    long total = 0;
                    foreach (var b in _bases)
                    {
                        if (b.MinFloor <= floor) { total += b.Weight; }
                    }

                    if (total == 0) { throw new LootDataException($"{floor} 階で出る基礎品がありません。"); }
                    if (total > int.MaxValue) { throw new LootDataException($"{floor} 階の基礎品の重みの合計が int を超えます。"); }
                }

                long affixTotal = 0;
                foreach (var a in _affixes) { affixTotal += a.Weight; }
                if (affixTotal > int.MaxValue) { throw new LootDataException("Affix の重みの合計が int を超えます。"); }

                var hero = new StatBlock(_hero);
                return new LootData(hero, _bases, _affixes, _drops);
            }

            private void Hero(int lineNo, string[] parts)
            {
                if (_hero != null) { throw Error(lineNo, "hero の行が 2 つあります。"); }
                if (parts.Length != 1 + (StatRules.Count * 2)) { throw Error(lineNo, $"hero には {StatRules.Count} 個の能力値が必要です。"); }
                var values = new int[StatRules.Count];
                var seen = new bool[StatRules.Count];
                for (int i = 1; i < parts.Length; i += 2)
                {
                    var stat = ParseStat(lineNo, parts[i]);
                    if (seen[(int)stat]) { throw Error(lineNo, $"hero の {parts[i]} が 2 回あります。"); }
                    seen[(int)stat] = true;
                    int value = ParseInt(lineNo, parts[i + 1]);
                    if (value < StatRules.Min(stat) || value > StatRules.Max(stat))
                    {
                        throw Error(lineNo, $"hero の {parts[i]} が範囲外です（{StatRules.Min(stat)}〜{StatRules.Max(stat)}）。");
                    }

                    values[(int)stat] = value;
                }

                _hero = values;
            }

            private void Base(int lineNo, string[] parts)
            {
                // base <id> <slot> <minFloor> <weight> <name> (<stat> <kind> <value>)+
                if (parts.Length < 9 || (parts.Length - 6) % 3 != 0) { throw Error(lineNo, "base の形式が違います。"); }
                var id = parts[1];
                RequireNewId(lineNo, id);
                if (!SlotInfo.TryParse(parts[2], out var slot)) { throw Error(lineNo, $"不明な枠 {parts[2]} です。"); }
                int minFloor = ParseFloor(lineNo, parts[3]);
                int weight = ParseWeight(lineNo, parts[4]);
                var mods = new List<StatMod>();
                for (int i = 6; i < parts.Length; i += 3)
                {
                    var stat = ParseStat(lineNo, parts[i]);
                    var kind = ParseKind(lineNo, parts[i + 1], stat);
                    int value = ParseModValue(lineNo, parts[i + 2]);
                    mods.Add(new StatMod(stat, kind, value));
                }

                _bases.Add(new ItemBaseDef(_bases.Count, id, parts[5], slot, minFloor, weight, mods));
                _current = null;
            }

            private void Affix(int lineNo, string[] parts)
            {
                // affix <id> <prefix|suffix> <group> <weight> <slots> <name>
                if (parts.Length != 7) { throw Error(lineNo, "affix の形式が違います。"); }
                var id = parts[1];
                RequireNewId(lineNo, id);
                bool isPrefix = parts[2] switch
                {
                    "prefix" => true,
                    "suffix" => false,
                    _ => throw Error(lineNo, $"prefix か suffix を指定してください（{parts[2]}）。"),
                };
                int weight = ParseWeight(lineNo, parts[4]);
                int mask = 0;
                foreach (var token in parts[5].Split(','))
                {
                    if (token.Length == 0) { continue; }
                    if (!SlotInfo.TryParse(token, out var slot)) { throw Error(lineNo, $"不明な枠 {token} です。"); }
                    mask |= 1 << (int)slot;
                }

                if (mask == 0) { throw Error(lineNo, $"Affix {id} の付く枠がありません。"); }
                _current = new AffixDef(_affixes.Count, id, parts[6], isPrefix, parts[3], weight, mask);
                _affixes.Add(_current);
            }

            private void Tier(int lineNo, string[] parts)
            {
                // tier <minFloor> (<stat> <kind> <min> <max>)+
                if (_current == null) { throw Error(lineNo, "tier は affix の後に書きます。"); }
                if (parts.Length < 6 || (parts.Length - 2) % 4 != 0) { throw Error(lineNo, "tier の形式が違います。"); }
                int minFloor = ParseFloor(lineNo, parts[1]);
                var tiers = _current.Tiers;
                if (tiers.Count > 0 && tiers[tiers.Count - 1].MinFloor >= minFloor)
                {
                    throw Error(lineNo, $"Affix {_current.Id} の段の階は増える順に書きます。");
                }

                var mods = new List<ModRange>();
                for (int i = 2; i < parts.Length; i += 4)
                {
                    var stat = ParseStat(lineNo, parts[i]);
                    var kind = ParseKind(lineNo, parts[i + 1], stat);
                    int min = ParseModValue(lineNo, parts[i + 2]);
                    int max = ParseModValue(lineNo, parts[i + 3]);
                    if (min > max) { throw Error(lineNo, $"Affix {_current.Id} の範囲が逆転しています（{min}〜{max}）。"); }
                    mods.Add(new ModRange(stat, kind, min, max));
                }

                _current.AddTier(new AffixTier(minFloor, mods));
            }

            private void Drop(int lineNo, string[] parts)
            {
                // drop <id> chance <p> count <n> normal <w> magic <w> rare <w> potion <p>
                if (parts.Length != 14) { throw Error(lineNo, "drop の形式が違います。"); }
                var id = parts[1];
                if (_drops.ContainsKey(id)) { throw Error(lineNo, $"ドロップ表 {id} が 2 回あります。"); }
                var values = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 2; i < parts.Length; i += 2)
                {
                    if (values.ContainsKey(parts[i])) { throw Error(lineNo, $"drop の {parts[i]} が 2 回あります。"); }
                    values[parts[i]] = ParseInt(lineNo, parts[i + 1]);
                }

                int Get(string key, int min, int max)
                {
                    if (!values.TryGetValue(key, out var v)) { throw Error(lineNo, $"drop に {key} がありません。"); }
                    if (v < min || v > max) { throw Error(lineNo, $"drop の {key} が範囲外です（{min}〜{max}）。"); }
                    return v;
                }

                var table = new DropTable(
                    id,
                    Get("chance", 0, 100),
                    Get("count", 1, 4),
                    Get("normal", 0, int.MaxValue),
                    Get("magic", 0, int.MaxValue),
                    Get("rare", 0, int.MaxValue),
                    Get("potion", 0, 100));
                long sum = (long)table.NormalWeight + table.MagicWeight + table.RareWeight;
                if (sum == 0) { throw Error(lineNo, $"ドロップ表 {id} の希少度の重みが全て 0 です。"); }
                if (sum > int.MaxValue) { throw Error(lineNo, $"ドロップ表 {id} の希少度の重みの合計が int を超えます。"); }
                _drops[id] = table;
                _current = null;
            }

            private AffixDef? Find(string id)
            {
                foreach (var a in _affixes)
                {
                    if (a.Id == id) { return a; }
                }

                return null;
            }

            private void RequireNewId(int lineNo, string id)
            {
                foreach (var b in _bases)
                {
                    if (b.Id == id) { throw Error(lineNo, $"ID {id} が重複しています。"); }
                }

                if (Find(id) != null) { throw Error(lineNo, $"ID {id} が重複しています。"); }
            }

            private static StatKind ParseStat(int lineNo, string token) =>
                StatRules.TryParse(token, out var stat) ? stat : throw Error(lineNo, $"不明な能力 {token} です。");

            private static ModKind ParseKind(int lineNo, string token, StatKind stat)
            {
                switch (token)
                {
                    case "flat": return ModKind.Flat;
                    case "inc":
                        if (!StatRules.AcceptsIncreased(stat)) { throw Error(lineNo, $"{StatRules.Name(stat)} は％で増やせません（flat だけ）。"); }
                        return ModKind.Increased;
                    default: throw Error(lineNo, $"flat か inc を指定してください（{token}）。");
                }
            }

            private static int ParseFloor(int lineNo, string token)
            {
                int floor = ParseInt(lineNo, token);
                if (floor < 1 || floor > MaxFloor) { throw Error(lineNo, $"階は 1〜{MaxFloor} です（{floor}）。"); }
                return floor;
            }

            private static int ParseWeight(int lineNo, string token)
            {
                int weight = ParseInt(lineNo, token);
                if (weight <= 0) { throw Error(lineNo, $"重みは正の数です（{weight}）。"); }
                return weight;
            }

            private static int ParseModValue(int lineNo, string token)
            {
                int value = ParseInt(lineNo, token);
                if (value < -MaxModValue || value > MaxModValue) { throw Error(lineNo, $"修飾の値は ±{MaxModValue} までです（{value}）。"); }
                return value;
            }

            private static int ParseInt(int lineNo, string token) =>
                int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : throw Error(lineNo, $"整数ではありません（{token}）。");

            private static LootDataException Error(int lineNo, string message) => new($"{lineNo} 行目: {message}");
        }
    }
}
