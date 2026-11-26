using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Game.Core.Runtime;
using Game.Core.Script;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>テストの道具。小さなテスト用スクリプトと素材の表。</summary>
    internal static class Lab
    {
        /// <summary>テスト用の素材（背景 2・人物 2）。</summary>
        public static ScriptCatalog Catalog { get; } = new(
            new[] { "hall", "room" },
            new[]
            {
                new KeyValuePair<string, string>("mio", "calm"),
                new KeyValuePair<string, string>("mio", "smile"),
                new KeyValuePair<string, string>("ren", "calm"),
            });

        /// <summary>
        /// 選択肢 1 箇所・結末 3 つ。0：鍵を調べる → good、1：澪を信じる → normal、2：何もしない → bad。
        /// </summary>
        public const string Small = @"# テスト用
$var clue = 0
$var trusted = false
@char mio 澪 #8fc3e0
@char ren 蓮 #e0b08f

*start
@bg hall
@show mio calm left
mio「こんばんは」
扉が閉まる音がした。
?choice
- 鍵を調べる -> look_key { clue += 1 }
- 澪を信じる -> trust { trusted = true }
- 何もしない -> verdict
?end

*look_key
@show ren calm right
鍵は冷たかった。
-> verdict

*trust
@show mio smile left
mio「ありがとう」
-> verdict

*verdict
$if clue >= 1 -> good
$if trusted -> normal
@ending bad 闇
*good
@ending good 光
*normal
@ending normal 灰
";

        public static ScriptRules Rules() => new();

        /// <summary>配列の定数を引数に直接書かない（CA1861）ための道具。</summary>
        public static string[] Of(params string[] items) => items;

        public static int[] Ints(params int[] items) => items;

        public static CompileResult Compile(string source, ScriptRules? rules = null) =>
            ScriptCompiler.Compile(source, Catalog, rules ?? Rules());

        public static CompiledScript Script(string source = Small)
        {
            var r = Compile(source);
            Assert.That(r.Ok, Is.True, r.Report());
            return r.Script!;
        }

        /// <summary>コンパイルが失敗し、指定の行に指定の文を含む誤りがあることを確かめる。</summary>
        public static void AssertError(string source, int line, string fragment, ScriptRules? rules = null)
        {
            var r = Compile(source, rules);
            Assert.That(r.Ok, Is.False, "誤りが見つからない");
            Assert.That(r.Script, Is.Null);
            var hit = r.Errors.Any(e => e.Line == line && e.Message.Contains(fragment, System.StringComparison.Ordinal));
            Assert.That(hit, Is.True, $"行 {line} に「{fragment}」がない：\n{r.Report()}");
        }

        /// <summary>Small の 1 行を置き換えたスクリプト（行番号は 1 から）。</summary>
        public static string Replace(int line, string text, string source = Small)
        {
            var lines = source.Replace("\r", "").Split('\n');
            lines[line - 1] = text;
            return string.Join("\n", lines);
        }

        /// <summary>Small の行番号（1 から）を、その行の文字列から求める。</summary>
        public static int LineOf(string text, string source = Small)
        {
            var lines = source.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i] == text) { return i + 1; }
            }

            Assert.Fail($"行「{text}」がない");
            return 0;
        }

        public static string DataText(string name, [CallerFilePath] string source = "")
        {
            var dir = Path.GetDirectoryName(source) ?? ".";
            return File.ReadAllText(Path.GetFullPath(Path.Combine(dir, "..", "..", "Game", "Data", name)));
        }

        public static string ReadmeText([CallerFilePath] string source = "")
        {
            var dir = Path.GetDirectoryName(source) ?? ".";
            return File.ReadAllText(Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "README.md")));
        }

        /// <summary>文は送り、選択肢は列から選んで結末まで進める。</summary>
        public static ScriptRunner Play(CompiledScript script, params int[] choices)
        {
            var r = new ScriptRunner(script);
            var next = 0;
            while (r.Phase != RunnerPhase.Ended)
            {
                var result = r.Phase == RunnerPhase.Text ? r.Advance() : r.Choose(choices[next++]);
                Assert.That(result.Accepted, Is.True, result.Reason);
            }

            Assert.That(next, Is.EqualTo(choices.Length), "選択の列が余った");
            return r;
        }
    }
}
