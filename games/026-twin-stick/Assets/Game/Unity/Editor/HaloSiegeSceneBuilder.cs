using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>
    /// シーンとアリーナの Stage Prefab を生成する。Stage Prefab は設定のアリーナの大きさから作り、既にあれば上書きせず一致するかだけ確かめる。
    /// 実行：<c>unity run games/026-twin-stick -- -executeMethod Game.Unity.Editor.HaloSiegeSceneBuilder.Build</c>
    /// </summary>
    public static class HaloSiegeSceneBuilder
    {
        private const string Root = "Assets/Game/Unity/";
        private const string Art = HaloSiegeArtGenerator.ArtRoot;
        private const string ScenePath = "Assets/Scenes/HaloSiege.unity";
        private const string StagePath = Art + "HaloSiegeStage.prefab";
        private const string ObjectPrefix = "HaloSiege";

        [MenuItem("Game/Rebuild Halo Siege Scene")]
        public static void Build()
        {
            HaloSiegeArtGenerator.GenerateIfMissing();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            // シーンを開くと未使用アセットがアンロードされるので、アセットの読み込みはこの後に行う。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = LoadOrCreate(Root + "HaloSiegeSettings.asset", ScriptableObject.CreateInstance<HaloSiegeSettings>);
            settings.Validate();
            var panel = LoadOrCreate(Root + "UI/HaloSiegePanelSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PanelSettings>();
                value.themeStyleSheet = Load<ThemeStyleSheet>(Root + "UI/HaloSiegeTheme.tss");
                value.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                value.referenceResolution = new Vector2Int(1920, 1080);
                value.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                value.match = .5f;
                return value;
            });

            // Prefab を作り直すと古いインスタンスが別名で残ることがあるので、前方一致で消す（018 の知見）。
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name.StartsWith(ObjectPrefix, StringComparison.Ordinal)) { Object.DestroyImmediate(root); }
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(LoadStage(settings));
            instance.transform.position = Vector3.zero;
            var stage = instance.GetComponent<HaloSiegeStage>();

            var rules = settings.Rules;
            var camera = Object.FindAnyObjectByType<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = rules.ViewHeightCells / 2f;
            camera.transform.position = new Vector3(rules.ArenaWidthCells / 2f, rules.ArenaHeightCells / 2f, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.02f, .02f, .06f);

            var game = new GameObject(ObjectPrefix + "Game");
            var hud = game.AddComponent<PanelRenderer>();
            hud.panelSettings = panel;
            hud.visualTreeAsset = Load<VisualTreeAsset>(Root + "UI/Hud.uxml");
            var component = game.AddComponent<HaloSiegeGame>();
            var serialized = new SerializedObject(component);
            Assign(serialized, "_settings", settings);
            Assign(serialized, "_hudRenderer", hud);
            Assign(serialized, "_camera", camera);
            Assign(serialized, "_stage", stage);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<HaloSiegeCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "HALO SIEGE";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();

            var count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<HaloSiegeStage>() != null) { count++; }
            }

            if (count != 1) { throw new InvalidOperationException($"Stage のインスタンスが {count} 個あります。"); }
            Debug.Log("[HaloSiegeSceneBuilder] シーン生成完了");
        }

        // 配置の正は Stage Prefab。既にあれば調整を上書きせず、設定のアリーナと一致するかだけ確かめる。
        private static GameObject LoadStage(HaloSiegeSettings settings)
        {
            var rules = settings.Rules;
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(StagePath);
            if (existing != null)
            {
                var mismatch = existing.GetComponent<HaloSiegeStage>().Mismatch(rules);
                if (mismatch != null) { throw new InvalidOperationException($"{StagePath} が設定のアリーナと一致しません：{mismatch} Prefab を消して作り直してください。"); }
                return existing;
            }

            var material = Material();
            var root = new GameObject("HaloSiegeStage");
            try
            {
                var stage = root.AddComponent<HaloSiegeStage>();
                var grid = new GameObject("Grid").AddComponent<Grid>();
                grid.transform.SetParent(root.transform, false);
                var floor = Tilemap(grid.transform, "Floor", 0, material);
                var walls = Tilemap(grid.transform, "Walls", 1, material);
                var floorTile = TileAsset("Floor", HaloSiegeArtGenerator.FloorFile);
                var wallTile = TileAsset("Wall", HaloSiegeArtGenerator.WallFile);
                for (var row = -1; row <= rules.ArenaHeightCells; row++)
                {
                    for (var column = -1; column <= rules.ArenaWidthCells; column++)
                    {
                        var cell = new Vector3Int(column, row, 0);
                        var inside = column >= 0 && row >= 0 && column < rules.ArenaWidthCells && row < rules.ArenaHeightCells;
                        if (inside) { floor.SetTile(cell, floorTile); }
                        else { walls.SetTile(cell, wallTile); }
                    }
                }

                var center = new Vector3(rules.ArenaWidthCells / 2f, rules.ArenaHeightCells / 2f, 0);
                var lane = Sprite(root.transform, "Lane", LoadSprite(HaloSiegeArtGenerator.LaneFile), material, 2);
                lane.enabled = false;
                var warning = Sprite(root.transform, "Warning", LoadSprite(HaloSiegeArtGenerator.WarningFile), material, 3);
                warning.enabled = false;
                var enemy = Sprite(root.transform, "Enemy", LoadSprite(HaloSiegeArtGenerator.ChaserFile), material, 5);
                enemy.enabled = false;
                var bullet = Sprite(root.transform, "Bullet", LoadSprite(HaloSiegeArtGenerator.BulletFile), material, 6);
                bullet.enabled = false;
                var ship = Sprite(root.transform, "Ship", LoadSprite(HaloSiegeArtGenerator.ShipFile), material, 8);
                ship.transform.localPosition = center;
                var crosshair = Sprite(root.transform, "Crosshair", LoadSprite(HaloSiegeArtGenerator.CrosshairFile), material, 20);
                crosshair.transform.localPosition = center + new Vector3(3, 0, 0);
                crosshair.enabled = false;

                var serialized = new SerializedObject(stage);
                Assign(serialized, "_floor", floor);
                Assign(serialized, "_walls", walls);
                Assign(serialized, "_ship", ship);
                Assign(serialized, "_crosshair", crosshair);
                Assign(serialized, "_enemyTemplate", enemy);
                Assign(serialized, "_warningTemplate", warning);
                Assign(serialized, "_laneTemplate", lane);
                Assign(serialized, "_bulletTemplate", bullet);
                Assign(serialized, "_chaser", LoadSprite(HaloSiegeArtGenerator.ChaserFile));
                Assign(serialized, "_bouncer", LoadSprite(HaloSiegeArtGenerator.BouncerFile));
                Assign(serialized, "_lancer", LoadSprite(HaloSiegeArtGenerator.LancerFile));
                serialized.ApplyModifiedPropertiesWithoutUndo();
                return PrefabUtility.SaveAsPrefabAsset(root, StagePath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Tilemap Tilemap(Transform parent, string name, int order, Material material)
        {
            var item = new GameObject(name);
            item.transform.SetParent(parent, false);
            var tilemap = item.AddComponent<Tilemap>();
            var renderer = item.AddComponent<TilemapRenderer>();
            renderer.sortingOrder = order;
            renderer.sharedMaterial = material;
            return tilemap;
        }

        private static Tile TileAsset(string name, string file)
        {
            if (!AssetDatabase.IsValidFolder(Art + "Tiles")) { AssetDatabase.CreateFolder(Art.TrimEnd('/'), "Tiles"); }
            return LoadOrCreate($"{Art}Tiles/{name}.asset", () =>
            {
                var tile = ScriptableObject.CreateInstance<Tile>();
                tile.sprite = LoadSprite(file);
                tile.colliderType = UnityEngine.Tilemaps.Tile.ColliderType.None;
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
