#nullable enable
using System;
using System.IO;
using Game.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>
    /// 石庭の絵・Stage Prefab・UI 設定・シーンを作り、検証する。既にある Prefab と設定アセットは、
    /// Stage の照合（Mismatch）を通る限り上書きしない（手で調整した値を残す）。
    /// 実行：<c>unity run games/055-go -- -executeMethod Game.Unity.Editor.SekiteiSceneBuilder.Build</c>
    /// </summary>
    public static class SekiteiSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Sekitei.unity";
        private const string UnityRoot = "Assets/Game/Unity/";
        private const string ArtRoot = SekiteiArtGenerator.ArtDir;
        private const string PrefabPath = UnityRoot + "Prefabs/SekiteiStage.prefab";
        private const string Prefix = "Sekitei";

        [MenuItem("Game/Rebuild Sekitei Scene")]
        public static void Build()
        {
            SekiteiArtGenerator.GenerateIfMissing();
            EnsureFolder(UnityRoot + "Prefabs");
            EnsureFolder(UnityRoot + "UI");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null && AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/SampleScene.unity") != null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            WriteTextIfMissing(UnityRoot + "UI/SekiteiTheme.tss", "@import url(\"unity-theme://default\");\n");
            WriteTextIfMissing(
                UnityRoot + "UI/Hud.uxml",
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">\n    <ui:VisualElement name=\"hud\" style=\"flex-grow: 1; position: absolute; left: 0; right: 0; top: 0; bottom: 0;\" />\n</ui:UXML>\n");
            AssetDatabase.Refresh();

            var settings = LoadOrCreate(SekiteiBuildCheck.SettingsPath, ScriptableObject.CreateInstance<SekiteiSettings>);
            settings.Validate();
            var error2 = SekiteiBuildCheck.Check();
            if (error2 != null) { throw new InvalidOperationException(error2); }
            BuildStagePrefab();
            LoadOrCreate(UnityRoot + "UI/SekiteiPanelSettings.asset", () =>
            {
                var ps = ScriptableObject.CreateInstance<PanelSettings>();
                ps.themeStyleSheet = Required<ThemeStyleSheet>(UnityRoot + "UI/SekiteiTheme.tss");
                ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                ps.referenceResolution = new Vector2Int(1920, 1080);
                ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                ps.match = 1f;
                return ps;
            });
            AssetDatabase.SaveAssets();

            // シーンを開いてからアセットを読む（OpenScene のアンロードで参照が切れるのを防ぐ）。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            settings = Required<SekiteiSettings>(SekiteiBuildCheck.SettingsPath);
            var panelSettings = Required<PanelSettings>(UnityRoot + "UI/SekiteiPanelSettings.asset");
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name.StartsWith(Prefix, StringComparison.Ordinal) || root.GetComponent<SekiteiGame>() != null || root.GetComponent<SekiteiStage>() != null)
                {
                    Object.DestroyImmediate(root);
                }
            }

            var stageObj = (GameObject)PrefabUtility.InstantiatePrefab(Required<GameObject>(PrefabPath));
            var stage = stageObj.GetComponent<SekiteiStage>();
            var mismatch = stage.Mismatch();
            if (mismatch != null) { throw new InvalidOperationException("SekiteiStage: " + mismatch); }

            var camera = Object.FindAnyObjectByType<Camera>();
            if (camera == null)
            {
                var camObj = new GameObject("Main Camera") { tag = "MainCamera" };
                camera = camObj.AddComponent<Camera>();
            }

            camera.orthographic = true;
            camera.orthographicSize = BoardProjection.CameraSize;
            camera.transform.position = BoardProjection.CameraPosition;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.16f, 0.17f, 0.15f, 1f);

            var gameObj = new GameObject(Prefix + "Game");
            var hud = gameObj.AddComponent<PanelRenderer>();
            hud.panelSettings = panelSettings;
            hud.visualTreeAsset = Required<VisualTreeAsset>(UnityRoot + "UI/Hud.uxml");
            var game = gameObj.AddComponent<SekiteiGame>();
            var so = new SerializedObject(game);
            so.FindProperty("_settings").objectReferenceValue = settings;
            so.FindProperty("_hudRenderer").objectReferenceValue = hud;
            so.FindProperty("_camera").objectReferenceValue = camera;
            so.FindProperty("_stage").objectReferenceValue = stage;
            so.ApplyModifiedPropertiesWithoutUndo();
            gameObj.AddComponent<SekiteiCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "Sekitei";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();

            var games = 0;
            var stages = 0;
            foreach (var r in scene.GetRootGameObjects())
            {
                if (r.GetComponent<SekiteiGame>() != null) { games++; }
                if (r.GetComponent<SekiteiStage>() != null) { stages++; }
            }

            if (games != 1 || stages != 1) { throw new InvalidOperationException($"SekiteiGame が {games}・SekiteiStage が {stages} です。"); }
            Debug.Log("[SekiteiSceneBuilder] シーンを作り、検証しました。");
            if (Application.isBatchMode) { EditorApplication.Exit(0); }
        }

        private static void BuildStagePrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null && existing.GetComponent<SekiteiStage>()?.Mismatch() == null) { return; }
            var root = new GameObject("SekiteiStage");
            try
            {
                var stage = root.AddComponent<SekiteiStage>();
                var black = Required<Sprite>(ArtRoot + SekiteiArtGenerator.BlackFile);
                var white = Required<Sprite>(ArtRoot + SekiteiArtGenerator.WhiteFile);
                var board = Sprite(root.transform, "Board", Required<Sprite>(ArtRoot + SekiteiArtGenerator.BoardFile), Vector3.zero, SekiteiStage.BoardOrder);
                var marksRoot = new GameObject("Marks").transform;
                marksRoot.SetParent(root.transform, false);
                var stonesRoot = new GameObject("Stones").transform;
                stonesRoot.SetParent(root.transform, false);
                var stones = new Object[Points.Count];
                var marks = new Object[Points.Count];
                for (var p = 0; p < Points.Count; p++)
                {
                    var mark = Sprite(marksRoot, "Mark_" + Points.Name(p), Required<Sprite>(ArtRoot + SekiteiArtGenerator.MarkFile), BoardProjection.World(p), SekiteiStage.MarkOrder);
                    mark.enabled = false;
                    marks[p] = mark;
                    var stone = Sprite(stonesRoot, "Stone_" + Points.Name(p), black, BoardProjection.World(p), SekiteiStage.StoneOrder);
                    stone.enabled = false;
                    stones[p] = stone;
                }

                var last = Sprite(root.transform, "LastMove", Required<Sprite>(ArtRoot + SekiteiArtGenerator.LastFile), Vector3.zero, SekiteiStage.LastOrder);
                last.enabled = false;
                var cursor = Sprite(root.transform, "Cursor", Required<Sprite>(ArtRoot + SekiteiArtGenerator.CursorFile), Vector3.zero, SekiteiStage.CursorOrder);
                cursor.enabled = false;
                var ghost = Sprite(root.transform, "Ghost", black, Vector3.zero, SekiteiStage.StoneOrder);
                ghost.color = new Color(1f, 1f, 1f, 0.45f);
                ghost.enabled = false;

                var so = new SerializedObject(stage);
                so.FindProperty("_board").objectReferenceValue = board;
                Assign(so.FindProperty("_stones"), stones);
                Assign(so.FindProperty("_marks"), marks);
                so.FindProperty("_last").objectReferenceValue = last;
                so.FindProperty("_cursor").objectReferenceValue = cursor;
                so.FindProperty("_ghost").objectReferenceValue = ghost;
                so.FindProperty("_blackStone").objectReferenceValue = black;
                so.FindProperty("_whiteStone").objectReferenceValue = white;
                so.ApplyModifiedPropertiesWithoutUndo();
                var mismatch = stage.Mismatch();
                if (mismatch != null) { throw new InvalidOperationException("SekiteiStage を作れません: " + mismatch); }
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static SpriteRenderer Sprite(Transform parent, string name, Sprite sprite, Vector3 position, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = sprite;
            r.sortingOrder = order;
            return r;
        }

        private static void Assign(SerializedProperty prop, Object[] values)
        {
            prop.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++) { prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i]; }
        }

        private static T LoadOrCreate<T>(string path, Func<T> factory)
            where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) { return existing; }
            EnsureFolder(Path.GetDirectoryName(path)!.Replace('\\', '/'));
            var created = factory();
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

        private static T Required<T>(string path)
            where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            return asset != null ? asset : throw new InvalidOperationException($"アセットがありません: {path}");
        }

        private static void WriteTextIfMissing(string path, string text)
        {
            if (File.Exists(path)) { return; }
            File.WriteAllText(path, text);
            AssetDatabase.ImportAsset(path);
        }

        private static void EnsureFolder(string path)
        {
            path = path.TrimEnd('/');
            if (AssetDatabase.IsValidFolder(path)) { return; }
            var slash = path.LastIndexOf('/');
            var parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
