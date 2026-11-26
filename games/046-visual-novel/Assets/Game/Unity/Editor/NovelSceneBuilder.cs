#nullable enable
using System;
using System.IO;
using Game.Core;
using Game.Core.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>
    /// LANTERN WAKE の絵・Stage Prefab・UI 設定・シーンを作り、検証する。既にある Prefab と設定アセットは、
    /// シナリオの素材の表と一致する限り上書きしない（手で調整した値を残す）。
    /// 実行：<c>unity run games/046-visual-novel -- -executeMethod Game.Unity.Editor.NovelSceneBuilder.Build</c>
    /// </summary>
    public static class NovelSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/LanternWake.unity";
        private const string UnityRoot = "Assets/Game/Unity/";
        private const string ArtRoot = NovelArtGenerator.ArtDir;
        private const string PrefabPath = UnityRoot + "Prefabs/NovelStage.prefab";
        private const string Prefix = "Novel";

        [MenuItem("Game/Rebuild Lantern Wake Scene")]
        public static void Build()
        {
            NovelArtGenerator.GenerateIfMissing();
            EnsureFolder(UnityRoot + "Prefabs");
            EnsureFolder(UnityRoot + "UI");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null && AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/SampleScene.unity") != null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            WriteTextIfMissing(UnityRoot + "UI/NovelTheme.tss", "@import url(\"unity-theme://default\");\n");
            WriteTextIfMissing(
                UnityRoot + "UI/Hud.uxml",
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">\n    <ui:VisualElement name=\"hud\" style=\"flex-grow: 1; position: absolute; left: 0; right: 0; top: 0; bottom: 0;\" />\n</ui:UXML>\n");
            AssetDatabase.Refresh();

            var settings = LoadOrCreate(ScenarioBuildCheck.SettingsPath, ScriptableObject.CreateInstance<NovelSettings>);
            settings.Validate();
            var error2 = ScenarioBuildCheck.Check();
            if (error2 != null) { throw new InvalidOperationException(error2); }
            BuildStagePrefab();
            LoadOrCreate(UnityRoot + "UI/NovelPanelSettings.asset", () =>
            {
                var ps = ScriptableObject.CreateInstance<PanelSettings>();
                ps.themeStyleSheet = Required<ThemeStyleSheet>(UnityRoot + "UI/NovelTheme.tss");
                ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                ps.referenceResolution = new Vector2Int(1920, 1080);
                ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                ps.match = 0.5f;
                return ps;
            });
            AssetDatabase.SaveAssets();

            // シーンを開いてからアセットを読む（OpenScene のアンロードで参照が切れるのを防ぐ）。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            settings = Required<NovelSettings>(ScenarioBuildCheck.SettingsPath);
            var panelSettings = Required<PanelSettings>(UnityRoot + "UI/NovelPanelSettings.asset");
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name.StartsWith(Prefix, StringComparison.Ordinal) || root.GetComponent<NovelGame>() != null || root.GetComponent<NovelStage>() != null)
                {
                    Object.DestroyImmediate(root);
                }
            }

            var stageObj = (GameObject)PrefabUtility.InstantiatePrefab(Required<GameObject>(PrefabPath));
            var stage = stageObj.GetComponent<NovelStage>();
            var mismatch = stage.Mismatch();
            if (mismatch != null) { throw new InvalidOperationException("NovelStage: " + mismatch); }

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
            camera.backgroundColor = new Color(0.04f, 0.05f, 0.08f, 1f);

            var gameObj = new GameObject(Prefix + "Game");
            var hud = gameObj.AddComponent<PanelRenderer>();
            hud.panelSettings = panelSettings;
            hud.visualTreeAsset = Required<VisualTreeAsset>(UnityRoot + "UI/Hud.uxml");
            var game = gameObj.AddComponent<NovelGame>();
            var so = new SerializedObject(game);
            so.FindProperty("_settings").objectReferenceValue = settings;
            so.FindProperty("_scenario").objectReferenceValue = Required<TextAsset>(ScenarioBuildCheck.ScenarioPath);
            so.FindProperty("_hudRenderer").objectReferenceValue = hud;
            so.FindProperty("_camera").objectReferenceValue = camera;
            so.FindProperty("_stage").objectReferenceValue = stage;
            so.ApplyModifiedPropertiesWithoutUndo();
            gameObj.AddComponent<NovelCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "LANTERN WAKE";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();

            var games = 0;
            var stages = 0;
            foreach (var r in scene.GetRootGameObjects())
            {
                if (r.GetComponent<NovelGame>() != null) { games++; }
                if (r.GetComponent<NovelStage>() != null) { stages++; }
            }

            if (games != 1 || stages != 1) { throw new InvalidOperationException($"NovelGame が {games}・NovelStage が {stages} です。"); }
            Debug.Log("[NovelSceneBuilder] シーンを作り、検証しました。");
            if (Application.isBatchMode) { EditorApplication.Exit(0); }
        }

        private static void BuildStagePrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null && existing.GetComponent<NovelStage>()?.Mismatch() == null) { return; }
            var root = new GameObject("NovelStage");
            try
            {
                var stage = root.AddComponent<NovelStage>();
                var bgIds = Scenario.Backgrounds;
                var bgs = new Object[bgIds.Count];
                for (int i = 0; i < bgs.Length; i++) { bgs[i] = Required<Sprite>(ArtRoot + NovelArtGenerator.BackgroundFile(bgIds[i])); }
                var ps = Scenario.Portraits;
                var portraits = new Object[ps.Count];
                var portraitIds = new string[ps.Count];
                for (int i = 0; i < ps.Count; i++)
                {
                    portraits[i] = Required<Sprite>(ArtRoot + NovelArtGenerator.PortraitFile(ps[i].Key, ps[i].Value));
                    portraitIds[i] = NovelStage.PortraitId(ps[i].Key, ps[i].Value);
                }

                var background = Sprite(root.transform, "Background", (Sprite)bgs[0], Vector3.zero, -100);
                var slots = new Object[ScriptRunner.SlotCount];
                for (int i = 0; i < slots.Length; i++)
                {
                    var r = Sprite(root.transform, "Slot_" + (StageSlot)i, (Sprite)portraits[0], NovelStage.SlotPositions[i], 10 + i);
                    r.transform.localScale = new Vector3(NovelStage.PortraitScale, NovelStage.PortraitScale, 1f);
                    r.enabled = false;
                    slots[i] = r;
                }

                var so = new SerializedObject(stage);
                so.FindProperty("_background").objectReferenceValue = background;
                Assign(so.FindProperty("_slots"), slots);
                Strings(so.FindProperty("_backgroundIds"), bgIds);
                Assign(so.FindProperty("_backgrounds"), bgs);
                Strings(so.FindProperty("_portraitIds"), portraitIds);
                Assign(so.FindProperty("_portraits"), portraits);
                so.ApplyModifiedPropertiesWithoutUndo();
                var mismatch = stage.Mismatch();
                if (mismatch != null) { throw new InvalidOperationException("NovelStage を作れません: " + mismatch); }
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
