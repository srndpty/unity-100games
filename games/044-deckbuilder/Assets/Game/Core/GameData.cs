using System;
using System.Collections.Generic;
using System.Globalization;

namespace Game.Core
{
    /// <summary>
    /// カード・敵・遭遇のデータ（Cards.txt・Enemies.txt・Encounters.txt）。
    /// 書式の誤りと存在しない ID への参照は <see cref="Parse"/> が <see cref="FormatException"/> で拒否し、
    /// 規則上の誤り（対象の種類と効果の矛盾・数の範囲など）は <see cref="DeckDataValidator"/> が理由付きで拒否する。
    /// </summary>
    public sealed class GameData
    {
        private readonly Dictionary<string, CardDef> _cardById = new(StringComparer.Ordinal);
        private readonly Dictionary<string, EnemyDef> _enemyById = new(StringComparer.Ordinal);

        private GameData(CardDef[] cards, EnemyDef[] enemies, EncounterDef[] encounters, CardDef[] starter)
        {
            Cards = Array.AsReadOnly(cards);
            Enemies = Array.AsReadOnly(enemies);
            Encounters = Array.AsReadOnly(encounters);
            StarterDeck = Array.AsReadOnly(starter);
            foreach (var c in cards) { _cardById.TryAdd(c.Id, c); }
            foreach (var e in enemies) { _enemyById.TryAdd(e.Id, e); }
        }

        public IReadOnlyList<CardDef> Cards { get; }

        public IReadOnlyList<EnemyDef> Enemies { get; }

        public IReadOnlyList<EncounterDef> Encounters { get; }

        /// <summary>初期デッキ（Cards.txt の deck 行の順）。</summary>
        public IReadOnlyList<CardDef> StarterDeck { get; }

        public CardDef Card(string id) => _cardById.TryGetValue(id, out var c) ? c : throw new KeyNotFoundException($"カード {id} がありません。");

        public EnemyDef Enemy(string id) => _enemyById.TryGetValue(id, out var e) ? e : throw new KeyNotFoundException($"敵 {id} がありません。");

        public bool TryGetEnemy(string id, out EnemyDef enemy)
        {
            if (_enemyById.TryGetValue(id, out var e))
            {
                enemy = e;
                return true;
            }

            enemy = null!;
            return false;
        }

        /// <summary>読み込んで検証する。誤りがあれば理由を並べた例外。</summary>
        public static GameData Load(string cards, string enemies, string encounters, DeckConfig config)
        {
            var data = Parse(cards, enemies, encounters);
            var errors = DeckDataValidator.Validate(data, config);
            if (errors.Count > 0) { throw new InvalidOperationException("データが不正です：" + string.Join(" / ", errors)); }
            return data;
        }

        public static GameData Parse(string cards, string enemies, string encounters)
        {
            var cardDefs = ParseCards(cards, out var deckIds);
            var byId = new Dictionary<string, CardDef>(StringComparer.Ordinal);
            foreach (var c in cardDefs) { byId.TryAdd(c.Id, c); }
            var starter = new List<CardDef>();
            long total = 0;
            foreach (var (id, count, line) in deckIds)
            {
                // 展開の前に枚数を確かめる（巨大な枚数で検証の前に資源を使い切らない）。
                total += count;
                if (count < 1 || total > DeckConfig.MaxStarterDeck)
                {
                    throw new FormatException($"Cards.txt {line} 行目: deck の枚数は 1 以上、初期デッキは合計 {DeckConfig.MaxStarterDeck} 枚以下です（{count} 枚、ここまでの合計 {total} 枚）。");
                }

                if (!byId.TryGetValue(id, out var def)) { throw new FormatException($"Cards.txt {line} 行目: deck のカード {id} がありません。"); }
                for (int i = 0; i < count; i++) { starter.Add(def); }
            }

            var enemyDefs = ParseEnemies(enemies);
            var encounterDefs = ParseEncounters(encounters, enemyDefs);
            return new GameData(cardDefs, enemyDefs, encounterDefs, starter.ToArray());
        }

