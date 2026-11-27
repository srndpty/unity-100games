#nullable enable
using System;
using System.IO;
using Game.Core.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>
    /// 夏、流星群の夜まで の絵・Stage Prefab・UI 設定・シーンを作り、検証する。既にある Prefab と設定アセットは、
    /// 台本の素材の表と一致する限り上書きしない（手で調整した値を残す）。
    /// 実行：<c>unity run games/050-dating-sim -- -executeMethod Game.Unity.Editor.SummerSceneBuilder.Build</c>
    /// </summary>
    public static class SummerSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/MeteorSummer.unity";
        private const string UnityRoot = "Assets/Game/Unity/";
        private const string ArtRoot = SummerArtGenerator.ArtDir;
        private const string PrefabPath = UnityRoot + "Prefabs/SummerStage.prefab";
        private const string Prefix = "Summer";

        [MenuItem("Game/Rebuild Meteor Summer Scene")]
        public static void Build()
        {
            SummerArtGenerator.GenerateIfMissing();
            EnsureFolder(UnityRoot + "Prefabs");
            EnsureFolder(UnityRoot + "UI");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null && AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/SampleScene.unity") != null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            WriteTextIfMissing(UnityRoot + "UI/SummerTheme.tss", "@import url(\"unity-theme://default\");\n");
            WriteTextIfMissing(
                UnityRoot + "UI/Hud.uxml",
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">\n    <ui:VisualElement name=\"hud\" style=\"flex-grow: 1; position: absolute; left: 0; right: 0; top: 0; bottom: 0;\" />\n</ui:UXML>\n");
            AssetDatabase.Refresh();

            var settings = LoadOrCreate(SummerBuildCheck.SettingsPath, ScriptableObject.CreateInstance<SummerSettings>);
            settings.Validate();
            var error2 = SummerBuildCheck.Check();
            if (error2 != null) { throw new InvalidOperationException(error2); }
            var book = SummerBuildCheck.LoadBook();
            BuildStagePrefab(book);
            LoadOrCreate(UnityRoot + "UI/SummerPanelSettings.asset", () =>
            {
                var ps = ScriptableObject.CreateInstance<PanelSettings>();
                ps.themeStyleSheet = Required<ThemeStyleSheet>(UnityRoot + "UI/SummerTheme.tss");
                ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                ps.referenceResolution = new Vector2Int(1920, 1080);
                ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                ps.match = 0.5f;
                return ps;
            });
            AssetDatabase.SaveAssets();

            // シーンを開いてからアセットを読む（OpenScene のアンロードで参照が切れるのを防ぐ）。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            settings = Required<SummerSettings>(SummerBuildCheck.SettingsPath);
            var panelSettings = Required<PanelSettings>(UnityRoot + "UI/SummerPanelSettings.asset");
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name.StartsWith(Prefix, StringComparison.Ordinal) || root.GetComponent<SummerGame>() != null || root.GetComponent<SummerStage>() != null)
                {
                    Object.DestroyImmediate(root);
                }
            }

            var stageObj = (GameObject)PrefabUtility.InstantiatePrefab(Required<GameObject>(PrefabPath));
            var stage = stageObj.GetComponent<SummerStage>();
            var mismatch = stage.Mismatch(book);
            if (mismatch != null) { throw new InvalidOperationException("SummerStage: " + mismatch); }

            var camera = Object.FindAnyObjectByType<Camera>();
            if (camera == null)
            {
                var camObj = new GameObject("Main Camera") { tag = "MainCamera" };
                camera = camObj.AddComponent<Camera>();
            }

            camera.orthographic = true;
            camera.orthographicSize = 5.625f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.03f, 0.04f, 0.10f, 1f);

            var gameObj = new GameObject(Prefix + "Game");
            var hud = gameObj.AddComponent<PanelRenderer>();
            hud.panelSettings = panelSettings;
            hud.visualTreeAsset = Required<VisualTreeAsset>(UnityRoot + "UI/Hud.uxml");
            var game = gameObj.AddComponent<SummerGame>();
            var so = new SerializedObject(game);
            so.FindProperty("_settings").objectReferenceValue = settings;
            so.FindProperty("_book").objectReferenceValue = Required<TextAsset>(SummerBuildCheck.BookPath);
            so.FindProperty("_hudRenderer").objectReferenceValue = hud;
            so.FindProperty("_camera").objectReferenceValue = camera;
            so.FindProperty("_stage").objectReferenceValue = stage;
            so.ApplyModifiedPropertiesWithoutUndo();
            gameObj.AddComponent<SummerCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "Meteor Summer";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();

            var games = 0;
            var stages = 0;
            foreach (var r in scene.GetRootGameObjects())
            {
                if (r.GetComponent<SummerGame>() != null) { games++; }
                if (r.GetComponent<SummerStage>() != null) { stages++; }
            }

            if (games != 1 || stages != 1) { throw new InvalidOperationException($"SummerGame が {games}・SummerStage が {stages} です。"); }
            Debug.Log("[SummerSceneBuilder] シーンを作り、検証しました。");
            if (Application.isBatchMode) { EditorApplication.Exit(0); }
        }

        private static void BuildStagePrefab(SummerBook book)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null && existing.GetComponent<SummerStage>()?.Mismatch(book) == null) { return; }
            var root = new GameObject("SummerStage");
            try
            {
                var stage = root.AddComponent<SummerStage>();
                var bgIds = new string[book.Backgrounds.Count];
                var bgs = new Object[bgIds.Length];
                for (int i = 0; i < bgs.Length; i++)
                {
                    bgIds[i] = book.Backgrounds[i].Id;
                    bgs[i] = Required<Sprite>(ArtRoot + SummerArtGenerator.BackgroundFile(bgIds[i]));
                }

                var ps = SummerStage.PortraitsOf(book);
                var portraits = new Object[ps.Count];
                var portraitIds = new string[ps.Count];
                for (int i = 0; i < ps.Count; i++)
                {
                    portraits[i] = Required<Sprite>(ArtRoot + SummerArtGenerator.PortraitFile(ps[i].Character, ps[i].Face));
                    portraitIds[i] = SummerStage.PortraitId(ps[i].Character, ps[i].Face);
                }

                var background = Sprite(root.transform, "Background", (Sprite)bgs[0], Vector3.zero, -100);
                var portrait = Sprite(root.transform, "Portrait", (Sprite)portraits[0], SummerStage.PortraitPosition, 10);
                portrait.transform.localScale = new Vector3(SummerStage.PortraitScale, SummerStage.PortraitScale, 1f);
                portrait.enabled = false;

                var so = new SerializedObject(stage);
                so.FindProperty("_background").objectReferenceValue = background;
                so.FindProperty("_portrait").objectReferenceValue = portrait;
                Strings(so.FindProperty("_backgroundIds"), bgIds);
                Assign(so.FindProperty("_backgrounds"), bgs);
                Strings(so.FindProperty("_portraitIds"), portraitIds);
                Assign(so.FindProperty("_portraits"), portraits);
                so.ApplyModifiedPropertiesWithoutUndo();
                var mismatch = stage.Mismatch(book);
                if (mismatch != null) { throw new InvalidOperationException("SummerStage を作れません: " + mismatch); }
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
            for (int i = 0; i < values.Length; i++) { prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i]; }
        }

        private static void Strings(SerializedProperty prop, System.Collections.Generic.IReadOnlyList<string> values)
        {
            prop.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++) { prop.GetArrayElementAtIndex(i).stringValue = values[i]; }
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
