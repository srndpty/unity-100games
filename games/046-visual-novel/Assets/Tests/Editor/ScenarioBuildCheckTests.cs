#nullable enable
using System;
using Game.Unity.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Game.Unity.EditorTests
{
    /// <summary>題材固有：存在しないラベル参照をビルド時に検出する（ビルドの前の検証フック）。</summary>
    public sealed class ScenarioBuildCheckTests
    {
        [TearDown]
        public void TearDown() => ScenarioBuildCheck.SourceOverride = null;

        private static string RealScenario => AssetDatabase.LoadAssetAtPath<TextAsset>(ScenarioBuildCheck.ScenarioPath).text;

        [Test]
        public void RealScenarioPassesTheBuildCheck()
        {
            Assert.That(ScenarioBuildCheck.Check(), Is.Null);
            Assert.DoesNotThrow(() => new ScenarioBuildCheck().OnPreprocessBuild(null!));
        }

        [Test]
        public void MissingLabelStopsTheBuildWithLineNumber()
        {
            var source = RealScenario;
            var line = Array.IndexOf(source.Replace("\r", "", StringComparison.Ordinal).Split('\n'), "-> hall_meeting") + 1;
            Assert.That(line, Is.GreaterThan(0));
            ScenarioBuildCheck.SourceOverride = source.Replace("-> hall_meeting", "-> hall_meetng", StringComparison.Ordinal);
            var e = Assert.Throws<BuildFailedException>(() => new ScenarioBuildCheck().OnPreprocessBuild(null!));
            Assert.That(e!.Message, Does.Contain("存在しないラベル hall_meetng"));
            Assert.That(e.Message, Does.Contain($"行 {line}："));
        }

        [Test]
        public void SettingsAssetIsValidAndMatchesTheRules()
        {
            var settings = AssetDatabase.LoadAssetAtPath<NovelSettings>(ScenarioBuildCheck.SettingsPath);
            Assert.That(settings, Is.Not.Null);
            Assert.DoesNotThrow(settings.Validate);
        }

        [Test]
        public void TooLongLineStopsTheBuildWhenTheLimitShrinks()
        {
            // 設定の上限字数を下げると、本番シナリオの長い文がビルドを止める（設定と検証が同じ値を使う）。
            var settings = AssetDatabase.LoadAssetAtPath<NovelSettings>(ScenarioBuildCheck.SettingsPath);
            var saved = settings.MaxTextLength;
            try
            {
                settings.MaxTextLength = 20;
                Assert.That(ScenarioBuildCheck.Check(), Does.Contain("文は 20 文字まで"));
            }
            finally
            {
                settings.MaxTextLength = saved;
            }
        }
    }
}
