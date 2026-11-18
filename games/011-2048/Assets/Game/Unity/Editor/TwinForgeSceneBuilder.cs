using System;
using System.Collections.Generic;
using System.Linq;
using Game.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    public static class TwinForgeSceneBuilder
    {
        private const string Root = "Assets/Game/Unity/";
        private const string Art = TwinForgeArtGenerator.ArtRoot;
        private const string ScenePath = "Assets/Scenes/TwinForge.unity";
        private const float BoardCenterX = -6.5f;
        private const float CellPitch = 3.7f;
        private const float TileScale = 3.5f;
        private const float FrameScale = 15.6f;

        [MenuItem("Game/Rebuild Twin Forge Scene")]
        public static void Build()
        {
            TwinForgeArtGenerator.GenerateIfMissing();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            // シーンを開くと未使用アセットがアンロードされるので、アセットの読み込みはこの後に行う。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = LoadOrCreate(Root + "TwinForgeSettings.asset", ScriptableObject.CreateInstance<TwinForgeSettings>);
            settings.Validate();
            var panel = LoadOrCreate(Root + "UI/TwinForgePanelSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PanelSettings>();
                value.themeStyleSheet = Load<ThemeStyleSheet>(Root + "UI/TwinForgeTheme.tss");
                value.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                value.referenceResolution = new Vector2Int(1920, 1080);
                value.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                value.match = .5f;
                return value;
            });

            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "TwinForge" || root.name == "TwinForgeStage") { Object.DestroyImmediate(root); }
            }

            var stage = (GameObject)PrefabUtility.InstantiatePrefab(LoadStage());
            var view = stage.GetComponent<TwinForgeView>();

            var camera = Object.FindAnyObjectByType<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 9;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.07f, .055f, .05f);

            var game = new GameObject("TwinForge");
            var hud = game.AddComponent<PanelRenderer>();
            hud.panelSettings = panel;
            hud.visualTreeAsset = Load<VisualTreeAsset>(Root + "UI/Hud.uxml");
            var component = game.AddComponent<TwinForgeGame>();
            var serialized = new SerializedObject(component);
            Assign(serialized, "_settings", settings);
            Assign(serialized, "_hudRenderer", hud);
            Assign(serialized, "_camera", camera);
            Assign(serialized, "_view", view);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<TwinForgeCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "TWIN FORGE";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();
            Debug.Log("[TwinForgeSceneBuilder] シーン生成完了");
        }

        // 配置の正は Stage Prefab。既にあれば調整を上書きしない。
        private static GameObject LoadStage()
        {
            var path = Art + "TwinForgeStage.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) { return existing; }

            var material = LoadOrCreate(Art + "Unlit.mat", () => new Material(Shader.Find("Sprites/Default")));
            var root = new GameObject("TwinForgeStage");
            try
            {
                root.transform.position = new Vector3(BoardCenterX, 0, 0);
                var view = root.AddComponent<TwinForgeView>();

                var backdrop = Sprite(root.transform, "Backdrop", TwinForgeArtGenerator.BackdropFile, material, -20);
                backdrop.transform.localPosition = new Vector3(-BoardCenterX, 0, 0);
                backdrop.transform.localScale = new Vector3(36, 20.25f, 1);
                var frame = Sprite(root.transform, "Frame", TwinForgeArtGenerator.FrameFile, material, -10);
                frame.transform.localScale = new Vector3(FrameScale, FrameScale, 1);

                var center = (BoardState.Size - 1) / 2f;
                var slotRoot = new GameObject("Slots").transform;
                slotRoot.SetParent(root.transform, false);
                var tileRoot = new GameObject("Tiles").transform;
                tileRoot.SetParent(root.transform, false);
                var tiles = new List<Object>();
                for (var cell = 0; cell < BoardState.CellCount; cell++)
                {
                    var x = cell % BoardState.Size;
                    var y = cell / BoardState.Size;
                    var slot = Sprite(slotRoot, "Slot" + x + "_" + y, TwinForgeArtGenerator.SlotFile, material, 0);
                    slot.transform.localPosition = new Vector3((x - center) * CellPitch, (center - y) * CellPitch, 0);
                    slot.transform.localScale = new Vector3(TileScale, TileScale, 1);

                    var tile = Sprite(tileRoot, "Tile" + cell, TwinForgeArtGenerator.PlateFile(2), material, 10);
                    tile.transform.localScale = new Vector3(TileScale, TileScale, 1);
                    tile.enabled = false;
                    tiles.Add(tile);
                }

                var plates = TwinForgeArtGenerator.PlateValues
                    .Select(value => (Object)Load<Sprite>(Art + TwinForgeArtGenerator.PlateFile(value)))
                    .ToList();

                var serialized = new SerializedObject(view);
                serialized.FindProperty("_cellPitch").floatValue = CellPitch;
                serialized.FindProperty("_tileScale").floatValue = TileScale;
                AssignArray(serialized, "_tiles", tiles);
                AssignArray(serialized, "_plates", plates);
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
