using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Game.Core.Runtime;

namespace Game.Core.Script
{
    /// <summary>コンパイルの結果。誤りが 1 件でもあれば <see cref="Script"/> は null。</summary>
    public sealed class CompileResult
    {
        public CompileResult(CompiledScript? script, IReadOnlyList<ScriptError> errors, IReadOnlyList<StoryPath> paths)
        {
            Script = script;
            Errors = errors;
            Paths = paths;
        }

        public CompiledScript? Script { get; }
        public IReadOnlyList<ScriptError> Errors { get; }

        /// <summary>網羅探索で見つけた全ての選択の列と結末（誤りがあれば空のことがある）。</summary>
        public IReadOnlyList<StoryPath> Paths { get; }
        public bool Ok => Errors.Count == 0;

        /// <summary>誤りを 1 行ずつ並べた文（ビルドの失敗の表示に使う）。</summary>
        public string Report() => string.Join("\n", Errors);
    }

    /// <summary>
    /// 会話 DSL のコンパイラ。構文解析 → 名前の解決と意味の検証 → 命令列 → 静的な到達性 → 実行器による網羅探索
    /// （どの経路も結末で終わる・全ての結末に届く）の順に検証する。誤りは行番号と理由付きで返す。
    /// </summary>
    public static class ScriptCompiler
    {
        private static readonly IReadOnlyList<ScriptError> _none = Array.AsReadOnly(Array.Empty<ScriptError>());
        private static readonly IReadOnlyList<StoryPath> _noPaths = Array.AsReadOnly(Array.Empty<StoryPath>());

        public static CompileResult Compile(string source, ScriptCatalog catalog, ScriptRules rules)
        {
            if (source == null) { throw new ArgumentNullException(nameof(source)); }
            if (catalog == null) { throw new ArgumentNullException(nameof(catalog)); }
            if (rules == null) { throw new ArgumentNullException(nameof(rules)); }
            var errors = new List<ScriptError>();
            foreach (var e in rules.Validate()) { errors.Add(new ScriptError(0, "規則：" + e)); }
            if (errors.Count > 0) { return Fail(errors); }

            // 行数は分ける前に数える（巨大な入力を展開する前に拒否する、044）。
            int lineCount = 1;
            foreach (var ch in source)
            {
                if (ch == '\n' && ++lineCount > rules.MaxLines)
                {
                    errors.Add(new ScriptError(0, $"スクリプトは {rules.MaxLines} 行まで"));
                    return Fail(errors);
                }
            }

            var lines = source.Replace("\r", "").Split('\n');
            var statements = ScriptParser.Parse(lines, errors);
            if (errors.Count > 0) { return Fail(errors); }

            var c = new Resolver(catalog, rules, errors);
            var compiled = c.Build(statements);
            if (compiled == null || errors.Count > 0) { return Fail(errors); }

            CheckReachability(compiled, errors);
            if (errors.Count > 0) { return Fail(errors); }

            var explored = PathExplorer.Explore(compiled, rules.MaxPaths);
            if (!explored.Ok)
            {
                errors.Add(new ScriptError(explored.ErrorLine, explored.Error));
                return Fail(errors);
            }

            var reached = new bool[compiled.Endings.Count];
            foreach (var p in explored.Paths) { reached[p.Ending] = true; }
            for (int i = 0; i < reached.Length; i++)
            {
                if (!reached[i]) { errors.Add(new ScriptError(EndingLine(compiled, i), $"結末 {compiled.Endings[i].Id} にはどの選び方でも届きません")); }
            }

            if (errors.Count > 0) { return Fail(errors); }
            return new CompileResult(compiled, _none, explored.Paths);
        }

        private static CompileResult Fail(List<ScriptError> errors) => new(null, errors.AsReadOnly(), _noPaths);

        private static int EndingLine(CompiledScript s, int ending)
        {
            foreach (var ins in s.Instructions)
            {
                if (ins.Kind == InstructionKind.Ending && ins.Ending == ending) { return ins.Line; }
            }

            return 0;
        }

