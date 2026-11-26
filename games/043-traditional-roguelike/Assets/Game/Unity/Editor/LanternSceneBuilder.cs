using System;
using Game.Core;
using IntGeometry;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using TileAsset = UnityEngine.Tilemaps.Tile;

namespace Game.Unity.Editor
{
    /// <summary>
    /// シーンと Stage Prefab を生成する。Stage Prefab にはシード 1 の 1 階を焼き（プレイ前から Scene ビューで見える）、
    /// 既にあれば上書きせず見本の階と一致するかだけ確かめる。
    /// 実行：<c>unity run games/043-traditional-roguelike -- -executeMethod Game.Unity.Editor.LanternSceneBuilder.Build</c>
    /// </summary>
    public static class LanternSceneBuilder
    {
        private const string Root = "Assets/Game/Unity/";
        private const string Art = LanternArtGenerator.ArtRoot;
        private const string ScenePath = "Assets/Scenes/MireLantern.unity";
        private const string StagePath = Art + "LanternStage.prefab";
        private const string SettingsPath = Root + "LanternSettings.asset";
        private const string ObjectPrefix = "Lantern";

        [MenuItem("Game/Rebuild Mire Lantern Scene")]
        public static void Build()
        {
            LanternArtGenerator.GenerateIfMissing();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            // シーンを開くと未使用アセットがアンロードされるので、アセットの読み込みはこの後に行う。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = LoadOrCreate(SettingsPath, ScriptableObject.CreateInstance<LanternSettings>);
            settings.Validate();
            var preview = FloorGenerator.Generate(LanternStage.PreviewSeed, LanternStage.PreviewFloor, settings.Rules);
            var panel = LoadOrCreate(Root + "UI/LanternPanelSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PanelSettings>();
                value.themeStyleSheet = Load<ThemeStyleSheet>(Root + "UI/LanternTheme.tss");
                value.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                value.referenceResolution = new Vector2Int(1920, 1080);
                value.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                value.match = 1f;
                return value;
            });

            // Prefab を作り直すと古いインスタンスが別名で残ることがあるので、前方一致で消す（018）。
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name.StartsWith(ObjectPrefix, StringComparison.Ordinal)) { Object.DestroyImmediate(root); }
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(LoadStage(preview));
            instance.transform.position = Vector3.zero;
            var stage = instance.GetComponent<LanternStage>();

            var camera = Object.FindAnyObjectByType<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 18;
            camera.transform.position = new Vector3(24, 11, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.03f, .04f, .035f);

            var game = new GameObject(ObjectPrefix + "Game");
            var hud = game.AddComponent<PanelRenderer>();
            hud.panelSettings = panel;
            hud.visualTreeAsset = Load<VisualTreeAsset>(Root + "UI/Hud.uxml");
            var component = game.AddComponent<LanternGame>();
            var serialized = new SerializedObject(component);
            Assign(serialized, "_settings", settings);
            Assign(serialized, "_hudRenderer", hud);
            Assign(serialized, "_camera", camera);
            Assign(serialized, "_stage", stage);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<LanternCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "MIRE LANTERN";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();

