using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core.Analysis;
using IntGeometry;

namespace Game.Core.Rules
{
    /// <summary>コンパイルの結果。誤りが 1 件でもあれば Book は null。</summary>
    public sealed class CompileResult
    {
        internal CompileResult(RuleBook? book, IReadOnlyList<RuleError> errors, StateSpaceReport? report)
        {
            Book = book;
            Errors = errors;
            Report = report;
        }

        public RuleBook? Book { get; }
        public IReadOnlyList<RuleError> Errors { get; }

        /// <summary>状態空間の探索の結果（名前の解決までに誤りがあれば null）。</summary>
        public StateSpaceReport? Report { get; }

        public bool Ok => Book != null;
    }

    /// <summary>
    /// ルール表のコンパイル：構文解析 → 名前の解決と意味の検証 → 状態空間の探索（詰み・使われないルール・曖昧さ・
    /// 持ち物の違反・依存の宣言との照合）。誤りは行番号と理由付きで全て集める。
    /// </summary>
    public static class RuleCompiler
    {
        public static CompileResult Compile(string source, BookLimits limits)
        {
            if (source == null) { throw new ArgumentNullException(nameof(source)); }
            if (limits == null) { throw new ArgumentNullException(nameof(limits)); }
            limits.Validate();
            var errors = new List<RuleError>();
            var lines = source.Replace("\r\n", "\n").Split('\n');
            if (lines.Length > limits.MaxLines)
            {
                errors.Add(new RuleError(0, $"行数 {lines.Length} が上限 {limits.MaxLines} を超えています"));
                return Failed(errors, null);
            }

            var statements = RuleParser.Parse(lines, errors);
            var book = new Resolver(limits, errors).Resolve(statements, Fingerprint(source));
            if (book == null || errors.Count > 0) { return Failed(errors, null); }

            if (PuzzleGraph.DeclaredClosure(book.Puzzles) == null)
            {
                errors.Add(new RuleError(0, "謎の after の宣言が循環しています"));
                return Failed(errors, null);
            }

            var report = StateSpace.Explore(book, limits.MaxStates);
            errors.AddRange(report.Errors);
            if (errors.Count == 0) { PuzzleGraph.Compare(book, report.Precedes, errors); }
            return errors.Count > 0 ? Failed(errors, report) : new CompileResult(book, Array.Empty<RuleError>(), report);
        }

        /// <summary>本文の指紋（FNV-1a、改行は LF にそろえる）。</summary>
        public static uint Fingerprint(string source)
        {
            if (source == null) { throw new ArgumentNullException(nameof(source)); }
            uint h = 2166136261u;
            foreach (var c in source.Replace("\r\n", "\n"))
            {
                unchecked { h = (h ^ c) * 16777619u; }
            }

            return h;
        }

        private static CompileResult Failed(List<RuleError> errors, StateSpaceReport? report) =>
            new(null, errors.OrderBy(e => e.Line == 0 ? int.MaxValue : e.Line).ToList().AsReadOnly(), report);

        /// <summary>名前の解決と意味の検証。</summary>
        private sealed class Resolver
        {
            private readonly BookLimits _limits;
            private readonly List<RuleError> _errors;
            private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
            private readonly List<RoomDef> _rooms = new();
            private readonly List<HotspotDef> _hotspots = new();
            private readonly List<ItemDef> _items = new();
            private readonly List<string> _flags = new();
            private readonly List<PuzzleDef> _puzzles = new();
            private readonly List<Rule> _rules = new();
            private readonly List<Topic> _topics = new();

            public Resolver(BookLimits limits, List<RuleError> errors)
            {
                _limits = limits;
                _errors = errors;
            }