        /// <summary>条件分岐は両方へ進むとみなした到達性。届かない命令と、スクリプトの終わりへの素通りを拒否する。</summary>
        private static void CheckReachability(CompiledScript s, List<ScriptError> errors)
        {
            var ins = s.Instructions;
            var reached = new bool[ins.Count];
            var stack = new Stack<int>();
            stack.Push(0);
            var fellOff = -1;
            while (stack.Count > 0)
            {
                var pc = stack.Pop();
                if (pc >= ins.Count)
                {
                    continue;
                }

                if (reached[pc]) { continue; }
                reached[pc] = true;
                var x = ins[pc];
                void Next()
                {
                    if (pc + 1 >= ins.Count) { fellOff = fellOff < 0 ? x.Line : fellOff; }
                    else { stack.Push(pc + 1); }
                }

                switch (x.Kind)
                {
                    case InstructionKind.Ending:
                        break;
                    case InstructionKind.Jump:
                        stack.Push(x.Target);
                        break;
                    case InstructionKind.JumpIf:
                        stack.Push(x.Target);
                        Next();
                        break;
                    case InstructionKind.Choice:
                        foreach (var o in x.Options) { stack.Push(o.Target); }
                        break;
                    default:
                        Next();
                        break;
                }
            }

            if (fellOff > 0) { errors.Add(new ScriptError(fellOff, "ここからスクリプトの終わりまで結末（@ending）も飛び先もありません")); }

            // 届かない命令は、続きの塊の最初の 1 つだけを報告する。
            for (int i = 0; i < ins.Count; i++)
            {
                if (!reached[i] && (i == 0 || reached[i - 1]))
                {
                    errors.Add(new ScriptError(ins[i].Line, "ここから先はどこからも到達しません"));
                }
            }
        }

        /// <summary>名前の解決と意味の検証、命令列の生成。</summary>
        private sealed class Resolver
        {
            private readonly ScriptCatalog _catalog;
            private readonly ScriptRules _rules;
            private readonly List<ScriptError> _errors;
            private readonly List<VariableDef> _vars = new();
            private readonly Dictionary<string, int> _varIndex = new(StringComparer.Ordinal);
            private readonly List<CharacterDef> _chars = new();
            private readonly Dictionary<string, int> _charIndex = new(StringComparer.Ordinal);
            private readonly List<EndingDef> _endings = new();
            private readonly Dictionary<string, int> _endingIndex = new(StringComparer.Ordinal);
            private readonly Dictionary<string, int> _labels = new(StringComparer.Ordinal);

            public Resolver(ScriptCatalog catalog, ScriptRules rules, List<ScriptError> errors)
            {
                _catalog = catalog;
                _rules = rules;
                _errors = errors;
            }

