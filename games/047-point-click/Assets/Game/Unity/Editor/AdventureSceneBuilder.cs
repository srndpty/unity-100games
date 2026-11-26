#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Game.Core;
using Game.Core.Rules;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>
    /// TIDEWATCH の絵・Stage Prefab・UI 設定・設定アセット・シーンを作り、検証する。既にある Prefab と設定アセットは、
    /// ルール表と一致する限り上書きしない（手で調整した値を残す）。
    /// 実行：<c>unity run games/047-point-click -- -executeMethod Game.Unity.Editor.AdventureSceneBuilder.Build</c>
    /// </summary>
    public static class AdventureSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Tidewatch.unity";
        public const string SettingsPath = UnityRoot + "AdventureSettings.asset";
        public const string WalkthroughPath = "Assets/Game/Data/Walkthrough.txt";
        private const string UnityRoot = "Assets/Game/Unity/";
        private const string ArtRoot = AdventureArtGenerator.ArtDir;
        private const string PrefabPath = UnityRoot + "Prefabs/RoomStage.prefab";
        private const string Prefix = "Adventure";

        [MenuItem("Game/Rebuild Tidewatch Scene")]
        public static void Build()
        {
            var error = RulesBuildCheck.Check();
            if (error != null) { throw new InvalidOperationException(error); }
            var book = Adventure.Compile(Required<TextAsset>(RulesBuildCheck.RulesPath).text).Book!;
            AdventureArtGenerator.GenerateIfMissing(book);
            EnsureFolder(UnityRoot + "Prefabs");
            EnsureFolder(UnityRoot + "UI");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null && AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/SampleScene.unity") != null)
            {
                var moved = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(moved)) { throw new InvalidOperationException(moved); }
            }

            WriteTextIfMissing(UnityRoot + "UI/AdventureTheme.tss", "@import url(\"unity-theme://default\");\n");
            WriteTextIfMissing(
                UnityRoot + "UI/Hud.uxml",
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">\n    <ui:VisualElement name=\"hud\" style=\"flex-grow: 1; position: absolute; left: 0; right: 0; top: 0; bottom: 0;\" />\n</ui:UXML>\n");
            AssetDatabase.Refresh();

            var settings = LoadOrCreate(SettingsPath, ScriptableObject.CreateInstance<AdventureSettings>);
            settings.Validate();
            BuildStagePrefab(book);
            LoadOrCreate(UnityRoot + "UI/AdventurePanelSettings.asset", () =>
            {
                var ps = ScriptableObject.CreateInstance<PanelSettings>();
                ps.themeStyleSheet = Required<ThemeStyleSheet>(UnityRoot + "UI/AdventureTheme.tss");
                ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                ps.referenceResolution = new Vector2Int(1920, 1080);
                ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                ps.match = 1f;
                return ps;
            });
            AssetDatabase.SaveAssets();

            // シーンを開いてからアセットを読む（OpenScene のアンロードで参照が切れるのを防ぐ）。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            settings = Required<AdventureSettings>(SettingsPath);
            var panelSettings = Required<PanelSettings>(UnityRoot + "UI/AdventurePanelSettings.asset");
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name.StartsWith(Prefix, StringComparison.Ordinal) || root.name.StartsWith("RoomStage", StringComparison.Ordinal)
                    || root.GetComponent<AdventureGame>() != null || root.GetComponent<RoomStage>() != null)
                {
                    Object.DestroyImmediate(root);
                }
            }

            var stageObj = (GameObject)PrefabUtility.InstantiatePrefab(Required<GameObject>(PrefabPath));
            var stage = stageObj.GetComponent<RoomStage>();
            var mismatch = stage.Mismatch(book);
            if (mismatch != null) { throw new InvalidOperationException("RoomStage: " + mismatch); }

            var camera = Object.FindAnyObjectByType<Camera>();
            if (camera == null)
            {
                var camObj = new GameObject("Main Camera") { tag = "MainCamera" };
                camera = camObj.AddComponent<Camera>();
            }

            camera.orthographic = true;
            camera.orthographicSize = StageLayout.RoomHeight * 0.5f;
            camera.transform.position = new Vector3(StageLayout.RoomWidth * 0.5f, StageLayout.RoomHeight * 0.5f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.03f, 0.03f, 0.05f, 1f);

            var gameObj = new GameObject(Prefix + "Game");
            var hud = gameObj.AddComponent<PanelRenderer>();
            hud.panelSettings = panelSettings;
            hud.visualTreeAsset = Required<VisualTreeAsset>(UnityRoot + "UI/Hud.uxml");
            var game = gameObj.AddComponent<AdventureGame>();
            var so = new SerializedObject(game);
            so.FindProperty("_settings").objectReferenceValue = settings;
            so.FindProperty("_rules").objectReferenceValue = Required<TextAsset>(RulesBuildCheck.RulesPath);
            so.FindProperty("_hudRenderer").objectReferenceValue = hud;
            so.FindProperty("_camera").objectReferenceValue = camera;
            so.FindProperty("_stage").objectReferenceValue = stage;
            so.ApplyModifiedPropertiesWithoutUndo();
            var capture = new SerializedObject(gameObj.AddComponent<AdventureCaptureScenario>());
            capture.FindProperty("_walkthrough").objectReferenceValue = Required<TextAsset>(WalkthroughPath);
            capture.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "TIDEWATCH";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();

            var games = 0;
            var stages = 0;
            foreach (var r in scene.GetRootGameObjects())
            {
                if (r.GetComponent<AdventureGame>() != null) { games++; }
                if (r.GetComponent<RoomStage>() != null) { stages++; }
            }

            if (games != 1 || stages != 1) { throw new InvalidOperationException($"AdventureGame が {games}・RoomStage が {stages} です。"); }
            Debug.Log("[AdventureSceneBuilder] シーンを作り、検証しました。");
            if (Application.isBatchMode) { EditorApplication.Exit(0); }
        }

        /// <summary>Stage の Prefab を作る。物の位置と大きさはルール表の矩形から（照合と同じ <see cref="StageLayout"/> で）決める。</summary>
        public static GameObject CreateStage(RuleBook book)
        {
            if (book == null) { throw new ArgumentNullException(nameof(book)); }
            var root = new GameObject("RoomStage");
            var stage = root.AddComponent<RoomStage>();
            var roomRoots = new Object[book.Rooms.Count];
            var backgrounds = new Object[book.Rooms.Count];
            var roomIds = new string[book.Rooms.Count];
            for (int i = 0; i < book.Rooms.Count; i++)
            {
                var room = new GameObject("Room_" + book.Rooms[i].Id);
                room.transform.SetParent(root.transform, false);
                roomRoots[i] = room.transform;
                roomIds[i] = book.Rooms[i].Id;
                var bg = Sprite(room.transform, "Background", Required<Sprite>(ArtRoot + AdventureArtGenerator.BackgroundFile(book.Rooms[i].Id)), -100);
                bg.transform.localPosition = new Vector3(StageLayout.RoomWidth * 0.5f, StageLayout.RoomHeight * 0.5f, 0f);
                bg.transform.localScale = new Vector3(StageLayout.RoomWidth / bg.sprite.bounds.size.x, StageLayout.RoomHeight / bg.sprite.bounds.size.y, 1f);
                backgrounds[i] = bg;
            }

            var hotspots = new Object[book.Hotspots.Count];
            var hotspotIds = new string[book.Hotspots.Count];
            for (int i = 0; i < book.Hotspots.Count; i++)
            {
                var h = book.Hotspots[i];
                hotspotIds[i] = h.Id;
                var r = Sprite((Transform)roomRoots[h.Room], h.Id, Required<Sprite>(ArtRoot + AdventureArtGenerator.HotspotFile(h.Id)), i);
                r.transform.localPosition = StageLayout.Center(h.Area);
                r.transform.localScale = StageLayout.FitScale(r.sprite, h.Area);
                r.enabled = h.InitiallyVisible;
                hotspots[i] = r;
            }

            var lamp = book.Hotspots[book.HotspotIndex("lighthouse.lamp")];
            var lit = Sprite((Transform)roomRoots[lamp.Room], "LampLit", Required<Sprite>(ArtRoot + AdventureArtGenerator.LampLitFile), book.Hotspots.Count);
            lit.transform.localPosition = StageLayout.Center(lamp.Area);
            lit.transform.localScale = StageLayout.FitScale(lit.sprite, lamp.Area);
            lit.enabled = false;

            var frames = new Object[3];
            for (int f = 0; f < frames.Length; f++) { frames[f] = Required<Sprite>(ArtRoot + AdventureArtGenerator.PlayerFile(f)); }
            var player = Sprite(root.transform, "Player", (Sprite)frames[0], RoomStage.PlayerOrder);
            player.transform.localPosition = StageLayout.Feet(book.Rooms[book.StartRoom].Start);
            player.enabled = false;

            var itemIds = new string[book.Items.Count];
            var icons = new Object[book.Items.Count];
            for (int i = 0; i < book.Items.Count; i++)
            {
                itemIds[i] = book.Items[i].Id;
                icons[i] = Required<Sprite>(ArtRoot + AdventureArtGenerator.IconFile(book.Items[i].Id));
            }

            var so = new SerializedObject(stage);
            Strings(so.FindProperty("_roomIds"), roomIds);
            Assign(so.FindProperty("_roomRoots"), roomRoots);
            Assign(so.FindProperty("_backgrounds"), backgrounds);
            Strings(so.FindProperty("_hotspotIds"), hotspotIds);
            Assign(so.FindProperty("_hotspots"), hotspots);
            so.FindProperty("_player").objectReferenceValue = player;
            Assign(so.FindProperty("_playerFrames"), frames);
            so.FindProperty("_lampLit").objectReferenceValue = lit;
            Strings(so.FindProperty("_itemIds"), itemIds);
            Assign(so.FindProperty("_itemIcons"), icons);
            so.ApplyModifiedPropertiesWithoutUndo();
            return root;
        }

        private static void BuildStagePrefab(RuleBook book)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null && existing.GetComponent<RoomStage>()?.Mismatch(book) == null) { return; }
            var root = CreateStage(book);
            try
            {
                var mismatch = root.GetComponent<RoomStage>().Mismatch(book);
                if (mismatch != null) { throw new InvalidOperationException("RoomStage を作れません: " + mismatch); }
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static SpriteRenderer Sprite(Transform parent, string name, Sprite sprite, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
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

        private static void Strings(SerializedProperty prop, IReadOnlyList<string> values)
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