            public RuleBook? Resolve(List<RawStatement> statements, uint fingerprint)
            {
                foreach (var s in statements.Where(s => s.Kind == RawKind.Room)) { AddRoom(s); }
                foreach (var s in statements.Where(s => s.Kind == RawKind.Item)) { AddItem(s); }
                foreach (var s in statements.Where(s => s.Kind == RawKind.Flag)) { AddFlag(s); }
                foreach (var s in statements.Where(s => s.Kind == RawKind.Puzzle)) { AddPuzzle(s); }
                foreach (var s in statements.Where(s => s.Kind == RawKind.Thing || s.Kind == RawKind.Npc || s.Kind == RawKind.Exit)) { AddHotspot(s); }
                foreach (var s in statements.Where(s => s.Kind == RawKind.Puzzle)) { ResolveAfter(s); }
                var start = Single(statements, RawKind.Start, "@start", id => Find(_rooms, r => r.Id == id), "部屋");
                var goal = Single(statements, RawKind.Goal, "@goal", id => Find(_puzzles, p => p.Id == id), "謎");
                foreach (var s in statements.Where(s => s.Kind == RawKind.Rule)) { AddRule(s); }
                foreach (var s in statements.Where(s => s.Kind == RawKind.Topic)) { AddTopic(s); }
                CheckCounts();
                CheckLooks(statements);
                if (_errors.Count > 0 || start < 0 || goal < 0) { return null; }
                return new RuleBook(
                    _rooms.AsReadOnly(), _hotspots.AsReadOnly(), _items.AsReadOnly(), _flags.AsReadOnly(), _puzzles.AsReadOnly(),
                    _rules.AsReadOnly(), _topics.AsReadOnly(), start, goal, _limits.InventoryCapacity, fingerprint);
            }

            private void AddRoom(RawStatement s)
            {
                if (!Declare(s, "部屋")) { return; }
                if (_rooms.Count >= _limits.MaxRooms) { Error(s.Line, $"部屋は {_limits.MaxRooms} 個までです"); return; }
                if (s.Width < 1 || s.Width > _limits.MaxRoomWidth) { Error(s.Line, $"部屋の幅は 1〜{_limits.MaxRoomWidth} です"); return; }
                if (s.StartX < 0 || s.StartX > s.Width) { Error(s.Line, $"start {s.StartX} が部屋の幅 0〜{s.Width} の外です"); return; }
                CheckName(s.Line, s.Name);
                _rooms.Add(new RoomDef(_rooms.Count, s.Id, s.Name, s.Width, s.StartX));
            }

            private void AddItem(RawStatement s)
            {
                if (!Declare(s, "持ち物")) { return; }
                if (_items.Count >= _limits.MaxItems) { Error(s.Line, $"持ち物は {_limits.MaxItems} 種類までです"); return; }
                CheckName(s.Line, s.Name);
                _items.Add(new ItemDef(_items.Count, s.Id, s.Name));
            }

            private void AddFlag(RawStatement s)
            {
                if (!Declare(s, "フラグ")) { return; }
                if (_flags.Count >= _limits.MaxFlags) { Error(s.Line, $"フラグは {_limits.MaxFlags} 個までです"); return; }
                _flags.Add(s.Id);
            }

            private void AddPuzzle(RawStatement s)
            {
                if (!Declare(s, "謎")) { return; }
                if (_puzzles.Count >= _limits.MaxPuzzles) { Error(s.Line, $"謎は {_limits.MaxPuzzles} 個までです"); return; }
                CheckName(s.Line, s.Name);
                _puzzles.Add(new PuzzleDef(_puzzles.Count, s.Line, s.Id, s.Name));
            }

            private void ResolveAfter(RawStatement s)
            {
                var p = Find(_puzzles, x => x.Line == s.Line);
                if (p < 0) { return; }
                var after = new List<int>();
                foreach (var a in s.After)
                {
                    var q = Find(_puzzles, x => x.Id == a);
                    if (q < 0) { Error(s.Line, $"after の謎 {a} が宣言されていません"); continue; }
                    if (after.Contains(q)) { Error(s.Line, $"after の謎 {a} が重複しています"); continue; }
                    after.Add(q);
                }

                _puzzles[p].After = after.AsReadOnly();
            }