            public CompiledScript? Build(List<Statement> statements)
            {
                Declarations(statements);
                if (!Labels(statements)) { return null; }

                var instructions = new List<Instruction>();
                var label = "";
                var ordinal = 0;
                var choices = 0;
                foreach (var st in statements)
                {
                    switch (st.Kind)
                    {
                        case StatementKind.Label:
                            label = st.Name;
                            ordinal = 0;
                            break;
                        case StatementKind.Var:
                        case StatementKind.Char:
                            break;
                        case StatementKind.Text:
                            ordinal++;
                            instructions.Add(Text(st, $"{label}#{ordinal}"));
                            break;
                        case StatementKind.Background:
                            if (!_catalog.HasBackground(st.Name)) { Error(st.Line, $"背景 {st.Name} は素材にありません"); }
                            instructions.Add(Instruction.NewBackground(st.Line, st.Name));
                            break;
                        case StatementKind.Show:
                            instructions.Add(Show(st));
                            break;
                        case StatementKind.Hide:
                            instructions.Add(Instruction.NewHide(st.Line, Character(st.Line, st.Name)));
                            break;
                        case StatementKind.Clear:
                            instructions.Add(Instruction.NewClear(st.Line));
                            break;
                        case StatementKind.Set:
                            instructions.Add(Instruction.NewAssign(st.Line, Assign(st.Line, st.Assign!)));
                            break;
                        case StatementKind.If:
                            instructions.Add(Instruction.NewJumpIf(st.Line, Condition(st), Target(st.Line, st.Target)));
                            break;
                        case StatementKind.Jump:
                            instructions.Add(Instruction.NewJump(st.Line, Target(st.Line, st.Target)));
                            break;
                        case StatementKind.Choice:
                            choices++;
                            instructions.Add(Choice(st));
                            break;
                        case StatementKind.Ending:
                            instructions.Add(Instruction.NewEnding(st.Line, _endingIndex[st.Name], st.Text));
                            break;
                        default:
                            throw new InvalidOperationException($"未知の文 {st.Kind}");
                    }
                }

                if (instructions.Count == 0) { Error(0, "実行する文がありません"); }
                foreach (var st in statements)
                {
                    if (st.Kind == StatementKind.Label && _labels.TryGetValue(st.Name, out var at) && at >= instructions.Count)
                    {
                        Error(st.Line, $"ラベル {st.Name} の後に文がありません");
                    }
                }

                if (_rules.ChoiceCount > 0 && choices != _rules.ChoiceCount) { Error(0, $"選択肢は {_rules.ChoiceCount} 箇所（{choices}）"); }
                if (_rules.EndingCount > 0 && _endings.Count != _rules.EndingCount) { Error(0, $"結末は {_rules.EndingCount} つ（{_endings.Count}）"); }
                if (_errors.Count > 0) { return null; }
                return new CompiledScript(instructions, _vars, _chars, _endings, _labels, choices, _rules.FlowLimit, _rules.ValueLimit);
            }

            /// <summary>変数・人物・結末の宣言。変数と人物は最初のラベルより前にだけ書ける。</summary>
            private void Declarations(List<Statement> statements)
            {
                var seenLabel = false;
                foreach (var st in statements)
                {
                    switch (st.Kind)
                    {
                        case StatementKind.Label:
                            seenLabel = true;
                            break;
                        case StatementKind.Var:
                            if (seenLabel) { Error(st.Line, "$var は最初のラベルより前に書きます"); }
                            if (_varIndex.ContainsKey(st.Name)) { Error(st.Line, $"変数 {st.Name} が重複しています"); break; }
                            if (!st.Initial.IsBool && Math.Abs((long)st.Initial.Value) > _rules.ValueLimit) { Error(st.Line, $"値は ±{_rules.ValueLimit} まで（{st.Initial.Value}）"); }
                            _varIndex[st.Name] = _vars.Count;
                            _vars.Add(new VariableDef(st.Name, st.Initial.IsBool ? VariableType.Flag : VariableType.Number, st.Initial.Value));
                            break;
                        case StatementKind.Char:
                            if (seenLabel) { Error(st.Line, "@char は最初のラベルより前に書きます"); }
                            if (_charIndex.ContainsKey(st.Name)) { Error(st.Line, $"人物 {st.Name} が重複しています"); break; }
                            CheckName(st.Line, "話者名", st.Text);
                            _charIndex[st.Name] = _chars.Count;
                            _chars.Add(new CharacterDef(st.Name, st.Text, st.Color));
                            break;
                        case StatementKind.Ending:
                            if (!seenLabel) { Error(st.Line, "最初のラベル（*start など）より前には宣言（$var・@char）だけを書きます"); }
                            if (_endingIndex.ContainsKey(st.Name)) { Error(st.Line, $"結末 {st.Name} が重複しています"); break; }
                            CheckName(st.Line, "結末名", st.Text);
                            _endingIndex[st.Name] = _endings.Count;
                            _endings.Add(new EndingDef(st.Name, st.Text));
                            break;
                        default:
                            if (!seenLabel) { Error(st.Line, "最初のラベル（*start など）より前には宣言（$var・@char）だけを書きます"); }
                            break;
                    }
                }
            }

