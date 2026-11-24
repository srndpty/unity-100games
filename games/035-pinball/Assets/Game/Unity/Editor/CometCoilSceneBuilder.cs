using System;
using Game.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>
    /// シーンと Stage Prefab を生成する。Stage Prefab は既にあれば上書きせず、台のデータと合っているかだけ確かめる。
    /// 台のデータを変えたら Prefab を消して作り直す。
    /// 実行：<c>unity run games/035-pinball -- -executeMethod Game.Unity.Editor.CometCoilSceneBuilder.Build</c>
    /// </summary>
    public static class CometCoilSceneBuilder
    {
        public const string StagePath = Art + "CometCoilStage.prefab";
        public const string ScenePath = "Assets/Scenes/CometCoil.unity";

        /// <summary>台（44 マス）と枠（上下 2 マス）が縦に収まり、少し余白が残る大きさ。</summary>
        public const float CameraSize = 24.4f;

        private const string Root = "Assets/Game/Unity/";
        private const string Art = CometCoilArtGenerator.ArtRoot;
        private const string ObjectPrefix = "CometCoil";

        [MenuItem("Game/Rebuild Comet Coil Scene")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            // シーンを開くと未使用アセットがアンロードされるので、アセットの読み込みはこの後に行う。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = LoadOrCreate(Root + "CometCoilSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<CometCoilSettings>();
                value.Table = Load<TextAsset>("Assets/Game/Data/Table.txt");
                return value;
            });
            var table = settings.Validate();
            CometCoilArtGenerator.GenerateIfMissing(table, settings.Rules);
            var panel = LoadOrCreate(Root + "UI/CometCoilPanelSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PanelSettings>();
                value.themeStyleSheet = Load<ThemeStyleSheet>(Root + "UI/CometCoilTheme.tss");
                value.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                value.referenceResolution = new Vector2Int(1920, 1080);
                value.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                value.match = 1f;
                return value;
            });

            // Prefab を作り直すと古いインスタンスが別名で残ることがあるので、前方一致で消す（018 の知見）。
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name.StartsWith(ObjectPrefix, StringComparison.Ordinal)) { Object.DestroyImmediate(root); }
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(LoadStage(table, settings.Rules));
            var stage = instance.GetComponent<CometCoilStage>();

            var camera = Object.FindAnyObjectByType<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = CameraSize;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.02f, .02f, .06f);
            for (var i = camera.transform.childCount - 1; i >= 0; i--) { Object.DestroyImmediate(camera.transform.GetChild(i).gameObject); }

            var game = new GameObject(ObjectPrefix);
            var hud = game.AddComponent<PanelRenderer>();
            hud.panelSettings = panel;
            hud.visualTreeAsset = Load<VisualTreeAsset>(Root + "UI/Hud.uxml");
            var component = game.AddComponent<CometCoilGame>();
            var serialized = new SerializedObject(component);
            Assign(serialized, "_settings", settings);
            Assign(serialized, "_hudRenderer", hud);
            Assign(serialized, "_camera", camera);
            Assign(serialized, "_stage", stage);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<CometCoilCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "COMET COIL";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();

            var count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<CometCoilStage>() != null) { count++; }
            }

            if (count != 1) { throw new InvalidOperationException($"Stage のインスタンスが {count} 個あります。"); }
            Debug.Log("[CometCoilSceneBuilder] シーン生成完了");
        }

        // 配置の正は Stage Prefab。既にあれば調整を上書きせず、台のデータと合っているかだけ確かめる。
        private static GameObject LoadStage(Table table, PinballConfig config)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(StagePath);
            if (existing != null)
            {
                var mismatch = existing.GetComponent<CometCoilStage>().Mismatch(table, config);
                if (mismatch != null) { throw new InvalidOperationException($"{StagePath} が台のデータと合いません：{mismatch} Prefab を消して作り直してください。"); }
                return existing;
            }

            var material = LoadOrCreate(Art + "Unlit.mat", () => new Material(Shader.Find("Sprites/Default")));
            var root = new GameObject("CometCoilStage");
            try
            {
                var stage = root.AddComponent<CometCoilStage>();
                var background = Sprite(root.transform, "Background", CometCoilArtGenerator.BackgroundFile, material, -100);

                var rails = new System.Collections.Generic.List<Object>();
                var bumpers = new Object[Table.BumperCount];
                var targets = new Object[Table.TargetCount];
                foreach (var collider in table.Colliders)
                {
                    switch (collider.Kind)
                    {
                        case ColliderKind.Wall:
                            rails.Add(Sprite(root.transform, "Wall" + rails.Count, CometCoilArtGenerator.RailFile, material, 10));
                            break;
                        case ColliderKind.Gate:
                            rails.Add(Sprite(root.transform, "Gate" + rails.Count, CometCoilArtGenerator.GateFile, material, 11));
                            break;
                        case ColliderKind.Bumper:
                            bumpers[collider.Number] = Sprite(root.transform, "Bumper" + collider.Number, CometCoilArtGenerator.BumperFile, material, 15);
                            break;
                        case ColliderKind.Target:
                            targets[collider.Number] = Sprite(root.transform, "Target" + collider.Number, CometCoilArtGenerator.TargetFile, material, 12);
                            break;
                    }
                }

                var plunger = Sprite(root.transform, "Plunger", CometCoilArtGenerator.PlungerFile, material, 18);
                var left = Sprite(root.transform, "LeftFlipper", CometCoilArtGenerator.FlipperFile, material, 20);
                var right = Sprite(root.transform, "RightFlipper", CometCoilArtGenerator.FlipperFile, material, 20);
                var balls = new Object[PinballConfig.MaxBalls];
                for (var i = 0; i < balls.Length; i++) { balls[i] = Sprite(root.transform, "Ball" + i, CometCoilArtGenerator.BallFile, material, 30); }

                var serialized = new SerializedObject(stage);
                Assign(serialized, "_background", background);
                AssignArray(serialized, "_rails", rails.ToArray());
                AssignArray(serialized, "_bumpers", bumpers);
                AssignArray(serialized, "_targets", targets);
                Assign(serialized, "_leftFlipper", left);
                Assign(serialized, "_rightFlipper", right);
                AssignArray(serialized, "_balls", balls);
                Assign(serialized, "_plunger", plunger);
                serialized.ApplyModifiedPropertiesWithoutUndo();

                stage.Layout(table, config);

                // プレイ前の Scene ビューでも見えるよう、1 個目の球をレーンの置き場に置き、2 個目は隠す。
                ((SpriteRenderer)balls[0]).transform.localPosition = CometCoilStage.ToLocal(table.LaneRest);
                ((SpriteRenderer)balls[1]).enabled = false;
                var mismatch = stage.Mismatch(table, config);
                if (mismatch != null) { throw new InvalidOperationException("作った Stage が台のデータと合いません：" + mismatch); }
                return PrefabUtility.SaveAsPrefabAsset(root, StagePath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static SpriteRenderer Sprite(Transform parent, string name, string file, Material material, int order)
        {
            var item = new GameObject(name);
            item.transform.SetParent(parent, false);
            var renderer = item.AddComponent<SpriteRenderer>();
            renderer.sprite = Load<Sprite>(Art + file);
            renderer.sharedMaterial = material;
            renderer.sortingOrder = order;
            return renderer;
        }

        private static void Assign(SerializedObject target, string field, Object value) =>
            target.FindProperty(field).objectReferenceValue = value;

        private static void AssignArray(SerializedObject target, string field, Object[] values)
        {
            var property = target.FindProperty(field);
            property.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++) { property.GetArrayElementAtIndex(i).objectReferenceValue = values[i]; }
        }

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
