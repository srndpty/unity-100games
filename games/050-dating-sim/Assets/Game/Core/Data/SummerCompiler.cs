using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using TileMotion;

namespace Game.Core.Data
{
    /// <summary>データの誤り 1 件（行番号は 1 から、行に結びつかない誤りは 0）。</summary>
    public sealed class SummerError
    {
        public SummerError(int line, string message)
        {
            Line = line;
            Message = message;
        }

        public int Line { get; }
        public string Message { get; }

        public override string ToString() =>
            Line > 0 ? string.Format(CultureInfo.InvariantCulture, "行 {0}：{1}", Line, Message) : Message;
    }

    public sealed class CompileResult
    {
        internal CompileResult(SummerBook? book, IReadOnlyList<SummerError> errors)
        {
            Book = book;
            Errors = errors;
        }

        /// <summary>誤りがなければコンパイル結果（あれば null）。</summary>
        public SummerBook? Book { get; }

        public IReadOnlyList<SummerError> Errors { get; }
        public bool Ok => Book != null;
    }

    /// <summary>コンパイルの上限（展開・確保の前に検証する、044）。</summary>
    public static class SummerLimits
    {
        public const int MaxLines = 4000;
        public const int MaxStats = 16;
        public const int MaxFlags = 64;
        public const int MaxCharacters = 8;
        public const int MaxActions = 12;
        public const int MaxEvents = 96;
        public const int MaxEndings = 8;
        public const int MaxSceneLines = 40;
        public const int MinChoices = 2;
        public const int MaxChoices = 3;
        public const int MaxDays = 60;
        public const int MaxPriority = 1000;
        public const int MaxPercent = 400;
        public const int MaxStatValue = 99999;

        /// <summary>1 行の会話の文字数の上限（会話の枠に収まる長さ）。</summary>
        public const int MaxTextLength = 80;
    }

    /// <summary>
    /// 夏休みのデータ（行指向 DSL）のコンパイラ。行を文に分ける構文解析 → 名前と型の解決 → 意味の検証（範囲・数・区間の検証）の順に進め、
    /// 誤りは行番号と理由を付けて全て返す。文法は作品の README の「DSL の文法」が正。
    /// </summary>
    public static class SummerCompiler
    {
        private static readonly char[] _spaces = { ' ', '\t' };
        private static readonly string[] _weekdays = { "mon", "tue", "wed", "thu", "fri", "sat", "sun" };
        private static readonly HashSet<string> _reserved = new(StringComparer.Ordinal)
        {
            "day", "weekday", "action", "and", "or", "not", "set", "clear", "mon", "tue", "wed", "thu", "fri", "sat", "sun",
        };

        private static readonly Regex _id = new("^[a-z_][a-z0-9_]*$", RegexOptions.CultureInvariant);
        private static readonly Regex _say = new("^([a-z_][a-z0-9_]*)(?::([a-z_][a-z0-9_]*))?「(.*)」$", RegexOptions.CultureInvariant);
        private static readonly Regex _token = new(@"\s*(\(|\)|==|!=|<=|>=|<|>|-?[0-9]+|[a-z_][a-z0-9_]*)", RegexOptions.CultureInvariant);

        public static CompileResult Compile(string source)
        {
            if (source == null) { throw new ArgumentNullException(nameof(source)); }
            var c = new Context(source);
            c.Run();
            return new CompileResult(c.Errors.Count == 0 ? c.Book : null, c.Errors.AsReadOnly());
        }

        /// <summary>1 つの文（ヘッダの行と、塊なら本文の行）。</summary>
        private sealed class Raw
        {
            public Raw(int line, string keyword, string rest)
            {
                Line = line;
                Keyword = keyword;
                Rest = rest;
            }

            public int Line { get; }
            public string Keyword { get; }
            public string Rest { get; }
            public List<(int Line, string Text)> Body { get; } = new();
        }

        private enum ValueType
        {
            Int,
            Weekday,
            Action,
        }

        private sealed class Context
        {
            private readonly string _source;
            private readonly List<Raw> _raws = new();
            private readonly List<StatDef> _stats = new();
            private readonly List<string> _flags = new();
            private readonly List<CharDef> _chars = new();
            private readonly List<BackgroundDef> _bgs = new();
            private readonly List<ActionDef> _actions = new();
            private readonly List<OutcomeDef> _outcomes = new();
            private readonly List<EventDef> _events = new();
            private readonly List<EndingDef> _endings = new();
            private readonly List<(int Line, string A, string B)> _exclusiveRaw = new();
            private readonly Dictionary<string, List<(Cond When, int Weight)>> _overrides = new(StringComparer.Ordinal);
            private int _days;
            private Weekday _first;
            private bool _calendar;
            private int _forced = -1;
            private Cond? _forcedWhen;

            public Context(string source) => _source = source;

            public List<SummerError> Errors { get; } = new();
            public SummerBook? Book { get; private set; }