            /// <summary>ラベルの番地。宣言は命令にならないので数えない。</summary>
            private bool Labels(List<Statement> statements)
            {
                var pc = 0;
                foreach (var st in statements)
                {
                    if (st.Kind == StatementKind.Label)
                    {
                        if (_labels.ContainsKey(st.Name)) { Error(st.Line, $"ラベル {st.Name} が重複しています"); }
                        else { _labels[st.Name] = pc; }
                    }
                    else if (st.Kind is not (StatementKind.Var or StatementKind.Char))
                    {
                        pc++;
                    }
                }

                if (_labels.Count == 0) { Error(0, "ラベルが 1 つもありません"); }
                return _errors.Count == 0;
            }

            private Instruction Text(Statement st, string id)
            {
                var speaker = -1;
                if (st.Name.Length > 0)
                {
                    if (!_charIndex.TryGetValue(st.Name, out speaker))
                    {
                        Error(st.Line, $"話者 {st.Name} は @char で定義されていません");
                        speaker = -1;
                    }
                }

                CheckText(st.Line, st.Text, _rules.MaxTextLength, "文");
                return Instruction.NewText(st.Line, speaker, st.Text, id);
            }

            private Instruction Show(Statement st)
            {
                var ch = Character(st.Line, st.Name);
                if (ch >= 0 && !_catalog.HasExpression(st.Name, st.Expression))
                {
                    Error(st.Line, _catalog.HasCharacter(st.Name) ? $"{st.Name} の表情 {st.Expression} は素材にありません" : $"人物 {st.Name} の立ち絵は素材にありません");
                }

                StageSlot slot;
                switch (st.Slot)
                {
                    case "left": slot = StageSlot.Left; break;
                    case "center": slot = StageSlot.Center; break;
                    case "right": slot = StageSlot.Right; break;
                    default:
                        Error(st.Line, $"位置 {st.Slot} は left・center・right のどれかです");
                        slot = StageSlot.Center;
                        break;
                }

                return Instruction.NewShow(st.Line, ch, st.Expression, slot);
            }

            private int Character(int line, string id)
            {
                if (_charIndex.TryGetValue(id, out var i)) { return i; }
                Error(line, $"人物 {id} は @char で定義されていません");
                return -1;
            }

            private int Target(int line, string label)
            {
                if (_labels.TryGetValue(label, out var pc)) { return pc; }
                Error(line, $"存在しないラベル {label} を参照しています");
                return 0;
            }

            private int Variable(int line, string name, out VariableType type)
            {
                if (_varIndex.TryGetValue(name, out var i))
                {
                    type = _vars[i].Type;
                    return i;
                }

                Error(line, $"変数 {name} は $var で宣言されていません");
                type = VariableType.Number;
                return -1;
            }

            private Assignment Assign(int line, RawAssign a)
            {
                var v = Variable(line, a.Variable, out var type);
                var op = a.Op switch
                {
                    "+=" => AssignOp.Add,
                    "-=" => AssignOp.Subtract,
                    _ => AssignOp.Set,
                };
                if (v >= 0)
                {
                    if (type == VariableType.Flag && (op != AssignOp.Set || !a.Value.IsBool)) { Error(line, $"bool の変数 {a.Variable} には「= true/false」だけを書きます"); }
                    if (type == VariableType.Number && a.Value.IsBool) { Error(line, $"int の変数 {a.Variable} に true/false は入れられません"); }
                    if (!a.Value.IsBool && Math.Abs((long)a.Value.Value) > _rules.ValueLimit) { Error(line, $"値は ±{_rules.ValueLimit} まで（{a.Value.Value}）"); }
                }

                return new Assignment(Math.Max(v, 0), op, a.Value.Value);
            }

