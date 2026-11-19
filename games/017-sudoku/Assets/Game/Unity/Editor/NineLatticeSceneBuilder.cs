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
    public static class NineLatticeSceneBuilder
    {
        private const string Root = "Assets/Game/Unity/";
        private const string Art = NineLatticeArtGenerator.ArtRoot;
        private const string ScenePath = "Assets/Scenes/NineLattice.unity";
        private const float BoardX = -3.6f;
        private const float BoardY = 0f;
        private const float FrameThickness = .14f;
        private const int BackdropOrder = -20;
        private const int PlateOrder = -11;
        private const int CellOrder = -10;
        private const int NoteOrder = -2;
        private const int DigitOrder = 0;
        private const int SelectionOrder = 2;

        private static readonly Color _plateColor = new(.16f, .2f, .32f);
        private static readonly Color _selectionColor = new(.85f, .52f, .12f);

        [MenuItem("Game/Rebuild Nine Lattice Scene")]
        public static void Build()
        {
            NineLatticeArtGenerator.GenerateIfMissing();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            // シーンを開くと未使用アセットがアンロードされるので、アセットの読み込みはこの後に行う。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = LoadOrCreate(Root + "NineLatticeSettings.asset", ScriptableObject.CreateInstance<NineLatticeSettings>);
            settings.Validate();
            var panel = LoadOrCreate(Root + "UI/NineLatticePanelSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PanelSettings>();
                value.themeStyleSheet = Load<ThemeStyleSheet>(Root + "UI/NineLatticeTheme.tss");
                value.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                value.referenceResolution = new Vector2Int(1920, 1080);
                value.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                value.match = .5f;
                return value;
            });

            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "NineLattice" || root.name == "NineLatticeStage") { Object.DestroyImmediate(root); }
            }

            var stage = (GameObject)PrefabUtility.InstantiatePrefab(LoadStage());
            var view = stage.GetComponentInChildren<NineLatticeBoardView>();

            var camera = Object.FindAnyObjectByType<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5.1f;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.07f, .11f, .2f);

            var game = new GameObject("NineLattice");
            var hud = game.AddComponent<PanelRenderer>();
            hud.panelSettings = panel;
            hud.visualTreeAsset = Load<VisualTreeAsset>(Root + "UI/Hud.uxml");
            var component = game.AddComponent<NineLatticeGame>();
            var serialized = new SerializedObject(component);
            Assign(serialized, "_settings", settings);
            Assign(serialized, "_hudRenderer", hud);
            Assign(serialized, "_camera", camera);
            Assign(serialized, "_view", view);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<NineLatticeCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "NINE LATTICE";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();
            Debug.Log("[NineLatticeSceneBuilder] シーン生成完了");
        }

        // 配置の正は Stage Prefab。既にあれば調整を上書きしない。
        private static GameObject LoadStage()
        {
            var path = Art + "NineLatticeStage.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) { return existing; }

            var material = LoadOrCreate(Art + "Unlit.mat", () => new Material(Shader.Find("Sprites/Default")));
            var root = new GameObject("NineLatticeStage");
            try
            {
                var backdrop = Sprite(root.transform, "Backdrop", NineLatticeArtGenerator.BackdropFile, material, BackdropOrder);
                backdrop.transform.localScale = new Vector3(20, 11.5f, 1);
                BuildBoard(root.transform, material);
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // 下地の板、マス、候補の小さい数字、大きい数字、選択の枠。プレイ前から Scene ビューで見える。
        private static void BuildBoard(Transform parent, Material material)
        {
            var board = new GameObject("Board").transform;
            board.SetParent(parent, false);
            board.localPosition = new Vector3(BoardX, BoardY, 0);

            // マスの隙間から見える板が罫線になる。ブロックの境目は隙間が広いので太い線になる。
            var plate = Sprite(board, "Plate", NineLatticeArtGenerator.SolidFile, material, PlateOrder);
            plate.color = _plateColor;
            var size = Board.Size + (NineLatticeBoardView.BoxGap * 2) - (1 - NineLatticeBoardView.CellScale) + (FrameThickness * 2);
            plate.transform.localScale = new Vector3(size, size, 1);

            var digitSprites = new List<Object>();
            for (var digit = 1; digit <= Board.Size; digit++) { digitSprites.Add(Load<Sprite>(Art + NineLatticeArtGenerator.DigitFile(digit))); }

            var cells = new List<Object>();
            var cellGroup = Group(board, "Cells");
            for (var cell = 0; cell < Board.CellCount; cell++) { cells.Add(Sprite(cellGroup, $"Cell{Board.Row(cell)}{Board.Column(cell)}", NineLatticeArtGenerator.CellFile, material, CellOrder)); }

            var notes = new List<Object>();
            var noteGroup = Group(board, "Notes");
            for (var cell = 0; cell < Board.CellCount; cell++)
            {
                for (var digit = 1; digit <= Board.Size; digit++)
                {
                    var note = Sprite(noteGroup, $"Note{Board.Row(cell)}{Board.Column(cell)}-{digit}", NineLatticeArtGenerator.DigitFile(digit), material, NoteOrder);
                    note.enabled = false;
                    notes.Add(note);
                }
            }

            var digits = new List<Object>();
            var digitGroup = Group(board, "Digits");
            for (var cell = 0; cell < Board.CellCount; cell++)
            {
                var renderer = Sprite(digitGroup, $"Digit{Board.Row(cell)}{Board.Column(cell)}", NineLatticeArtGenerator.DigitFile(1), material, DigitOrder);
                renderer.enabled = false;
                digits.Add(renderer);
            }

            var selection = Sprite(board, "Selection", NineLatticeArtGenerator.SelectionFile, material, SelectionOrder);
            selection.color = _selectionColor;
            selection.enabled = false;

            var view = board.gameObject.AddComponent<NineLatticeBoardView>();
            var serialized = new SerializedObject(view);
            AssignArray(serialized, "_cells", cells);
            AssignArray(serialized, "_digits", digits);
            AssignArray(serialized, "_notes", notes);
            AssignArray(serialized, "_digitSprites", digitSprites);
            Assign(serialized, "_selection", selection);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            view.Arrange();

            // 素の状態でも盤が見えるよう、初期の色と、飾りの問題を描いておく。
            view.RenderStill(PuzzleGenerator.Generate(0, new SudokuConfig(), Difficulty.Normal).Givens);
        }

        private static Transform Group(Transform parent, string name)
        {
            var group = new GameObject(name).transform;
            group.SetParent(parent, false);
            return group;
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
