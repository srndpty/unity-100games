using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Game.Core.Script
{
    internal enum StatementKind
    {
        Label,
        Var,
        Char,
        Background,
        Show,
        Hide,
        Clear,
        Text,
        Set,
        If,
        Jump,
        Choice,
        Ending,
    }

    /// <summary>リテラル（int か bool）。</summary>
    internal readonly struct Literal
    {
        public Literal(bool isBool, int value)
        {
            IsBool = isBool;
            Value = value;
        }

        public bool IsBool { get; }
        public int Value { get; }
    }

    /// <summary>名前のまま（未解決）の代入。</summary>
    internal sealed class RawAssign
    {
        public RawAssign(string variable, string op, Literal value)
        {
            Variable = variable;
            Op = op;
            Value = value;
        }

        public string Variable { get; }

        /// <summary>"="・"+="・"-="。</summary>
        public string Op { get; }
        public Literal Value { get; }
    }

    internal sealed class RawOption
    {
        public RawOption(int line, string text, string target, IReadOnlyList<RawAssign> assignments)
        {
            Line = line;
            Text = text;
            Target = target;
            Assignments = assignments;
        }

        public int Line { get; }
        public string Text { get; }
        public string Target { get; }
        public IReadOnlyList<RawAssign> Assignments { get; }
    }

    /// <summary>構文解析の結果の 1 文。名前は未解決のまま持つ（解決と意味の検証はコンパイラ）。</summary>
    internal sealed class Statement
    {
        public Statement(StatementKind kind, int line)
        {
            Kind = kind;
            Line = line;
        }

        public StatementKind Kind { get; }
        public int Line { get; }

        /// <summary>Label・Var・Char・Background・Show・Hide・Ending の ID、Text の話者（地の文は空）。</summary>
        public string Name { get; set; } = "";

        /// <summary>Text の本文、Char の表示名、Ending の名前。</summary>
        public string Text { get; set; } = "";

        /// <summary>Show の表情。</summary>
        public string Expression { get; set; } = "";

        /// <summary>Show の位置（left・center・right）。</summary>
        public string Slot { get; set; } = "";

        public uint Color { get; set; }

        /// <summary>Var の初期値。</summary>
        public Literal Initial { get; set; }

        public RawAssign? Assign { get; set; }

        /// <summary>If の条件：変数名・比較（空なら bool の真偽）・否定・値。</summary>
        public string CondVariable { get; set; } = "";
        public string CondOp { get; set; } = "";
        public bool CondNegate { get; set; }
        public Literal CondValue { get; set; }

        /// <summary>If・Jump の飛び先のラベル名。</summary>
        public string Target { get; set; } = "";

        public IReadOnlyList<RawOption> Options { get; set; } = Array.Empty<RawOption>();
    }

    /// <summary>
    /// 会話 DSL の構文解析（1 行 1 文）。書式の誤りだけを行番号付きで拾い、名前の解決や意味の検証はしない。
    /// 文法は作品の README の「DSL の文法」が正。
    /// </summary>
    internal static class ScriptParser
    {
        private static readonly char[] _spaces = { ' ', '\t' };
        private static readonly Regex _identifier = new("^[a-z_][a-z0-9_]*$", RegexOptions.CultureInvariant);
        private static readonly Regex _speakerLine = new("^([A-Za-z_][A-Za-z0-9_]*)「(.*)」$", RegexOptions.CultureInvariant);
        private static readonly Regex _speakerStart = new("^[A-Za-z_][A-Za-z0-9_]*「", RegexOptions.CultureInvariant);
        private static readonly Regex _color = new("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant);
        private static readonly Regex _comparison = new("^([a-z_][a-z0-9_]*)\\s*(==|!=|<=|>=|<|>)\\s*(\\S+)$", RegexOptions.CultureInvariant);
        private static readonly Regex _assignment = new("^([a-z_][a-z0-9_]*)\\s*(\\+=|-=|=)\\s*(\\S+)$", RegexOptions.CultureInvariant);

        public static bool IsIdentifier(string s) => _identifier.IsMatch(s);

        public static List<Statement> Parse(IReadOnlyList<string> lines, List<ScriptError> errors)
        {
            var result = new List<Statement>();
            Statement? choice = null;
            List<RawOption>? options = null;
            for (int i = 0; i < lines.Count; i++)
            {
                var line = i + 1;
                var s = lines[i].Trim();
                if (s.Length == 0 || s[0] == '#') { continue; }

                if (choice != null)
                {
                    if (s == "?end")
                    {
                        choice.Options = options!.AsReadOnly();
                        result.Add(choice);
                        choice = null;
                        options = null;
                    }
                    else if (s.StartsWith("- ", StringComparison.Ordinal))
                    {
                        var o = ParseOption(line, s.Substring(2).Trim(), errors);
                        if (o != null) { options!.Add(o); }
                    }
                    else
                    {
                        errors.Add(new ScriptError(line, "選択肢の中に書けるのは「- 文 -> ラベル」と ?end だけです"));
                    }

                    continue;
                }

                var st = ParseLine(line, s, errors);
                if (st == null) { continue; }
                if (st.Kind == StatementKind.Choice)
                {
                    choice = st;
                    options = new List<RawOption>();
                }
                else
                {
                    result.Add(st);
                }
            }

            if (choice != null) { errors.Add(new ScriptError(choice.Line, "?choice が ?end で閉じられていません")); }
            return result;
        }

        private static Statement? ParseLine(int line, string s, List<ScriptError> errors)
        {
            if (s[0] == '*')
            {
                var name = s.Substring(1).Trim();
                if (!IsIdentifier(name)) { return Fail(errors, line, $"ラベル名「{name}」は英小文字・数字・_ で書きます"); }
                return new Statement(StatementKind.Label, line) { Name = name };
            }

            if (s.StartsWith("->", StringComparison.Ordinal))
            {
                var target = s.Substring(2).Trim();
                if (!IsIdentifier(target)) { return Fail(errors, line, $"飛び先「{target}」はラベル名ではありません"); }
                return new Statement(StatementKind.Jump, line) { Target = target };
            }

            if (s.StartsWith("- ", StringComparison.Ordinal)) { return Fail(errors, line, "選択肢の項目は ?choice と ?end の間に書きます"); }

            if (s[0] == '?')
            {
                if (s == "?choice") { return new Statement(StatementKind.Choice, line); }
                if (s == "?end") { return Fail(errors, line, "?end に対応する ?choice がありません"); }
                return Fail(errors, line, $"未知の命令 {FirstWord(s)}");
            }

            if (s[0] == '$') { return ParseDollar(line, s, errors); }
            if (s[0] == '@') { return ParseAt(line, s, errors); }
            return ParseText(line, s, errors);
        }

        private static Statement? ParseDollar(int line, string s, List<ScriptError> errors)
        {
            var word = FirstWord(s);
            var rest = s.Substring(word.Length).Trim();
            switch (word)
            {
                case "$var":
                    {
                        var m = _assignment.Match(rest);
                        if (!m.Success || m.Groups[2].Value != "=") { return Fail(errors, line, "$var は「$var 名前 = 値」と書きます"); }
                        if (!TryLiteral(m.Groups[3].Value, out var v)) { return Fail(errors, line, $"値「{m.Groups[3].Value}」は整数か true/false です"); }
                        return new Statement(StatementKind.Var, line) { Name = m.Groups[1].Value, Initial = v };
                    }

                case "$set":
                    {
                        var a = ParseAssign(line, rest, errors);
                        return a == null ? null : new Statement(StatementKind.Set, line) { Assign = a };
                    }

                case "$if":
                    return ParseIf(line, rest, errors);
                default:
                    return Fail(errors, line, $"未知の命令 {word}");
            }
        }

        private static Statement? ParseIf(int line, string rest, List<ScriptError> errors)
        {
            var arrow = rest.LastIndexOf("->", StringComparison.Ordinal);
            if (arrow < 0) { return Fail(errors, line, "$if は「$if 条件 -> ラベル」と書きます"); }
            var cond = rest.Substring(0, arrow).Trim();
            var target = rest.Substring(arrow + 2).Trim();
            if (!IsIdentifier(target)) { return Fail(errors, line, $"飛び先「{target}」はラベル名ではありません"); }
            var st = new Statement(StatementKind.If, line) { Target = target };
            var m = _comparison.Match(cond);
            if (m.Success)
            {
                if (!TryLiteral(m.Groups[3].Value, out var v)) { return Fail(errors, line, $"値「{m.Groups[3].Value}」は整数か true/false です"); }
                st.CondVariable = m.Groups[1].Value;
                st.CondOp = m.Groups[2].Value;
                st.CondValue = v;
                return st;
            }

            var negate = cond.StartsWith('!');
            var name = negate ? cond.Substring(1).Trim() : cond;
            if (!IsIdentifier(name)) { return Fail(errors, line, $"条件「{cond}」は「変数 比較 値」か「変数」「!変数」です"); }
            st.CondVariable = name;
            st.CondNegate = negate;
            return st;
        }

        private static RawAssign? ParseAssign(int line, string text, List<ScriptError> errors)
        {
            var m = _assignment.Match(text.Trim());
            if (!m.Success) { return FailAssign(errors, line, $"代入「{text.Trim()}」は「変数 = 値」「変数 += 値」「変数 -= 値」です"); }
            if (!TryLiteral(m.Groups[3].Value, out var v)) { return FailAssign(errors, line, $"値「{m.Groups[3].Value}」は整数か true/false です"); }
            return new RawAssign(m.Groups[1].Value, m.Groups[2].Value, v);
        }

        private static Statement? ParseAt(int line, string s, List<ScriptError> errors)
        {
            var parts = s.Split(_spaces, StringSplitOptions.RemoveEmptyEntries);
            var word = parts[0];
            switch (word)
            {
                case "@char":
                    if (parts.Length != 4 || !_color.IsMatch(parts[3])) { return Fail(errors, line, "@char は「@char ID 表示名 #RRGGBB」と書きます"); }
                    if (!IsIdentifier(parts[1])) { return Fail(errors, line, $"人物の ID「{parts[1]}」は英小文字・数字・_ で書きます"); }
                    return new Statement(StatementKind.Char, line)
                    {
                        Name = parts[1],
                        Text = parts[2],
                        Color = uint.Parse(parts[3].AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    };
                case "@bg":
                    if (parts.Length != 2) { return Fail(errors, line, "@bg は「@bg 背景」と書きます"); }
                    return new Statement(StatementKind.Background, line) { Name = parts[1] };
                case "@show":
                    if (parts.Length != 4) { return Fail(errors, line, "@show は「@show 人物 表情 left|center|right」と書きます"); }
                    return new Statement(StatementKind.Show, line) { Name = parts[1], Expression = parts[2], Slot = parts[3] };
                case "@hide":
                    if (parts.Length != 2) { return Fail(errors, line, "@hide は「@hide 人物」と書きます"); }
                    return new Statement(StatementKind.Hide, line) { Name = parts[1] };
                case "@clear":
                    if (parts.Length != 1) { return Fail(errors, line, "@clear は引数を取りません"); }
                    return new Statement(StatementKind.Clear, line);
                case "@ending":
                    if (parts.Length != 3) { return Fail(errors, line, "@ending は「@ending ID 名前」と書きます"); }
                    if (!IsIdentifier(parts[1])) { return Fail(errors, line, $"結末の ID「{parts[1]}」は英小文字・数字・_ で書きます"); }
                    return new Statement(StatementKind.Ending, line) { Name = parts[1], Text = parts[2] };
                default:
                    return Fail(errors, line, $"未知の命令 {word}");
            }
        }

        private static Statement? ParseText(int line, string s, List<ScriptError> errors)
        {
            var m = _speakerLine.Match(s);
            if (m.Success)
            {
                return new Statement(StatementKind.Text, line) { Name = m.Groups[1].Value, Text = m.Groups[2].Value };
            }

            if (_speakerStart.IsMatch(s)) { return Fail(errors, line, "台詞は「話者「本文」」と 」 で閉じます"); }
            return new Statement(StatementKind.Text, line) { Text = s };
        }

        private static RawOption? ParseOption(int line, string s, List<ScriptError> errors)
        {
            var assigns = new List<RawAssign>();
            var body = s;
            if (body.EndsWith('}'))
            {
                var open = body.LastIndexOf('{');
                if (open < 0) { return FailOption(errors, line, "選択肢の代入の { がありません"); }
                var inner = body.Substring(open + 1, body.Length - open - 2);
                body = body.Substring(0, open).Trim();
                foreach (var part in inner.Split(';'))
                {
                    if (part.Trim().Length == 0) { continue; }
                    var a = ParseAssign(line, part, errors);
                    if (a == null) { return null; }
                    assigns.Add(a);
                }
            }
            else if (body.Contains('{'))
            {
                return FailOption(errors, line, "選択肢の代入の } がありません");
            }

            var arrow = body.LastIndexOf("->", StringComparison.Ordinal);
            if (arrow < 0) { return FailOption(errors, line, "選択肢は「- 文 -> ラベル」と書きます"); }
            var text = body.Substring(0, arrow).Trim();
            var target = body.Substring(arrow + 2).Trim();
            if (!IsIdentifier(target)) { return FailOption(errors, line, $"飛び先「{target}」はラベル名ではありません"); }
            return new RawOption(line, text, target, assigns.AsReadOnly());
        }

        private static bool TryLiteral(string s, out Literal value)
        {
            if (s == "true") { value = new Literal(true, 1); return true; }
            if (s == "false") { value = new Literal(true, 0); return true; }
            if (int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n))
            {
                value = new Literal(false, n);
                return true;
            }

            value = default;
            return false;
        }

        private static string FirstWord(string s)
        {
            var end = s.IndexOfAny(_spaces);
            return end < 0 ? s : s.Substring(0, end);
        }

        private static Statement? Fail(List<ScriptError> errors, int line, string message)
        {
            errors.Add(new ScriptError(line, message));
            return null;
        }

        private static RawAssign? FailAssign(List<ScriptError> errors, int line, string message)
        {
            errors.Add(new ScriptError(line, message));
            return null;
        }

        private static RawOption? FailOption(List<ScriptError> errors, int line, string message)
        {
            errors.Add(new ScriptError(line, message));
            return null;
        }
    }
}
