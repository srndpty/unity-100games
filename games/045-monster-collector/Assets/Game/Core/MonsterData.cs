using System;
using System.Collections.Generic;
using System.Globalization;

namespace Game.Core
{
    /// <summary>
    /// 属性・状態異常・技・種族・世界（規則・地図・草むら・トレーナー）のデータ。
    /// 書式の誤りと存在しない ID への参照は <see cref="Parse"/> が <see cref="FormatException"/> で拒否し、
    /// 規則上の誤り（値の範囲・効果の矛盾・数の過不足）は <see cref="MonsterDataValidator"/> が理由付きで拒否する。
    /// </summary>
    public sealed class MonsterData
    {
        private readonly int[,] _chart;
        private readonly Dictionary<string, TypeDef> _types = new(StringComparer.Ordinal);
        private readonly Dictionary<string, StatusDef> _statuses = new(StringComparer.Ordinal);
        private readonly Dictionary<string, MoveDef> _moves = new(StringComparer.Ordinal);
        private readonly Dictionary<string, SpeciesDef> _species = new(StringComparer.Ordinal);
        private readonly Dictionary<string, MapDef> _maps = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TrainerDef> _trainers = new(StringComparer.Ordinal);

        private MonsterData(
            TypeDef[] types,
            int[,] chart,
            StatusDef[] statuses,
            MoveDef[] moves,
            SpeciesDef[] species,
            MapDef[] maps,
            TrainerDef[] trainers,
            Rules rules,
            List<string> duplicates)
        {
            Types = Array.AsReadOnly(types);
            _chart = chart;
            Statuses = Array.AsReadOnly(statuses);
            Moves = Array.AsReadOnly(moves);
            Species = Array.AsReadOnly(species);
            Maps = Array.AsReadOnly(maps);
            Trainers = Array.AsReadOnly(trainers);
            Rules = rules;
            Duplicates = duplicates.AsReadOnly();
            foreach (var t in types) { _types.TryAdd(t.Id, t); }
            foreach (var s in statuses) { _statuses.TryAdd(s.Id, s); }
            foreach (var m in moves) { _moves.TryAdd(m.Id, m); }
            foreach (var s in species) { _species.TryAdd(s.Id, s); }
            foreach (var m in maps) { _maps.TryAdd(m.Id, m); }
            foreach (var t in trainers) { _trainers.TryAdd(t.Id, t); }
        }

        public IReadOnlyList<TypeDef> Types { get; }

        public IReadOnlyList<StatusDef> Statuses { get; }

        public IReadOnlyList<MoveDef> Moves { get; }

        public IReadOnlyList<SpeciesDef> Species { get; }

        public IReadOnlyList<MapDef> Maps { get; }

        public IReadOnlyList<TrainerDef> Trainers { get; }

        public Rules Rules { get; }

        /// <summary>重複した ID（検証で拒否する）。</summary>
        internal IReadOnlyList<string> Duplicates { get; }

        /// <summary>相性の百分率（攻撃側の技の属性 → 受ける側の属性）。技に属性がなければ 100。</summary>
        public int Effectiveness(TypeDef? moveType, TypeDef defender)
        {
            if (moveType == null) { return 100; }
            return _chart[moveType.Index, defender.Index];
        }

        public TypeDef Type(string id) => _types.TryGetValue(id, out var v) ? v : throw new KeyNotFoundException($"属性 {id} がありません。");

        public StatusDef Status(string id) => _statuses.TryGetValue(id, out var v) ? v : throw new KeyNotFoundException($"状態異常 {id} がありません。");

        public MoveDef Move(string id) => _moves.TryGetValue(id, out var v) ? v : throw new KeyNotFoundException($"技 {id} がありません。");

        public SpeciesDef SpeciesOf(string id) => _species.TryGetValue(id, out var v) ? v : throw new KeyNotFoundException($"種族 {id} がありません。");