        private static IEnumerable<(int Line, string[] Parts)> Lines(string text)
        {
            if (text == null) { throw new ArgumentNullException(nameof(text)); }
            var lines = text.Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var raw = lines[i];
                int comment = raw.IndexOf('#', StringComparison.Ordinal);
                if (comment >= 0) { raw = raw.Substring(0, comment); }
                raw = raw.Trim();
                if (raw.Length == 0) { continue; }
                yield return (i + 1, raw.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }
        }

        private static int Int(string file, int line, string s)
        {
            if (!int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v))
            {
                throw new FormatException($"{file} {line} 行目: 整数ではありません（{s}）。");
            }

            return v;
        }

        private static void Arity(string file, int line, string[] parts, int count)
        {
            if (parts.Length != count) { throw new FormatException($"{file} {line} 行目: {parts[0]} の項目の数が違います。"); }
        }

        private static T EnumOf<T>(string file, int line, string s, params (string Name, T Value)[] table)
        {
            foreach (var (name, value) in table)
            {
                if (string.Equals(name, s, StringComparison.Ordinal)) { return value; }
            }

            throw new FormatException($"{file} {line} 行目: {s} は使えません。");
        }

        private static TargetMode Target(string file, int line, string s) => EnumOf(
            file,
            line,
            s,
            ("self", TargetMode.Self),
            ("chosen", TargetMode.Chosen),
            ("all", TargetMode.AllEnemies),
            ("random", TargetMode.RandomEnemy),
            ("player", TargetMode.Player));

        /// <summary>do 行。種類ごとに項目が決まっている。</summary>
        private static EffectDef Effect(string file, int line, string[] p)
        {
            if (p.Length < 2) { throw new FormatException($"{file} {line} 行目: do に種類がありません。"); }
            switch (p[1])
            {
                case "damage":
                    Arity(file, line, p, 4);
                    return new EffectDef(EffectKind.Damage, Int(file, line, p[2]), Target(file, line, p[3]));
                case "damage_block":
                    Arity(file, line, p, 3);
                    return new EffectDef(EffectKind.DamageByBlock, 0, Target(file, line, p[2]));
                case "block":
                    Arity(file, line, p, 4);
                    return new EffectDef(EffectKind.Block, Int(file, line, p[2]), Target(file, line, p[3]));
                case "double_block":
                    Arity(file, line, p, 3);
                    return new EffectDef(EffectKind.DoubleBlock, 0, Target(file, line, p[2]));
                case "draw":
                    Arity(file, line, p, 3);
                    return new EffectDef(EffectKind.Draw, Int(file, line, p[2]), TargetMode.None);
                case "energy":
                    Arity(file, line, p, 3);
                    return new EffectDef(EffectKind.GainEnergy, Int(file, line, p[2]), TargetMode.None);
                case "exhaust_random":
                    Arity(file, line, p, 3);
                    return new EffectDef(EffectKind.ExhaustRandom, Int(file, line, p[2]), TargetMode.None);
                case "status":
                    Arity(file, line, p, 5);
                    var status = EnumOf(
                        file,
                        line,
                        p[2],
                        ("strength", StatusKind.Strength),
                        ("weak", StatusKind.Weak),
                        ("vulnerable", StatusKind.Vulnerable),
                        ("poison", StatusKind.Poison),
                        ("bulwark", StatusKind.Bulwark),
                        ("miasma", StatusKind.Miasma));
                    return new EffectDef(EffectKind.Status, Int(file, line, p[3]), Target(file, line, p[4]), status);
                case "summon":
                    Arity(file, line, p, 3);
                    return new EffectDef(EffectKind.Summon, 1, TargetMode.None, StatusKind.Strength, p[2]);
                default:
                    throw new FormatException($"{file} {line} 行目: 効果 {p[1]} は使えません。");
            }
        }

