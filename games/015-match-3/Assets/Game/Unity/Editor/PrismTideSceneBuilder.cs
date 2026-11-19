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
    public static class PrismTideSceneBuilder
    {
        private const string Root = "Assets/Game/Unity/";
        private const string Art = PrismTideArtGenerator.ArtRoot;
        private const string ScenePath = "Assets/Scenes/PrismTide.unity";
        private const float BoardX = -3.4f;
        private const float BoardY = -.2f;
        private const float FrameThickness = .22f;
        private const int BackdropOrder = -20;
        private const int FrameOrder = -11;
        private const int SlotOrder = -10;
        private const int PieceOrder = 0;
        private const int MarkerOrder = 2;

        private static readonly Color _frameColor = new(.32f, .66f, .74f);

        [MenuItem("Game/Rebuild Prism Tide Scene")]
        public static void Build()
        {
            PrismTideArtGenerator.GenerateIfMissing();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            // シーンを開くと未使用アセットがアンロードされるので、アセットの読み込みはこの後に行う。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = LoadOrCreate(Root + "PrismTideSettings.asset", ScriptableObject.CreateInstance<PrismTideSettings>);
            settings.Validate();
            var panel = LoadOrCreate(Root + "UI/PrismTidePanelSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PanelSettings>();
                value.themeStyleSheet = Load<ThemeStyleSheet>(Root + "UI/PrismTideTheme.tss");
                value.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                value.referenceResolution = new Vector2Int(1920, 1080);
                value.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                value.match = .5f;
                return value;
            });

            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "PrismTide" || root.name == "PrismTideStage") { Object.DestroyImmediate(root); }
            }

            var stage = (GameObject)PrefabUtility.InstantiatePrefab(LoadStage());
            var view = stage.GetComponentInChildren<PrismBoardView>();

            var camera = Object.FindAnyObjectByType<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 4.8f;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.02f, .06f, .09f);

            var game = new GameObject("PrismTide");
            var hud = game.AddComponent<PanelRenderer>();
            hud.panelSettings = panel;
            hud.visualTreeAsset = Load<VisualTreeAsset>(Root + "UI/Hud.uxml");
            var component = game.AddComponent<PrismTideGame>();
            var serialized = new SerializedObject(component);
            Assign(serialized, "_settings", settings);
            Assign(serialized, "_hudRenderer", hud);
            Assign(serialized, "_camera", camera);
            Assign(serialized, "_view", view);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<PrismTideCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "PRISM TIDE";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();
            Debug.Log("[PrismTideSceneBuilder] シーン生成完了");
        }

        // 配置の正は Stage Prefab。既にあれば調整を上書きしない。
        private static GameObject LoadStage()
        {
            var path = Art + "PrismTideStage.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) { return existing; }

            var material = LoadOrCreate(Art + "Unlit.mat", () => new Material(Shader.Find("Sprites/Default")));
            var root = new GameObject("PrismTideStage");
            try
            {
                var backdrop = Sprite(root.transform, "Backdrop", PrismTideArtGenerator.BackdropFile, material, BackdropOrder);
                backdrop.transform.localScale = new Vector3(19, 11, 1);
                BuildBoard(root.transform, material);
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // 枠、マスのくぼみ、結晶、選択枠、カーソル。プレイ前から Scene ビューで見える。
        private static void BuildBoard(Transform parent, Material material)
        {
            var board = new GameObject("Board").transform;
            board.SetParent(parent, false);
            board.localPosition = new Vector3(BoardX, BoardY, 0);

            var half = (Board.Width + FrameThickness) / 2f;
            var length = Board.Width + (FrameThickness * 2);
            Block(board, "FrameLeft", material, new Vector3(-half, 0, 0), new Vector3(FrameThickness, length, 1));
            Block(board, "FrameRight", material, new Vector3(half, 0, 0), new Vector3(FrameThickness, length, 1));
            Block(board, "FrameBottom", material, new Vector3(0, -half, 0), new Vector3(Board.Width, FrameThickness, 1));
            Block(board, "FrameTop", material, new Vector3(0, half, 0), new Vector3(Board.Width, FrameThickness, 1));

            var slots = Sprite(board, "Slots", PrismTideArtGenerator.SlotFile, material, SlotOrder);
            slots.drawMode = SpriteDrawMode.Tiled;
            slots.size = new Vector2(Board.Width, Board.Height);

            // 補充の結晶は盤面の上から落ちてくるので、盤面の外を隠す。
            var mask = new GameObject("Mask").AddComponent<SpriteMask>();
            mask.transform.SetParent(board, false);
            mask.sprite = Load<Sprite>(Art + PrismTideArtGenerator.SolidFile);
            mask.transform.localScale = new Vector3(Board.Width, Board.Height, 1);

            var cells = new List<Object>();
            var group = new GameObject("Pieces").transform;
            group.SetParent(board, false);
            var first = PrismTideArtGenerator.CrystalFile(Piece.Coral);
            for (var i = 0; i < Board.CellCount; i++)
            {
                var cell = Sprite(group, "Piece" + i, first, material, PieceOrder);
                cell.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
                cells.Add(cell);
            }

            var sprites = new List<Object>();
            foreach (var piece in PrismTideArtGenerator.AllPieces()) { sprites.Add(Load<Sprite>(Art + PrismTideArtGenerator.CrystalFile(piece))); }
            var selection = Sprite(board, "Selection", PrismTideArtGenerator.SelectionFile, material, MarkerOrder);
            selection.enabled = false;
            var cursor = Sprite(board, "Cursor", PrismTideArtGenerator.CursorFile, material, MarkerOrder + 1);
            cursor.enabled = false;

            var view = board.gameObject.AddComponent<PrismBoardView>();
            var serialized = new SerializedObject(view);
            AssignArray(serialized, "_cells", cells);
            AssignArray(serialized, "_sprites", sprites);
            Assign(serialized, "_selection", selection);
            Assign(serialized, "_cursor", cursor);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            view.Arrange();
        }

        private static void Block(Transform parent, string name, Material material, Vector3 position, Vector3 scale)
        {
            var block = Sprite(parent, name, PrismTideArtGenerator.SolidFile, material, FrameOrder);
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
