using System;
using System.IO;
using System.Runtime.CompilerServices;
using Game.Core.Data;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>App のテストの道具。</summary>
    internal static class AppLab
    {
        private static SummerBook? _book;

        /// <summary>本番の台本。</summary>
        public static SummerBook Book
        {
            get
            {
                if (_book == null)
                {
                    var r = SummerCompiler.Compile(DataText("Summer.txt"));
                    Assert.That(r.Errors, Is.Empty, string.Join("\n", r.Errors));
                    _book = r.Book!;
                }

                return _book;
            }
        }

        public static string DataText(string name, [CallerFilePath] string source = "")
        {
            var dir = Path.GetDirectoryName(source) ?? ".";
            return File.ReadAllText(Path.GetFullPath(Path.Combine(dir, "..", "..", "Game", "Data", name)));
        }

        /// <summary>文字送りなしの設定（テストを短くする）。</summary>
        public static FlowSettings Instant => new() { TicksPerChar = 0 };

        public static SummerFlow Flow(MemorySaveStore? store = null, int seed = 1, FlowSettings? settings = null) =>
            new(Book, store ?? new MemorySaveStore(), seed, settings ?? Instant);

        /// <summary>操縦役で画面の流れを結果画面まで進める（クリックした命令は実行されたことを確かめる）。</summary>
        public static void DriveToResult(SummerFlow flow, Autopilot pilot, int maxSteps = 5000)
        {
            for (int i = 0; i < maxSteps && flow.Screen != FlowScreen.Result; i++)
            {
                var expected = pilot.Drive(flow);
                flow.StepTicks(1);
                if (expected >= 0) { Assert.That(flow.Commands, Is.EqualTo(expected), "送った命令が実行された"); }
            }

            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Result));
        }

        public static void Tap(SummerFlow flow, InputButton button)
        {
            flow.Press(button);
            flow.FlushIntents();
        }

        public static void ClickNow(SummerFlow flow, int index)
        {
            flow.Click(index);
            flow.FlushIntents();
        }

        public static int Count(string s, string part)
        {
            var n = 0;
            for (int i = s.IndexOf(part, StringComparison.Ordinal); i >= 0; i = s.IndexOf(part, i + 1, StringComparison.Ordinal)) { n++; }
            return n;
        }
    }
}