            private Condition Condition(Statement st)
            {
                var v = Variable(st.Line, st.CondVariable, out var type);
                if (st.CondOp.Length == 0)
                {
                    if (v >= 0 && type != VariableType.Flag) { Error(st.Line, $"int の変数 {st.CondVariable} は比較（>= など）で条件にします"); }
                    return new Condition(Math.Max(v, 0), CompareOp.Equal, st.CondNegate ? 0 : 1);
                }

                var op = st.CondOp switch
                {
                    "==" => CompareOp.Equal,
                    "!=" => CompareOp.NotEqual,
                    "<" => CompareOp.Less,
                    "<=" => CompareOp.LessOrEqual,
                    ">" => CompareOp.Greater,
                    _ => CompareOp.GreaterOrEqual,
                };
                if (v >= 0)
                {
                    if (type == VariableType.Flag && (op is not (CompareOp.Equal or CompareOp.NotEqual) || !st.CondValue.IsBool))
                    {
                        Error(st.Line, $"bool の変数 {st.CondVariable} は「{st.CondVariable}」「!{st.CondVariable}」か == / != true/false で条件にします");
                    }

                    if (type == VariableType.Number && st.CondValue.IsBool) { Error(st.Line, $"int の変数 {st.CondVariable} と true/false は比べられません"); }
                    if (!st.CondValue.IsBool && Math.Abs((long)st.CondValue.Value) > _rules.ValueLimit) { Error(st.Line, $"値は ±{_rules.ValueLimit} まで（{st.CondValue.Value}）"); }
                }

                return new Condition(Math.Max(v, 0), op, st.CondValue.Value);
            }

            private Instruction Choice(Statement st)
            {
                if (st.Options.Count < _rules.MinOptions || st.Options.Count > _rules.MaxOptions)
                {
                    Error(st.Line, $"選択肢は {_rules.MinOptions}〜{_rules.MaxOptions} 個（{st.Options.Count}）");
                }

                var options = new List<ChoiceOption>();
                foreach (var o in st.Options)
                {
                    CheckText(o.Line, o.Text, _rules.MaxOptionLength, "選択肢");
                    var assigns = new List<Assignment>();
                    foreach (var a in o.Assignments) { assigns.Add(Assign(o.Line, a)); }
                    options.Add(new ChoiceOption(o.Text, Target(o.Line, o.Target), assigns.AsReadOnly()));
                }

                return Instruction.NewChoice(st.Line, new ReadOnlyCollection<ChoiceOption>(options));
            }

            private void CheckName(int line, string what, string text)
            {
                if (text.Length > _rules.MaxNameLength) { Error(line, $"{what}は {_rules.MaxNameLength} 文字まで（{text.Length}）"); }
                CheckChars(line, text, what);
            }

            private void CheckText(int line, string text, int max, string what)
            {
                if (text.Length == 0) { Error(line, $"{what}が空です"); return; }
                if (text.Length > max) { Error(line, $"{what}は {max} 文字まで（{text.Length}）"); }
                CheckChars(line, text, what);
            }

            /// <summary>制御文字と、UI のリッチテキストのタグになる &lt; &gt; を拒否する。</summary>
            private void CheckChars(int line, string text, string what)
            {
                foreach (var ch in text)
                {
                    if (ch < ' ' || ch == '\u007f') { Error(line, $"{what}に制御文字（U+{(int)ch:X4}）があります"); return; }
                    if (ch is '<' or '>') { Error(line, $"{what}に < > は使えません"); return; }
                }

                var open = 0;
                foreach (var ch in text)
                {
                    if (ch == '「') { open++; }
                    else if (ch == '」' && --open < 0) { break; }
                }

                if (open != 0) { Error(line, $"{what}の「」が対応していません"); }
            }

            private void Error(int line, string message) => _errors.Add(new ScriptError(line, message));
        }
    }
}
