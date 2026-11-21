using System;
using System.Collections.Generic;
using Game.App;
using Game.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    public static class FireflyHushSceneBuilder
    {
        private const string Root = "Assets/Game/Unity/";
        private const string Art = FireflyHushArtGenerator.ArtRoot;
        private const string ScenePath = "Assets/Scenes/FireflyHush.unity";
        private const string StagePath = Art + "FireflyHushStage.prefab";
        private const string ObjectPrefix = "FireflyHush";
        private const float BoardCenterX = -4.6f;
        private const float Pitch = 1.9f;
        private const float HitSize = 1.7f;

        [MenuItem("Game/Rebuild Firefly Hush Scene")]
        public static void Build()
        {
            FireflyHushArtGenerator.GenerateIfMissing();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            // シーンを開くと未使用アセットがアンロードされるので、アセットの読み込みはこの後に行う。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = LoadOrCreate(Root + "FireflyHushSettings.asset", ScriptableObject.CreateInstance<FireflyHushSettings>);
            settings.Validate();
            var panel = LoadOrCreate(Root + "UI/FireflyHushPanelSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PanelSettings>();
                value.themeStyleSheet = Load<ThemeStyleSheet>(Root + "UI/FireflyHushTheme.tss");
                value.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                value.referenceResolution = new Vector2Int(1920, 1080);
                value.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                value.match = .5f;
                return value;
            });

            // Prefab を作り直すと古いインスタンスが別名で残ることがあるので、前方一致で消す。
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name.StartsWith(ObjectPrefix, StringComparison.Ordinal)) { Object.DestroyImmediate(root); }
            }

            var stage = (GameObject)PrefabUtility.InstantiatePrefab(LoadStage());
            var view = stage.GetComponent<FireflyHushBoardView>();

            var camera = Object.FindAnyObjectByType<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 6;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.05f, .07f, .12f);

            var game = new GameObject(ObjectPrefix);
            var hud = game.AddComponent<PanelRenderer>();
            hud.panelSettings = panel;
            hud.visualTreeAsset = Load<VisualTreeAsset>(Root + "UI/Hud.uxml");
            var component = game.AddComponent<FireflyHushGame>();
            var serialized = new SerializedObject(component);
            Assign(serialized, "_settings", settings);
            Assign(serialized, "_hudRenderer", hud);
            Assign(serialized, "_camera", camera);
            Assign(serialized, "_view", view);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<FireflyHushCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "FIREFLY HUSH";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();

            var stages = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<FireflyHushBoardView>() != null) { stages++; }
            }

            if (stages != 1) { throw new InvalidOperationException($"盤のインスタンスが {stages} 個あります。"); }
            Debug.Log("[FireflyHushSceneBuilder] シーン生成完了");
        }

        // 配置の正は Stage Prefab。既にあれば調整を上書きしない。
        private static GameObject LoadStage()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(StagePath);
            if (existing != null) { return existing; }

            var material = LoadOrCreate(Art + "Unlit.mat", () => new Material(Shader.Find("Sprites/Default")));
            var layout = new BoardLayout(Pitch, HitSize);
            var root = new GameObject("FireflyHushStage");
            try
            {
                root.transform.position = new Vector3(BoardCenterX, 0, 0);
                var view = root.AddComponent<FireflyHushBoardView>();

                // 背景は画面の中央に置く。16:10〜21:9 の画面で端が見えないよう少し大きくする。
                var backdrop = Sprite(root.transform, "Backdrop", FireflyHushArtGenerator.BackdropFile, material, -20);
                backdrop.transform.localPosition = new Vector3(-BoardCenterX, 0, 0);
                backdrop.transform.localScale = new Vector3(1.35f, 1.15f, 1);

                var frame = Sprite(root.transform, "Frame", FireflyHushArtGenerator.FrameFile, material, -10);
                frame.color = new Color(.1f, .13f, .17f, .85f);
                var frameSize = layout.Extent + .5f;
                frame.transform.localScale = new Vector3(frameSize, frameSize, 1);

                var tiles = new List<Object>();
                var glows = new List<Object>();
                var marks = new List<Object>();
                var cells = new GameObject("Cells").transform;
                cells.SetParent(root.transform, false);
                for (var cell = 0; cell < LightsBoard.CellCount; cell++)
                {
                    var (x, y) = layout.CellCenter(cell);
                    var holder = new GameObject($"Cell{cell % LightsBoard.Size}_{cell / LightsBoard.Size}").transform;
                    holder.SetParent(cells, false);
                    holder.localPosition = new Vector3(x, y, 0);

                    var tile = Sprite(holder, "Tile", FireflyHushArtGenerator.TileFile, material, 0);
                    tile.transform.localScale = Vector3.one * HitSize;
                    tiles.Add(tile);
                    var glow = Sprite(holder, "Glow", FireflyHushArtGenerator.GlowFile, material, 5);
                    glow.transform.localScale = Vector3.one * 1.4f;
                    glow.color = new Color(1, 1, 1, 0);
                    glows.Add(glow);
                    var mark = Sprite(holder, "Mark", FireflyHushArtGenerator.MarkFile, material, 8);
                    mark.transform.localScale = Vector3.one * HitSize;
                    mark.enabled = false;
                    marks.Add(mark);
                }

                var cursor = Sprite(root.transform, "Cursor", FireflyHushArtGenerator.CursorFile, material, 10);
                cursor.transform.localScale = Vector3.one * (Pitch - .05f);
                cursor.enabled = false;

                var serialized = new SerializedObject(view);
                serialized.FindProperty("_pitch").floatValue = Pitch;
                serialized.FindProperty("_hitSize").floatValue = HitSize;
                AssignArray(serialized, "_tiles", tiles);
                AssignArray(serialized, "_glows", glows);
                AssignArray(serialized, "_marks", marks);
                Assign(serialized, "_cursor", cursor);
                serialized.ApplyModifiedPropertiesWithoutUndo();
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