        public MapDef Map(string id) => _maps.TryGetValue(id, out var v) ? v : throw new KeyNotFoundException($"地図 {id} がありません。");

        public TrainerDef Trainer(string id) => _trainers.TryGetValue(id, out var v) ? v : throw new KeyNotFoundException($"トレーナー {id} がありません。");

        public bool TryGetSpecies(string id, out SpeciesDef species)
        {
            if (_species.TryGetValue(id, out var s))
            {
                species = s;
                return true;
            }

            species = null!;
            return false;
        }

        public bool TryGetMove(string id, out MoveDef move)
        {
            if (_moves.TryGetValue(id, out var m))
            {
                move = m;
                return true;
            }

            move = null!;
            return false;
        }

        public bool TryGetMap(string id, out MapDef map)
        {
            if (_maps.TryGetValue(id, out var m))
            {
                map = m;
                return true;
            }

            map = null!;
            return false;
        }

        /// <summary>その地図のそのマスに立っているトレーナー（倒したかは問わない）。</summary>
        public TrainerDef? TrainerAt(string mapId, int x, int y)
        {
            foreach (var t in Trainers)
            {
                if (t.X == x && t.Y == y && string.Equals(t.MapId, mapId, StringComparison.Ordinal)) { return t; }
            }

            return null;
        }

        /// <summary>読み込んで検証する。誤りがあれば理由を並べた例外。</summary>
        public static MonsterData Load(string types, string statuses, string moves, string species, string world)
        {
            var data = Parse(types, statuses, moves, species, world);
            var errors = MonsterDataValidator.Validate(data);
            if (errors.Count > 0) { throw new InvalidOperationException("データが不正です：" + string.Join(" / ", errors)); }
            return data;
        }

        public static MonsterData Parse(string types, string statuses, string moves, string species, string world)
        {
            var duplicates = new List<string>();
            var typeDefs = ParseTypes(types, duplicates, out var chart);
            var typeById = Index(typeDefs, t => t.Id);
            var statusDefs = ParseStatuses(statuses, duplicates);
            var statusById = Index(statusDefs, s => s.Id);
            var moveDefs = ParseMoves(moves, typeById, statusById, duplicates);
            var moveById = Index(moveDefs, m => m.Id);
            var speciesDefs = ParseSpecies(species, typeById, moveById, duplicates);
            var speciesById = Index(speciesDefs, s => s.Id);
            var rules = new Rules();
            ParseWorld(world, rules, moveById, speciesById, duplicates, out var maps, out var trainers);
            var chartArray = new int[typeDefs.Length, typeDefs.Length];
            for (int a = 0; a < typeDefs.Length; a++)
            {
                for (int d = 0; d < typeDefs.Length; d++) { chartArray[a, d] = 100; }
            }

            foreach (var (attack, defend, percent, line) in chart)
            {
                if (!typeById.TryGetValue(attack, out var at)) { throw new FormatException($"Types.txt {line} 行目: 属性 {attack} がありません。"); }
                if (!typeById.TryGetValue(defend, out var dt)) { throw new FormatException($"Types.txt {line} 行目: 属性 {defend} がありません。"); }
                chartArray[at.Index, dt.Index] = percent;
            }

            return new MonsterData(typeDefs, chartArray, statusDefs, moveDefs, speciesDefs, maps, trainers, rules, duplicates);
        }

        private static Dictionary<string, T> Index<T>(T[] items, Func<T, string> id)
        {
            var map = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (var item in items) { map.TryAdd(id(item), item); }
            return map;
        }