            public void Run()
            {
                if (!Split()) { return; }

                // 名前だけ先に集める（宣言の順に依らず参照できる）。
                foreach (var r in _raws) { Declare(r); }
                foreach (var r in _raws)
                {
                    if (r.Keyword == "@heart") { DeclareStat(r, Tokens(r.Rest), true); }
                }

                if (!_calendar) { Error(0, "@calendar がありません"); }
                foreach (var r in _raws) { Define(r); }
                Validate();
                if (Errors.Count > 0) { return; }

                var exclusive = new bool[_events.Count, _events.Count];
                foreach (var (line, a, b) in _exclusiveRaw)
                {
                    var ia = _events.FindIndex(e => e.Id == a);
                    var ib = _events.FindIndex(e => e.Id == b);
                    exclusive[ia, ib] = true;
                    exclusive[ib, ia] = true;
                }

                Book = new SummerBook(_days, _first, _stats, _flags, _chars, _bgs, _actions, _outcomes, _forced, _forcedWhen, _events, exclusive, _endings, Fingerprint());
                CheckRanges(Book);
                if (Errors.Count > 0) { Book = null; }
            }

            private void Error(int line, string message) => Errors.Add(new SummerError(line, message));

            // ---- 構文：行を文と塊に分ける ----

            private bool Split()
            {
                var lines = _source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                if (lines.Length > SummerLimits.MaxLines)
                {
                    Error(0, $"行数が {lines.Length} です（{SummerLimits.MaxLines} 行まで）");
                    return false;
                }

                Raw? block = null;
                for (int i = 0; i < lines.Length; i++)
                {
                    var n = i + 1;
                    var text = StripComment(lines[i]).Trim();
                    if (text.Length == 0) { continue; }
                    if (block != null)
                    {
                        if (text == "@end")
                        {
                            block = null;
                            continue;
                        }

                        if (text.StartsWith("@event ", StringComparison.Ordinal) || text.StartsWith("@ending ", StringComparison.Ordinal))
                        {
                            Error(n, $"行 {block.Line} の塊が @end で閉じていません");
                            block = null;
                        }
                        else
                        {
                            block.Body.Add((n, text));
                            continue;
                        }
                    }

                    if (!text.StartsWith('@'))
                    {
                        Error(n, "会話・選択肢は @event か @ending の塊の中に書きます");
                        continue;
                    }

                    var space = text.IndexOfAny(_spaces);
                    var keyword = space < 0 ? text : text.Substring(0, space);
                    var rest = space < 0 ? "" : text.Substring(space + 1).Trim();
                    var raw = new Raw(n, keyword, rest);
                    _raws.Add(raw);
                    if (keyword == "@event" || keyword == "@ending") { block = raw; }
                }

                if (block != null) { Error(block.Line, "塊が @end で閉じていません"); }
                return true;
            }

            private static string StripComment(string line)
            {
                var hash = line.IndexOf('#');

                // 色の指定（#rrggbb）は @char の行にだけ書くので、空白の直後の # だけをコメントとみなす。
                while (hash >= 0)
                {
                    if (hash == 0 || line[hash - 1] == ' ' || line[hash - 1] == '\t')
                    {
                        if (!IsColor(line, hash)) { return line.Substring(0, hash); }
                    }

                    hash = line.IndexOf('#', hash + 1);
                }

                return line;
            }