            private void AddHotspot(RawStatement s)
            {
                if (!Declare(s, "物")) { return; }
                if (_hotspots.Count >= _limits.MaxHotspots) { Error(s.Line, $"物・人物・出口は合わせて {_limits.MaxHotspots} 個までです"); return; }
                var dot = s.Id.IndexOf('.', StringComparison.Ordinal);
                var roomId = s.Id.Substring(0, dot);
                var room = Find(_rooms, r => r.Id == roomId);
                if (room < 0) { Error(s.Line, $"部屋 {roomId} が宣言されていません"); return; }
                var width = _rooms[room].Width;
                var r = s.Rect;
                if (r[2] < r[0] || r[3] < r[1]) { Error(s.Line, "rect は「左 下 右 上」の順で、右 ≥ 左・上 ≥ 下 です"); return; }
                if (r[0] < 0 || r[2] > width || r[1] < 0 || r[3] > BookLimits.RoomHeight) { Error(s.Line, $"rect が部屋（0〜{width} × 0〜{BookLimits.RoomHeight}）の外に出ています"); return; }
                if (s.Stand < 0 || s.Stand > width) { Error(s.Line, $"stand {s.Stand} が部屋の幅 0〜{width} の外です"); return; }
                CheckName(s.Line, s.Name);
                var kind = s.Kind == RawKind.Npc ? HotspotKind.Npc : s.Kind == RawKind.Exit ? HotspotKind.Exit : HotspotKind.Thing;
                var h = new HotspotDef(_hotspots.Count, s.Line, s.Id, s.Name, room, kind, new Rect(r[0], r[1], r[2], r[3]), s.Stand) { InitiallyVisible = !s.Hidden };
                if (kind == HotspotKind.Exit)
                {
                    var dest = Find(_rooms, x => x.Id == s.Destination);
                    if (dest < 0) { Error(s.Line, $"行き先の部屋 {s.Destination} が宣言されていません"); return; }
                    if (dest == room) { Error(s.Line, "出口の行き先が同じ部屋です"); return; }
                    if (s.At < 0 || s.At > _rooms[dest].Width) { Error(s.Line, $"at {s.At} が行き先の部屋の幅 0〜{_rooms[dest].Width} の外です"); return; }
                    h.Destination = dest;
                    h.Arrival = s.At;
                    h.InitiallyLocked = s.Locked;
                }

                _hotspots.Add(h);
            }

            private void AddRule(RawStatement s)
            {
                if (_rules.Count >= _limits.MaxRules) { Error(s.Line, $"ルールは {_limits.MaxRules} 個までです"); return; }
                var verb = s.Verb switch
                {
                    "look" => Verb.Look,
                    "take" => Verb.Take,
                    "talk" => Verb.Talk,
                    _ => Verb.Use,
                };
                var hotspot = Find(_hotspots, h => h.Id == s.Id);
                var item = Find(_items, i => i.Id == s.Id);
                Target target;
                if (hotspot >= 0) { target = Target.Hotspot(hotspot); }
                else if (item >= 0) { target = Target.Item(item); }
                else { Error(s.Line, $"対象 {s.Id} は物・人物・出口・持ち物のどれとしても宣言されていません"); return; }

                int tool = -1;
                if (s.Tool.Length > 0)
                {
                    tool = Find(_items, i => i.Id == s.Tool);
                    if (tool < 0) { Error(s.Line, $"使う持ち物 {s.Tool} が宣言されていません"); return; }
                    if (target.Kind == TargetKind.Item && target.Index == tool) { Error(s.Line, "持ち物をそれ自身に使う規則は書けません"); return; }
                }

                if (target.Kind == TargetKind.Item && verb != Verb.Look && !(verb == Verb.Use && tool >= 0))
                {
                    Error(s.Line, $"持ち物に書ける規則は look と「use 持ち物 on 持ち物」だけです（{s.Verb} {s.Id}）");
                    return;
                }

                if (target.Kind == TargetKind.Hotspot && _hotspots[target.Index].Kind == HotspotKind.Exit && verb == Verb.Use && tool < 0)
                {
                    Error(s.Line, "出口を（持ち物なしで）使うと移動になるので、規則は書けません");
                    return;
                }

                var conditions = ResolveConditions(s);
                var effects = ResolveEffects(s, false);
                if (conditions == null || effects == null) { return; }
                RuleBook.Normalize(ref target, ref tool);
                _rules.Add(new Rule(_rules.Count, s.Line, verb, target, tool, WithSolveGuards(conditions, effects), effects));
            }

