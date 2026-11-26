using System;
using System.Linq;
using Game.Core.Runtime;
using Game.Core.Script;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>本番シナリオ：検証・全経路の結末・固定ハッシュ。</summary>
    public sealed class ScenarioTests
    {
        private static CompileResult? _compiled;

        internal static CompileResult Compiled => _compiled ??= Scenario.Compile(Lab.DataText("Scenario.txt"));

        internal static CompiledScript Script => Compiled.Script!;

        [Test]
        public void ScenarioPassesValidation()
        {
            Assert.That(Compiled.Ok, Is.True, Compiled.Report());
            Assert.That(Script.ChoiceCount, Is.EqualTo(3));
            Assert.That(Script.Endings.Select(e => e.Id), Is.EquivalentTo(Lab.Of("truth", "normal", "bad")));
        }

        [Test]
        public void EveryPathEndsAndEveryEndingIsReached()
        {
            var paths = Compiled.Paths;
            Assert.That(paths.Count, Is.EqualTo(27), "3 箇所 × 3 択");
            Assert.That(paths.All(p => p.Choices.Count == 3), Is.True);
            var byEnding = paths.GroupBy(p => Script.Endings[p.Ending].Id).ToDictionary(g => g.Key, g => g.Count());
            Assert.That(byEnding["truth"], Is.EqualTo(2));
            Assert.That(byEnding["normal"], Is.EqualTo(5));
            Assert.That(byEnding["bad"], Is.EqualTo(20));
        }

        [Test]
        public void EveryChoiceMatters()
        {
            // どの箇所でも、選び方を 1 つ変えるだけで結末が変わる組がある（無意味な選択肢を作らない）。
            var ending = Compiled.Paths.ToDictionary(p => p.ChoicesText, p => p.Ending);
            for (int at = 0; at < 3; at++)
            {
                var matters = Compiled.Paths.Any(p => Enumerable.Range(0, 3).Any(alt =>
                {
                    var c = p.Choices.ToArray();
                    c[at] = alt;
                    return ending[string.Join(",", c)] != p.Ending;
                }));
                Assert.That(matters, Is.True, $"選択肢 {at + 1} は結末を変えない");
            }
        }

        [Test]
        public void ReadmeListsTheEndingDistribution()
        {
            var readme = Lab.ReadmeText();
            foreach (var p in Compiled.Paths.Where(p => Script.Endings[p.Ending].Id != "bad"))
            {
                var row = $"| {string.Join(" → ", p.Choices.Select(c => c + 1))} | {Script.Endings[p.Ending].Name} |";
                Assert.That(readme.Contains(row, StringComparison.Ordinal), Is.True, $"README に {row} がない");
            }
        }

        [Test]
        public void RouteToEachEndingReplays()
        {
            foreach (var id in Lab.Of("truth", "normal", "bad"))
            {
                var route = Scenario.RouteTo(Compiled, id);
                var r = Lab.Play(Script, route.ToArray());
                Assert.That(Script.Endings[r.EndingIndex].Id, Is.EqualTo(id));
            }
        }

        [Test]
        public void FixedHashesMatchOnDotnetAndMono()
        {
            // 同じスクリプトと選択の列なら同じ状態。値は .NET と Mono で同じであることを確かめた固定値。
            Assert.That(SaveHex(Script.Fingerprint), Is.EqualTo(Expected.Fingerprint));
            Assert.That(SaveHex(Lab.Play(Script, Scenario.RouteTo(Compiled, "truth").ToArray()).Hash()), Is.EqualTo(Expected.Truth));
            Assert.That(SaveHex(Lab.Play(Script, Scenario.RouteTo(Compiled, "normal").ToArray()).Hash()), Is.EqualTo(Expected.Normal));
            Assert.That(SaveHex(Lab.Play(Script, Scenario.RouteTo(Compiled, "bad").ToArray()).Hash()), Is.EqualTo(Expected.Bad));
        }

        [Test]
        public void ScenarioIsAboutTenMinutes()
        {
            // 1 周の文字数（通った文の合計）。1 分に 400 字を読むとして、文字送りと選ぶ時間を足して 10 分前後。
            var lengths = Compiled.Paths.Select(p => CharsOf(p.Choices.ToArray())).ToList();
            TestContext.WriteLine($"1 周の字数：最小 {lengths.Min()}・最大 {lengths.Max()}、文 {Script.TextIds.Count}");
            Assert.That(lengths.Min(), Is.GreaterThanOrEqualTo(3300));
        }

        [Test]
        public void ScenarioUsesEveryAsset()
        {
            var ins = Script.Instructions;
            foreach (var bg in Scenario.Backgrounds)
            {
                Assert.That(ins.Any(i => i.Kind == InstructionKind.Background && i.Background == bg), Is.True, $"背景 {bg} を使っていない");
            }

            foreach (var p in Scenario.Portraits)
            {
                var c = Script.Characters.ToList().FindIndex(x => x.Id == p.Key);
                Assert.That(ins.Any(i => i.Kind == InstructionKind.Show && i.Character == c && i.Expression == p.Value), Is.True, $"立ち絵 {p.Key} {p.Value} を使っていない");
            }
        }

        [Test]
        public void BrokenScenarioIsRejectedWithLine()
        {
            var src = Lab.DataText("Scenario.txt").Replace("-> hall_meeting", "-> hall_meetng", StringComparison.Ordinal);
            var r = Scenario.Compile(src);
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Errors.All(e => e.Line > 0 && e.Message.Contains("存在しないラベル hall_meetng", StringComparison.Ordinal)), Is.True, r.Report());
        }

        internal static string SaveHex(uint v) => v.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);

        private static int CharsOf(int[] choices)
        {
            var r = new ScriptRunner(Script);
            var total = 0;
            var next = 0;
            while (r.Phase != RunnerPhase.Ended)
            {
                if (r.Phase == RunnerPhase.Text)
                {
                    total += r.Current.Text.Length;
                    r.Advance();
                }
                else
                {
                    r.Choose(choices[next++]);
                }
            }

            return total;
        }

        private static class Expected
        {
            public const string Fingerprint = "FE5A8E61";
            public const string Truth = "41310A48";
            public const string Normal = "82B1D343";
            public const string Bad = "BED96A3E";
        }
    }
}
