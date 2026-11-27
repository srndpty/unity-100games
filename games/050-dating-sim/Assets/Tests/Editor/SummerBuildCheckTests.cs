#nullable enable
using System;
using Game.Unity.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Game.Unity.EditorTests
{
    /// <summary>ビルドの前の検証：本番の台本は通り、壊した台本は行番号付きでビルドを止める。</summary>
    public sealed class SummerBuildCheckTests
    {
        [TearDown]
        public void TearDown() => SummerBuildCheck.SourceOverride = null;

        private static string RealBook => AssetDatabase.LoadAssetAtPath<TextAsset>(SummerBuildCheck.BookPath).text;

        [Test]
        public void RealBookPassesTheBuildCheck()
        {
            Assert.That(SummerBuildCheck.Check(), Is.Null);
            Assert.DoesNotThrow(() => new SummerBuildCheck().OnPreprocessBuild(null!));
        }

        [Test]
        public void UndeclaredStatStopsTheBuildWithLineNumber()
        {
            const string From = "@event e_sumi_roof slot night day 5..28 priority 50 chance 40 once when sumi >= 25 and met_sumi";
            var source = RealBook;
            var line = Array.IndexOf(source.Replace("\r", "", StringComparison.Ordinal).Split('\n'), From) + 1;
            Assert.That(line, Is.GreaterThan(0));
            SummerBuildCheck.SourceOverride = source.Replace(From, From.Replace("sumi >= 25", "sumii >= 25", StringComparison.Ordinal), StringComparison.Ordinal);
            var e = Assert.Throws<BuildFailedException>(() => new SummerBuildCheck().OnPreprocessBuild(null!));
            Assert.That(e!.Message.Contains("sumii は宣言されていない能力値です", StringComparison.Ordinal), Is.True, e.Message);
            Assert.That(e.Message.Contains($"行 {line}：", StringComparison.Ordinal), Is.True, e.Message);
        }

        [Test]
        public void ImpossibleEndingStopsTheBuild()
        {
            SummerBuildCheck.SourceOverride = RealBook.Replace("when sumi >= 70 and sense >= 50", "when sumi >= 70 and sense >= 101", StringComparison.Ordinal);
            Assert.That(SummerBuildCheck.Check(), Does.Contain("ED sumi の条件は、取りうる能力値の範囲では成り立ちません"));
        }

        [Test]
        public void SettingsAssetIsValid()
        {
            var settings = AssetDatabase.LoadAssetAtPath<SummerSettings>(SummerBuildCheck.SettingsPath);
            Assert.That(settings, Is.Not.Null);
            Assert.DoesNotThrow(settings.Validate);
        }
    }
}
