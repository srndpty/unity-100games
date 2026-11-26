#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Game.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>
    /// THORNWILD KEEPERS の絵・タイル・Stage Prefab・UI 設定・シーンを作り、検証する。既にある Prefab と設定アセットは、
    /// データと一致する限り上書きしない（手で調整した値を残す）。
    /// 実行：<c>unity run games/045-monster-collector -- -executeMethod Game.Unity.Editor.KeeperSceneBuilder.Build</c>
    /// </summary>
    public static class KeeperSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Thornwild.unity";
        private const string UnityRoot = "Assets/Game/Unity/";
        private const string DataRoot = "Assets/Game/Data/";
        private const string ArtRoot = KeeperArtGenerator.ArtDir;
        private const string PrefabRoot = UnityRoot + "Prefabs/";
        private const string Prefix = "Keeper";

        [MenuItem("Game/Rebuild Thornwild Scene")]
        public static void Build()
        {
            KeeperArtGenerator.GenerateIfMissing();
            EnsureFolder(PrefabRoot);
            EnsureFolder(UnityRoot + "Tiles");
            EnsureFolder(UnityRoot + "UI");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null && AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/SampleScene.unity") != null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            WriteTextIfMissing(UnityRoot + "UI/KeeperTheme.tss", "@import url(\"unity-theme://default\");\n");
            WriteTextIfMissing(
                UnityRoot + "UI/Hud.uxml",
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">\n    <ui:VisualElement name=\"hud\" style=\"flex-grow: 1; position: absolute; left: 0; right: 0; top: 0; bottom: 0;\" />\n</ui:UXML>\n");
            AssetDatabase.Refresh();

            var settings = LoadOrCreate(UnityRoot + "KeeperSettings.asset", ScriptableObject.CreateInstance<KeeperSettings>);
            settings.Validate();
            var data = LoadData();
            BuildStagePrefabs(data);
            var panelSettings = LoadOrCreate(UnityRoot + "UI/KeeperPanelSettings.asset", () =>
            {
                var ps = ScriptableObject.CreateInstance<PanelSettings>();
                ps.themeStyleSheet = Required<ThemeStyleSheet>(UnityRoot + "UI/KeeperTheme.tss");
                ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                ps.referenceResolution = new Vector2Int(1920, 1080);
                ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                ps.match = 0.5f;
                return ps;
            });
            AssetDatabase.SaveAssets();

            // シーンを開いてからアセットを読む（OpenScene のアンロードで参照が切れるのを防ぐ）。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            settings = Required<KeeperSettings>(UnityRoot + "KeeperSettings.asset");
            panelSettings = Required<PanelSettings>(UnityRoot + "UI/KeeperPanelSettings.asset");
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name.StartsWith(Prefix, StringComparison.Ordinal) || root.name.StartsWith("Stage_", StringComparison.Ordinal) ||
                    root.name.StartsWith("BattleStage", StringComparison.Ordinal) || root.GetComponent<KeeperGame>() != null)
                {
                    Object.DestroyImmediate(root);
                }
            }

            var worldRoot = new GameObject(Prefix + "World");
            var stages = new List<Object>();
            foreach (var map in data.Maps)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(Required<GameObject>($"{PrefabRoot}Stage_{map.Id}.prefab"), worldRoot.transform);
                var stage = instance.GetComponent<KeeperMapStage>();
                var mismatch = stage.Mismatch(data);
                if (mismatch != null) { throw new InvalidOperationException($"Stage_{map.Id}: {mismatch}"); }
                stage.ShowPreview(data);
                instance.SetActive(string.Equals(map.Id, data.Rules.StartMapId, StringComparison.Ordinal));
                stages.Add(stage);
            }

            var battleInstance = (GameObject)PrefabUtility.InstantiatePrefab(Required<GameObject>(PrefabRoot + "BattleStage.prefab"), worldRoot.transform);
            var battle = battleInstance.GetComponent<KeeperBattleStage>();
            var battleMismatch = battle.Mismatch(data.Species.Count);
            if (battleMismatch != null) { throw new InvalidOperationException("BattleStage: " + battleMismatch); }
            battleInstance.SetActive(false);

            var camera = Object.FindAnyObjectByType<Camera>();
            if (camera == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                camera = go.AddComponent<Camera>();
            }

            camera.orthographic = true;
            camera.orthographicSize = 5.6f;
            var start = data.Map(data.Rules.StartMapId);
            camera.transform.position = new Vector3(start.Width * 0.5f, start.Height * 0.5f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.10f, 0.16f, 0.10f, 1f);

            var gameObj = new GameObject(Prefix + "Game");
            var hud = gameObj.AddComponent<PanelRenderer>();
            hud.panelSettings = panelSettings;
            hud.visualTreeAsset = Required<VisualTreeAsset>(UnityRoot + "UI/Hud.uxml");
            var game = gameObj.AddComponent<KeeperGame>();
            var so = new SerializedObject(game);
            so.FindProperty("_settings").objectReferenceValue = settings;
            so.FindProperty("_types").objectReferenceValue = Required<TextAsset>(DataRoot + "Types.txt");
            so.FindProperty("_statuses").objectReferenceValue = Required<TextAsset>(DataRoot + "Statuses.txt");
            so.FindProperty("_moves").objectReferenceValue = Required<TextAsset>(DataRoot + "Moves.txt");
            so.FindProperty("_species").objectReferenceValue = Required<TextAsset>(DataRoot + "Species.txt");
            so.FindProperty("_world").objectReferenceValue = Required<TextAsset>(DataRoot + "World.txt");
            so.FindProperty("_hudRenderer").objectReferenceValue = hud;
            so.FindProperty("_camera").objectReferenceValue = camera;
            Assign(so.FindProperty("_mapStages"), stages.ToArray());
            so.FindProperty("_battleStage").objectReferenceValue = battle;
            so.ApplyModifiedPropertiesWithoutUndo();
            gameObj.AddComponent<KeeperCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "THORNWILD KEEPERS";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();

            var games = 0;
            foreach (var r in scene.GetRootGameObjects())
            {
                if (r.GetComponent<KeeperGame>() != null) { games++; }
            }

            if (games != 1) { throw new InvalidOperationException($"KeeperGame の数が {games} です。"); }
            Debug.Log("[KeeperSceneBuilder] シーンを作り、検証しました。");
            if (Application.isBatchMode) { EditorApplication.Exit(0); }
        }

        public static MonsterData LoadData() => MonsterData.Load(
            Required<TextAsset>(DataRoot + "Types.txt").text,
            Required<TextAsset>(DataRoot + "Statuses.txt").text,
            Required<TextAsset>(DataRoot + "Moves.txt").text,
            Required<TextAsset>(DataRoot + "Species.txt").text,
            Required<TextAsset>(DataRoot + "World.txt").text);

        private static void BuildStagePrefabs(MonsterData data)
        {
            var tiles = new Dictionary<string, TileBase>
            {
                ["_ground"] = Tile("Ground"),
                ["_road"] = Tile("Road"),
                ["_flower"] = Tile("Flower"),
                ["_grassA"] = Tile("GrassA"),
                ["_grassB"] = Tile("GrassB"),
                ["_grassC"] = Tile("GrassC"),
                ["_water"] = Tile("Water"),
                ["_wall"] = Tile("Wall"),
                ["_house"] = Tile("House"),
                ["_counter"] = Tile("Counter"),
            };
            var heroSprites = new List<Object>();
            foreach (var d in new[] { "Up", "Right", "Down", "Left" })
            {
                heroSprites.Add(Required<Sprite>($"{ArtRoot}Hero{d}0.png"));
                heroSprites.Add(Required<Sprite>($"{ArtRoot}Hero{d}1.png"));
            }

            foreach (var map in data.Maps)
            {
                var path = $"{PrefabRoot}Stage_{map.Id}.prefab";
                var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (existing != null && existing.GetComponent<KeeperMapStage>()?.Mismatch(data) == null) { continue; }
                var root = new GameObject($"Stage_{map.Id}");
                try
                {
                    root.AddComponent<Grid>().cellSize = Vector3.one;
                    var stage = root.AddComponent<KeeperMapStage>();
                    var tmObj = new GameObject("Terrain");
                    tmObj.transform.SetParent(root.transform, false);
                    var tilemap = tmObj.AddComponent<Tilemap>();
                    tmObj.AddComponent<TilemapRenderer>().sortingOrder = -20;
                    var so = new SerializedObject(stage);
                    so.FindProperty("_mapId").stringValue = map.Id;
                    so.FindProperty("_terrain").objectReferenceValue = tilemap;
                    foreach (var kv in tiles) { so.FindProperty(kv.Key).objectReferenceValue = kv.Value; }
                    so.ApplyModifiedPropertiesWithoutUndo();
                    for (int y = 0; y < map.Height; y++)
                    {
                        for (int x = 0; x < map.Width; x++) { tilemap.SetTile(KeeperMapStage.TileCell(x, y, map.Height), stage.TileFor(map.TileAt(x, y))); }
                    }

                    var trainers = new List<Object>();
                    var ids = new List<string>();
                    foreach (var t in data.Trainers)
                    {
                        if (!string.Equals(t.MapId, map.Id, StringComparison.Ordinal)) { continue; }
                        trainers.Add(Sprite(root.transform, "Trainer_" + t.Id, Required<Sprite>($"{ArtRoot}Trainer_{t.Id}.png"), KeeperMapStage.CellCenter(t.X, t.Y, map.Height), 10));
                        ids.Add(t.Id);
                    }

                    var hero = Sprite(root.transform, "Hero", (Sprite)heroSprites[4], KeeperMapStage.CellCenter(data.Rules.StartX, data.Rules.StartY, map.Height), 20);
                    so = new SerializedObject(stage);
                    Assign(so.FindProperty("_trainers"), trainers.ToArray());
                    var idProp = so.FindProperty("_trainerIds");
                    idProp.arraySize = ids.Count;
                    for (int i = 0; i < ids.Count; i++) { idProp.GetArrayElementAtIndex(i).stringValue = ids[i]; }
                    so.FindProperty("_hero").objectReferenceValue = hero;
                    Assign(so.FindProperty("_heroSprites"), heroSprites.ToArray());
                    so.ApplyModifiedPropertiesWithoutUndo();
                    stage.ShowPreview(data);
                    var mismatch = stage.Mismatch(data);
                    if (mismatch != null) { throw new InvalidOperationException($"Stage_{map.Id} を作れません: {mismatch}"); }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    Object.DestroyImmediate(root);
                }
            }

            var battlePath = PrefabRoot + "BattleStage.prefab";
            var battleExisting = AssetDatabase.LoadAssetAtPath<GameObject>(battlePath);
            if (battleExisting != null && battleExisting.GetComponent<KeeperBattleStage>()?.Mismatch(data.Species.Count) == null) { return; }
            var b = new GameObject("BattleStage");
            try
            {
                var stage = b.AddComponent<KeeperBattleStage>();
                var backdrop = Sprite(b.transform, "Backdrop", Required<Sprite>(ArtRoot + "Backdrop.png"), Vector3.zero, -50);
                var fronts = new List<Object>();
                var backs = new List<Object>();
                foreach (var s in data.Species)
                {
                    fronts.Add(Required<Sprite>($"{ArtRoot}Front_{s.Id}.png"));
                    backs.Add(Required<Sprite>($"{ArtRoot}Back_{s.Id}.png"));
                }

                var player = Sprite(b.transform, "Player", (Sprite)backs[0], KeeperBattleStage.PlayerPosition, 10);
                player.transform.localScale = new Vector3(2.2f, 2.2f, 1f);
                var foe = Sprite(b.transform, "Foe", (Sprite)fronts[0], KeeperBattleStage.FoePosition, 10);
                foe.transform.localScale = new Vector3(2f, 2f, 1f);
                var ball = Sprite(b.transform, "Ball", Required<Sprite>(ArtRoot + "Ball.png"), KeeperBattleStage.FoePosition, 30);
                ball.transform.localScale = new Vector3(2f, 2f, 1f);
                ball.enabled = false;
                var so = new SerializedObject(stage);
                so.FindProperty("_backdrop").objectReferenceValue = backdrop;
                so.FindProperty("_player").objectReferenceValue = player;
                so.FindProperty("_foe").objectReferenceValue = foe;
                so.FindProperty("_ball").objectReferenceValue = ball;
                Assign(so.FindProperty("_fronts"), fronts.ToArray());
                Assign(so.FindProperty("_backs"), backs.ToArray());
                so.ApplyModifiedPropertiesWithoutUndo();
                var mismatch = stage.Mismatch(data.Species.Count);
                if (mismatch != null) { throw new InvalidOperationException("BattleStage を作れません: " + mismatch); }
                PrefabUtility.SaveAsPrefabAsset(b, battlePath);
            }
            finally
            {
                Object.DestroyImmediate(b);
            }
        }

        private static TileBase Tile(string name)
        {
            var sprite = Required<Sprite>($"{ArtRoot}Tile{name}.png");
            var tile = LoadOrCreate($"{UnityRoot}Tiles/{name}.asset", () =>
            {
                var t = ScriptableObject.CreateInstance<Tile>();
                t.sprite = sprite;
                t.colliderType = UnityEngine.Tilemaps.Tile.ColliderType.None;
                return t;
            });
            if (tile.sprite == null)
            {
                tile.sprite = sprite;
                EditorUtility.SetDirty(tile);
            }

            return tile;
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
