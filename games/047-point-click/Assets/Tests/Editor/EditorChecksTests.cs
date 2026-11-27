#nullable enable
using System;
using Game.Core;
using Game.Core.Rules;
using Game.Unity.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Unity.EditorTests
{
    /// <summary>ビルドの前の検証フックと、Stage の Prefab とルール表の照合。</summary>
    public sealed class EditorChecksTests
    {
        private const string PrefabPath = "Assets/Game/Unity/Prefabs/RoomStage.prefab";

        [TearDown]
        public void TearDown() => RulesBuildCheck.SourceOverride = null;

        private static string RealRules => AssetDatabase.LoadAssetAtPath<TextAsset>(RulesBuildCheck.RulesPath).text;

        private static RuleBook Book => Adventure.Compile(RealRules).Book!;

        [Test]
        public void RealRulesPassTheBuildCheck()
        {
            Assert.That(RulesBuildCheck.Check(), Is.Null);
            Assert.DoesNotThrow(() => new RulesBuildCheck().OnPreprocessBuild(null!));
        }

        [Test]
        public void MisspelledNameStopsTheBuildWithLineNumber()
        {
            var source = RealRules;
            var lines = source.Replace("\r", "", StringComparison.Ordinal).Split('\n');
            var line = Array.FindIndex(lines, l => l.StartsWith("use knife on dock.net", StringComparison.Ordinal)) + 1;
            Assert.That(line, Is.GreaterThan(0));
            RulesBuildCheck.SourceOverride = source.Replace("use knife on dock.net", "use knife on dock.nett", StringComparison.Ordinal);
            var e = Assert.Throws<BuildFailedException>(() => new RulesBuildCheck().OnPreprocessBuild(null!));
            Assert.That(e!.Message.Contains("対象 dock.nett", StringComparison.Ordinal), Is.True, e.Message);
            Assert.That(e.Message.Contains($"行 {line}：", StringComparison.Ordinal), Is.True, e.Message);
        }

        [Test]
        public void DeadEndStopsTheBuild()
        {
            RulesBuildCheck.SourceOverride = RealRules + "\nuse bottle on dock.sea : lose bottle ; 「瓶を海に投げ捨てた。」\n";
            var error = RulesBuildCheck.Check();
            Assert.That(error, Is.Not.Null);
            Assert.That(error!.Contains("詰み", StringComparison.Ordinal), Is.True, error);
        }

        [Test]
        public void SettingsAssetIsValid()
        {
            var settings = AssetDatabase.LoadAssetAtPath<AdventureSettings>(AdventureSceneBuilder.SettingsPath);
            Assert.That(settings, Is.Not.Null);
            Assert.DoesNotThrow(settings.Validate);
        }

        [Test]
        public void StagePrefabMatchesTheRules()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<RoomStage>().Mismatch(Book), Is.Null);
        }

        [Test]
        public void StageMismatch_DetectsMovedResizedAndReorderedObjects()
        {
            var book = Book;
            var root = AdventureSceneBuilder.CreateStage(book);
            try
            {
                var stage = root.GetComponent<RoomStage>();
                Assert.That(stage.Mismatch(book), Is.Null);

                var knife = stage.Hotspots[book.HotspotIndex("cottage.knife")];
                var saved = knife.transform.localPosition;
                knife.transform.localPosition = saved + new Vector3(0.05f, 0f, 0f);
                Assert.That(stage.Mismatch(book), Does.Contain("cottage.knife の位置"));
                knife.transform.localPosition = saved;

                var scale = knife.transform.localScale;
                knife.transform.localScale = scale * 1.1f;
                Assert.That(stage.Mismatch(book), Does.Contain("cottage.knife の大きさ"));
                knife.transform.localScale = scale;

                var order = knife.sortingOrder;
                knife.sortingOrder = -200;
                Assert.That(stage.Mismatch(book), Does.Contain("背景より奥"));
                knife.sortingOrder = order;
                Assert.That(stage.Mismatch(book), Is.Null);

                // ルール表の側で物を 1 つ動かしても、古い Prefab は合わないと分かる。
                var moved = Adventure.Compile(RealRules.Replace("rect 740 470 860 520 stand 800", "rect 745 470 865 520 stand 800", StringComparison.Ordinal)).Book!;
                Assert.That(stage.Mismatch(moved), Does.Contain("cottage.knife の位置"));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