        private sealed class CardDraft
        {
            public string Id = string.Empty;
            public string Name = string.Empty;
            public CardType? Type;
            public Rarity? Rarity;
            public int? Cost;
            public bool Exhaust;
            public readonly List<EffectDef> Effects = new();
            public int Line;
        }

        private static CardDef[] ParseCards(string text, out List<(string Id, int Count, int Line)> deck)
        {
            const string File = "Cards.txt";
            var drafts = new List<CardDraft>();
            deck = new List<(string, int, int)>();
            CardDraft? current = null;
            foreach (var (line, p) in Lines(text))
            {
                switch (p[0])
                {
                    case "card":
                        Arity(File, line, p, 3);
                        current = new CardDraft { Id = p[1], Name = p[2], Line = line };
                        drafts.Add(current);
                        continue;
                    case "deck":
                        Arity(File, line, p, 3);
                        deck.Add((p[1], Int(File, line, p[2]), line));
                        continue;
                }

                if (current == null) { throw new FormatException($"{File} {line} 行目: card より前です。"); }
                switch (p[0])
                {
                    case "type":
                        Arity(File, line, p, 2);
                        current.Type = EnumOf(File, line, p[1], ("attack", CardType.Attack), ("skill", CardType.Skill), ("power", CardType.Power));
                        break;
                    case "rarity":
                        Arity(File, line, p, 2);
                        current.Rarity = EnumOf(
                            File,
                            line,
                            p[1],
                            ("starter", Core.Rarity.Starter),
                            ("common", Core.Rarity.Common),
                            ("uncommon", Core.Rarity.Uncommon),
                            ("rare", Core.Rarity.Rare));
                        break;
                    case "cost":
                        Arity(File, line, p, 2);
                        current.Cost = Int(File, line, p[1]);
                        break;
                    case "exhaust":
                        Arity(File, line, p, 1);
                        current.Exhaust = true;
                        break;
                    case "do":
                        current.Effects.Add(Effect(File, line, p));
                        break;
                    default:
                        throw new FormatException($"{File} {line} 行目: {p[0]} は使えません。");
                }
            }

            var result = new CardDef[drafts.Count];
            for (int i = 0; i < drafts.Count; i++)
            {
                var d = drafts[i];
                if (d.Type == null || d.Rarity == null || d.Cost == null)
                {
                    throw new FormatException($"{File} {d.Line} 行目: {d.Id} に type・rarity・cost のどれかがありません。");
                }

                result[i] = new CardDef(i, d.Id, d.Name, d.Type.Value, d.Rarity.Value, d.Cost.Value, d.Exhaust, d.Effects.ToArray());
            }

            return result;
        }

        private sealed class EnemyDraft
        {
            public string Id = string.Empty;
            public string Name = string.Empty;
            public int? Hp;
            public MoveOrder Order = MoveOrder.Cycle;
            public readonly List<(string Id, string Name, int Weight, List<EffectDef> Effects)> Moves = new();
            public string? HalfMove;
            public int HalfLine;
            public int Line;
        }