            private void AddTopic(RawStatement s)
            {
                if (_topics.Count >= _limits.MaxTopics) { Error(s.Line, $"話題は {_limits.MaxTopics} 個までです"); return; }
                var npc = Find(_hotspots, h => h.Id == s.Id);
                if (npc < 0 || _hotspots[npc].Kind != HotspotKind.Npc) { Error(s.Line, $"話題の相手 {s.Id} は @npc で宣言された人物ではありません"); return; }
                if (_topics.Any(t => t.Npc == npc && t.Id == s.Name)) { Error(s.Line, $"人物 {s.Id} の話題 {s.Name} が重複しています"); return; }
                CheckText(s.Line, s.Question);
                var conditions = ResolveConditions(s);
                var effects = ResolveEffects(s, true);
                if (conditions == null || effects == null) { return; }
                _topics.Add(new Topic(_topics.Count, s.Line, npc, s.Name, s.Question, WithSolveGuards(conditions, effects), effects));
            }

            private List<Condition>? ResolveConditions(RawStatement s)
            {
                if (s.Conditions.Count > _limits.MaxConditions) { Error(s.Line, $"条件は {_limits.MaxConditions} 個までです"); return null; }
                var result = new List<Condition>();
                foreach (var c in s.Conditions)
                {
                    int index;
                    ConditionKind kind;
                    switch (c.Kind)
                    {
                        case "has":
                            kind = ConditionKind.Has;
                            index = Find(_items, i => i.Id == c.Name);
                            if (index < 0) { Error(s.Line, $"条件の持ち物 {c.Name} が宣言されていません"); return null; }
                            break;
                        case "solved":
                            kind = ConditionKind.Solved;
                            index = Find(_puzzles, p => p.Id == c.Name);
                            if (index < 0) { Error(s.Line, $"条件の謎 {c.Name} が宣言されていません"); return null; }
                            break;
                        default:
                            kind = ConditionKind.Flag;
                            index = _flags.IndexOf(c.Name);
                            if (index < 0) { Error(s.Line, $"条件のフラグ {c.Name} が宣言されていません"); return null; }
                            break;
                    }

                    if (result.Any(x => x.Kind == kind && x.Index == index)) { Error(s.Line, $"条件 {c.Name} が重複しています"); return null; }
                    result.Add(new Condition(kind, index, c.Negated));
                }

                return result;
            }

            private List<Effect>? ResolveEffects(RawStatement s, bool topic)
            {
                if (s.Effects.Count > _limits.MaxEffects) { Error(s.Line, $"効果は {_limits.MaxEffects} 個までです"); return null; }
                var result = new List<Effect>();
                foreach (var e in s.Effects)
                {
                    switch (e.Kind)
                    {
                        case "say":
                            CheckText(s.Line, e.Name);
                            result.Add(new Effect(EffectKind.Say, -1, e.Name));
                            continue;
                        case "give":
                        case "lose":
                            {
                                var i = Find(_items, x => x.Id == e.Name);
                                if (i < 0) { Error(s.Line, $"効果の持ち物 {e.Name} が宣言されていません"); return null; }
                                result.Add(new Effect(e.Kind == "give" ? EffectKind.Give : EffectKind.Lose, i, ""));
                                continue;
                            }

                        case "set":
                        case "clear":
                            {
                                var i = _flags.IndexOf(e.Name);
                                if (i < 0) { Error(s.Line, $"効果のフラグ {e.Name} が宣言されていません"); return null; }
                                result.Add(new Effect(e.Kind == "set" ? EffectKind.Set : EffectKind.Clear, i, ""));
                                continue;
                            }

                        case "solve":
                            {
                                var i = Find(_puzzles, x => x.Id == e.Name);
                                if (i < 0) { Error(s.Line, $"効果の謎 {e.Name} が宣言されていません"); return null; }
                                if (result.Any(x => x.Kind == EffectKind.Solve && x.Index == i)) { Error(s.Line, $"謎 {e.Name} を 1 つのルールで二度解いています"); return null; }
                                result.Add(new Effect(EffectKind.Solve, i, ""));
                                continue;
                            }

                        case "unlock":
                            {
                                var i = Find(_hotspots, x => x.Id == e.Name);
                                if (i < 0 || _hotspots[i].Kind != HotspotKind.Exit) { Error(s.Line, $"unlock {e.Name} は @exit で宣言された出口ではありません"); return null; }
                                result.Add(new Effect(EffectKind.Unlock, i, ""));
                                continue;
                            }

                        default:
                            {
                                var i = Find(_hotspots, x => x.Id == e.Name);
                                if (i < 0) { Error(s.Line, $"{e.Kind} {e.Name} は物・人物・出口として宣言されていません"); return null; }
                                result.Add(new Effect(e.Kind == "show" ? EffectKind.Show : EffectKind.Hide, i, ""));
                                continue;
                            }
                    }
                }

                if (!topic && result.Count == 0) { Error(s.Line, "効果がありません"); return null; }
                return result;
            }