        private static TypeDef[] ParseTypes(string text, List<string> duplicates, out List<(string, string, int, int)> chart)
        {
            const string File = "Types.txt";
            var list = new List<TypeDef>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            chart = new List<(string, string, int, int)>();
            foreach (var (line, p) in Lines(text))
            {
                switch (p[0])
                {
                    case "type":
                        Arity(File, line, p, 3);
                        if (!seen.Add(p[1])) { duplicates.Add($"属性 {p[1]}"); }
                        list.Add(new TypeDef(p[1], p[2], list.Count));
                        break;
                    case "chart":
                        Arity(File, line, p, 4);
                        chart.Add((p[1], p[2], Int(File, line, p[3]), line));
                        break;
                    default:
                        throw new FormatException($"{File} {line} 行目: {p[0]} は使えません。");
                }
            }

            return list.ToArray();
        }

        private static StatusDef[] ParseStatuses(string text, List<string> duplicates)
        {
            const string File = "Statuses.txt";
            var list = new List<StatusDef>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (line, p) in Lines(text))
            {
                if (p[0] != "status") { throw new FormatException($"{File} {line} 行目: {p[0]} は使えません。"); }
                Arity(File, line, p, 8);
                if (!seen.Add(p[1])) { duplicates.Add($"状態異常 {p[1]}"); }
                var blocks = Int(File, line, p[3]);
                if (blocks != 0 && blocks != 1) { throw new FormatException($"{File} {line} 行目: 行動不能は 0 か 1 です。"); }
                list.Add(new StatusDef(
                    p[1],
                    p[2],
                    list.Count,
                    blocks == 1,
                    Int(File, line, p[4]),
                    Int(File, line, p[5]),
                    Int(File, line, p[6]),
                    Int(File, line, p[7])));
            }

            return list.ToArray();
        }

        private static MoveDef[] ParseMoves(
            string text,
            Dictionary<string, TypeDef> types,
            Dictionary<string, StatusDef> statuses,
            List<string> duplicates)
        {
            const string File = "Moves.txt";
            var list = new List<MoveDef>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string[]? head = null;
            int headLine = 0;
            var effects = new List<EffectDef>();

            void Flush()
            {
                if (head == null) { return; }
                TypeDef? type = null;
                if (head[3] != "-")
                {
                    if (!types.TryGetValue(head[3], out var t)) { throw new FormatException($"{File} {headLine} 行目: 属性 {head[3]} がありません。"); }
                    type = t;
                }

                list.Add(new MoveDef(
                    head[1],
                    head[2],
                    list.Count,
                    type,
                    Int(File, headLine, head[4]),
                    Int(File, headLine, head[5]),
                    Int(File, headLine, head[6]),
                    Int(File, headLine, head[7]),
                    effects.ToArray()));
                effects.Clear();
                head = null;
            }

            foreach (var (line, p) in Lines(text))
            {
                switch (p[0])
                {
                    case "move":
                        Flush();
                        Arity(File, line, p, 8);
                        if (!seen.Add(p[1])) { duplicates.Add($"技 {p[1]}"); }
                        head = p;
                        headLine = line;
                        break;
                    case "do":
                        if (head == null) { throw new FormatException($"{File} {line} 行目: do の前に move がありません。"); }
                        effects.Add(Effect(File, line, p, statuses));
                        break;
                    default:
                        throw new FormatException($"{File} {line} 行目: {p[0]} は使えません。");
                }
            }

            Flush();
            return list.ToArray();
        }

