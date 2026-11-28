#nullable enable
using Game.Unity.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Unity.EditorTests
{
    /// <summary>ビルドの前の検証：設定アセットは通り、範囲の外の設定はビルドを止める。</summary>
    public sealed class SekiteiBuildCheckTests
    {
        [Test]
        public void SettingsAsset_PassesTheBuildCheck()
        {
            Assert.That(SekiteiBuildCheck.Check(), Is.Null);
            Assert.DoesNotThrow(() => new SekiteiBuildCheck().OnPreprocessBuild(null!));
            var settings = AssetDatabase.LoadAssetAtPath<SekiteiSettings>(SekiteiBuildCheck.SettingsPath);
            Assert.That(settings.ToConfig().Validate(), Is.Empty);
            Assert.That(settings.ToMcts().Validate(), Is.Empty);
            Assert.That(settings.ToFlowSettings().Validate(), Is.Empty);
        }

        [Test]
        public void OutOfRangeSettings_AreReportedWithTheField()
        {
            var bad = ScriptableObject.CreateInstance<SekiteiSettings>();
            try
            {
                bad.Komi = 100;
                bad.StrongPlayouts = 0;
                var error = SekiteiBuildCheck.Check(bad);
                Assert.That(error, Does.Contain("Komi"));
                Assert.That(error, Does.Contain("StrongPlayouts"));
                bad.Komi = 7;
                bad.StrongPlayouts = 8000;
                bad.PlayoutsPerTick = 0;
                Assert.That(SekiteiBuildCheck.Check(bad), Does.Contain("PlayoutsPerTick"));
                bad.PlayoutsPerTick = 20;
                bad.ThinkMillisecondsPerFrame = float.NaN;
                Assert.That(SekiteiBuildCheck.Check(bad), Does.Contain("ThinkMillisecondsPerFrame"));
                bad.ThinkMillisecondsPerFrame = 8f;
                bad.Volume = float.PositiveInfinity;
                Assert.That(SekiteiBuildCheck.Check(bad), Does.Contain("Volume"));
                bad.Volume = 0.05f;
                Assert.That(SekiteiBuildCheck.Check(bad), Is.Null, "戻せば通る");
            }
            finally
            {
                Object.DestroyImmediate(bad);
            }
        }
    }
}
