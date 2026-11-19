using System;
using System.Collections.Generic;
using Game.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using Position = Game.Core.Position;

namespace Game.Unity.Editor
{
    public static class DuskDawnSceneBuilder
    {
        private const string Root = "Assets/Game/Unity/";
        private const string Art = DuskDawnArtGenerator.ArtRoot;
        private const string ScenePath = "Assets/Scenes/DuskDawn.unity";
        private const float BoardX = -3.7f;
        private const float BoardY = 0f;
        private const float FrameThickness = .26f;
        private const int BackdropOrder = -20;
        private const int FrameOrder = -11;
        private const int CellOrder = -10;
        private const int HintOrder = -5;
        private const int DiscOrder = 0;
        private const int MarkerOrder = 2;

        private static readonly Color _frameColor = new(.4f, .3f, .2f);

        [MenuItem("Game/Rebuild Dusk Dawn Scene")]
        public static void Build()
        {
            DuskDawnArtGenerator.GenerateIfMissing();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            // シーンを開くと未使用アセットがアンロードされるので、アセットの読み込みはこの後に行う。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = LoadOrCreate(Root + "DuskDawnSettings.asset", ScriptableObject.CreateInstance<DuskDawnSettings>);
            settings.Validate();
            var panel = LoadOrCreate(Root + "UI/DuskDawnPanelSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PanelSettings>();
                value.themeStyleSheet = Load<ThemeStyleSheet>(Root + "UI/DuskDawnTheme.tss");
                value.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                value.referenceResolution = new Vector2Int(1920, 1080);
                value.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                value.match = .5f;
                return value;
            });

            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "DuskDawn" || root.name == "DuskDawnStage") { Object.DestroyImmediate(root); }
            }

            var stage = (GameObject)PrefabUtility.InstantiatePrefab(LoadStage());
            var view = stage.GetComponentInChildren<DuskDawnBoardView>();

            var camera = Object.FindAnyObjectByType<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 4.9f;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.05f, .04f, .12f);

            var game = new GameObject("DuskDawn");
            var hud = game.AddComponent<PanelRenderer>();
            hud.panelSettings = panel;
            hud.visualTreeAsset = Load<VisualTreeAsset>(Root + "UI/Hud.uxml");
            var component = game.AddComponent<DuskDawnGame>();
            var serialized = new SerializedObject(component);
            Assign(serialized, "_settings", settings);
            Assign(serialized, "_hudRenderer", hud);
            Assign(serialized, "_camera", camera);
            Assign(serialized, "_view", view);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<DuskDawnCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "DUSK & DAWN";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();
            Debug.Log("[DuskDawnSceneBuilder] シーン生成完了");
        }

        // 配置の正は Stage Prefab。既にあれば調整を上書きしない。
        private static GameObject LoadStage()
        {
            var path = Art + "DuskDawnStage.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) { return existing; }

            var material = LoadOrCreate(Art + "Unlit.mat", () => new Material(Shader.Find("Sprites/Default")));
            var root = new GameObject("DuskDawnStage");
            try
            {
                var backdrop = Sprite(root.transform, "Backdrop", DuskDawnArtGenerator.BackdropFile, material, BackdropOrder);
                backdrop.transform.localScale = new Vector3(19, 11, 1);
                BuildBoard(root.transform, material);
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // 枠、マス、合法手の印、石、直前の手の印、カーソル。プレイ前から Scene ビューで見える。
        private static void BuildBoard(Transform parent, Material material)
        {
            var board = new GameObject("Board").transform;
            board.SetParent(parent, false);
            board.localPosition = new Vector3(BoardX, BoardY, 0);

            var half = (Square.Size + FrameThickness) / 2f;
            var length = Square.Size + (FrameThickness * 2);
            Block(board, "FrameLeft", material, new Vector3(-half, 0, 0), new Vector3(FrameThickness, length, 1));
            Block(board, "FrameRight", material, new Vector3(half, 0, 0), new Vector3(FrameThickness, length, 1));
            Block(board, "FrameBottom", material, new Vector3(0, -half, 0), new Vector3(Square.Size, FrameThickness, 1));
            Block(board, "FrameTop", material, new Vector3(0, half, 0), new Vector3(Square.Size, FrameThickness, 1));

            var cells = Sprite(board, "Cells", DuskDawnArtGenerator.CellFile, material, CellOrder);
            cells.drawMode = SpriteDrawMode.Tiled;
            cells.size = new Vector2(Square.Size, Square.Size);

            var hints = new List<Object>();
            var hintGroup = Group(board, "Hints");
            for (var i = 0; i < Square.Count; i++)
            {
                var hint = Sprite(hintGroup, "Hint" + Square.FromIndex(i).Name, DuskDawnArtGenerator.HintFile, material, HintOrder);
                hint.enabled = false;
                hints.Add(hint);
            }

            var discs = new List<Object>();
            var discGroup = Group(board, "Discs");
            var start = Position.Initial;
            for (var i = 0; i < Square.Count; i++)
            {
                var square = Square.FromIndex(i);
                var file = start.At(square) == Disc.White ? DuskDawnArtGenerator.DawnFile : DuskDawnArtGenerator.DuskFile;
                var disc = Sprite(discGroup, "Disc" + square.Name, file, material, DiscOrder);
                disc.transform.localScale = Vector3.one * .86f;
                disc.enabled = start.At(square) != Disc.Empty;
                discs.Add(disc);
            }

            var lastMove = Sprite(board, "LastMove", DuskDawnArtGenerator.LastMoveFile, material, MarkerOrder);
            lastMove.enabled = false;
            var cursor = Sprite(board, "Cursor", DuskDawnArtGenerator.CursorFile, material, MarkerOrder + 1);
            cursor.enabled = false;

            var view = board.gameObject.AddComponent<DuskDawnBoardView>();
            var serialized = new SerializedObject(view);
            AssignArray(serialized, "_discs", discs);
            AssignArray(serialized, "_hints", hints);
            Assign(serialized, "_blackSprite", Load<Sprite>(Art + DuskDawnArtGenerator.DuskFile));
            Assign(serialized, "_whiteSprite", Load<Sprite>(Art + DuskDawnArtGenerator.DawnFile));
            Assign(serialized, "_lastMove", lastMove);
            Assign(serialized, "_cursor", cursor);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            view.Arrange();
        }

        private static Transform Group(Transform parent, string name)
        {
            var group = new GameObject(name).transform;
            group.SetParent(parent, false);
            return group;
        }

        private static void Block(Transform parent, string name, Material material, Vector3 position, Vector3 scale)
        {
            var block = Sprite(parent, name, DuskDawnArtGenerator.SolidFile, material, FrameOrder);
            block.color = _frameColor;
            block.transform.localPosition = position;
            block.transform.localScale = scale;
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
