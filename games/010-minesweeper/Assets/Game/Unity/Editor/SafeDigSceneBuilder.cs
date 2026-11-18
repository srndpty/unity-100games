using System;
using System.Collections.Generic;
using Game.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    public static class SafeDigSceneBuilder
    {
        private const string Root = "Assets/Game/Unity/";
        private const string Art = SafeDigArtGenerator.ArtRoot;
        private const string ScenePath = "Assets/Scenes/SafeDig.unity";
        private const float BoardCenterX = -6.5f;

        [MenuItem("Game/Rebuild Safe Dig Scene")]
        public static void Build()
        {
            SafeDigArtGenerator.GenerateIfMissing();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            // シーンを開くと未使用アセットがアンロードされるので、アセットの読み込みはこの後に行う。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = LoadOrCreate(Root + "SafeDigSettings.asset", ScriptableObject.CreateInstance<SafeDigSettings>);
            settings.Validate();
            var panel = LoadOrCreate(Root + "UI/SafeDigPanelSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PanelSettings>();
                value.themeStyleSheet = Load<ThemeStyleSheet>(Root + "UI/SafeDigTheme.tss");
                value.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                value.referenceResolution = new Vector2Int(1920, 1080);
                value.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                value.match = .5f;
                return value;
            });

            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "SafeDig" || root.name == "SafeDigStage") { Object.DestroyImmediate(root); }
            }

            var stage = (GameObject)PrefabUtility.InstantiatePrefab(LoadStage(settings.Board));
            var view = stage.GetComponent<SafeDigView>();

            var camera = Object.FindAnyObjectByType<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 9;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.11f, .15f, .11f);

            var game = new GameObject("SafeDig");
            var hud = game.AddComponent<PanelRenderer>();
            hud.panelSettings = panel;
            hud.visualTreeAsset = Load<VisualTreeAsset>(Root + "UI/Hud.uxml");
            var component = game.AddComponent<SafeDigGame>();
            var serialized = new SerializedObject(component);
            Assign(serialized, "_settings", settings);
            Assign(serialized, "_hudRenderer", hud);
            Assign(serialized, "_camera", camera);
            Assign(serialized, "_view", view);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<SafeDigCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "SAFE DIG";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();
            Debug.Log("[SafeDigSceneBuilder] シーン生成完了");
        }

        // 配置の正は Stage Prefab。既にあれば調整を上書きしない。
        private static GameObject LoadStage(BoardConfig config)
        {
            var path = Art + "SafeDigStage.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) { return existing; }

            var material = LoadOrCreate(Art + "Unlit.mat", () => new Material(Shader.Find("Sprites/Default")));
            var root = new GameObject("SafeDigStage");
            try
            {
                root.transform.position = new Vector3(BoardCenterX, 0, 0);
                var view = root.AddComponent<SafeDigView>();
                var frame = Sprite(root.transform, "Frame", SafeDigArtGenerator.FrameFile, material, -10);
                frame.color = new Color(.3f, .24f, .16f);
                frame.transform.localScale = new Vector3(config.Width + .5f, config.Height + .5f, 1);

                var hidden = SafeDigArtGenerator.FaceFile(CellFace.Hidden);
                var cells = new List<Object>();
                var cellRoot = new GameObject("Cells").transform;
                cellRoot.SetParent(root.transform, false);
                for (var y = 0; y < config.Height; y++)
                {
                    for (var x = 0; x < config.Width; x++)
                    {
                        var cell = Sprite(cellRoot, "Cell" + x + "_" + y, hidden, material, 0);
                        cell.transform.localPosition = new Vector3(x - ((config.Width - 1) / 2f), ((config.Height - 1) / 2f) - y, 0);
                        cells.Add(cell);
                    }
                }

                var faces = new List<Object>();
                foreach (CellFace face in Enum.GetValues(typeof(CellFace))) { faces.Add(Load<Sprite>(Art + SafeDigArtGenerator.FaceFile(face))); }
                var cursor = Sprite(root.transform, "Cursor", SafeDigArtGenerator.CursorFile, material, 10);
                cursor.enabled = false;

                var serialized = new SerializedObject(view);
                serialized.FindProperty("_width").intValue = config.Width;
                serialized.FindProperty("_height").intValue = config.Height;
                AssignArray(serialized, "_cells", cells);
                AssignArray(serialized, "_faces", faces);
                Assign(serialized, "_cursor", cursor);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                return PrefabUtility.SaveAsPrefabAsset(root, path);
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

        private static void AssignArray(SerializedObject target, string field, List<Object> values)
        {
            var property = target.FindProperty(field);
            property.arraySize = values.Count;
            for (var i = 0; i < values.Count; i++) { property.GetArrayElementAtIndex(i).objectReferenceValue = values[i]; }
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