            int count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<LanternStage>() != null) { count++; }
            }

            if (count != 1) { throw new InvalidOperationException($"Stage のインスタンスが {count} 個あります。"); }
            Debug.Log("[LanternSceneBuilder] シーン生成完了");
        }

        // 配置の正は Stage Prefab。既にあれば調整を上書きせず、見本の階と一致するかだけ確かめる。
        private static GameObject LoadStage(FloorLayout preview)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(StagePath);
            if (existing != null)
            {
                var mismatch = existing.GetComponent<LanternStage>().Mismatch(preview.Grid);
                if (mismatch != null) { throw new InvalidOperationException($"{StagePath} が見本の階と一致しません：{mismatch} Prefab を消して作り直してください。"); }
                return existing;
            }

            var material = Material();
            var root = new GameObject("LanternStage");
            try
            {
                var stage = root.AddComponent<LanternStage>();
                var tiles = new[]
                {
                    TileFor("Wall", LanternArtGenerator.WallFile),
                    TileFor("Room", LanternArtGenerator.RoomFile),
                    TileFor("Corridor", LanternArtGenerator.CorridorFile),
                    TileFor("Stairs", LanternArtGenerator.StairsFile),
                };
                var grid = new GameObject("Grid").AddComponent<Grid>();
                grid.transform.SetParent(root.transform, false);
                var terrainObject = new GameObject("Terrain");
                terrainObject.transform.SetParent(grid.transform, false);
                var terrain = terrainObject.AddComponent<Tilemap>();
                var terrainRenderer = terrainObject.AddComponent<TilemapRenderer>();
                terrainRenderer.sortingOrder = 0;
                terrainRenderer.sharedMaterial = material;
                var g = preview.Grid;
                for (int y = 0; y < g.Height; y++)
                {
                    for (int x = 0; x < g.Width; x++)
                    {
                        var p = new Vec2i(x, y);
                        if (!LanternStage.ShowsInPreview(g, p)) { continue; }
                        var cell = LanternStage.Cell(p, g.Height);
                        terrain.SetTile(cell, tiles[(int)g[p]]);
                        terrain.SetTileFlags(cell, TileFlags.None);
                        terrain.SetColor(cell, LanternStage.Remembered);
                    }
                }

                var actors = new GameObject("Actors");
                actors.transform.SetParent(root.transform, false);
                var start = LanternStage.CellCenter(preview.Start, g.Height);
                var item = Sprite(actors.transform, "Item", LoadSprite(LanternArtGenerator.ItemFiles[0]), material, 2);
                item.enabled = false;
                var monster = Sprite(actors.transform, "Monster", LoadSprite(LanternArtGenerator.MonsterFiles[0]), material, 4);
                monster.enabled = false;
                var sleep = Sprite(actors.transform, "Sleep", LoadSprite(LanternArtGenerator.SleepFile), material, 5);
                sleep.enabled = false;
                var player = Sprite(actors.transform, "Player", LoadSprite(LanternArtGenerator.PlayerFile), material, 6);
                player.transform.localPosition = start;
                var facing = Sprite(actors.transform, "Facing", LoadSprite(LanternArtGenerator.FacingFile), material, 7);
                facing.transform.localPosition = start;
                facing.transform.localRotation = LanternStage.FacingRotation(Direction.South);
                var hit = Sprite(actors.transform, "Hit", LoadSprite(LanternArtGenerator.HitFile), material, 8);
                hit.enabled = false;

                var serialized = new SerializedObject(stage);
                Assign(serialized, "_terrain", terrain);
                Assign(serialized, "_wall", tiles[0]);
                Assign(serialized, "_room", tiles[1]);
                Assign(serialized, "_corridor", tiles[2]);
                Assign(serialized, "_stairs", tiles[3]);
                Assign(serialized, "_player", player);
                Assign(serialized, "_facing", facing);
                Assign(serialized, "_monsterTemplate", monster);
                Assign(serialized, "_itemTemplate", item);
                Assign(serialized, "_sleepTemplate", sleep);
                Assign(serialized, "_hitTemplate", hit);
                AssignArray(serialized, "_monsterSprites", Array.ConvertAll(LanternArtGenerator.MonsterFiles, LoadSprite));
                AssignArray(serialized, "_itemSprites", Array.ConvertAll(LanternArtGenerator.ItemFiles, LoadSprite));
                serialized.FindProperty("_width").intValue = g.Width;
                serialized.FindProperty("_height").intValue = g.Height;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                var reason = stage.Mismatch(g);
                if (reason != null) { throw new InvalidOperationException("生成した Stage が見本の階と一致しません：" + reason); }
                return PrefabUtility.SaveAsPrefabAsset(root, StagePath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static TileAsset TileFor(string name, string file)
        {
            if (!AssetDatabase.IsValidFolder(Art + "Tiles")) { AssetDatabase.CreateFolder(Art.TrimEnd('/'), "Tiles"); }
            return LoadOrCreate($"{Art}Tiles/{name}.asset", () =>
            {
                var tile = ScriptableObject.CreateInstance<TileAsset>();
                tile.sprite = LoadSprite(file);
                tile.colliderType = TileAsset.ColliderType.None;
                tile.flags = TileFlags.None;
                return tile;
            });
        }

        private static Material Material() => LoadOrCreate(Art + "Unlit.mat", () => new Material(Shader.Find("Sprites/Default")));

        private static Sprite LoadSprite(string file) => Load<Sprite>(Art + file);

        private static SpriteRenderer Sprite(Transform parent, string name, Sprite sprite, Material material, int order)
        {
            var item = new GameObject(name);
            item.transform.SetParent(parent, false);
            var renderer = item.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sharedMaterial = material;
            renderer.sortingOrder = order;
            return renderer;
        }

        private static void Assign(SerializedObject target, string field, Object value) =>
            target.FindProperty(field).objectReferenceValue = value;

        private static void AssignArray<T>(SerializedObject target, string field, T[] values) where T : Object
        {
            var property = target.FindProperty(field);
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) { property.GetArrayElementAtIndex(i).objectReferenceValue = values[i]; }
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
