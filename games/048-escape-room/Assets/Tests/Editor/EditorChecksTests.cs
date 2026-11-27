#nullable enable
using System;
using Game.Core.Geometry;
using Game.Core.Mechanisms;
using Game.Core.Rooms;
using Game.Unity.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Unity.EditorTests
{
    /// <summary>ビルドの前の検証フック（部屋の定義・状態空間・Stage Prefab）と、Stage の Prefab と Core の箱の照合。</summary>
    public sealed class EditorChecksTests
    {
        [TearDown]
        public void TearDown() => RoomBuildCheck.RoomOverride = null;

        [Test]
        public void RealRoomPassesTheBuildCheck()
        {
            Assert.That(RoomBuildCheck.Check(), Is.Null);
            Assert.DoesNotThrow(() => new RoomBuildCheck().OnPreprocessBuild(null!));
        }

        [Test]
        public void DeadEndRoomStopsTheBuild()
        {
            RoomBuildCheck.RoomOverride = () => SmallRoom(keyInsideOwnLock: true);
            var e = Assert.Throws<BuildFailedException>(() => new RoomBuildCheck().OnPreprocessBuild(null!));
            Assert.That(e!.Message.Contains("脱出に到達しません", StringComparison.Ordinal), Is.True, e.Message);
        }

        [Test]
        public void InvalidDefinitionStopsTheBuildWithReason()
        {
            RoomBuildCheck.RoomOverride = () =>
            {
                var b = new RoomBuilder("壊れた部屋", B(-2000, 0, -2000, 2000, 2800, 2000));
                b.Item("key", "鍵", "どこか");
                return b.Build(0);
            };
            var error = RoomBuildCheck.Check();
            Assert.That(error, Is.Not.Null);
            Assert.That(error!.Contains("出口の仕掛けの番号が範囲外", StringComparison.Ordinal), Is.True, error);
        }

        [Test]
        public void SettingsAssetIsValid()
        {
            var settings = AssetDatabase.LoadAssetAtPath<EscapeSettings>(EscapeSceneBuilder.SettingsPath);
            Assert.That(settings, Is.Not.Null);
            Assert.DoesNotThrow(settings.Validate);
            settings = Object.Instantiate(settings);
            try
            {
                settings.Volume = float.NaN;
                Assert.Throws<InvalidOperationException>(settings.Validate);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void StagePrefabMatchesTheRoom()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EscapeSceneBuilder.PrefabPath);
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<StudyStage>().Mismatch(StudyRoom.Create()), Is.Null);
        }

        [Test]
        public void StageMismatch_DetectsMovedResizedAndColliderChanges()
        {
            var def = StudyRoom.Create();
            var root = EscapeSceneBuilder.CreateStage(def);
            try
            {
                var stage = root.GetComponent<StudyStage>();
                Assert.That(stage.Mismatch(def), Is.Null);
                var index = PartIndex(def, "desk.front");
                var part = stage.PartAt(index);

                var saved = part.localPosition;
                part.localPosition = saved + new Vector3(0.01f, 0f, 0f);
                Assert.That(stage.Mismatch(def), Does.Contain("desk.front の位置"));
                part.localPosition = saved;

                var scale = part.localScale;
                part.localScale = scale * 1.1f;
                Assert.That(stage.Mismatch(def), Does.Contain("desk.front の大きさ"));
                part.localScale = scale;

                var collider = part.GetComponent<BoxCollider>();
                collider.size = new Vector3(1f, 2f, 1f);
                Assert.That(stage.Mismatch(def), Does.Contain("BoxCollider"));
                collider.size = Vector3.one;
                Assert.That(stage.Mismatch(def), Is.Null);

                part.name = "desk.frontx";
                Assert.That(stage.Mismatch(def), Does.Contain("名前"));
                part.name = "desk.front";
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static int PartIndex(RoomDef def, string id)
        {
            foreach (var p in def.Parts)
            {
                if (p.Id == id) { return p.Index; }
            }

            throw new ArgumentException(id);
        }

        private static Box3 B(int x0, int y0, int z0, int x1, int y1, int z1) => new(new Vec3i(x0, y0, z0), new Vec3i(x1, y1, z1));

        /// <summary>小さな部屋：ダイヤルの中の鍵でキャビネットを開けると出口の鍵（keyInsideOwnLock なら鍵がキャビネットの中で詰む）。</summary>
        private static RoomDef SmallRoom(bool keyInsideOwnLock)
        {
            var b = new RoomBuilder("テスト", B(-2000, 0, -2000, 2000, 2800, 2000));
            var clue = b.Examinable("note", "メモ", new Vec3i(0, 0, 1), 0);
            var key = b.Item("key", "鍵", "引き出し");
            var exitKey = b.Item("exit_key", "出口の鍵", "キャビネット");
            b.Mechanism("dial", "ダイヤル", MechanismKind.Dial, -1, "a", "b");
            b.Mechanism("cabinet", "キャビネット", MechanismKind.KeyLock, key, "a", "b");
            b.Mechanism("door", "扉", MechanismKind.KeyLock, exitKey, "a", "b");
            for (int w = 0; w < 4; w++) { b.WallView("wall" + w, "壁", new CameraPose(new Vec3i(0, 1600, 0), w, 0, 1500)); }
            var near = b.CloseUp("near", "近く", new CameraPose(new Vec3i(0, 1600, 500), 0, 0, 1500));
            b.Examine("note", "メモ", near, B(-900, 1000, 1500, -800, 1100, 1600), clue);
            for (int i = 0; i < 3; i++) { b.Slot($"dial{i}", "桁", near, B(-600 + (i * 100), 1000, 1500, -520 + (i * 100), 1100, 1600), 0, i); }
            b.Pickup("key", "鍵", near, B(0, 1000, 1500, 100, 1100, 1600), key, keyInsideOwnLock ? 1 : 0);
            b.Keyhole("keyhole", "鍵穴", near, B(300, 1000, 1500, 400, 1100, 1600), 1);
            b.Pickup("exit_key", "出口の鍵", near, B(500, 1000, 1500, 600, 1100, 1600), exitKey, 1);
            b.Keyhole("door", "扉の鍵穴", near, B(700, 1000, 1500, 800, 1100, 1600), 2);
            return b.Build(2);
        }
    }
}