            /// <summary>`solve 謎` を含むルール・話題に「その謎が未解決」の条件を足す（解決済みの謎を二度解かない）。</summary>
            private static System.Collections.ObjectModel.ReadOnlyCollection<Condition> WithSolveGuards(List<Condition> conditions, List<Effect> effects)
            {
                foreach (var e in effects)
                {
                    if (e.Kind != EffectKind.Solve) { continue; }
                    if (conditions.Any(c => c.Kind == ConditionKind.Solved && c.Index == e.Index && c.Negated)) { continue; }
                    conditions.Add(new Condition(ConditionKind.Solved, e.Index, true));
                }

                return conditions.AsReadOnly();
            }

            private int Single(List<RawStatement> statements, RawKind kind, string keyword, Func<string, int> find, string what)
            {
                var found = statements.Where(s => s.Kind == kind).ToList();
                if (found.Count == 0) { Error(0, $"{keyword} がありません"); return -1; }
                if (found.Count > 1) { Error(found[1].Line, $"{keyword} は 1 つだけです"); return -1; }
                var index = find(found[0].Id);
                if (index < 0) { Error(found[0].Line, $"{keyword} の{what} {found[0].Id} が宣言されていません"); }
                return index;
            }

            private void CheckCounts()
            {
                if (_limits.ExpectedRooms > 0 && _rooms.Count != _limits.ExpectedRooms) { Error(0, $"部屋は {_limits.ExpectedRooms} 個です（今は {_rooms.Count} 個）"); }
                if (_limits.ExpectedPuzzles > 0 && _puzzles.Count != _limits.ExpectedPuzzles) { Error(0, $"謎は {_limits.ExpectedPuzzles} 個です（今は {_puzzles.Count} 個）"); }
                if (_rooms.Count == 0) { Error(0, "部屋がありません"); }
                if (_puzzles.Count == 0) { Error(0, "謎がありません"); }
            }

            /// <summary>全ての物・人物・出口・持ち物に look の規則がある（何もない返事を作らない）。</summary>
            private void CheckLooks(List<RawStatement> statements)
            {
                foreach (var h in _hotspots)
                {
                    if (!_rules.Any(r => r.Verb == Verb.Look && r.Target == Target.Hotspot(h.Index))) { Error(h.Line, $"{h.Id} に look の規則がありません"); }
                }

                foreach (var i in _items)
                {
                    if (_rules.Any(r => r.Verb == Verb.Look && r.Target == Target.Item(i.Index))) { continue; }
                    var line = statements.First(s => s.Kind == RawKind.Item && s.Id == i.Id).Line;
                    Error(line, $"持ち物 {i.Id} に look の規則がありません");
                }
            }

            private bool Declare(RawStatement s, string what)
            {
                if (_names.TryGetValue(s.Id, out var existing))
                {
                    Error(s.Line, $"{what}の ID {s.Id} は{existing}としてすでに宣言されています");
                    return false;
                }

                _names[s.Id] = what;
                return true;
            }

            private void CheckName(int line, string name)
            {
                if (name.Length > 12) { Error(line, $"表示名「{name}」は 12 字までです"); }
            }

            private void CheckText(int line, string text)
            {
                if (text.Trim().Length == 0) { Error(line, "空の文があります"); return; }
                if (text.Length > _limits.MaxTextLength) { Error(line, $"文が {text.Length} 字で、上限 {_limits.MaxTextLength} 字を超えています"); }
                foreach (var c in text)
                {
                    if (char.IsControl(c)) { Error(line, "文に制御文字があります"); return; }
                }
            }

            private void Error(int line, string message) => _errors.Add(new RuleError(line, message));

            private static int Find<T>(List<T> list, Func<T, bool> match)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (match(list[i])) { return i; }
                }

                return -1;
            }
        }
    }
}
