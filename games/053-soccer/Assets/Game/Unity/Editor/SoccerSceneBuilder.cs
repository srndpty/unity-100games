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
    /// MINI PITCH の絵・Stage Prefab・UI 設定・シーンを作り、検証する。既にある Prefab と設定アセットは、
    /// Stage の照合（Mismatch）を通る限り上書きしない（手で調整した値を残す）。
    /// 実行：<c>unity run games/053-soccer -- -executeMethod Game.Unity.Editor.SoccerSceneBuilder.Build</c>
    /// </summary>
    public static class SoccerSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/MiniPitch.unity";
        private const string UnityRoot = "Assets/Game/Unity/";
        private const string ArtRoot = SoccerArtGenerator.ArtDir;
        private const string PrefabPath = UnityRoot + "Prefabs/SoccerStage.prefab";
        private const string Prefix = "Soccer";

        [MenuItem("Game/Rebuild Mini Pitch Scene")]
        public static void Build()
        {
            SoccerArtGenerator.GenerateIfMissing();
            EnsureFolder(UnityRoot + "Prefabs");
            EnsureFolder(UnityRoot + "UI");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null && AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/SampleScene.unity") != null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            WriteTextIfMissing(UnityRoot + "UI/SoccerTheme.tss", "@import url(\"unity-theme://default\");\n");
            WriteTextIfMissing(
                UnityRoot + "UI/Hud.uxml",
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">\n    <ui:VisualElement name=\"hud\" style=\"flex-grow: 1; position: absolute; left: 0; right: 0; top: 0; bottom: 0;\" />\n</ui:UXML>\n");
            AssetDatabase.Refresh();

            var settings = LoadOrCreate(SoccerBuildCheck.SettingsPath, ScriptableObject.CreateInstance<SoccerSettings>);
            settings.Validate();
            var error2 = SoccerBuildCheck.Check();
            if (error2 != null) { throw new InvalidOperationException(error2); }
            BuildStagePrefab();
            LoadOrCreate(UnityRoot + "UI/SoccerPanelSettings.asset", () =>
            {
                var ps = ScriptableObject.CreateInstance<PanelSettings>();
                ps.themeStyleSheet = Required<ThemeStyleSheet>(UnityRoot + "UI/SoccerTheme.tss");
                ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                ps.referenceResolution = new Vector2Int(1920, 1080);
                ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                ps.match = 0.5f;
                return ps;
            });
            AssetDatabase.SaveAssets();

            // シーンを開いてからアセットを読む（OpenScene のアンロードで参照が切れるのを防ぐ）。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            settings = Required<SoccerSettings>(SoccerBuildCheck.SettingsPath);
            var panelSettings = Required<PanelSettings>(UnityRoot + "UI/SoccerPanelSettings.asset");
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name.StartsWith(Prefix, StringComparison.Ordinal) || root.GetComponent<SoccerGame>() != null || root.GetComponent<SoccerStage>() != null)
                {
                    Object.DestroyImmediate(root);
                }
            }

            var stageObj = (GameObject)PrefabUtility.InstantiatePrefab(Required<GameObject>(PrefabPath));
            var stage = stageObj.GetComponent<SoccerStage>();
            var mismatch = stage.Mismatch();
            if (mismatch != null) { throw new InvalidOperationException("SoccerStage: " + mismatch); }

            var camera = Object.FindAnyObjectByType<Camera>();
            if (camera == null)
            {
                var camObj = new GameObject("Main Camera") { tag = "MainCamera" };
                camera = camObj.AddComponent<Camera>();
            }

            camera.orthographic = true;
            camera.orthographicSize = 12.6f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.10f, 0.22f, 0.13f, 1f);

            var gameObj = new GameObject(Prefix + "Game");
            var hud = gameObj.AddComponent<PanelRenderer>();
            hud.panelSettings = panelSettings;
            hud.visualTreeAsset = Required<VisualTreeAsset>(UnityRoot + "UI/Hud.uxml");
            var game = gameObj.AddComponent<SoccerGame>();
            var so = new SerializedObject(game);
            so.FindProperty("_settings").objectReferenceValue = settings;
            so.FindProperty("_hudRenderer").objectReferenceValue = hud;
            so.FindProperty("_camera").objectReferenceValue = camera;
            so.FindProperty("_stage").objectReferenceValue = stage;
            so.ApplyModifiedPropertiesWithoutUndo();
            gameObj.AddComponent<SoccerCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "Mini Pitch";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();

            var games = 0;
            var stages = 0;
            foreach (var r in scene.GetRootGameObjects())
            {
                if (r.GetComponent<SoccerGame>() != null) { games++; }
                if (r.GetComponent<SoccerStage>() != null) { stages++; }
            }

            if (games != 1 || stages != 1) { throw new InvalidOperationException($"SoccerGame が {games}・SoccerStage が {stages} です。"); }
            Debug.Log("[SoccerSceneBuilder] シーンを作り、検証しました。");
            if (Application.isBatchMode) { EditorApplication.Exit(0); }
        }

        private static void BuildStagePrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null && existing.GetComponent<SoccerStage>()?.Mismatch() == null) { return; }
            var root = new GameObject("SoccerStage");
            try
            {
                var stage = root.AddComponent<SoccerStage>();
                var pitch = Sprite(root.transform, "Pitch", Required<Sprite>(ArtRoot + SoccerArtGenerator.PitchFile), Vector3.zero, -100);
                var marker = Sprite(root.transform, "Marker", Required<Sprite>(ArtRoot + SoccerArtGenerator.RingFile), Vector3.zero, 5);
                marker.transform.localScale = Vector3.one * PitchProjection.PlayerScale;
                var players = new Object[Match.PlayerCount];
                var facings = new Object[Match.PlayerCount];
                var targets = new Object[Match.PlayerCount];
                for (int i = 0; i < Match.PlayerCount; i++)
                {
                    var body = Sprite(root.transform, "Player" + i, Required<Sprite>(ArtRoot + SoccerArtGenerator.PlayerFile), Vector3.zero, 10);
                    body.color = SoccerStage.ColorOf(i);
                    body.transform.localScale = Vector3.one * PitchProjection.PlayerScale;
                    var facing = new GameObject("Facing");
                    facing.transform.SetParent(body.transform, false);
                    var arrow = Sprite(facing.transform, "Arrow", Required<Sprite>(ArtRoot + SoccerArtGenerator.FacingFile), new Vector3(0.42f, 0f, 0f), 11);
                    arrow.color = Color.white;
                    players[i] = body;
                    facings[i] = facing.transform;
                    var target = Sprite(root.transform, "Target" + i, Required<Sprite>(ArtRoot + SoccerArtGenerator.TargetFile), Vector3.zero, 4);
                    target.enabled = false;
                    targets[i] = target;
                }

                var shadow = Sprite(root.transform, "Shadow", Required<Sprite>(ArtRoot + SoccerArtGenerator.ShadowFile), Vector3.zero, 20);
                var ball = Sprite(root.transform, "Ball", Required<Sprite>(ArtRoot + SoccerArtGenerator.BallFile), Vector3.zero, 30);

                var so = new SerializedObject(stage);
                so.FindProperty("_pitch").objectReferenceValue = pitch;
                Assign(so.FindProperty("_players"), players);
                Assign(so.FindProperty("_facings"), facings);
                so.FindProperty("_ball").objectReferenceValue = ball;
                so.FindProperty("_shadow").objectReferenceValue = shadow;
                so.FindProperty("_marker").objectReferenceValue = marker;
                Assign(so.FindProperty("_targets"), targets);
                so.ApplyModifiedPropertiesWithoutUndo();
                var mismatch = stage.Mismatch();
                if (mismatch != null) { throw new InvalidOperationException("SoccerStage を作れません: " + mismatch); }
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
