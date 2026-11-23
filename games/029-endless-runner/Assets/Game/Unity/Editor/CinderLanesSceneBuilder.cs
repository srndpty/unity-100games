using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>
    /// シーンと Stage Prefab（道の区画・自機・障害物 3 種の見本）を生成する。Stage Prefab は既にあれば上書きせず、設定と一致するかだけ確かめる。
    /// 実行：<c>unity run games/029-endless-runner -- -executeMethod Game.Unity.Editor.CinderLanesSceneBuilder.Build</c>
    /// </summary>
    public static class CinderLanesSceneBuilder
    {
        private const string Root = "Assets/Game/Unity/";
        private const string Art = CinderLanesArtGenerator.ArtRoot;
        private const string ScenePath = "Assets/Scenes/CinderLanes.unity";
        private const string StagePath = Art + "CinderLanesStage.prefab";
        private const string ObjectPrefix = "CinderLanes";

        /// <summary>空とフォグの色（遠くの出現をフォグで隠す）。</summary>
        public static readonly Color Sky = new(.16f, .07f, .05f);

        [MenuItem("Game/Rebuild Cinder Lanes Scene")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            // シーンを開くと未使用アセットがアンロードされるので、アセットの生成と読み込みはこの後に行う。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            CinderLanesArtGenerator.GenerateIfMissing();
            var settings = LoadOrCreate(Root + "CinderLanesSettings.asset", ScriptableObject.CreateInstance<CinderLanesSettings>);
            settings.Validate();
            var panel = LoadOrCreate(Root + "UI/CinderLanesPanelSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PanelSettings>();
                value.themeStyleSheet = Load<ThemeStyleSheet>(Root + "UI/CinderLanesTheme.tss");
                value.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                value.referenceResolution = new Vector2Int(1920, 1080);
                value.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                value.match = .5f;
                return value;
            });

            // Prefab を作り直すと古いインスタンスが別名で残ることがあるので、前方一致で消す（018 の知見）。
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name.StartsWith(ObjectPrefix, StringComparison.Ordinal)) { Object.DestroyImmediate(root); }
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(LoadStage(settings));
            instance.transform.position = Vector3.zero;
            var stage = instance.GetComponent<CinderLanesStage>();

            var camera = Object.FindAnyObjectByType<Camera>();
            camera.orthographic = false;
            camera.fieldOfView = 60;
            camera.nearClipPlane = .3f;
            camera.farClipPlane = 200;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Sky;
            camera.transform.position = CinderLanesGame.CameraOffset;
            camera.transform.LookAt(new Vector3(0, 1.2f, 10));

            var light = Object.FindAnyObjectByType<Light>();
            light.transform.rotation = Quaternion.Euler(45, -25, 0);
            light.color = new Color(1f, .82f, .66f);
            light.intensity = 1.6f;

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.42f, .34f, .34f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Sky;
            (RenderSettings.fogStartDistance, RenderSettings.fogEndDistance) = CinderLanesGame.FogRange(settings.Rules.LookAheadMeters);

            var game = new GameObject(ObjectPrefix + "Game");
            var hud = game.AddComponent<PanelRenderer>();
            hud.panelSettings = panel;
            hud.visualTreeAsset = Load<VisualTreeAsset>(Root + "UI/Hud.uxml");
            var component = game.AddComponent<CinderLanesGame>();
            var serialized = new SerializedObject(component);
            Assign(serialized, "_settings", settings);
            Assign(serialized, "_hudRenderer", hud);
            Assign(serialized, "_camera", camera);
            Assign(serialized, "_stage", stage);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<CinderLanesCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "CINDER LANES";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();

            var count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<CinderLanesStage>() != null) { count++; }
            }

            if (count != 1) { throw new InvalidOperationException($"Stage のインスタンスが {count} 個あります。"); }
            Debug.Log("[CinderLanesSceneBuilder] シーン生成完了");
        }

        // 配置の正は Stage Prefab。既にあれば調整を上書きせず、設定と一致するかだけ確かめる。
        private static GameObject LoadStage(CinderLanesSettings settings)
        {
            var rules = settings.Rules;
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(StagePath);
            if (existing != null)
            {
                var mismatch = existing.GetComponent<CinderLanesStage>().Mismatch(rules);
                if (mismatch != null) { throw new InvalidOperationException($"{StagePath} が設定と一致しません：{mismatch} Prefab を消して作り直してください。"); }
                return existing;
            }

            var root = new GameObject("CinderLanesStage");
            try
            {
                var stage = root.AddComponent<CinderLanesStage>();
                const float SegmentLength = 8f;
                var roadWidth = Game.Core.RunConfig.Lanes * rules.LaneWidth / CinderLanesStage.UnitsPerMeter;

                // 道の区画（8 m）：道・両脇の灰の地面・両脇の灯の柱。最初の区画が見本で、プールはこれを複製する。
                var segment = new GameObject("Segment");
                segment.transform.SetParent(root.transform, false);
                segment.transform.localPosition = new Vector3(0, 0, -16);
                Part(segment.transform, "Road", PrimitiveType.Cube, CinderLanesArtGenerator.Road, new Vector3(0, -.05f, SegmentLength / 2), new Vector3(roadWidth, .1f, SegmentLength));
                foreach (var side in new[] { -1, 1 })
                {
                    var name = side < 0 ? "L" : "R";
                    Part(segment.transform, "Ground" + name, PrimitiveType.Cube, CinderLanesArtGenerator.Ash, new Vector3(side * ((roadWidth / 2) + 15), -.08f, SegmentLength / 2), new Vector3(30, .1f, SegmentLength));
                    Part(segment.transform, "Post" + name, PrimitiveType.Cube, CinderLanesArtGenerator.Post, new Vector3(side * ((roadWidth / 2) + .6f), 1.1f, 0), new Vector3(.3f, 2.2f, .3f));
                    Part(segment.transform, "Lamp" + name, PrimitiveType.Sphere, CinderLanesArtGenerator.Lamp, new Vector3(side * ((roadWidth / 2) + .6f), 2.4f, 0), Vector3.one * .45f);
                }

                // 自機：足元が原点。見た目はユニティちゃん（当たり判定は Core の箱のまま）。
                var runner = new GameObject("Runner");
                runner.transform.SetParent(root.transform, false);
                var model = new GameObject("Model");
                model.transform.SetParent(runner.transform, false);
                var animator = CinderLanesCharacter.Place(model.transform);

                // 障害物の見本：Scene ビューで大きさと見た目を確かめられるよう、レーンに 1 つずつ置く（実行時は隠してプールの元にする）。
                var samples = new GameObject("Samples");
                samples.transform.SetParent(root.transform, false);
                var wall = Sample(samples.transform, "Wall", CinderLanesArtGenerator.Wall, new Game.Core.Obstacle(Game.Core.ObstacleKind.Wall, 0, 6), rules);
                var hurdle = Sample(samples.transform, "Hurdle", CinderLanesArtGenerator.Hurdle, new Game.Core.Obstacle(Game.Core.ObstacleKind.Hurdle, 1, 8), rules);
                var beam = Sample(samples.transform, "Beam", CinderLanesArtGenerator.Beam, new Game.Core.Obstacle(Game.Core.ObstacleKind.Beam, 2, 11), rules);

                var serialized = new SerializedObject(stage);
                Assign(serialized, "_runner", runner.transform);
                Assign(serialized, "_model", model.transform);
                Assign(serialized, "_animator", animator);
                serialized.FindProperty("_crashLength").floatValue = CinderLanesCharacter.CrashLength();
                serialized.FindProperty("_crashEnd").floatValue = CinderLanesCharacter.LyingTime();
                Assign(serialized, "_wallTemplate", wall);
                Assign(serialized, "_hurdleTemplate", hurdle);
                Assign(serialized, "_beamTemplate", beam);
                Assign(serialized, "_segmentTemplate", segment);
                var passed = serialized.FindProperty("_passedMaterials");
                passed.arraySize = 3;
                var kinds = new[] { CinderLanesArtGenerator.Wall, CinderLanesArtGenerator.Hurdle, CinderLanesArtGenerator.Beam };
                for (var i = 0; i < kinds.Length; i++) { passed.GetArrayElementAtIndex(i).objectReferenceValue = CinderLanesArtGenerator.Material(CinderLanesArtGenerator.Passed(kinds[i])); }
                serialized.FindProperty("_segmentLength").floatValue = SegmentLength;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                return PrefabUtility.SaveAsPrefabAsset(root, StagePath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>障害物の見本：単位の立方体を当たり箱の大きさに拡大して、箱の中心に置く（描画も同じ置き方）。</summary>
        private static GameObject Sample(Transform parent, string name, string material, Game.Core.Obstacle obstacle, Game.Core.RunConfig rules)
        {
            var box = obstacle.Box(rules);
            const float M = CinderLanesStage.UnitsPerMeter;
            var center = new Vector3((box.X + (box.Width / 2f)) / M, (box.Y + (box.Height / 2f)) / M, (box.Z + (box.Depth / 2f)) / M);
            return Part(parent, name, PrimitiveType.Cube, material, center, new Vector3(box.Width / M, box.Height / M, box.Depth / M)).gameObject;
        }

        private static Transform Part(Transform parent, string name, PrimitiveType shape, string material, Vector3 position, Vector3 scale)
        {
            var item = GameObject.CreatePrimitive(shape);
            item.name = name;
            Object.DestroyImmediate(item.GetComponent<Collider>());
            item.transform.SetParent(parent, false);
            item.transform.localPosition = position;
            item.transform.localScale = scale;
            var renderer = item.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = CinderLanesArtGenerator.Material(material);
            renderer.shadowCastingMode = ShadowCastingMode.On;
            return item.transform;
        }

        private static void Assign(SerializedObject target, string field, Object value) =>
            target.FindProperty(field).objectReferenceValue = value;

        private static T Load<T>(string path) where T : Object =>
            AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException("アセットがありません: " + path);

        private static T LoadOrCreate<T>(string path, Func<T> factory) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) { return asset; }
            asset = factory();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