        private static EffectDef Effect(string file, int line, string[] p, Dictionary<string, StatusDef> statuses)
        {
            if (p.Length < 2) { throw new FormatException($"{file} {line} 行目: do に種類がありません。"); }
            switch (p[1])
            {
                case "damage":
                    Arity(file, line, p, 2);
                    return new EffectDef(EffectKind.Damage, EffectTarget.Foe, null, 0, StatKind.Attack, 0);
                case "inflict":
                    Arity(file, line, p, 4);
                    if (!statuses.TryGetValue(p[2], out var status)) { throw new FormatException($"{file} {line} 行目: 状態異常 {p[2]} がありません。"); }
                    return new EffectDef(EffectKind.Inflict, EffectTarget.Foe, status, Int(file, line, p[3]), StatKind.Attack, 0);
                case "heal":
                    Arity(file, line, p, 3);
                    return new EffectDef(EffectKind.Heal, EffectTarget.Self, null, Int(file, line, p[2]), StatKind.Attack, 0);
                case "stat":
                    Arity(file, line, p, 5);
                    var target = EnumOf(file, line, p[2], ("self", EffectTarget.Self), ("foe", EffectTarget.Foe));
                    var stat = EnumOf(file, line, p[3], ("atk", StatKind.Attack), ("def", StatKind.Defense), ("spd", StatKind.Speed));
                    return new EffectDef(EffectKind.Stat, target, null, 0, stat, Int(file, line, p[4]));
                default:
                    throw new FormatException($"{file} {line} 行目: 効果 {p[1]} は使えません。");
            }
        }

        private static SpeciesDef[] ParseSpecies(
            string text,
            Dictionary<string, TypeDef> types,
            Dictionary<string, MoveDef> moves,
            List<string> duplicates)
        {
            const string File = "Species.txt";
            var list = new List<SpeciesDef>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string[]? head = null;
            int headLine = 0;
            var learn = new List<LearnDef>();

            void Flush()
            {
                if (head == null) { return; }
                if (!types.TryGetValue(head[3], out var type)) { throw new FormatException($"{File} {headLine} 行目: 属性 {head[3]} がありません。"); }
                list.Add(new SpeciesDef(
                    head[1],
                    head[2],
                    list.Count,
                    type,
                    Int(File, headLine, head[4]),
                    Int(File, headLine, head[5]),
                    Int(File, headLine, head[6]),
                    Int(File, headLine, head[7]),
                    Int(File, headLine, head[8]),
                    Int(File, headLine, head[9]),
                    learn.ToArray()));
                learn.Clear();
                head = null;
            }

            foreach (var (line, p) in Lines(text))
            {
                switch (p[0])
                {
                    case "species":
                        Flush();
                        Arity(File, line, p, 10);
                        if (!seen.Add(p[1])) { duplicates.Add($"種族 {p[1]}"); }
                        head = p;
                        headLine = line;
                        break;
                    case "learn":
                        if (head == null) { throw new FormatException($"{File} {line} 行目: learn の前に species がありません。"); }
                        Arity(File, line, p, 3);
                        if (!moves.TryGetValue(p[2], out var move)) { throw new FormatException($"{File} {line} 行目: 技 {p[2]} がありません。"); }
                        learn.Add(new LearnDef(Int(File, line, p[1]), move));
                        break;
                    default:
                        throw new FormatException($"{File} {line} 行目: {p[0]} は使えません。");
                }
            }

            Flush();
            return list.ToArray();
        }

