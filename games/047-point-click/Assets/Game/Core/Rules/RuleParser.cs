using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Game.Core.Rules
{
    internal enum RawKind
    {
        Room,
        Thing,
        Npc,
        Exit,
        Item,
        Flag,
        Puzzle,
        Goal,
        Start,
        Rule,
        Topic,
    }

    /// <summary>名前のままの条件（has・solved・フラグ）。</summary>
    internal sealed class RawCondition
    {
        public RawCondition(string kind, string name, bool negated)
        {
            Kind = kind;
            Name = name;
            Negated = negated;
        }

        /// <summary>"has"・"solved"・"flag"。</summary>
        public string Kind { get; }
        public string Name { get; }
        public bool Negated { get; }
    }

    /// <summary>名前のままの効果。文（「…」）は Kind が "say"。</summary>
    internal sealed class RawEffect
    {
        public RawEffect(string kind, string name)
        {
            Kind = kind;
            Name = name;
        }

        public string Kind { get; }

        /// <summary>対象の名前、say なら文。</summary>
        public string Name { get; }
    }

    /// <summary>構文解析の結果の 1 文。名前は未解決のまま持つ（解決と意味の検証はコンパイラ）。</summary>
    internal sealed class RawStatement
    {
        public RawStatement(RawKind kind, int line)
        {
            Kind = kind;
            Line = line;
        }

        public RawKind Kind { get; }
        public int Line { get; }

        /// <summary>宣言の ID（物は「部屋.名前」）、Goal・Start の参照先、ルールの対象、話題の人物。</summary>
        public string Id { get; set; } = "";

        /// <summary>表示名、話題の ID。</summary>
        public string Name { get; set; } = "";

        public int Width { get; set; }
        public int StartX { get; set; }
        public int[] Rect { get; set; } = new int[4];
        public int Stand { get; set; }
        public bool Hidden { get; set; }
        public bool Locked { get; set; }
        public string Destination { get; set; } = "";
        public int At { get; set; }
        public IReadOnlyList<string> After { get; set; } = Array.Empty<string>();

        public string Verb { get; set; } = "";

        /// <summary>use の道具の持ち物（なければ空）。</summary>
        public string Tool { get; set; } = "";

        /// <summary>話題の問い（「」を除いた文）。</summary>
        public string Question { get; set; } = "";

        public IReadOnlyList<RawCondition> Conditions { get; set; } = Array.Empty<RawCondition>();
        public IReadOnlyList<RawEffect> Effects { get; set; } = Array.Empty<RawEffect>();
    }

    /// <summary>
    /// ルール表の構文解析（1 行 1 文）。書式の誤りだけを行番号付きで拾い、名前の解決や意味の検証はしない。
    /// 文法は作品の README の「ルール表の文法」が正。
    /// </summary>
    internal static class RuleParser
    {
        private static readonly Regex _identifier = new("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant);
        private static readonly Regex _hotspotId = new("^[a-z][a-z0-9_]*\\.[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant);

        public static bool IsIdentifier(string s) => _identifier.IsMatch(s);

        public static bool IsHotspotId(string s) => _hotspotId.IsMatch(s);

        public static List<RawStatement> Parse(IReadOnlyList<string> lines, List<RuleError> errors)
        {
            var result = new List<RawStatement>();
            for (int i = 0; i < lines.Count; i++)
            {
                var line = i + 1;
                var s = lines[i].Trim();
                if (s.Length == 0 || s[0] == '#') { continue; }
                var st = s[0] == '@' ? ParseDeclaration(line, s, errors) : ParseRule(line, s, errors);
                if (st != null) { result.Add(st); }
            }

            return result;
        }

        /// <summary>空白で区切る。「…」は中の空白も含めて 1 語。閉じていない「 と対応のない 」は誤り。</summary>
        internal static List<string>? Tokenize(int line, string s, List<RuleError> errors)
        {
            var tokens = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false;
            foreach (var c in s)
            {
                if (quoted)
                {
                    sb.Append(c);
                    if (c == '」') { quoted = false; }
                    else if (c == '「') { return FailTokens(errors, line, "「 の中に 「 は書けません"); }
                    continue;
                }

                if (c == '「')
                {
                    if (sb.Length > 0) { return FailTokens(errors, line, "「 の前には空白が要ります"); }
                    quoted = true;
                    sb.Append(c);
                }
                else if (c == '」')
                {
                    return FailTokens(errors, line, "対応する 「 のない 」があります");
                }
                else if (c == ' ' || c == '\t' || c == '　')
                {
                    Flush(sb, tokens);
                }
                else
                {
                    if (sb.Length > 0 && sb[0] == '「') { return FailTokens(errors, line, "」 の後には空白が要ります"); }
                    sb.Append(c);
                }
            }

            if (quoted) { return FailTokens(errors, line, "「 が 」で閉じられていません"); }
            Flush(sb, tokens);
            return tokens;
        }

        internal static bool IsQuoted(string token) => token.Length >= 2 && token[0] == '「' && token[token.Length - 1] == '」';

        internal static string Unquote(string token) => token.Substring(1, token.Length - 2);

        private static void Flush(StringBuilder sb, List<string> tokens)
        {
            if (sb.Length == 0) { return; }
            tokens.Add(sb.ToString());
            sb.Clear();
        }

        private static RawStatement? ParseDeclaration(int line, string s, List<RuleError> errors)
        {
            var t = Tokenize(line, s, errors);
            if (t == null) { return null; }
            var c = new Cursor(t, 1);
            switch (t[0])
            {
                case "@room":
                    {
                        var st = new RawStatement(RawKind.Room, line);
                        if (!c.Id(out var id, false) || !c.Word(out var name)
                            || !c.Keyword("width") || !c.Int(out var width) || !c.Keyword("start") || !c.Int(out var start) || !c.End())
                        {
                            return Fail(errors, line, "@room は「@room 部屋 名前 width 幅 start 位置」と書きます");
                        }

                        st.Id = id;
                        st.Name = name;
                        st.Width = width;
                        st.StartX = start;
                        return st;
                    }

                case "@object":
                case "@npc":
                case "@exit":
                    return ParseHotspot(line, t, errors);
                case "@item":
                    {
                        if (!c.Id(out var id, false) || !c.Word(out var name) || !c.End()) { return Fail(errors, line, "@item は「@item 持ち物 名前」と書きます"); }
                        return new RawStatement(RawKind.Item, line) { Id = id, Name = name };
                    }

                case "@flag":
                    {
                        if (!c.Id(out var id, false) || !c.End()) { return Fail(errors, line, "@flag は「@flag フラグ」と書きます"); }
                        return new RawStatement(RawKind.Flag, line) { Id = id };
                    }

                case "@puzzle":
                    {
                        if (!c.Id(out var id, false) || !c.Word(out var name)) { return Fail(errors, line, "@puzzle は「@puzzle 謎 名前 [after 謎 …]」と書きます"); }
                        var after = new List<string>();
                        if (!c.AtEnd)
                        {
                            if (!c.Keyword("after") || c.AtEnd) { return Fail(errors, line, "@puzzle の名前の後は「after 謎 …」だけです"); }
                            while (!c.AtEnd)
                            {
                                if (!c.Id(out var p, false)) { return Fail(errors, line, "after の後には謎の ID を並べます"); }
                                after.Add(p);
                            }
                        }

                        return new RawStatement(RawKind.Puzzle, line) { Id = id, Name = name, After = after.AsReadOnly() };
                    }

                case "@goal":
                    {
                        if (!c.Id(out var id, false) || !c.End()) { return Fail(errors, line, "@goal は「@goal 謎」と書きます"); }
                        return new RawStatement(RawKind.Goal, line) { Id = id };
                    }

                case "@start":
                    {
                        if (!c.Id(out var id, false) || !c.End()) { return Fail(errors, line, "@start は「@start 部屋」と書きます"); }
                        return new RawStatement(RawKind.Start, line) { Id = id };
                    }

                default:
                    return Fail(errors, line, $"未知の宣言 {t[0]}");
            }
        }

        private static RawStatement? ParseHotspot(int line, List<string> t, List<RuleError> errors)
        {
            var kind = t[0] == "@object" ? RawKind.Thing : t[0] == "@npc" ? RawKind.Npc : RawKind.Exit;
            var usage = kind == RawKind.Exit
                ? "@exit は「@exit 部屋.名前 表示名 rect x0 y0 x1 y1 stand x -> 部屋 at x [locked] [hidden]」と書きます"
                : $"{t[0]} は「{t[0]} 部屋.名前 表示名 rect x0 y0 x1 y1 stand x [hidden]」と書きます";
            var c = new Cursor(t, 1);
            var st = new RawStatement(kind, line);
            if (!c.Id(out var id, true) || !c.Word(out var name) || !c.Keyword("rect")) { return Fail(errors, line, usage); }
            for (int i = 0; i < 4; i++)
            {
                if (!c.Int(out var v)) { return Fail(errors, line, usage); }
                st.Rect[i] = v;
            }

            if (!c.Keyword("stand") || !c.Int(out var stand)) { return Fail(errors, line, usage); }
            st.Id = id;
            st.Name = name;
            st.Stand = stand;
            if (kind == RawKind.Exit)
            {
                if (!c.Keyword("->") || !c.Id(out var dest, false) || !c.Keyword("at") || !c.Int(out var at)) { return Fail(errors, line, usage); }
                st.Destination = dest;
                st.At = at;
            }

            while (!c.AtEnd)
            {
                if (c.Keyword("hidden") && !st.Hidden) { st.Hidden = true; }
                else if (kind == RawKind.Exit && c.Keyword("locked") && !st.Locked) { st.Locked = true; }
                else { return Fail(errors, line, usage); }
            }

            return st;
        }

        private static RawStatement? ParseRule(int line, string s, List<RuleError> errors)
        {
            var colon = TopLevelIndex(s, ':');
            if (colon < 0) { return Fail(errors, line, "ルールは「動詞 対象 … : 効果 ; 効果」と書きます（: がありません）"); }
            var head = Tokenize(line, s.Substring(0, colon), errors);
            if (head == null) { return null; }
            if (head.Count == 0) { return Fail(errors, line, "動詞がありません"); }
            var c = new Cursor(head, 1);
            RawStatement st;
            if (head[0] == "topic")
            {
                st = new RawStatement(RawKind.Topic, line) { Verb = "topic" };
                if (!c.Id(out var npc, true) || !c.Id(out var topicId, false) || !c.Quoted(out var question))
                {
                    return Fail(errors, line, "話題は「topic 部屋.人物 話題 「問い」 [when 条件] : 効果」と書きます");
                }

                st.Id = npc;
                st.Name = topicId;
                st.Question = question;
            }
            else if (head[0] == "look" || head[0] == "take" || head[0] == "use" || head[0] == "talk")
            {
                st = new RawStatement(RawKind.Rule, line) { Verb = head[0] };
                if (!c.Word(out var first)) { return Fail(errors, line, $"{head[0]} の後に対象がありません"); }
                if (head[0] == "use" && c.Keyword("on"))
                {
                    if (!c.Word(out var target)) { return Fail(errors, line, "use 持ち物 on の後に対象がありません"); }
                    st.Tool = first;
                    st.Id = target;
                }
                else
                {
                    st.Id = first;
                }
            }
            else
            {
                return Fail(errors, line, $"未知の動詞 {head[0]}（look・take・use・talk・topic）");
            }

            if (!c.AtEnd)
            {
                if (!c.Keyword("when")) { return Fail(errors, line, $"対象の後に書けるのは when 条件だけです（「{c.Peek}」）"); }
                var conds = ParseConditions(line, c, errors);
                if (conds == null) { return null; }
                st.Conditions = conds;
            }

            var effects = ParseEffects(line, s.Substring(colon + 1), errors);
            if (effects == null) { return null; }
            st.Effects = effects;
            return st;
        }

        private static IReadOnlyList<RawCondition>? ParseConditions(int line, Cursor c, List<RuleError> errors)
        {
            var result = new List<RawCondition>();
            while (true)
            {
                if (!c.Word(out var w)) { return FailList<RawCondition>(errors, line, "when の後（または and の後）に条件がありません"); }
                var negated = w[0] == '!';
                var word = negated ? w.Substring(1) : w;
                if (word == "has" || word == "solved")
                {
                    if (!c.Word(out var name)) { return FailList<RawCondition>(errors, line, $"{word} の後に名前がありません"); }
                    result.Add(new RawCondition(word, name, negated));
                }
                else
                {
                    if (word.Length == 0) { return FailList<RawCondition>(errors, line, "! の後に条件がありません"); }
                    result.Add(new RawCondition("flag", word, negated));
                }

                if (c.AtEnd) { return result.AsReadOnly(); }
                if (!c.Keyword("and")) { return FailList<RawCondition>(errors, line, $"条件は and でつなぎます（「{c.Peek}」）"); }
            }
        }

        private static IReadOnlyList<RawEffect>? ParseEffects(int line, string body, List<RuleError> errors)
        {
            var result = new List<RawEffect>();
            foreach (var part in SplitTopLevel(body, ';'))
            {
                var t = Tokenize(line, part, errors);
                if (t == null) { return null; }
                if (t.Count == 0) { return FailList<RawEffect>(errors, line, "空の効果があります（; が続いているか、効果がありません）"); }
                if (t.Count == 1 && IsQuoted(t[0]))
                {
                    result.Add(new RawEffect("say", Unquote(t[0])));
                    continue;
                }

                switch (t[0])
                {
                    case "give":
                    case "lose":
                    case "set":
                    case "clear":
                    case "solve":
                    case "show":
                    case "hide":
                    case "unlock":
                        if (t.Count != 2) { return FailList<RawEffect>(errors, line, $"効果 {t[0]} は「{t[0]} 名前」と書きます"); }
                        result.Add(new RawEffect(t[0], t[1]));
                        break;
                    default:
                        return FailList<RawEffect>(errors, line, $"未知の効果 {t[0]}（give・lose・set・clear・solve・show・hide・unlock・「文」）");
                }
            }

            return result.AsReadOnly();
        }

        /// <summary>「…」の外にある最初の文字の位置。</summary>
        private static int TopLevelIndex(string s, char target)
        {
            bool quoted = false;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '「') { quoted = true; }
                else if (s[i] == '」') { quoted = false; }
                else if (!quoted && s[i] == target) { return i; }
            }

            return -1;
        }

        private static List<string> SplitTopLevel(string s, char separator)
        {
            var parts = new List<string>();
            var rest = s;
            while (true)
            {
                var i = TopLevelIndex(rest, separator);
                if (i < 0)
                {
                    parts.Add(rest);
                    return parts;
                }

                parts.Add(rest.Substring(0, i));
                rest = rest.Substring(i + 1);
            }
        }

        private static RawStatement? Fail(List<RuleError> errors, int line, string message)
        {
            errors.Add(new RuleError(line, message));
            return null;
        }

        private static List<string>? FailTokens(List<RuleError> errors, int line, string message)
        {
            errors.Add(new RuleError(line, message));
            return null;
        }

        private static IReadOnlyList<T>? FailList<T>(List<RuleError> errors, int line, string message)
        {
            errors.Add(new RuleError(line, message));
            return null;
        }

        /// <summary>語の列を前から読む。</summary>
        private sealed class Cursor
        {
            private readonly List<string> _tokens;
            private int _at;

            public Cursor(List<string> tokens, int at)
            {
                _tokens = tokens;
                _at = at;
            }

            public bool AtEnd => _at >= _tokens.Count;

            public string Peek => AtEnd ? "" : _tokens[_at];

            public bool End() => AtEnd;

            public bool Word(out string word)
            {
                word = Peek;
                if (AtEnd || IsQuoted(word)) { return false; }
                _at++;
                return true;
            }

            public bool Id(out string id, bool hotspot)
            {
                id = Peek;
                if (AtEnd || !(hotspot ? IsHotspotId(id) : IsIdentifier(id))) { return false; }
                _at++;
                return true;
            }

            public bool Keyword(string keyword)
            {
                if (AtEnd || _tokens[_at] != keyword) { return false; }
                _at++;
                return true;
            }

            public bool Int(out int value)
            {
                value = 0;
                if (AtEnd || !int.TryParse(_tokens[_at], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value)) { return false; }
                _at++;
                return true;
            }

            public bool Quoted(out string text)
            {
                text = "";
                if (AtEnd || !IsQuoted(_tokens[_at])) { return false; }
                text = Unquote(_tokens[_at]);
                _at++;
                return true;
            }
        }
    }
}