        private static EnemyDef[] ParseEnemies(string text)
        {
            const string File = "Enemies.txt";
            var drafts = new List<EnemyDraft>();
            EnemyDraft? current = null;
            foreach (var (line, p) in Lines(text))
            {
                if (p[0] == "enemy")
                {
                    Arity(File, line, p, 3);
                    current = new EnemyDraft { Id = p[1], Name = p[2], Line = line };
                    drafts.Add(current);
                    continue;
                }

                if (current == null) { throw new FormatException($"{File} {line} 行目: enemy より前です。"); }
                switch (p[0])
                {
                    case "hp":
                        Arity(File, line, p, 2);
                        current.Hp = Int(File, line, p[1]);
                        break;
                    case "order":
                        Arity(File, line, p, 2);
                        current.Order = EnumOf(File, line, p[1], ("cycle", MoveOrder.Cycle), ("random", MoveOrder.Random));
                        break;
                    case "move":
                        if (p.Length != 3 && p.Length != 4) { throw new FormatException($"{File} {line} 行目: move には id と名前（と重み）が必要です。"); }
                        current.Moves.Add((p[1], p[2], p.Length == 4 ? Int(File, line, p[3]) : 1, new List<EffectDef>()));
                        break;
                    case "do":
                        if (current.Moves.Count == 0) { throw new FormatException($"{File} {line} 行目: move より前です。"); }
                        current.Moves[current.Moves.Count - 1].Effects.Add(Effect(File, line, p));
                        break;
                    case "onhalf":
                        Arity(File, line, p, 2);
                        current.HalfMove = p[1];
                        current.HalfLine = line;
                        break;
                    default:
                        throw new FormatException($"{File} {line} 行目: {p[0]} は使えません。");
                }
            }

            var result = new EnemyDef[drafts.Count];
            for (int i = 0; i < drafts.Count; i++)
            {
                var d = drafts[i];
                if (d.Hp == null) { throw new FormatException($"{File} {d.Line} 行目: {d.Id} に hp がありません。"); }
                var moves = new MoveDef[d.Moves.Count];
                int half = -1;
                for (int m = 0; m < moves.Length; m++)
                {
                    var (id, name, weight, effects) = d.Moves[m];
                    moves[m] = new MoveDef(m, id, name, weight, effects.ToArray());
                    if (d.HalfMove != null && string.Equals(id, d.HalfMove, StringComparison.Ordinal) && half < 0) { half = m; }
                }

                if (d.HalfMove != null && half < 0) { throw new FormatException($"{File} {d.HalfLine} 行目: onhalf の行動 {d.HalfMove} がありません。"); }
                result[i] = new EnemyDef(i, d.Id, d.Name, d.Hp.Value, d.Order, moves, half);
            }

            return result;
        }

        private static EncounterDef[] ParseEncounters(string text, EnemyDef[] enemies)
        {
            const string File = "Encounters.txt";
            var byId = new Dictionary<string, EnemyDef>(StringComparer.Ordinal);
            foreach (var e in enemies) { byId.TryAdd(e.Id, e); }
            var drafts = new List<(string Id, string Name, EncounterTier? Tier, List<EnemyDef> Foes, int Line)>();
            foreach (var (line, p) in Lines(text))
            {
                if (p[0] == "encounter")
                {
                    Arity(File, line, p, 3);
                    drafts.Add((p[1], p[2], null, new List<EnemyDef>(), line));
                    continue;
                }

                if (drafts.Count == 0) { throw new FormatException($"{File} {line} 行目: encounter より前です。"); }
                var last = drafts[drafts.Count - 1];
                switch (p[0])
                {
                    case "tier":
                        Arity(File, line, p, 2);
                        var tier = EnumOf(
                            File,
                            line,
                            p[1],
                            ("easy", EncounterTier.Easy),
                            ("normal", EncounterTier.Normal),
                            ("hard", EncounterTier.Hard),
                            ("boss", EncounterTier.Boss));
                        drafts[drafts.Count - 1] = (last.Id, last.Name, tier, last.Foes, last.Line);
                        break;
                    case "foe":
                        Arity(File, line, p, 2);
                        if (!byId.TryGetValue(p[1], out var foe)) { throw new FormatException($"{File} {line} 行目: 敵 {p[1]} がありません。"); }
                        last.Foes.Add(foe);
                        break;
                    default:
                        throw new FormatException($"{File} {line} 行目: {p[0]} は使えません。");
                }
            }

            var result = new EncounterDef[drafts.Count];
            for (int i = 0; i < drafts.Count; i++)
            {
                var d = drafts[i];
                if (d.Tier == null) { throw new FormatException($"{File} {d.Line} 行目: {d.Id} に tier がありません。"); }
                result[i] = new EncounterDef(i, d.Id, d.Name, d.Tier.Value, d.Foes.ToArray());
            }

            return result;
        }
    }
}
