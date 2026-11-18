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
    public static class MazeSceneBuilder
    {
        private const string Root = "Assets/Game/Unity/";
        private const string Art = MazeArtGenerator.ArtRoot;
        private const string ScenePath = "Assets/Scenes/Maze.unity";
        private static readonly string[] _catFiles = { "CatTora.png", "CatMike.png", "CatKuro.png", "CatShiro.png" };
        private static readonly Color[] _markerColors =
        {
            new(.95f, .6f, .2f), new(.98f, .9f, .75f), new(.65f, .55f, .95f), new(.55f, .8f, 1f),
        };

        [MenuItem("Game/Rebuild Maze Scene")]
        public static void Build()
        {
            MazeArtGenerator.GenerateIfMissing();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            // シーンを開くと未使用アセットがアンロードされるので、アセットの読み込みはこの後に行う。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = LoadOrCreate(Root + "MazeSettings.asset", ScriptableObject.CreateInstance<MazeSettings>);
            settings.Validate();
            var panel = LoadOrCreate(Root + "UI/MazePanelSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PanelSettings>();
                value.themeStyleSheet = Load<ThemeStyleSheet>(Root + "UI/MazeTheme.tss");
                value.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                value.referenceResolution = new Vector2Int(1920, 1080);
                value.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                value.match = .5f;
                return value;
            });

            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "Maze" || root.name == "MazeStage") { Object.DestroyImmediate(root); }
            }

            var stage = (GameObject)PrefabUtility.InstantiatePrefab(LoadStage(settings.Game));
            var view = stage.GetComponent<MazeView>();
            view.Render(null, settings.Game.DyingTicks, false);

            var camera = Object.FindAnyObjectByType<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 8.6f;
            camera.transform.position = new Vector3(0, .9f, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.07f, .05f, .05f);

            var game = new GameObject("Maze");
            var hud = game.AddComponent<PanelRenderer>();
            hud.panelSettings = panel;
            hud.visualTreeAsset = Load<VisualTreeAsset>(Root + "UI/Hud.uxml");
            var component = game.AddComponent<MazeGame>();
            var serialized = new SerializedObject(component);
            Assign(serialized, "_settings", settings);
            Assign(serialized, "_hudRenderer", hud);
            Assign(serialized, "_camera", camera);
            Assign(serialized, "_view", view);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<MazeCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "NIBBLE MAZE";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();
            Debug.Log("[MazeSceneBuilder] シーン生成完了");
        }

        // 配置の正は Stage Prefab。既にあれば調整を上書きしない。
        private static GameObject LoadStage(MazeGameConfig config)
        {
            var path = Art + "MazeStage.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) { return existing; }

            var material = LoadOrCreate(Art + "Unlit.mat", () => new Material(Shader.Find("Sprites/Default")));
            var graph = new MazeGraph(MazeLayout.CreateDefault());
            var root = new GameObject("MazeStage");
            try
            {
                var view = root.AddComponent<MazeView>();
                Sprite(root.transform, "Board", "Board.png", material, -10, false);
                var maskObject = new GameObject("PlayfieldMask");
                maskObject.transform.SetParent(root.transform, false);
                var mask = maskObject.AddComponent<SpriteMask>();
                mask.sprite = Load<Sprite>(Art + "Mask.png");
                maskObject.transform.localScale = new Vector3(graph.Width, graph.Height, 1);

                var pellets = new List<Object>();
                var cells = new List<int>();
                var pelletRoot = new GameObject("Pellets").transform;
                pelletRoot.SetParent(root.transform, false);
                for (var cell = 0; cell < graph.CellCount; cell++)
                {
                    var point = graph.Point(cell);
                    var kind = graph.Layout.PelletAt(point.X, point.Y);
                    if (kind == Pellet.None) { continue; }
                    var pellet = Sprite(pelletRoot, "Pellet" + point.X + "_" + point.Y, kind == Pellet.Dot ? "Dot.png" : "Power.png", material, 0, false);
                    var size = kind == Pellet.Dot ? .2f : .7f;
                    pellet.transform.localScale = new Vector3(size, size, 1);
                    pellet.transform.localPosition = new Vector3(point.X - ((graph.Width - 1) / 2f), ((graph.Height - 1) / 2f) - point.Y, 0);
                    pellets.Add(pellet);
                    cells.Add(cell);
                }

                var cats = new List<Object>();
                var wraps = new List<Object>();
                var markers = new List<Object>();
                var faces = new List<Object>();
                for (var i = 0; i < _catFiles.Length; i++)
                {
                    cats.Add(Sprite(root.transform, "Cat" + i, _catFiles[i], material, 10, true));
                    wraps.Add(Sprite(root.transform, "Cat" + i + "Wrap", _catFiles[i], material, 10, true));
                    var marker = Sprite(root.transform, "Marker" + i, "Marker.png", material, 5, false);
                    marker.color = _markerColors[i];
                    marker.enabled = false;
                    markers.Add(marker);
                    faces.Add(Load<Sprite>(Art + _catFiles[i]));
                }

                var serialized = new SerializedObject(view);
                Assign(serialized, "_mouse", Sprite(root.transform, "Mouse", "Mouse.png", material, 12, true));
                Assign(serialized, "_mouseWrap", Sprite(root.transform, "MouseWrap", "Mouse.png", material, 12, true));
                AssignArray(serialized, "_cats", cats);
                AssignArray(serialized, "_catWraps", wraps);
                AssignArray(serialized, "_markers", markers);
                AssignArray(serialized, "_pellets", pellets);
                AssignArray(serialized, "_catFaces", faces);
                Assign(serialized, "_dazedFace", Load<Sprite>(Art + "CatDazed.png"));
                Assign(serialized, "_returningFace", Load<Sprite>(Art + "CatReturning.png"));
                var cellProperty = serialized.FindProperty("_pelletCells");
                cellProperty.arraySize = cells.Count;
                for (var i = 0; i < cells.Count; i++) { cellProperty.GetArrayElementAtIndex(i).intValue = cells[i]; }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                view.Render(null, config.DyingTicks, false);
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static SpriteRenderer Sprite(Transform parent, string name, string file, Material material, int order, bool masked)
        {
            var item = new GameObject(name);
            item.transform.SetParent(parent, false);
            var renderer = item.AddComponent<SpriteRenderer>();
            renderer.sprite = Load<Sprite>(Art + file);
            renderer.sharedMaterial = material;
            renderer.sortingOrder = order;
            if (masked) { renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask; }
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