        private static void ParseWorld(
            string text,
            Rules rules,
            Dictionary<string, MoveDef> moves,
            Dictionary<string, SpeciesDef> species,
            List<string> duplicates,
            out MapDef[] maps,
            out TrainerDef[] trainers)
        {
            const string File = "World.txt";
            var mapList = new List<MapDef>();
            var mapById = new Dictionary<string, MapDef>(StringComparer.Ordinal);
            var trainerList = new List<TrainerDef>();
            var trainerById = new Dictionary<string, TrainerDef>(StringComparer.Ordinal);
            string[]? mapHead = null;
            int mapLine = 0;
            List<string>? rows = null;

            SpeciesDef SpeciesRef(int line, string id) =>
                species.TryGetValue(id, out var s) ? s : throw new FormatException($"{File} {line} 行目: 種族 {id} がありません。");

            MapDef MapRef(int line, string id) =>
                mapById.TryGetValue(id, out var m) ? m : throw new FormatException($"{File} {line} 行目: 地図 {id} がありません。");

            TrainerDef TrainerRef(int line, string id) =>
                trainerById.TryGetValue(id, out var t) ? t : throw new FormatException($"{File} {line} 行目: トレーナー {id} がありません。");

            foreach (var (line, p) in Lines(text))
            {
                if (rows != null)
                {
                    if (p[0] == "row")
                    {
                        Arity(File, line, p, 2);
                        rows.Add(p[1]);
                        continue;
                    }

                    if (p[0] != "end") { throw new FormatException($"{File} {line} 行目: 地図の中では row か end です。"); }
                    if (rows.Count == 0) { throw new FormatException($"{File} {mapLine} 行目: 地図 {mapHead![1]} に行がありません。"); }
                    foreach (var r in rows)
                    {
                        if (r.Length != rows[0].Length) { throw new FormatException($"{File} {mapLine} 行目: 地図 {mapHead![1]} の行の長さがそろっていません。"); }
                    }

                    var map = new MapDef(mapHead![1], mapHead[2], mapList.Count, rows.ToArray());
                    if (!mapById.TryAdd(map.Id, map)) { duplicates.Add($"地図 {map.Id}"); }
                    mapList.Add(map);
                    rows = null;
                    mapHead = null;
                    continue;
                }

                switch (p[0])
                {
                    case "rule":
                        ParseRule(File, line, p, rules, moves);
                        break;
                    case "starter":
                        Arity(File, line, p, 2);
                        rules.AddStarter(SpeciesRef(line, p[1]));
                        break;
                    case "map":
                        Arity(File, line, p, 3);
                        mapHead = p;
                        mapLine = line;
                        rows = new List<string>();
                        break;
                    case "portal":
                        Arity(File, line, p, 8);
                        MapRef(line, p[1]).AddPortal(new PortalDef(
                            Int(File, line, p[2]),
                            Int(File, line, p[3]),
                            p[4],
                            Int(File, line, p[5]),
                            Int(File, line, p[6]),
                            DirectionOf(File, line, p[7])));
                        break;
                    case "grass":
                        Arity(File, line, p, 4);
                        if (p[2].Length != 1 || !Tiles.IsGrass(p[2][0])) { throw new FormatException($"{File} {line} 行目: 草むらの文字は {Tiles.GrassChars} のどれかです。"); }
                        var gmap = MapRef(line, p[1]);
                        if (gmap.GrassFor(p[2][0]) != null) { duplicates.Add($"草むら {p[1]} {p[2]}"); }
                        gmap.AddGrass(new GrassDef(p[2][0], Int(File, line, p[3])));
                        break;
                    case "wild":
                        Arity(File, line, p, 7);
                        var grass = p[2].Length == 1 ? MapRef(line, p[1]).GrassFor(p[2][0]) : null;
                        if (grass == null) { throw new FormatException($"{File} {line} 行目: 草むら {p[1]} {p[2]} がありません。"); }
                        grass.Add(new WildDef(SpeciesRef(line, p[3]), Int(File, line, p[4]), Int(File, line, p[5]), Int(File, line, p[6])));
                        break;
                    case "trainer":
                        Arity(File, line, p, 7);
                        var trainer = new TrainerDef(p[1], p[2], trainerList.Count, p[3], Int(File, line, p[4]), Int(File, line, p[5]), DirectionOf(File, line, p[6]));
                        if (!trainerById.TryAdd(trainer.Id, trainer)) { duplicates.Add($"トレーナー {trainer.Id}"); }
                        trainerList.Add(trainer);
                        break;
                    case "intro":
                        if (p.Length < 3) { throw new FormatException($"{File} {line} 行目: intro の台詞がありません。"); }
                        TrainerRef(line, p[1]).Intro = string.Join(" ", p, 2, p.Length - 2);
                        break;
                    case "defeat":
                        if (p.Length < 3) { throw new FormatException($"{File} {line} 行目: defeat の台詞がありません。"); }
                        TrainerRef(line, p[1]).Defeat = string.Join(" ", p, 2, p.Length - 2);
                        break;
                    case "tmon":
                        Arity(File, line, p, 4);
                        TrainerRef(line, p[1]).Add(new TrainerMonDef(SpeciesRef(line, p[2]), Int(File, line, p[3])));
                        break;
                    default:
                        throw new FormatException($"{File} {line} 行目: {p[0]} は使えません。");
                }
            }

            if (rows != null) { throw new FormatException($"{File} {mapLine} 行目: 地図 {mapHead![1]} に end がありません。"); }
            maps = mapList.ToArray();
            trainers = trainerList.ToArray();
        }

