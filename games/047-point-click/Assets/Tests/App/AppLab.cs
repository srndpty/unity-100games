using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Game.Core;
using Game.Core.Analysis;
using Game.Core.Play;
using Game.Core.Rules;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>App のテストの道具。本番のルール表と解答手順を使う。</summary>
    internal static class AppLab
    {
        private static RuleBook? _book;

        public static RuleBook Book
        {
            get
            {
                if (_book != null) { return _book; }
                var r = Adventure.Compile(DataText("Rules.txt"));
                return _book = r.Book ?? throw new InvalidOperationException(string.Join("\n", r.Errors));
            }
        }

        public static Move[] Walkthrough => Adventure.ParseWalkthrough(Book, DataText("Walkthrough.txt")).ToArray();

        public static string DataText(string name, [CallerFilePath] string source = "")
        {
            var dir = Path.GetDirectoryName(source) ?? ".";
            return File.ReadAllText(Path.GetFullPath(Path.Combine(dir, "..", "..", "Game", "Data", name)));
        }

        public static AdventureFlow NewFlow(FlowSettings? settings = null) => new(Book, new WorldSettings(), settings);

        /// <summary>タイトルから「はじめから」で遊ぶ画面へ。</summary>
        public static AdventureFlow Playing(FlowSettings? settings = null)
        {
            var f = NewFlow(settings);
            f.Press(IntentKind.Confirm);
            f.FlushIntents();
            Assert.That(f.Screen, Is.EqualTo(FlowScreen.Playing));
            return f;
        }

        public static int H(string id) => Book.HotspotIndex(id);

        public static int I(string id) => Book.ItemIndex(id);

        /// <summary>物の矩形の中心をクリックする。</summary>
        public static void ClickHotspot(AdventureFlow f, string id)
        {
            var a = Book.Hotspots[H(id)].Area;
            f.ClickWorld((a.MinX + a.MaxX) / 2, (a.MinY + a.MaxY) / 2);
        }

        public static void ClickVerb(AdventureFlow f, Verb verb) => f.Click(ClickTarget.Verb, AdventureFlow.Verbs.IndexOf(verb));

        public static void ClickItem(AdventureFlow f, string id) => f.Click(ClickTarget.Item, f.Inventory.ToList().IndexOf(I(id)));

        /// <summary>歩き終えて台詞が出るまで（または上限まで）Tick を進める。</summary>
        public static void Settle(AdventureFlow f, int maxTicks = 2000)
        {
            f.FlushIntents();
            for (int i = 0; i < maxTicks && f.IsWalking; i++) { f.StepTicks(1); }
        }

        /// <summary>台詞を全て送る。</summary>
        public static void SkipLines(AdventureFlow f)
        {
            for (int i = 0; i < 100 && f.CurrentLine != null; i++)
            {
                f.Press(IntentKind.Confirm);
                f.FlushIntents();
            }
        }

        /// <summary>解答手順の最初の n 手まで進める（台詞は送り切る）。</summary>
        public static AdventureFlow After(int moves)
        {
            var f = Playing();
            var pilot = new Autopilot(Walkthrough.Take(moves).ToArray());
            for (int i = 0; i < 100000 && !(pilot.Done && f.IsIdle); i++)
            {
                pilot.Drive(f);
                f.StepTicks(1);
            }

            SkipLines(f);
            return f;
        }

        /// <summary>配列の定数を引数に直接書かない（CA1861）ための道具。</summary>
        public static string[] Of(params string[] items) => items;
    }
}
