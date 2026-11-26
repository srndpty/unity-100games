using System.IO;
using System.Runtime.CompilerServices;
using Game.Core;
using Game.Core.Runtime;
using Game.Core.Script;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>App のテストの道具。本番シナリオで Flow を作る。</summary>
    internal static class AppLab
    {
        private static CompileResult? _compiled;

        public static CompileResult Compiled
        {
            get
            {
                if (_compiled == null)
                {
                    _compiled = Scenario.Compile(DataText("Scenario.txt"));
                    Assert.That(_compiled.Ok, Is.True, _compiled.Report());
                }

                return _compiled;
            }
        }

        public static CompiledScript Script => Compiled.Script!;

        public static int[] Route(string ending)
        {
            var r = Scenario.RouteTo(Compiled, ending);
            var a = new int[r.Count];
            for (int i = 0; i < a.Length; i++) { a[i] = r[i]; }
            return a;
        }

        public static NovelFlow NewFlow(MemorySaveStore? quick = null, MemorySaveStore? read = null, FlowSettings? settings = null) =>
            new(Script, quick ?? new MemorySaveStore(), read ?? new MemorySaveStore(), settings);

        /// <summary>はじめから始めた Flow。</summary>
        public static NovelFlow Started(MemorySaveStore? quick = null, MemorySaveStore? read = null, FlowSettings? settings = null)
        {
            var f = NewFlow(quick, read, settings);
            f.Click(NovelFlow.TitleNew);
            f.FlushIntents();
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Reading));
            return f;
        }

        /// <summary>文を全文出して送る（決定 2 回：全文 → 送る）。</summary>
        public static void Next(NovelFlow f)
        {
            if (!f.IsRevealed) { f.Tap(InputButton.Confirm); }
            f.Tap(InputButton.Confirm);
            f.FlushIntents();
        }

        /// <summary>次の選択肢まで送る。</summary>
        public static void ToChoice(NovelFlow f)
        {
            for (int i = 0; i < 10000 && !f.IsChoice; i++) { Next(f); }
            Assert.That(f.IsChoice, Is.True);
        }

        public static void Choose(NovelFlow f, int option)
        {
            f.Click(option);
            f.FlushIntents();
        }

        public static string DataText(string name, [CallerFilePath] string source = "")
        {
            var dir = Path.GetDirectoryName(source) ?? ".";
            return File.ReadAllText(Path.GetFullPath(Path.Combine(dir, "..", "..", "Game", "Data", name)));
        }
    }
}
