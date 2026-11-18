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
    public static class HarborShiftSceneBuilder
    {
        private const string Root = "Assets/Game/Unity/";
        private const string Art = HarborShiftArtGenerator.ArtRoot;
        private const string ScenePath = "Assets/Scenes/HarborShift.unity";
        private const float BoardCenterX = -6.9f;
        private const int FloorOrder = 0;
        private const int GoalOrder = 1;
        private const int WallOrder = 2;
        private const int BoxOrder = 3;
        private const int PlayerOrder = 4;

        [MenuItem("Game/Rebuild Harbor Shift Scene")]
        public static void Build()
        {
            HarborShiftArtGenerator.GenerateIfMissing();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            // シーンを開くと未使用アセットがアンロードされるので、アセットの読み込みはこの後に行う。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = LoadOrCreate(Root + "HarborShiftSettings.asset", ScriptableObject.CreateInstance<HarborShiftSettings>);
            settings.Validate();
            var panel = LoadOrCreate(Root + "UI/HarborShiftPanelSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PanelSettings>();
                value.themeStyleSheet = Load<ThemeStyleSheet>(Root + "UI/HarborShiftTheme.tss");
                value.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                value.referenceResolution = new Vector2Int(1920, 1080);
                value.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                value.match = .5f;
                return value;
            });

            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "HarborShift" || root.name == "HarborShiftStage") { Object.DestroyImmediate(root); }
            }

            var stage = (GameObject)PrefabUtility.InstantiatePrefab(LoadStage());
            var view = stage.GetComponent<HarborShiftView>();

            var camera = Object.FindAnyObjectByType<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 9;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.03f, .08f, .13f);

            var game = new GameObject("HarborShift");
            var hud = game.AddComponent<PanelRenderer>();
            hud.panelSettings = panel;
            hud.visualTreeAsset = Load<VisualTreeAsset>(Root + "UI/Hud.uxml");
            var component = game.AddComponent<HarborShiftGame>();
            var serialized = new SerializedObject(component);
            Assign(serialized, "_settings", settings);
            Assign(serialized, "_hudRenderer", hud);
            Assign(serialized, "_camera", camera);
            Assign(serialized, "_view", view);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<HarborShiftCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "HARBOR SHIFT";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();
            Debug.Log("[HarborShiftSceneBuilder] シーン生成完了");
        }

        // 配置の正は Stage Prefab。既にあれば調整を上書きしない。
        private static GameObject LoadStage()
        {
            var path = Art + "HarborShiftStage.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) { return existing; }

            var material = LoadOrCreate(Art + "Unlit.mat", () => new Material(Shader.Find("Sprites/Default")));
            var root = new GameObject("HarborShiftStage");
            try
            {
                root.transform.position = new Vector3(BoardCenterX, 0, 0);
                var view = root.AddComponent<HarborShiftView>();

                var backdrop = Sprite(root.transform, "Backdrop", HarborShiftArtGenerator.BackdropFile, material, -20);
                backdrop.transform.localPosition = new Vector3(-BoardCenterX, 0, 0);
                backdrop.transform.localScale = new Vector3(36, 20.25f, 1);

                var board = new GameObject("Board").transform;
                board.SetParent(root.transform, false);
                var cellCount = LevelMap.MaxWidth * LevelMap.MaxHeight;
                var floors = Pool(board, "Floors", HarborShiftArtGenerator.FloorFile, material, FloorOrder, cellCount);
                var goals = Pool(board, "Goals", HarborShiftArtGenerator.GoalFile, material, GoalOrder, LevelMap.MaxBoxes);
                var walls = Pool(board, "Walls", HarborShiftArtGenerator.WallFile, material, WallOrder, cellCount);
                var boxes = Pool(board, "Boxes", HarborShiftArtGenerator.BoxFile, material, BoxOrder, LevelMap.MaxBoxes);
                var player = Sprite(board, "Player", HarborShiftArtGenerator.PlayerFile, material, PlayerOrder);

                var serialized = new SerializedObject(view);
                Assign(serialized, "_boardRoot", board);
                AssignArray(serialized, "_floors", floors);
                AssignArray(serialized, "_walls", walls);
                AssignArray(serialized, "_goals", goals);
                AssignArray(serialized, "_boxes", boxes);
                Assign(serialized, "_player", player);
                Assign(serialized, "_boxSprite", Load<Sprite>(Art + HarborShiftArtGenerator.BoxFile));
                Assign(serialized, "_boxPlacedSprite", Load<Sprite>(Art + HarborShiftArtGenerator.BoxPlacedFile));
                serialized.ApplyModifiedPropertiesWithoutUndo();

                // プレイ前から Scene ビューで見えるよう、第1問の配置を保存する。
                var first = LevelCatalog.Maps[0];
                view.Show(first, first.Initial, Direction.Down);
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static List<Object> Pool(Transform parent, string name, string file, Material material, int order, int count)
        {
            var group = new GameObject(name).transform;
            group.SetParent(parent, false);
            var items = new List<Object>();
            for (var i = 0; i < count; i++)
            {
                var item = Sprite(group, name + i, file, material, order);
                item.enabled = false;
                items.Add(item);
            }

            return items;
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