        private static void ParseRule(string file, int line, string[] p, Rules rules, Dictionary<string, MoveDef> moves)
        {
            if (p.Length < 3) { throw new FormatException($"{file} {line} 行目: rule の値がありません。"); }
            switch (p[1])
            {
                case "step_ticks": Arity(file, line, p, 3); rules.StepTicks = Int(file, line, p[2]); break;
                case "grace_steps": Arity(file, line, p, 3); rules.GraceSteps = Int(file, line, p[2]); break;
                case "party_max": Arity(file, line, p, 3); rules.PartyMax = Int(file, line, p[2]); break;
                case "box_max": Arity(file, line, p, 3); rules.BoxMax = Int(file, line, p[2]); break;
                case "level_cap": Arity(file, line, p, 3); rules.LevelCap = Int(file, line, p[2]); break;
                case "starter_level": Arity(file, line, p, 3); rules.StarterLevel = Int(file, line, p[2]); break;
                case "balls": Arity(file, line, p, 3); rules.Balls = Int(file, line, p[2]); break;
                case "potions": Arity(file, line, p, 3); rules.Potions = Int(file, line, p[2]); break;
                case "potion_heal": Arity(file, line, p, 3); rules.PotionHeal = Int(file, line, p[2]); break;
                case "exp_yield_divisor": Arity(file, line, p, 3); rules.ExpYieldDivisor = Int(file, line, p[2]); break;
                case "trainer_exp_percent": Arity(file, line, p, 3); rules.TrainerExpPercent = Int(file, line, p[2]); break;
                case "exp_curve":
                    Arity(file, line, p, 4);
                    rules.ExpCurveNumerator = Int(file, line, p[2]);
                    rules.ExpCurveDenominator = Int(file, line, p[3]);
                    break;
                case "start":
                    Arity(file, line, p, 5);
                    rules.StartMapId = p[2];
                    rules.StartX = Int(file, line, p[3]);
                    rules.StartY = Int(file, line, p[4]);
                    break;
                case "center":
                    Arity(file, line, p, 5);
                    rules.CenterMapId = p[2];
                    rules.CenterX = Int(file, line, p[3]);
                    rules.CenterY = Int(file, line, p[4]);
                    break;
                case "struggle":
                    Arity(file, line, p, 3);
                    if (!moves.TryGetValue(p[2], out var move)) { throw new FormatException($"{file} {line} 行目: 技 {p[2]} がありません。"); }
                    rules.Struggle = move;
                    break;
                default:
                    throw new FormatException($"{file} {line} 行目: 規則 {p[1]} は使えません。");
            }
        }

        private static Direction DirectionOf(string file, int line, string s) => EnumOf(
            file,
            line,
            s,
            ("up", Direction.Up),
            ("right", Direction.Right),
            ("down", Direction.Down),
            ("left", Direction.Left));

        /// <summary>空行と行頭が # の行を除き、空白で区切る。地図の行（row）は # を含むので行頭だけを注釈とみなす。</summary>
        private static IEnumerable<(int Line, string[] Parts)> Lines(string text)
        {
            if (text == null) { throw new ArgumentNullException(nameof(text)); }
            var lines = text.Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var raw = lines[i].Trim();
                if (raw.Length == 0 || raw[0] == '#') { continue; }
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
    }
}
