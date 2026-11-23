using System;
using System.IO;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>本番の台本（Assets/Game/Patterns）を読む。.NET（テストの出力先）と Unity（プロジェクトの直下）の両方から探す。</summary>
    internal static class TestScripts
    {
        public static BossScript[] Load()
        {
            var directory = Find();
            return new[]
            {
                ScriptParser.ParseOrThrow(File.ReadAllText(Path.Combine(directory, "boss1.txt")), "boss1.txt"),
                ScriptParser.ParseOrThrow(File.ReadAllText(Path.Combine(directory, "boss2.txt")), "boss2.txt"),
            };
        }

        /// <summary>ボス 1 は <paramref name="first"/>、ボス 2 は <paramref name="second"/> の弾幕を 3 つずつ並べた台本の組。</summary>
        public static BossScript[] Of(string first, string second)
        {
            static BossScript Boss(string pattern) =>
                ScriptParser.ParseOrThrow($"boss \"B\" radius=24\n{pattern}\n{pattern}\n{pattern}\n", "test");
            return new[] { Boss(first), Boss(second) };
        }

        private static string Find()
        {
            foreach (var start in new[] { TestContext.CurrentContext.TestDirectory, Environment.CurrentDirectory })
            {
                for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
                {
                    var candidate = Path.Combine(directory.FullName, "Assets", "Game", "Patterns");
                    if (Directory.Exists(candidate)) { return candidate; }
                }
            }

            throw new DirectoryNotFoundException("Assets/Game/Patterns が見つかりません。");
        }
    }
}
