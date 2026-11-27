#nullable enable
using Game.Unity.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Game.Unity.EditorTests
{
    /// <summary>ビルドの前の検証：設定アセットは通り、範囲の外の設定はビルドを止める。</summary>
    public sealed class SoccerBuildCheckTests
    {
        [Test]
        public void SettingsAsset_PassesTheBuildCheck()
        {
            Assert.That(SoccerBuildCheck.Check(), Is.Null);
            Assert.DoesNotThrow(() => new SoccerBuildCheck().OnPreprocessBuild(null!));
            var settings = AssetDatabase.LoadAssetAtPath<SoccerSettings>(SoccerBuildCheck.SettingsPath);
            Assert.That(settings.ToConfig().Validate(), Is.Empty);
        }

        [Test]
        public void OutOfRangeSettings_AreReportedWithTheField()
        {
            var bad = ScriptableObject.CreateInstance<SoccerSettings>();
            try
            {
                bad.MatchMinutes = 0;
                bad.ShotSpeed = 5000;
                var error = SoccerBuildCheck.Check(bad);
                Assert.That(error, Does.Contain("MatchTicks"));
                Assert.That(error, Does.Contain("ShotSpeed"));
                bad.MatchMinutes = 5;
                bad.ShotSpeed = 430;
                bad.Volume = float.NaN;
                Assert.That(SoccerBuildCheck.Check(bad), Does.Contain("Volume"));
            }
            finally
            {
                Object.DestroyImmediate(bad);
            }
        }
    }
}
