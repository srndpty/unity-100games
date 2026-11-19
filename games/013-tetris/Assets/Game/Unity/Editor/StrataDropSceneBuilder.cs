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
    public static class StrataDropSceneBuilder
    {
        private const string Root = "Assets/Game/Unity/";
        private const string Art = StrataDropArtGenerator.ArtRoot;
        private const string ScenePath = "Assets/Scenes/StrataDrop.unity";
        private const float WellCenterY = -1f;
        private const float SideX = 9.5f;
        private const int BackdropOrder = -20;
        private const int PanelOrder = -10;
        private const int WellOrder = -9;
        private const int FrameOrder = -8;
        private const int LockedOrder = 0;
        private const int GhostOrder = 1;
        private const int ActiveOrder = 2;
        private const int FlashOrder = 3;

        private static readonly Color _panelColor = new(.05f, .04f, .04f, .78f);
        private static readonly Color _frameColor = new(.42f, .31f, .20f);
        private static readonly Color _flashColor = new(1, 1, 1, 0);

        [MenuItem("Game/Rebuild Strata Drop Scene")]
        public static void Build()
        {
            StrataDropArtGenerator.GenerateIfMissing();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            // シーンを開くと未使用アセットがアンロードされるので、アセットの読み込みはこの後に行う。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = LoadOrCreate(Root + "StrataDropSettings.asset", ScriptableObject.CreateInstance<StrataDropSettings>);
            settings.Validate();
            var panel = LoadOrCreate(Root + "UI/StrataDropPanelSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PanelSettings>();
                value.themeStyleSheet = Load<ThemeStyleSheet>(Root + "UI/StrataDropTheme.tss");
                value.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                value.referenceResolution = new Vector2Int(1920, 1080);
                value.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                value.match = .5f;
                return value;
            });

            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "StrataDrop" || root.name == "StrataDropStage") { Object.DestroyImmediate(root); }
            }

            var stage = (GameObject)PrefabUtility.InstantiatePrefab(LoadStage());
            var view = stage.GetComponent<StrataDropView>();

            var camera = Object.FindAnyObjectByType<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 11.5f;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.04f, .03f, .03f);

            var game = new GameObject("StrataDrop");
            var hud = game.AddComponent<PanelRenderer>();
            hud.panelSettings = panel;
            hud.visualTreeAsset = Load<VisualTreeAsset>(Root + "UI/Hud.uxml");
            var component = game.AddComponent<StrataDropGame>();
            var serialized = new SerializedObject(component);
            Assign(serialized, "_settings", settings);
            Assign(serialized, "_hudRenderer", hud);
            Assign(serialized, "_camera", camera);
            Assign(serialized, "_view", view);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<StrataDropCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "STRATA DROP";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();
            Debug.Log("[StrataDropSceneBuilder] シーン生成完了");
        }

        // 配置の正は Stage Prefab。既にあれば調整を上書きしない。
        private static GameObject LoadStage()
        {
            var path = Art + "StrataDropStage.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) { return existing; }

            var material = LoadOrCreate(Art + "Unlit.mat", () => new Material(Shader.Find("Sprites/Default")));
            var root = new GameObject("StrataDropStage");
            try
            {
                var view = root.AddComponent<StrataDropView>();
                BuildScenery(root.transform, material);

                var board = new GameObject("Board").transform;
                board.SetParent(root.transform, false);
                var firstOre = StrataDropArtGenerator.OreFile(PieceKind.I);
                var cells = Pool(board, "Cells", firstOre, material, LockedOrder, Board.Width * StrataDropView.DrawnRows);
                var ghost = Pool(board, "Ghost", firstOre, material, GhostOrder, PieceShapes.CellCount);
                var active = Pool(board, "Active", firstOre, material, ActiveOrder, PieceShapes.CellCount);
                var flashes = Pool(board, "Flashes", StrataDropArtGenerator.SolidFile, material, FlashOrder, StrataConfig.ClearableLines);
                foreach (SpriteRenderer flash in flashes)
                {
                    flash.color = _flashColor;
                    flash.transform.localScale = new Vector3(Board.Width, 1, 1);
                }

                var hold = Pool(root.transform, "Hold", firstOre, material, ActiveOrder, PieceShapes.CellCount);
                var next = Pool(root.transform, "Next", firstOre, material, ActiveOrder, StrataGame.PreviewCount * PieceShapes.CellCount);
                var holdAnchor = Anchor(root.transform, "HoldAnchor", new Vector3(-SideX, 5.5f, 0));
                var nextAnchors = new List<Object>();
                for (var i = 0; i < StrataGame.PreviewCount; i++)
                {
                    nextAnchors.Add(Anchor(root.transform, "NextAnchor" + i, new Vector3(SideX, 6.6f - (i * 3.1f), 0)));
                }

                var ores = new List<Object>();
                for (var i = 0; i < PieceShapes.KindCount; i++) { ores.Add(Load<Sprite>(Art + StrataDropArtGenerator.OreFile((PieceKind)i))); }

                var serialized = new SerializedObject(view);
                Assign(serialized, "_boardRoot", board);
                AssignArray(serialized, "_cells", cells);
                AssignArray(serialized, "_active", active);
                AssignArray(serialized, "_ghost", ghost);
                AssignArray(serialized, "_hold", hold);
                AssignArray(serialized, "_next", next);
                AssignArray(serialized, "_flashes", flashes);
                Assign(serialized, "_holdAnchor", holdAnchor);
                AssignArray(serialized, "_nextAnchors", nextAnchors);
                AssignArray(serialized, "_ores", ores);
                serialized.ApplyModifiedPropertiesWithoutUndo();

                view.Arrange();
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // 背景、坑道、枠、ホールドと次の鉱石の台。プレイ前から Scene ビューで見える。
        private static void BuildScenery(Transform parent, Material material)
        {
            var backdrop = Sprite(parent, "Backdrop", StrataDropArtGenerator.BackdropFile, material, BackdropOrder);
            backdrop.transform.localScale = new Vector3(56, 25, 1);

            var well = Sprite(parent, "Well", StrataDropArtGenerator.WellTileFile, material, WellOrder);
            well.drawMode = SpriteDrawMode.Tiled;
            well.size = new Vector2(Board.Width, Board.VisibleHeight);
            well.transform.localPosition = new Vector3(0, WellCenterY, 0);

            Block(parent, "FrameLeft", material, FrameOrder, _frameColor, new Vector3(-5.2f, WellCenterY - .2f, 0), new Vector3(.4f, 20.4f, 1));
            Block(parent, "FrameRight", material, FrameOrder, _frameColor, new Vector3(5.2f, WellCenterY - .2f, 0), new Vector3(.4f, 20.4f, 1));
            Block(parent, "FrameBottom", material, FrameOrder, _frameColor, new Vector3(0, -11.2f, 0), new Vector3(10.8f, .4f, 1));
            Block(parent, "HoldPanel", material, PanelOrder, _panelColor, new Vector3(-SideX, 5.5f, 0), new Vector3(6, 6, 1));
            Block(parent, "NextPanel", material, PanelOrder, _panelColor, new Vector3(SideX, .4f, 0), new Vector3(6, 16.2f, 1));
        }

        private static void Block(Transform parent, string name, Material material, int order, Color color, Vector3 position, Vector3 scale)
        {
            var block = Sprite(parent, name, StrataDropArtGenerator.SolidFile, material, order);
            block.color = color;
            block.transform.localPosition = position;
            block.transform.localScale = scale;
        }

        private static Transform Anchor(Transform parent, string name, Vector3 position)
        {
            var anchor = new GameObject(name).transform;
            anchor.SetParent(parent, false);
            anchor.localPosition = position;
            return anchor;
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
