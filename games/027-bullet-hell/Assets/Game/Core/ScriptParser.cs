using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace Game.Core
{
    /// <summary>台本の誤り。行と列は 1 始まり。</summary>
    public readonly struct ScriptError
    {
        public ScriptError(int line, int column, string message)
        {
            Line = line;
            Column = column;
            Message = message;
        }

        public int Line { get; }

        public int Column { get; }

        public string Message { get; }

        public override string ToString() => $"{Line}:{Column}: {Message}";
    }

    public sealed class ScriptParseResult
    {
        internal ScriptParseResult(BossScript? script, IList<ScriptError> errors)
        {
            Script = script;
            var copy = new ScriptError[errors.Count];
            errors.CopyTo(copy, 0);
            Errors = Array.AsReadOnly(copy);
        }

        /// <summary>誤りがなければ台本、あれば null。</summary>
        public BossScript? Script { get; }

        public ReadOnlyCollection<ScriptError> Errors { get; }

        public bool Succeeded => Script != null;
    }

    /// <summary>
    /// 弾幕の台本（行単位の小さな言語）を読む。1 ファイル = ボス 1 体。
    /// <code>
    /// boss "名前" radius=24
    /// pattern "名前" hp=900 time=40s
    ///   move to at=192,360 over=60
    ///   emit ring every=20 count=24 speed=1.8 spin=7 shape=orb color=blue
    /// </code>
    /// <para>
    /// 文 = キーワード（boss / pattern / move / emit）＋頭（名前の文字列、または種類の語）＋ <c>名前=値</c> の並び。
    /// 値は数・語・<c>rand(a, b)</c> で、<c>,</c> で並べると複数の値になる。<c>#</c> から行末は注釈。字下げに意味はない。
    /// 数は浮動小数点を通さずに整数の単位へ直す（<see cref="FixedPoint"/>）。単位：位置・半径は px、速さ・加速は px/Tick、
    /// 角度は度（0 = 右、反時計回り）、時間は Tick（<c>40s</c> のように s を付けると秒）。
    /// </para>
    /// <para>
    /// 誤りは行・列付きで集め、1 つの文で最初の 1 つだけを報告して次の行へ進む。
    /// </para>
    /// </summary>
    public static class ScriptParser
    {
        private static readonly Quantity _tickQuantity = new("Tick", 1, 1, "0", "36000", allowSeconds: true, integerOnly: true);
        private static readonly Quantity _countQuantity = new("発", 1, 1, "1", "256", allowSeconds: false, integerOnly: true);
        private static readonly Quantity _hpQuantity = new("", 1, 1, "1", "100000", allowSeconds: false, integerOnly: true);
        private static readonly Quantity _radiusQuantity = new("px", Field.Unit, 1, "1", "64", allowSeconds: false, integerOnly: false);
        private static readonly Quantity _xQuantity = new("px", Field.Unit, 1, "0", "384", allowSeconds: false, integerOnly: false);
        private static readonly Quantity _yQuantity = new("px", Field.Unit, 1, "0", "448", allowSeconds: false, integerOnly: false);
        private static readonly Quantity _offsetQuantity = new("px", Field.Unit, 1, "-200", "200", allowSeconds: false, integerOnly: false);
        private static readonly Quantity _speedQuantity = new("px/Tick", BulletPool.SpeedOne, 1, "0", "16", allowSeconds: false, integerOnly: false);
        private static readonly Quantity _accelQuantity = new("px/Tick²", BulletPool.SpeedOne, 1, "-1", "1", allowSeconds: false, integerOnly: false);
        private static readonly Quantity _angleQuantity = new("度", Trig.FullTurn, 360, "-360", "360", allowSeconds: false, integerOnly: false);
        private static readonly Quantity _spreadQuantity = new("度", Trig.FullTurn, 360, "0", "360", allowSeconds: false, integerOnly: false);
        private static readonly Quantity _curveQuantity = new("度/Tick", Trig.FullTurn, 360, "-45", "45", allowSeconds: false, integerOnly: false);

        /// <summary>台本を読む。誤りがあれば <see cref="ScriptParseResult.Errors"/> に入れ、台本は null にする。</summary>
        public static ScriptParseResult Parse(string text)
        {
            if (text == null) { throw new ArgumentNullException(nameof(text)); }
            var builder = new Builder();
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var statement = Lexer.ReadStatement(lines[i].TrimEnd('\r'), i + 1, builder.Errors);
                if (statement != null) { builder.Add(statement); }
            }

            return builder.Finish(lines.Length);
        }

        /// <summary>台本を読み、誤りがあれば一覧を載せた <see cref="FormatException"/> にする。</summary>
        public static BossScript ParseOrThrow(string text, string sourceName)
        {
            var result = Parse(text);
            if (result.Script != null) { return result.Script; }

            var message = new StringBuilder();
            message.Append(sourceName).Append(" の台本に誤りがあります。");
            foreach (var error in result.Errors) { message.Append('\n').Append(sourceName).Append(':').Append(error); }
            throw new FormatException(message.ToString());
        }

        private enum TokenKind
        {
            Word,
            Number,
            Text,
            Equals,
            Comma,
            Open,
            Close,
        }

        private enum ValueKind
        {
            Number,
            Word,
            Rand,
        }

        private sealed class Token
        {
            public Token(TokenKind kind, string text, int column)
            {
                Kind = kind;
                Text = text;
                Column = column;
            }

            public TokenKind Kind { get; }

            public string Text { get; }

            public int Column { get; }
        }

        private sealed class Value
        {
            public Value(ValueKind kind, string text, string upper, int column)
            {
                Kind = kind;
                Text = text;
                Upper = upper;
                Column = column;
            }

            public ValueKind Kind { get; }

            /// <summary>数・語の文字列、rand なら下限の文字列。</summary>
            public string Text { get; }

            /// <summary>rand の上限の文字列。</summary>
            public string Upper { get; }

            public int Column { get; }
        }

        private sealed class Param
        {
            public Param(string name, int column, List<Value> values)
            {
                Name = name;
                Column = column;
                Values = values;
            }

            public string Name { get; }

            public int Column { get; }

            public List<Value> Values { get; }

            public bool Used { get; set; }
        }

        private sealed class Statement
        {
            public Statement(string keyword, int line, int column, Token head, List<Param> parameters)
            {
                Keyword = keyword;
                Line = line;
                Column = column;
                Head = head;
                Params = parameters;
            }

            public string Keyword { get; }

            public int Line { get; }

            public int Column { get; }

            public Token Head { get; }

            public List<Param> Params { get; }
        }

        /// <summary>数の単位と範囲。範囲は書いた単位の文字列で持ち、整数の単位へ直して比べる。</summary>
        private sealed class Quantity
        {
            public Quantity(string unit, long numerator, long denominator, string minText, string maxText, bool allowSeconds, bool integerOnly)
            {
                Unit = unit;
                Numerator = numerator;
                Denominator = denominator;
                MinText = minText;
                MaxText = maxText;
                AllowSeconds = allowSeconds;
                IntegerOnly = integerOnly;
                if (!FixedPoint.TryParse(minText, numerator, denominator, out var min) || !FixedPoint.TryParse(maxText, numerator, denominator, out var max))
                {
                    throw new ArgumentException("範囲の書き方が誤っています。");
                }

                Min = min;
                Max = max;
            }

            public string Unit { get; }

            public long Numerator { get; }

            public long Denominator { get; }

            public string MinText { get; }

            public string MaxText { get; }

            public bool AllowSeconds { get; }

            public bool IntegerOnly { get; }

            public int Min { get; }

            public int Max { get; }
        }

        /// <summary>1 行を字句に分け、文の形（キーワード・頭・引数の並び）にする。</summary>
        private static class Lexer
        {
            public static Statement? ReadStatement(string line, int lineNumber, List<ScriptError> errors)
            {
                var tokens = new List<Token>();
                var i = 0;
                while (i < line.Length)
                {
                    var c = line[i];
                    if (c == ' ' || c == '\t') { i++; continue; }
                    if (c == '#') { break; }

                    var start = i;
                    if (IsWordStart(c))
                    {
                        while (i < line.Length && IsWordPart(line[i])) { i++; }
                        tokens.Add(new Token(TokenKind.Word, line.Substring(start, i - start), start + 1));
                    }
                    else if (IsDigit(c) || (c == '-' && i + 1 < line.Length && (IsDigit(line[i + 1]) || line[i + 1] == '.')) || c == '.')
                    {
                        i++;
                        while (i < line.Length && (IsDigit(line[i]) || line[i] == '.')) { i++; }
                        while (i < line.Length && IsWordPart(line[i])) { i++; }
                        tokens.Add(new Token(TokenKind.Number, line.Substring(start, i - start), start + 1));
                    }
                    else if (c == '"')
                    {
                        i++;
                        while (i < line.Length && line[i] != '"') { i++; }
                        if (i >= line.Length)
                        {
                            errors.Add(new ScriptError(lineNumber, start + 1, "文字列が閉じていません。"));
                            return null;
                        }

                        tokens.Add(new Token(TokenKind.Text, line.Substring(start + 1, i - start - 1), start + 1));
                        i++;
                    }
                    else
                    {
                        var kind = c switch
                        {
                            '=' => TokenKind.Equals,
                            ',' => TokenKind.Comma,
                            '(' => TokenKind.Open,
                            ')' => TokenKind.Close,
                            _ => (TokenKind?)null,
                        };
                        if (kind == null)
                        {
                            errors.Add(new ScriptError(lineNumber, start + 1, $"使えない文字 '{c}' があります。"));
                            return null;
                        }

                        tokens.Add(new Token(kind.Value, c.ToString(), start + 1));
                        i++;
                    }
                }

                return tokens.Count == 0 ? null : Shape(tokens, lineNumber, errors);
            }

            private static Statement? Shape(List<Token> tokens, int line, List<ScriptError> errors)
            {
                var keyword = tokens[0];
                if (keyword.Kind != TokenKind.Word)
                {
                    errors.Add(new ScriptError(line, keyword.Column, "行はキーワード（boss / pattern / move / emit）で始めてください。"));
                    return null;
                }

                if (tokens.Count < 2)
                {
                    errors.Add(new ScriptError(line, keyword.Column, $"{keyword.Text} の後に名前か種類を書いてください。"));
                    return null;
                }

                var head = tokens[1];
                var parameters = new List<Param>();
                var i = 2;
                while (i < tokens.Count)
                {
                    var name = tokens[i];
                    if (name.Kind != TokenKind.Word || i + 1 >= tokens.Count || tokens[i + 1].Kind != TokenKind.Equals)
                    {
                        errors.Add(new ScriptError(line, name.Column, "引数は 名前=値 の形で書いてください。"));
                        return null;
                    }

                    i += 2;
                    var values = new List<Value>();
                    while (true)
                    {
                        if (i >= tokens.Count)
                        {
                            errors.Add(new ScriptError(line, name.Column, $"{name.Text} の値がありません。"));
                            return null;
                        }

                        var value = ReadValue(tokens, ref i, line, errors);
                        if (value == null) { return null; }
                        values.Add(value);
                        if (i < tokens.Count && tokens[i].Kind == TokenKind.Comma)
                        {
                            i++;
                            continue;
                        }

                        break;
                    }

                    foreach (var existing in parameters)
                    {
                        if (existing.Name == name.Text)
                        {
                            errors.Add(new ScriptError(line, name.Column, $"{name.Text} が 2 回書かれています。"));
                            return null;
                        }
                    }

                    parameters.Add(new Param(name.Text, name.Column, values));
                }

                return new Statement(keyword.Text, line, keyword.Column, head, parameters);
            }

            private static Value? ReadValue(List<Token> tokens, ref int i, int line, List<ScriptError> errors)
            {
                var token = tokens[i];
                if (token.Kind == TokenKind.Number)
                {
                    i++;
                    return new Value(ValueKind.Number, token.Text, token.Text, token.Column);
                }

                if (token.Kind == TokenKind.Word && token.Text == "rand")
                {
                    // rand ( 数 , 数 )
                    if (i + 5 < tokens.Count
                        && tokens[i + 1].Kind == TokenKind.Open
                        && tokens[i + 2].Kind == TokenKind.Number
                        && tokens[i + 3].Kind == TokenKind.Comma
                        && tokens[i + 4].Kind == TokenKind.Number
                        && tokens[i + 5].Kind == TokenKind.Close)
                    {
                        var value = new Value(ValueKind.Rand, tokens[i + 2].Text, tokens[i + 4].Text, token.Column);
                        i += 6;
                        return value;
                    }

                    errors.Add(new ScriptError(line, token.Column, "rand は rand(下限, 上限) の形で書いてください。"));
                    return null;
                }

                if (token.Kind == TokenKind.Word)
                {
                    i++;
                    return new Value(ValueKind.Word, token.Text, token.Text, token.Column);
                }

                errors.Add(new ScriptError(line, token.Column, "値は数・語・rand(a, b) のどれかにしてください。"));
                return null;
            }

            private static bool IsWordStart(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';

            private static bool IsWordPart(char c) => IsWordStart(c) || IsDigit(c);

            private static bool IsDigit(char c) => c >= '0' && c <= '9';
        }

        /// <summary>文を順に受け取って台本を組み立てる。</summary>
        private sealed class Builder
        {
            private readonly List<PatternSpec> _patterns = new();
            private readonly List<MoveSpec> _moves = new();
            private readonly List<EmitterSpec> _emitters = new();
            private string? _bossName;
            private int _bossRadius;
            private bool _sawBoss;
            private PendingPattern? _pattern;
            private bool _failed;

            public List<ScriptError> Errors { get; } = new();

            public void Add(Statement statement)
            {
                _failed = false;
                switch (statement.Keyword)
                {
                    case "boss":
                        AddBoss(statement);
                        break;
                    case "pattern":
                        AddPattern(statement);
                        break;
                    case "move":
                        AddMove(statement);
                        break;
                    case "emit":
                        AddEmitter(statement);
                        break;
                    default:
                        Fail(statement.Line, statement.Column, $"未知のキーワード {statement.Keyword} です（boss / pattern / move / emit）。");
                        break;
                }
            }

            public ScriptParseResult Finish(int lineCount)
            {
                ClosePattern();
                if (!_sawBoss) { Errors.Add(new ScriptError(1, 1, "boss の行がありません。")); }
                else if (_patterns.Count == 0 && Errors.Count == 0) { Errors.Add(new ScriptError(lineCount, 1, "pattern が 1 つもありません。")); }

                if (Errors.Count > 0 || _bossName == null) { return new ScriptParseResult(null, Errors); }
                return new ScriptParseResult(new BossScript(_bossName, _bossRadius, _patterns), Errors);
            }

            private void AddBoss(Statement s)
            {
                if (_sawBoss)
                {
                    Fail(s.Line, s.Column, "boss は 1 ファイルに 1 つだけにしてください。");
                    return;
                }

                _sawBoss = true;
                var name = TextHead(s);
                var radius = Number(s, "radius", _radiusQuantity, required: true);
                if (!Done(s) || name == null) { return; }
                _bossName = name;
                _bossRadius = radius;
            }

            private void AddPattern(Statement s)
            {
                ClosePattern();
                if (!_sawBoss)
                {
                    Fail(s.Line, s.Column, "pattern の前に boss を書いてください。");
                    return;
                }

                var name = TextHead(s);
                var hp = Number(s, "hp", _hpQuantity, required: true);
                var time = Number(s, "time", _tickQuantity, required: true);
                if (time == 0) { Fail(s.Line, s.Column, "time は 1 Tick 以上にしてください。"); }

                // 誤りのある pattern も中身の文を読み続ける（続く move / emit を「pattern の外」と重ねて報告しない）。
                var valid = Done(s) && name != null;
                _pattern = new PendingPattern(name ?? "?", hp, time, s.Line, s.Column, valid);
            }

            private void AddMove(Statement s)
            {
                if (!InPattern(s)) { return; }
                var kind = WordHead(s, "to", "wait", "wander");
                var target = Vec2i.Zero;
                var area = Field.Bounds;
                if (kind == "to") { target = Point(s, "at"); }
                else if (kind == "wander") { area = Area(s, "area"); }

                var over = Number(s, "over", _tickQuantity, required: true);
                if (over == 0) { Fail(s.Line, s.Column, "over は 1 Tick 以上にしてください。"); }
                if (!Done(s) || kind == null) { return; }

                var moveKind = kind == "to" ? MoveKind.To : kind == "wait" ? MoveKind.Wait : MoveKind.Wander;
                _moves.Add(new MoveSpec(moveKind, target, area, over));
            }

            private void AddEmitter(Statement s)
            {
                if (!InPattern(s)) { return; }
                var kindText = WordHead(s, "ring", "fan", "aimed");
                var kind = kindText == "fan" ? EmitterKind.Fan : kindText == "aimed" ? EmitterKind.Aimed : EmitterKind.Ring;

                var every = Number(s, "every", _tickQuantity, required: true);
                if (every == 0) { Fail(s.Line, s.Column, "every は 1 Tick 以上にしてください。"); }
                var delay = Number(s, "delay", _tickQuantity, required: false);
                var count = Number(s, "count", _countQuantity, required: false, fallback: 1);
                var angle = Range(s, "angle", _angleQuantity, Ranged.Fixed(0));
                var spread = 0;
                if (kind != EmitterKind.Ring) { spread = Number(s, "spread", _spreadQuantity, required: false); }
                var spin = Number(s, "spin", _angleQuantity, required: false);
                var speed = Range(s, "speed", _speedQuantity, null);
                var accel = Range(s, "accel", _accelQuantity, Ranged.Fixed(0));
                var maxSpeed = Number(s, "maxspeed", _speedQuantity, required: false, fallback: _speedQuantity.Max);
                var curve = Range(s, "curve", _curveQuantity, Ranged.Fixed(0));
                var (offsetX, offsetY) = Offset(s, "offset");
                var life = Number(s, "life", _tickQuantity, required: false);
                var shape = Choice(s, "shape", BulletShape.Small);
                var color = Choice(s, "color", BulletColor.Red);
                if (!_failed && speed.Max > maxSpeed)
                {
                    Fail(s.Line, ColumnOf(s, "speed"), "speed は maxspeed 以下にしてください。");
                }

                if (!Done(s)) { return; }
                _emitters.Add(new EmitterSpec(kind, every, delay, count, angle, spread, spin, speed, accel, maxSpeed, curve, offsetX, offsetY, life, shape, color));
            }

            private bool InPattern(Statement s)
            {
                if (_pattern != null) { return true; }
                Fail(s.Line, s.Column, $"{s.Keyword} は pattern の中に書いてください。");
                return false;
            }

            private void ClosePattern()
            {
                if (_pattern == null) { return; }
                if (_pattern.Valid && _emitters.Count == 0)
                {
                    Errors.Add(new ScriptError(_pattern.Line, _pattern.Column, $"pattern \"{_pattern.Name}\" に emit がありません。"));
                }
                else if (_pattern.Valid && Errors.Count == 0)
                {
                    _patterns.Add(new PatternSpec(_pattern.Name, _pattern.Hp, _pattern.Time, _moves, _emitters));
                }

                _pattern = null;
                _moves.Clear();
                _emitters.Clear();
            }

            /// <summary>文の終わり：使われなかった引数を誤りにし、文が誤りなく読めたかを返す。</summary>
            private bool Done(Statement s)
            {
                foreach (var p in s.Params)
                {
                    if (!p.Used) { Fail(s.Line, p.Column, $"{s.Keyword} {s.Head.Text} に {p.Name} は書けません。"); }
                }

                return !_failed;
            }

            private string? TextHead(Statement s)
            {
                if (s.Head.Kind == TokenKind.Text && s.Head.Text.Length > 0) { return s.Head.Text; }
                Fail(s.Line, s.Head.Column, $"{s.Keyword} の後に \"名前\" を書いてください。");
                return null;
            }

            private string? WordHead(Statement s, params string[] allowed)
            {
                if (s.Head.Kind == TokenKind.Word && Array.IndexOf(allowed, s.Head.Text) >= 0) { return s.Head.Text; }
                Fail(s.Line, s.Head.Column, $"{s.Keyword} の種類は {string.Join(" / ", allowed)} のどれかにしてください。");
                return null;
            }

            private Param? Take(Statement s, string name, bool required)
            {
                foreach (var p in s.Params)
                {
                    if (p.Name != name) { continue; }
                    p.Used = true;
                    return p;
                }

                if (required) { Fail(s.Line, s.Column, $"{s.Keyword} に {name} がありません。"); }
                return null;
            }

            private static int ColumnOf(Statement s, string name)
            {
                foreach (var p in s.Params)
                {
                    if (p.Name == name) { return p.Column; }
                }

                return s.Column;
            }

            private int Number(Statement s, string name, Quantity quantity, bool required, int fallback = 0)
            {
                var p = Take(s, name, required);
                if (p == null) { return fallback; }
                if (p.Values.Count != 1)
                {
                    Fail(s.Line, p.Column, $"{name} の値は 1 つにしてください。");
                    return fallback;
                }

                var value = p.Values[0];
                if (value.Kind != ValueKind.Number)
                {
                    Fail(s.Line, value.Column, $"{name} は数にしてください。");
                    return fallback;
                }

                return Convert(s, name, value.Text, value.Column, quantity) ?? fallback;
            }

            private Ranged Range(Statement s, string name, Quantity quantity, Ranged? fallback)
            {
                var p = Take(s, name, required: fallback == null);
                var defaultValue = fallback ?? Ranged.Fixed(0);
                if (p == null) { return defaultValue; }
                if (p.Values.Count != 1)
                {
                    Fail(s.Line, p.Column, $"{name} の値は 1 つにしてください。");
                    return defaultValue;
                }

                return ToRanged(s, name, p.Values[0], quantity) ?? defaultValue;
            }

            private Ranged? ToRanged(Statement s, string name, Value value, Quantity quantity)
            {
                if (value.Kind == ValueKind.Word)
                {
                    Fail(s.Line, value.Column, $"{name} は数か rand(a, b) にしてください。");
                    return null;
                }

                var min = Convert(s, name, value.Text, value.Column, quantity);
                if (min == null) { return null; }
                if (value.Kind == ValueKind.Number) { return Ranged.Fixed(min.Value); }

                var max = Convert(s, name, value.Upper, value.Column, quantity);
                if (max == null) { return null; }
                if (max.Value < min.Value)
                {
                    Fail(s.Line, value.Column, $"{name} の rand は下限を上限以下にしてください。");
                    return null;
                }

                return new Ranged(min.Value, max.Value);
            }

            private Vec2i Point(Statement s, string name)
            {
                var p = Take(s, name, required: true);
                if (p == null) { return Vec2i.Zero; }
                if (p.Values.Count != 2)
                {
                    Fail(s.Line, p.Column, $"{name} は x,y の 2 つの数にしてください。");
                    return Vec2i.Zero;
                }

                var x = PlainNumber(s, name, p.Values[0], _xQuantity);
                var y = PlainNumber(s, name, p.Values[1], _yQuantity);
                return new Vec2i(x, y);
            }

            private Rect Area(Statement s, string name)
            {
                var p = Take(s, name, required: true);
                if (p == null) { return Field.Bounds; }
                if (p.Values.Count != 4)
                {
                    Fail(s.Line, p.Column, $"{name} は x1,y1,x2,y2 の 4 つの数にしてください。");
                    return Field.Bounds;
                }

                var x1 = PlainNumber(s, name, p.Values[0], _xQuantity);
                var y1 = PlainNumber(s, name, p.Values[1], _yQuantity);
                var x2 = PlainNumber(s, name, p.Values[2], _xQuantity);
                var y2 = PlainNumber(s, name, p.Values[3], _yQuantity);
                if (_failed) { return Field.Bounds; }
                if (x2 < x1 || y2 < y1)
                {
                    Fail(s.Line, p.Column, $"{name} は x1 <= x2、y1 <= y2 にしてください。");
                    return Field.Bounds;
                }

                return new Rect(x1, y1, x2, y2);
            }

            private (Ranged X, Ranged Y) Offset(Statement s, string name)
            {
                var none = (Ranged.Fixed(0), Ranged.Fixed(0));
                var p = Take(s, name, required: false);
                if (p == null) { return none; }
                if (p.Values.Count != 2)
                {
                    Fail(s.Line, p.Column, $"{name} は x,y の 2 つの値にしてください。");
                    return none;
                }

                var x = ToRanged(s, name, p.Values[0], _offsetQuantity);
                var y = ToRanged(s, name, p.Values[1], _offsetQuantity);
                return x == null || y == null ? none : (x.Value, y.Value);
            }

            private int PlainNumber(Statement s, string name, Value value, Quantity quantity)
            {
                if (value.Kind != ValueKind.Number)
                {
                    Fail(s.Line, value.Column, $"{name} は数にしてください。");
                    return 0;
                }

                return Convert(s, name, value.Text, value.Column, quantity) ?? 0;
            }

            private T Choice<T>(Statement s, string name, T fallback)
                where T : struct, Enum
            {
                var p = Take(s, name, required: false);
                if (p == null) { return fallback; }
                var names = Enum.GetNames(typeof(T));
                if (p.Values.Count == 1 && p.Values[0].Kind == ValueKind.Word)
                {
                    foreach (var candidate in names)
                    {
                        if (string.Equals(candidate, p.Values[0].Text, StringComparison.OrdinalIgnoreCase))
                        {
                            return Enum.Parse<T>(candidate);
                        }
                    }
                }

                Fail(s.Line, p.Column, $"{name} は {string.Join(" / ", names).ToLowerInvariant()} のどれかにしてください。");
                return fallback;
            }

            /// <summary>数の文字列を単位へ直し、範囲を確かめる。</summary>
            private int? Convert(Statement s, string name, string text, int column, Quantity quantity)
            {
                var seconds = quantity.AllowSeconds && text.EndsWith('s');
                var digits = seconds ? text.Substring(0, text.Length - 1) : text;
                int value;
                if (seconds)
                {
                    // 秒は 10^6 倍の整数にしてから 60 倍し、Tick の整数になるかを確かめる。
                    if (!FixedPoint.TryParse(digits, 1_000_000, 1, out var micro))
                    {
                        Fail(s.Line, column, $"{name} の値 {text} を読めません。");
                        return null;
                    }

                    var scaled = (long)micro * Field.TicksPerSecond;
                    if (scaled % 1_000_000 != 0)
                    {
                        Fail(s.Line, column, $"{name} の秒 {text} は Tick の整数になりません（1/60 秒の倍数にしてください）。");
                        return null;
                    }

                    value = (int)(scaled / 1_000_000);
                }
                else
                {
                    if ((quantity.IntegerOnly && digits.Contains('.', StringComparison.Ordinal)) || !FixedPoint.TryParse(digits, quantity.Numerator, quantity.Denominator, out value))
                    {
                        var kind = quantity.IntegerOnly ? "整数" : "数";
                        Fail(s.Line, column, $"{name} の値 {text} を{kind}として読めません。");
                        return null;
                    }
                }

                if (value < quantity.Min || value > quantity.Max)
                {
                    Fail(s.Line, column, $"{name} は {quantity.MinText}〜{quantity.MaxText}{quantity.Unit} にしてください（{text}）。");
                    return null;
                }

                return value;
            }

            private void Fail(int line, int column, string message)
            {
                if (_failed) { return; }
                _failed = true;
                Errors.Add(new ScriptError(line, column, message));
            }

            private sealed class PendingPattern
            {
                public PendingPattern(string name, int hp, int time, int line, int column, bool valid)
                {
                    Valid = valid;
                    Name = name;
                    Hp = hp;
                    Time = time;
                    Line = line;
                    Column = column;
                }

                public string Name { get; }

                public int Hp { get; }

                public int Time { get; }

                public int Line { get; }

                public int Column { get; }

                /// <summary>pattern の行に誤りがなかったか（誤りがあれば台本に入れない）。</summary>
                public bool Valid { get; }
            }
        }
    }
}