            private static bool IsColor(string line, int hash) =>
                line.StartsWith("@char ", StringComparison.Ordinal) && hash + 7 <= line.Length && Regex.IsMatch(line.Substring(hash, 7), "^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant);

            // ---- 名前の宣言 ----

            private void Declare(Raw r)
            {
                var t = Tokens(r.Rest);
                switch (r.Keyword)
                {
                    case "@calendar":
                        if (_calendar) { Error(r.Line, "@calendar が 2 つあります"); }
                        _calendar = true;
                        if (t.Length != 2 || !Int(t[0], out _days) || Array.IndexOf(_weekdays, t[1]) < 0)
                        {
                            Error(r.Line, "@calendar は「@calendar 日数 曜日」です（例：@calendar 30 mon）");
                            _days = 30;
                            return;
                        }

                        _first = (Weekday)Array.IndexOf(_weekdays, t[1]);
                        if (_days < 1 || _days > SummerLimits.MaxDays) { Error(r.Line, $"日数は 1〜{SummerLimits.MaxDays} です"); }
                        return;
                    case "@stat":
                        DeclareStat(r, t, false);
                        return;
                    case "@flag":
                        if (t.Length != 1 || !NewName(r.Line, t[0])) { Error(r.Line, "@flag は「@flag ID」です"); return; }
                        _flags.Add(t[0]);
                        return;
                    case "@char":
                        DeclareChar(r, t);
                        return;
                    case "@bg":
                        if (t.Length != 2 || !_id.IsMatch(t[0])) { Error(r.Line, "@bg は「@bg ID 名前」です"); return; }
                        if (_bgs.Exists(b => b.Id == t[0])) { Error(r.Line, $"背景 {t[0]} が重複しています"); return; }
                        _bgs.Add(new BackgroundDef(_bgs.Count, t[0], t[1]));
                        return;
                    default:
                        return;
                }
            }

            private void DeclareStat(Raw r, string[] t, bool heart)
            {
                // @stat ID 名前 最小..最大 = 初期値 [cost]　／　@heart 人物 最小..最大 = 初期値
                var o = heart ? 0 : 1;
                if (t.Length < 4 + o || t[2 + o] != "=" || (t.Length == 5 + o && t[4 + o] != "cost") || t.Length > 5 + o
                    || !Range(t[1 + o], out var min, out var max) || !Int(t[3 + o], out var init))
                {
                    Error(r.Line, heart ? "@heart は「@heart 人物 最小..最大 = 初期値」です" : "@stat は「@stat ID 名前 最小..最大 = 初期値 [cost]」です");
                    return;
                }

                var character = -1;
                var name = heart ? "" : t[1];
                if (heart)
                {
                    character = _chars.FindIndex(c => c.Id == t[0]);
                    if (character < 0) { Error(r.Line, $"人物 {t[0]} が宣言されていません（@char を先に書く）"); return; }
                    if (_chars[character].Heart >= 0) { Error(r.Line, $"人物 {t[0]} の好感度が 2 つあります"); return; }
                    name = _chars[character].Name;
                }
                else if (!NewName(r.Line, t[0]))
                {
                    return;
                }

                if (_stats.Count >= SummerLimits.MaxStats) { Error(r.Line, $"能力値は {SummerLimits.MaxStats} 個までです"); return; }
                if (min >= max || Math.Abs((long)min) > SummerLimits.MaxStatValue || Math.Abs((long)max) > SummerLimits.MaxStatValue)
                {
                    Error(r.Line, $"範囲は 最小 < 最大 で、±{SummerLimits.MaxStatValue} の中です");
                }

                if (init < min || init > max) { Error(r.Line, $"初期値 {init} が範囲 {min}..{max} の外です"); }
                var stat = new StatDef(_stats.Count, t[0], name, min, max, init, !heart && t.Length == 5 + o, character);
                _stats.Add(stat);
                if (heart) { _chars[character].Heart = stat.Index; }
            }

            private void DeclareChar(Raw r, string[] t)
            {
                // @char ID 名前 #rrggbb faces a,b,c
                if (t.Length != 5 || t[3] != "faces" || !t[2].StartsWith('#') || t[2].Length != 7
                    || !uint.TryParse(t[2].TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var color))
                {
                    Error(r.Line, "@char は「@char ID 名前 #rrggbb faces 表情,表情」です");
                    return;
                }

                if (!NewName(r.Line, t[0])) { return; }
                var faces = t[4].Split(',');
                foreach (var f in faces)
                {
                    if (!_id.IsMatch(f)) { Error(r.Line, $"表情 {f} は英小文字・数字・_ の ID です"); return; }
                }

                if (new HashSet<string>(faces, StringComparer.Ordinal).Count != faces.Length) { Error(r.Line, "表情が重複しています"); return; }
                if (_chars.Count >= SummerLimits.MaxCharacters) { Error(r.Line, $"人物は {SummerLimits.MaxCharacters} 人までです"); return; }
                _chars.Add(new CharDef(_chars.Count, t[0], t[1], color, Array.AsReadOnly(faces)));
            }

            /// <summary>条件式で使う名前（能力値・フラグ・人物）は 1 つの名前空間で重複させない。</summary>
            private bool NewName(int line, string id)
            {
                if (!_id.IsMatch(id)) { Error(line, $"ID {id} は英小文字・数字・_ です"); return false; }
                if (_reserved.Contains(id)) { Error(line, $"{id} は予約語です"); return false; }
                if (_stats.Exists(s => s.Id == id && !s.IsHeart) || _flags.Contains(id) || _chars.Exists(c => c.Id == id))
                {
                    Error(line, $"名前 {id} が重複しています（能力値・フラグ・人物で共通）");
                    return false;
                }

                return true;
            }

            // ---- 定義 ----

            private void Define(Raw r)
            {
                switch (r.Keyword)
                {
                    case "@calendar":
                    case "@stat":
                    case "@flag":
                    case "@char":
                    case "@bg":
                        return;
                    case "@heart":
                        return;
                    case "@action":
                        DefineAction(r);
                        return;
                    case "@outcome":
                        DefineOutcome(r);
                        return;
                    case "@weight":
                        DefineWeight(r);
                        return;
                    case "@forced":
                        {
                            var (head, when) = SplitWhen(r.Rest);
                            var t = Tokens(head);
                            if (_forced >= 0) { Error(r.Line, "@forced が 2 つあります"); return; }
                            if (t.Length != 1 || when == null) { Error(r.Line, "@forced は「@forced 行動 when 条件」です"); return; }
                            _forced = _actions.FindIndex(a => a.Id == t[0]);
                            if (_forced < 0) { Error(r.Line, $"行動 {t[0]} がありません（@action を先に書く）"); }
                            _forcedWhen = Condition(r.Line, when);
                            return;
                        }

                    case "@event":
                        DefineEvent(r);
                        return;
                    case "@exclusive":
                        {
                            var t = Tokens(r.Rest);
                            if (t.Length != 2 || t[0] == t[1]) { Error(r.Line, "@exclusive は「@exclusive イベント イベント」（別々の 2 つ）です"); return; }
                            _exclusiveRaw.Add((r.Line, t[0], t[1]));
                            return;
                        }

                    case "@ending":
                        DefineEnding(r);
                        return;
                    default:
                        Error(r.Line, $"未知の文 {r.Keyword} です");
                        return;
                }
            }

            private void DefineAction(Raw r)
            {
                // @action ID 名前 [week 曜日] [bg 背景] : 効果 ; 効果
                var colon = r.Rest.IndexOf(':');
                var head = Tokens(colon < 0 ? r.Rest : r.Rest.Substring(0, colon));
                if (colon < 0 || head.Length < 2 || !_id.IsMatch(head[0])) { Error(r.Line, "@action は「@action ID 名前 [week 曜日] [bg 背景] : 効果」です"); return; }
                if (_actions.Exists(a => a.Id == head[0])) { Error(r.Line, $"行動 {head[0]} が重複しています"); return; }
                if (_actions.Count >= SummerLimits.MaxActions) { Error(r.Line, $"行動は {SummerLimits.MaxActions} 個までです"); return; }
                var week = 0x7F;
                var bg = -1;
                for (int i = 2; i < head.Length; i += 2)
                {
                    if (i + 1 >= head.Length) { Error(r.Line, $"{head[i]} の値がありません"); return; }
                    switch (head[i])
                    {
                        case "week":
                            week = Week(r.Line, head[i + 1]);
                            break;
                        case "bg":
                            bg = Background(r.Line, head[i + 1]);
                            break;
                        default:
                            Error(r.Line, $"@action に {head[i]} は書けません");
                            return;
                    }
                }

                var effects = Effects(r.Line, r.Rest.Substring(colon + 1), true);
                if (effects.Count == 0) { Error(r.Line, "行動の効果がありません"); }
                _actions.Add(new ActionDef(_actions.Count, head[0], head[1], week, bg, effects.AsReadOnly(), r.Line));
            }

            private void DefineOutcome(Raw r)
            {
                // @outcome ID 名前 x百分率 weight 重み
                var t = Tokens(r.Rest);
                if (t.Length != 5 || !_id.IsMatch(t[0]) || !t[2].StartsWith('x') || !Int(t[2].TrimStart('x'), out var pct)
                    || t[3] != "weight" || !Int(t[4], out var weight))
                {
                    Error(r.Line, "@outcome は「@outcome ID 名前 x百分率 weight 重み」です");
                    return;
                }

                if (_outcomes.Exists(o => o.Id == t[0])) { Error(r.Line, $"結果の段階 {t[0]} が重複しています"); return; }
                if (pct < 0 || pct > SummerLimits.MaxPercent) { Error(r.Line, $"倍率は 0〜{SummerLimits.MaxPercent} % です"); }
                if (weight < 0 || weight > 1000) { Error(r.Line, "重みは 0〜1000 です"); }
                var list = new List<(Cond When, int Weight)>();
                _overrides[t[0]] = list;
                _outcomes.Add(new OutcomeDef(_outcomes.Count, t[0], t[1], pct, weight, list.AsReadOnly()));
            }

            private void DefineWeight(Raw r)
            {
                // @weight 段階 重み when 条件
                var (head, when) = SplitWhen(r.Rest);
                var t = Tokens(head);
                if (t.Length != 2 || when == null || !Int(t[1], out var weight)) { Error(r.Line, "@weight は「@weight 段階 重み when 条件」です"); return; }
                if (!_overrides.TryGetValue(t[0], out var list)) { Error(r.Line, $"結果の段階 {t[0]} がありません（@outcome を先に書く）"); return; }
                if (weight < 0 || weight > 1000) { Error(r.Line, "重みは 0〜1000 です"); }
                var cond = Condition(r.Line, when);
                if (cond != null) { list.Add((cond, weight)); }
            }

            private void DefineEvent(Raw r)
            {
                // @event ID slot after|night day a..b [week 曜日] priority n chance n [once] [bg 背景] [when 条件]
                var (head, when) = SplitWhen(r.Rest);
                var t = Tokens(head);
                if (t.Length < 1 || !_id.IsMatch(t[0])) { Error(r.Line, "@event の ID がありません"); return; }
                if (_events.Exists(e => e.Id == t[0])) { Error(r.Line, $"イベント {t[0]} が重複しています"); return; }
                if (_events.Count >= SummerLimits.MaxEvents) { Error(r.Line, $"イベントは {SummerLimits.MaxEvents} 個までです"); return; }
                Slot? slot = null;
                int from = 0;
                int to = 0;
                var hasDay = false;
                var week = 0x7F;
                var priority = -1;
                var chance = -1;
                var once = false;
                var bg = -1;
                for (int i = 1; i < t.Length; i++)
                {
                    if (t[i] == "once")
                    {
                        once = true;
                        continue;
                    }

                    if (i + 1 >= t.Length) { Error(r.Line, $"{t[i]} の値がありません"); return; }
                    var v = t[++i];
                    switch (t[i - 1])
                    {
                        case "slot":
                            slot = v == "after" ? Slot.After : v == "night" ? Slot.Night : (Slot?)null;
                            if (slot == null) { Error(r.Line, "slot は after か night です"); }
                            break;
                        case "day":
                            hasDay = Range(v, out from, out to) || (Int(v, out from) && (to = from) == from);
                            if (!hasDay) { Error(r.Line, "day は「a..b」か 1 つの日です"); }
                            break;
                        case "week":
                            week = Week(r.Line, v);
                            break;
                        case "priority":
                            if (!Int(v, out priority) || priority < 0 || priority > SummerLimits.MaxPriority) { Error(r.Line, $"priority は 0〜{SummerLimits.MaxPriority} です"); }
                            break;
                        case "chance":
                            if (!Int(v, out chance) || chance < 1 || chance > 100) { Error(r.Line, "chance は 1〜100 % です"); }
                            break;
                        case "bg":
                            bg = Background(r.Line, v);
                            break;
                        default:
                            Error(r.Line, $"@event に {t[i - 1]} は書けません");
                            return;
                    }
                }

                if (slot == null || !hasDay || priority < 0 || chance < 0) { Error(r.Line, "@event には slot・day・priority・chance が要ります"); return; }
                if (from < 1 || to > _days || from > to) { Error(r.Line, $"day は 1..{_days} の中で a ≤ b です"); }
                var cond = when == null ? null : Condition(r.Line, when);
                var (lines, effects, choices) = Body(r, true);
                if (lines.Count == 0) { Error(r.Line, "イベントの会話がありません"); }
                _events.Add(new EventDef(_events.Count, t[0], slot.Value, from, to, week, priority, chance, once, cond, bg, lines.AsReadOnly(), effects.AsReadOnly(), choices.AsReadOnly(), r.Line));
            }

            private void DefineEnding(Raw r)
            {
                // @ending ID 名前 priority n when 条件　／　@ending ID 名前 default
                var (head, when) = SplitWhen(r.Rest);
                var t = Tokens(head);
                if (t.Length < 3 || !_id.IsMatch(t[0])) { Error(r.Line, "@ending は「@ending ID 名前 priority n when 条件」か「@ending ID 名前 default」です"); return; }
                if (_endings.Exists(e => e.Id == t[0])) { Error(r.Line, $"ED {t[0]} が重複しています"); return; }
                if (_endings.Count >= SummerLimits.MaxEndings) { Error(r.Line, $"ED は {SummerLimits.MaxEndings} 個までです"); return; }
                int priority;
                Cond? cond = null;
                var bg = -1;
                var i = 2;
                if (t[2] == "default")
                {
                    if (when != null) { Error(r.Line, "既定の ED に条件は書けません"); }
                    priority = -1;
                    i = 3;
                }
                else
                {
                    if (t.Length < 4 || t[2] != "priority" || !Int(t[3], out priority) || priority < 0 || priority > SummerLimits.MaxPriority || when == null)
                    {
                        Error(r.Line, $"条件のある ED は「priority 0〜{SummerLimits.MaxPriority} when 条件」が要ります");
                        return;
                    }

                    cond = Condition(r.Line, when);
                    if (cond == null) { return; }
                    i = 4;
                }

                if (i + 1 < t.Length && t[i] == "bg") { bg = Background(r.Line, t[i + 1]); }
                else if (i < t.Length) { Error(r.Line, $"@ending に {t[i]} は書けません"); }
                var (lines, effects, choices) = Body(r, false);
                if (lines.Count == 0) { Error(r.Line, "ED の会話がありません"); }
                _endings.Add(new EndingDef(_endings.Count, t[0], t[1], priority, cond, bg, lines.AsReadOnly(), r.Line));
            }

            /// <summary>塊の本文：台詞・地の文・@bg・@do（イベントだけ）・選択肢（イベントだけ、最後にまとめて）。</summary>
            private (List<SceneLine> Lines, List<Effect> Effects, List<ChoiceDef> Choices) Body(Raw r, bool isEvent)
            {
                var lines = new List<SceneLine>();
                var effects = new List<Effect>();
                var choices = new List<ChoiceDef>();
                var pendingBg = -1;
                foreach (var (n, text) in r.Body)
                {
                    if (text.StartsWith("@bg ", StringComparison.Ordinal))
                    {
                        pendingBg = Background(n, text.Substring(4).Trim());
                        continue;
                    }

                    if (text.StartsWith("@do ", StringComparison.Ordinal))
                    {
                        if (!isEvent) { Error(n, "ED に @do は書けません"); continue; }
                        effects.AddRange(Effects(n, text.Substring(4), false));
                        continue;
                    }

                    if (text.StartsWith('-'))
                    {
                        if (!isEvent) { Error(n, "ED に選択肢は書けません"); continue; }
                        var colon = text.IndexOf(':');
                        var label = (colon < 0 ? text.Substring(1) : text.Substring(1, colon - 1)).Trim();
                        if (label.Length == 0) { Error(n, "選択肢の文がありません"); continue; }
                        if (label.Length > SummerLimits.MaxTextLength) { Error(n, $"選択肢の文が {label.Length} 字です（{SummerLimits.MaxTextLength} 字まで）"); }
                        var e = colon < 0 ? new List<Effect>() : Effects(n, text.Substring(colon + 1), false);
                        choices.Add(new ChoiceDef(label, e.AsReadOnly()));
                        continue;
                    }

                    if (text.StartsWith('@')) { Error(n, $"塊の中に {text.Split(' ')[0]} は書けません"); continue; }
                    if (choices.Count > 0) { Error(n, "選択肢の後に会話は書けません（選択肢は塊の最後）"); continue; }
                    var line = Say(n, text, pendingBg);
                    pendingBg = -1;
                    if (line != null) { lines.Add(line.Value); }
                }

                if (lines.Count > SummerLimits.MaxSceneLines) { Error(r.Line, $"会話が {lines.Count} 行です（{SummerLimits.MaxSceneLines} 行まで）"); }
                if (choices.Count != 0 && (choices.Count < SummerLimits.MinChoices || choices.Count > SummerLimits.MaxChoices))
                {
                    Error(r.Line, $"選択肢は {SummerLimits.MinChoices}〜{SummerLimits.MaxChoices} 個です（{choices.Count} 個）");
                }

                return (lines, effects, choices);
            }

            private SceneLine? Say(int n, string text, int bg)
            {
                var m = _say.Match(text);
                string body;
                var speaker = -1;
                var face = 0;
                if (m.Success)
                {
                    speaker = _chars.FindIndex(c => c.Id == m.Groups[1].Value);
                    if (speaker < 0) { Error(n, $"話者 {m.Groups[1].Value} が宣言されていません"); return null; }
                    if (m.Groups[2].Success)
                    {
                        face = IndexOfFace(_chars[speaker], m.Groups[2].Value);
                        if (face < 0) { Error(n, $"{_chars[speaker].Id} に表情 {m.Groups[2].Value} はありません"); return null; }
                    }

                    body = m.Groups[3].Value;
                }
                else
                {
                    body = text;
                }

                if (body.Length == 0) { Error(n, "空の文です"); return null; }
                if (body.Length > SummerLimits.MaxTextLength) { Error(n, $"文が {body.Length} 字です（{SummerLimits.MaxTextLength} 字まで）"); }
                return new SceneLine(speaker, face, bg, body);
            }

            private static int IndexOfFace(CharDef c, string face)
            {
                for (int i = 0; i < c.Faces.Count; i++)
                {
                    if (c.Faces[i] == face) { return i; }
                }

                return -1;
            }

            /// <summary>効果の列（; 区切り）。幅（a..b）は行動だけ。</summary>
            private List<Effect> Effects(int line, string text, bool allowRange)
            {
                var result = new List<Effect>();
                foreach (var part in text.Split(';'))
                {
                    var t = Tokens(part);
                    if (t.Length == 0) { continue; }
                    if (t.Length == 2 && (t[0] == "set" || t[0] == "clear"))
                    {
                        var f = _flags.IndexOf(t[1]);
                        if (f < 0) { Error(line, $"フラグ {t[1]} が宣言されていません"); continue; }
                        result.Add(new Effect(t[0] == "set" ? EffectKind.Set : EffectKind.Clear, f, 0, 0));
                        continue;
                    }

                    if (t.Length != 3 || (t[1] != "+=" && t[1] != "-="))
                    {
                        Error(line, $"効果「{part.Trim()}」は「能力値 += 値」「能力値 -= 値」「set フラグ」「clear フラグ」です");
                        continue;
                    }

                    var s = _stats.FindIndex(x => x.Id == t[0]);
                    if (s < 0) { Error(line, $"能力値 {t[0]} が宣言されていません"); continue; }
                    int min;
                    int max;
                    if (Range(t[2], out min, out max))
                    {
                        if (!allowRange) { Error(line, "値の幅（a..b）は @action の効果だけに書けます"); continue; }
                        if (min > max) { Error(line, $"幅 {t[2]} が逆向きです"); continue; }
                    }
                    else if (Int(t[2], out min))
                    {
                        max = min;
                    }
                    else
                    {
                        Error(line, $"値 {t[2]} は整数か a..b です");
                        continue;
                    }

                    if (min < 0) { Error(line, "値は 0 以上で書き、減らすときは -= を使います"); continue; }
                    if (max > SummerLimits.MaxStatValue) { Error(line, $"値は {SummerLimits.MaxStatValue} までです"); continue; }
                    result.Add(t[1] == "+=" ? new Effect(EffectKind.Add, s, min, max) : new Effect(EffectKind.Add, s, -max, -min));
                }

                return result;
            }

            // ---- 条件式 ----

            private static (string Head, string? When) SplitWhen(string rest)
            {
                var i = rest.IndexOf(" when ", StringComparison.Ordinal);
                if (i < 0) { return rest.StartsWith("when ", StringComparison.Ordinal) ? ("", rest.Substring(5)) : (rest, null); }
                return (rest.Substring(0, i), rest.Substring(i + 6));
            }

            private Cond? Condition(int line, string text)
            {
                var tokens = new List<string>();
                var pos = 0;
                while (pos < text.Length)
                {
                    if (char.IsWhiteSpace(text[pos]))
                    {
                        pos++;
                        continue;
                    }

                    var m = _token.Match(text, pos);
                    if (!m.Success || m.Index != pos && text.Substring(pos, m.Index - pos).Trim().Length > 0)
                    {
                        Error(line, $"条件「{text}」の {pos + 1} 文字目が読めません");
                        return null;
                    }

                    tokens.Add(m.Groups[1].Value);
                    pos = m.Index + m.Length;
                }

                var p = new CondParser(this, line, tokens);
                var cond = p.Parse();
                return cond;
            }

            private sealed class CondParser
            {
                private readonly Context _c;
                private readonly int _line;
                private readonly List<string> _t;
                private int _i;
                private bool _failed;

                public CondParser(Context c, int line, List<string> tokens)
                {
                    _c = c;
                    _line = line;
                    _t = tokens;
                }

                public Cond? Parse()
                {
                    if (_t.Count == 0) { return Fail("条件が空です"); }
                    var e = Or();
                    if (_failed) { return null; }
                    if (_i < _t.Count) { return Fail($"条件の {_t[_i]} の前で終わるはずです"); }
                    return e;
                }

                private string? Peek => _i < _t.Count ? _t[_i] : null;

                private Cond? Fail(string message)
                {
                    if (!_failed) { _c.Error(_line, message); }
                    _failed = true;
                    return null;
                }

                private Cond? Or()
                {
                    var a = And();
                    while (!_failed && Peek == "or")
                    {
                        _i++;
                        var b = And();
                        if (a == null || b == null) { return null; }
                        a = Cond.Or(a, b);
                    }

                    return a;
                }

                private Cond? And()
                {
                    var a = Not();
                    while (!_failed && Peek == "and")
                    {
                        _i++;
                        var b = Not();
                        if (a == null || b == null) { return null; }
                        a = Cond.And(a, b);
                    }

                    return a;
                }

                private Cond? Not()
                {
                    if (Peek == "not")
                    {
                        _i++;
                        var a = Not();
                        return a == null ? null : Cond.Not(a);
                    }

                    return Primary();
                }

                private Cond? Primary()
                {
                    var tok = Peek;
                    if (tok == null) { return Fail("条件が途中で終わっています"); }
                    if (tok == "(")
                    {
                        _i++;
                        var e = Or();
                        if (Peek != ")") { return Fail("括弧が閉じていません"); }
                        _i++;
                        return e;
                    }

                    // 比較か、フラグ単独。
                    var next = _i + 1 < _t.Count ? _t[_i + 1] : null;
                    if (next != null && IsOp(next))
                    {
                        if (_i + 2 >= _t.Count) { return Fail("比較の右辺がありません"); }
                        var left = _t[_i];
                        var op = Op(next);
                        var right = _t[_i + 2];
                        _i += 3;
                        return Compare(left, op, right);
                    }

                    _i++;
                    var f = _c._flags.IndexOf(tok);
                    if (f >= 0) { return Cond.Flag(f); }
                    if (_c._stats.Exists(s => s.Id == tok) || tok == "day" || tok == "weekday" || tok == "action")
                    {
                        return Fail($"{tok} は整数・曜日・行動の値で、比較が要ります（フラグだけ単独で書けます）");
                    }

                    return Fail($"{tok} は宣言されていないフラグです");
                }

                private Cond? Compare(string left, CompareOp op, string right)
                {
                    // action・weekday は相手側を行動・曜日の名前として解決する（行動と能力値が同じ ID でもよい）。
                    if (left == "action" || right == "action") { return Typed(left, op, right, "action", ValueType.Action); }
                    if (left == "weekday" || right == "weekday") { return Typed(left, op, right, "weekday", ValueType.Weekday); }
                    var a = IntOperand(left);
                    var b = IntOperand(right);
                    if (a == null || b == null) { return null; }
                    return Cond.Compare(a.Value, op, b.Value);
                }

                private Cond? Typed(string left, CompareOp op, string right, string keyword, ValueType type)
                {
                    if (op != CompareOp.Eq && op != CompareOp.Ne) { return Fail($"{keyword} は == か != で比べます"); }
                    if (left == keyword && right == keyword) { return Fail($"{keyword} どうしは比べられません"); }
                    var name = left == keyword ? right : left;
                    int value;
                    if (type == ValueType.Action)
                    {
                        value = _c._actions.FindIndex(a => a.Id == name);
                        if (value < 0) { return Fail($"行動 {name} がありません（action と比べるのは行動の ID。@action を先に書く）"); }
                    }
                    else
                    {
                        value = Array.IndexOf(_weekdays, name);
                        if (value < 0) { return Fail($"{name} は曜日（mon〜sun）ではありません"); }
                    }

                    var kind = type == ValueType.Action ? OperandKind.Action : OperandKind.Weekday;
                    var v = new Operand(kind, 0);
                    var k = new Operand(OperandKind.Const, value);
                    return left == keyword ? Cond.Compare(v, op, k) : Cond.Compare(k, op, v);
                }

                private Operand? IntOperand(string tok)
                {
                    if (tok == "day") { return new Operand(OperandKind.Day, 0); }
                    if (Int(tok, out var n)) { return new Operand(OperandKind.Const, n); }
                    var s = _c._stats.FindIndex(x => x.Id == tok);
                    if (s >= 0) { return new Operand(OperandKind.Stat, s); }
                    if (_c._flags.Contains(tok)) { Fail($"フラグ {tok} は数と比べられません"); return null; }
                    if (Array.IndexOf(_weekdays, tok) >= 0) { Fail($"曜日 {tok} は weekday と比べます"); return null; }
                    Fail($"{tok} は宣言されていない能力値です");
                    return null;
                }

                private static bool IsOp(string s) => s is "==" or "!=" or "<" or "<=" or ">" or ">=";

                private static CompareOp Op(string s) => s switch
                {
                    "==" => CompareOp.Eq,
                    "!=" => CompareOp.Ne,
                    "<" => CompareOp.Lt,
                    "<=" => CompareOp.Le,
                    ">" => CompareOp.Gt,
                    _ => CompareOp.Ge,
                };
            }

            // ---- 意味の検証 ----

            private void Validate()
            {
                if (_actions.Count == 0) { Error(0, "行動（@action）がありません"); }
                if (_outcomes.Count == 0) { Error(0, "結果の段階（@outcome）がありません"); }

                // どの状態でも重みの合計が正になる（全ての上書きのうち小さい方の和）。
                long minTotal = 0;
                foreach (var o in _outcomes)
                {
                    var min = o.Weight;
                    foreach (var (_, w) in o.Overrides) { min = Math.Min(min, w); }
                    minTotal += min;
                }

                if (_outcomes.Count > 0 && minTotal <= 0) { Error(0, "結果の段階の重みの合計が 0 になりうります（重みの最小の和が正になるように）"); }
                foreach (var (line, a, b) in _exclusiveRaw)
                {
                    if (!_events.Exists(e => e.Id == a)) { Error(line, $"イベント {a} がありません"); }
                    if (!_events.Exists(e => e.Id == b)) { Error(line, $"イベント {b} がありません"); }
                }

                var defaults = _endings.FindAll(e => e.IsDefault);
                if (defaults.Count != 1) { Error(0, $"既定の ED（default）はちょうど 1 つです（{defaults.Count} 個）"); }
                var priorities = new HashSet<int>();
                foreach (var e in _endings)
                {
                    if (!e.IsDefault && !priorities.Add(e.Priority)) { Error(e.Line, $"ED の priority {e.Priority} が重複しています（どちらが先か決まらない）"); }
                }

                foreach (var a in _actions)
                {
                    if (a.WeekMask == 0) { Error(a.Line, "選べる曜日がありません"); }
                }

                if (_flags.Count > SummerLimits.MaxFlags) { Error(0, $"フラグは {SummerLimits.MaxFlags} 個までです"); }
            }

            /// <summary>
            /// 区間の検証：各能力値が d 日目までに取りうる範囲（初期値から、1 日に動きうる最大の増減を d 日ぶん）を大きめに見積もり、
            /// どの日にも成り立たないイベント・ED の条件と、上の ED に必ず隠れる ED を拒否する。見積もりは過大なので「必ず偽」だけを言い切る。
            /// </summary>
            private void CheckRanges(SummerBook book)
            {
                var bounds = new StatBounds(book);
                foreach (var e in book.Events)
                {
                    var open = false;
                    var possible = false;
                    for (int d = e.DayFrom; d <= e.DayTo; d++)
                    {
                        if (!e.OpenOn(d, book.WeekdayOf(d))) { continue; }
                        open = true;
                        if (e.When == null || e.When.EvalRange(bounds.On(d), bounds.FlagPossible) != Tri.False) { possible = true; }
                    }

                    if (!open) { Error(e.Line, $"イベント {e.Id} は期間と曜日の上で出る日がありません"); }
                    else if (!possible) { Error(e.Line, $"イベント {e.Id} の条件は、取りうる能力値の範囲ではどの日にも成り立ちません"); }
                }

                var end = bounds.On(book.Days);
                foreach (var e in book.EndingsByPriority)
                {
                    if (e.IsDefault) { break; }
                    var tri = e.When!.EvalRange(end, bounds.FlagPossible);
                    if (tri == Tri.False) { Error(e.Line, $"ED {e.Id} の条件は、取りうる能力値の範囲では成り立ちません"); }
                    if (tri == Tri.True) { Error(e.Line, $"ED {e.Id} の条件は必ず成り立つので、優先度の低い ED に届きません"); }
                }

                if (book.ForcedWhen != null && book.ForcedWhen.EvalRange(end, bounds.FlagPossible) == Tri.True)
                {
                    Error(0, "@forced の条件が必ず成り立ち、ほかの行動を選べません");
                }
            }

            private uint Fingerprint()
            {
                var h = StateHash.Start();
                foreach (var ch in _source.Replace("\r\n", "\n")) { h.Mix(ch); }
                return h.Value;
            }

            // ---- 字句 ----

            private static string[] Tokens(string text) => text.Split(_spaces, StringSplitOptions.RemoveEmptyEntries);

            private static bool Int(string s, out int value) => int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

            private static bool Range(string s, out int min, out int max)
            {
                min = 0;
                max = 0;
                var i = s.IndexOf("..", StringComparison.Ordinal);
                return i > 0 && Int(s.Substring(0, i), out min) && Int(s.Substring(i + 2), out max);
            }

            private int Week(int line, string text)
            {
                var mask = 0;
                foreach (var part in text.Split(','))
                {
                    var i = part.IndexOf("..", StringComparison.Ordinal);
                    if (i > 0)
                    {
                        var a = Array.IndexOf(_weekdays, part.Substring(0, i));
                        var b = Array.IndexOf(_weekdays, part.Substring(i + 2));
                        if (a < 0 || b < 0 || a > b) { Error(line, $"曜日の範囲 {part} が読めません（mon..sun）"); return 0x7F; }
                        for (int d = a; d <= b; d++) { mask |= 1 << d; }
                        continue;
                    }

                    var w = Array.IndexOf(_weekdays, part);
                    if (w < 0) { Error(line, $"曜日 {part} が読めません（mon〜sun）"); return 0x7F; }
                    mask |= 1 << w;
                }

                return mask;
            }

            private int Background(int line, string id)
            {
                var i = _bgs.FindIndex(b => b.Id == id);
                if (i < 0) { Error(line, $"背景 {id} が宣言されていません"); }
                return i;
            }
        }
    }
}
