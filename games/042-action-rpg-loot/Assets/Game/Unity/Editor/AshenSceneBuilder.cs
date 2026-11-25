using System;
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
    /// シーンと Stage Prefab を生成する。Stage Prefab は地図（Floors.txt）から作り、既にあれば上書きせず一致するかだけ確かめる。
    /// 実行：<c>unity run games/042-action-rpg-loot -- -executeMethod Game.Unity.Editor.AshenSceneBuilder.Build</c>
    /// </summary>
    public static class AshenSceneBuilder
    {
        private const string Root = "Assets/Game/Unity/";
        private const string Art = AshenArtGenerator.ArtRoot;
        private const string ScenePath = "Assets/Scenes/AshenReliquary.unity";
        private const string StagePath = Art + "AshenStage.prefab";
        private const string SettingsPath = Root + "AshenSettings.asset";
        private const string ObjectPrefix = "Ashen";

        [MenuItem("Game/Rebuild Ashen Reliquary Scene")]
        public static void Build()
        {
            AshenArtGenerator.GenerateIfMissing();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            // シーンを開くと未使用アセットがアンロードされるので、アセットの読み込みはこの後に行う。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = LoadOrCreate(SettingsPath, () =>
            {
                var value = ScriptableObject.CreateInstance<AshenSettings>();
                value.Loot = Load<TextAsset>("Assets/Game/Data/Loot.txt");
                value.Floors = Load<TextAsset>("Assets/Game/Data/Floors.txt");
                return value;
            });
            settings.Validate();
            var floors = settings.ParseFloors();
            settings.ParseLoot();
            var panel = LoadOrCreate(Root + "UI/AshenPanelSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PanelSettings>();
                value.themeStyleSheet = Load<ThemeStyleSheet>(Root + "UI/AshenTheme.tss");
                value.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                value.referenceResolution = new Vector2Int(1920, 1080);
                value.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                value.match = .5f;
                return value;
            });

            // Prefab を作り直すと古いインスタンスが別名で残ることがあるので、前方一致で消す（018）。
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name.StartsWith(ObjectPrefix, StringComparison.Ordinal)) { Object.DestroyImmediate(root); }
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(LoadStage(floors));
            instance.transform.position = Vector3.zero;
            var stage = instance.GetComponent<AshenStage>();

            var camera = Object.FindAnyObjectByType<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = settings.ViewHeightTiles / 2f;
            var entrance = AshenStage.Local(floors[1].Entrance);
            camera.transform.position = new Vector3(Mathf.Max(entrance.x, 12), Mathf.Max(entrance.y, 7), -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.05f, .035f, .03f);

            var game = new GameObject(ObjectPrefix + "Game");
            var hud = game.AddComponent<PanelRenderer>();
            hud.panelSettings = panel;
            hud.visualTreeAsset = Load<VisualTreeAsset>(Root + "UI/Hud.uxml");
            var component = game.AddComponent<AshenGame>();
            var serialized = new SerializedObject(component);
            Assign(serialized, "_settings", settings);
            Assign(serialized, "_hudRenderer", hud);
            Assign(serialized, "_camera", camera);
            Assign(serialized, "_stage", stage);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<AshenCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "ASHEN RELIQUARY";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();

            int count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<AshenStage>() != null) { count++; }
            }

            if (count != 1) { throw new InvalidOperationException($"Stage のインスタンスが {count} 個あります。"); }
            Debug.Log("[AshenSceneBuilder] シーン生成完了");
        }

        // 配置の正は Stage Prefab。既にあれば調整を上書きせず、地図と一致するかだけ確かめる。
        private static GameObject LoadStage(FloorSet floors)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(StagePath);
            if (existing != null)
            {
                var mismatch = existing.GetComponent<AshenStage>().Mismatch(floors);
                if (mismatch != null) { throw new InvalidOperationException($"{StagePath} が地図と一致しません：{mismatch} Prefab を消して作り直してください。"); }
                return existing;
            }

            var material = Material();
            var root = new GameObject("AshenStage");
            try
            {
                var stage = root.AddComponent<AshenStage>();
                var floorTile = TileAsset("Floor", AshenArtGenerator.FloorFile);
                var wallTile = TileAsset("Wall", AshenArtGenerator.WallFile);
                var views = new AshenFloorView[floors.Floors.Count];
                var foeSprites = new[]
                {
                    LoadSprite(AshenArtGenerator.GruntFile), LoadSprite(AshenArtGenerator.ArcherFile),
                    LoadSprite(AshenArtGenerator.ChargerFile), LoadSprite(AshenArtGenerator.BossFile),
                };
                for (int n = 0; n < views.Length; n++)
                {
                    var map = floors.Floors[n];
                    var floorRoot = new GameObject($"Floor{map.Number}");
                    floorRoot.transform.SetParent(root.transform, false);
                    floorRoot.SetActive(n == 0);
                    var view = floorRoot.AddComponent<AshenFloorView>();
                    var grid = new GameObject("Grid").AddComponent<Grid>();
                    grid.transform.SetParent(floorRoot.transform, false);
                    var ground = Tilemap(grid.transform, "Ground", 0, material);
                    var walls = Tilemap(grid.transform, "Walls", 1, material);
                    for (int row = 0; row < map.Height; row++)
                    {
                        for (int column = 0; column < map.Width; column++)
                        {
                            var cell = new Vector3Int(column, row, 0);
                            if (map.IsSolid(column, row)) { walls.SetTile(cell, wallTile); }
                            else { ground.SetTile(cell, floorTile); }
                        }
                    }

                    SpriteRenderer stairs = null;
                    if (map.HasStairs)
                    {
                        stairs = Sprite(floorRoot.transform, "Stairs", LoadSprite(AshenArtGenerator.StairsFile), material, 2);
                        stairs.transform.localPosition = AshenStage.Local(FloorMap.TileCenter(map.StairsColumn, map.StairsRow));
                    }

                    var entrance = Sprite(floorRoot.transform, "Entrance", LoadSprite(AshenArtGenerator.EntranceFile), material, 2);
                    entrance.transform.localPosition = AshenStage.Local(map.Entrance);
                    var foes = new SpriteRenderer[map.Spawns.Count];
                    for (int i = 0; i < foes.Length; i++)
                    {
                        var spawn = map.Spawns[i];
                        foes[i] = Sprite(floorRoot.transform, $"Foe{i:00}_{spawn.Kind}", foeSprites[(int)spawn.Kind], material, 6);
                        foes[i].transform.localPosition = AshenStage.Local(spawn.Center);
                    }

                    var serializedView = new SerializedObject(view);
                    Assign(serializedView, "_ground", ground);
                    Assign(serializedView, "_walls", walls);
                    Assign(serializedView, "_stairs", stairs);
                    Assign(serializedView, "_entrance", entrance);
                    AssignArray(serializedView, "_foes", foes);
                    serializedView.ApplyModifiedPropertiesWithoutUndo();
                    views[n] = view;
                }

                var actors = new GameObject("Actors");
                actors.transform.SetParent(root.transform, false);
                var entrancePoint = AshenStage.Local(floors[1].Entrance);
                var lane = Sprite(actors.transform, "Lane", LoadSprite(AshenArtGenerator.LaneFile), material, 3);
                lane.enabled = false;
                var groundItem = Sprite(actors.transform, "GroundItem", LoadSprite(AshenArtGenerator.SlotFiles[0]), material, 4);
                groundItem.enabled = false;
                var spin = Sprite(actors.transform, "Spin", LoadSprite(AshenArtGenerator.SpinFile), material, 7);
                spin.enabled = false;
                var hero = Sprite(actors.transform, "Hero", LoadSprite(AshenArtGenerator.HeroFile), material, 8);
                hero.transform.localPosition = entrancePoint;
                var swing = Sprite(actors.transform, "Swing", LoadSprite(AshenArtGenerator.SwingFile), material, 9);
                swing.enabled = false;
                var projectile = Sprite(actors.transform, "Projectile", LoadSprite(AshenArtGenerator.ArrowFile), material, 10);
                projectile.enabled = false;
                var crosshair = Sprite(actors.transform, "Crosshair", LoadSprite(AshenArtGenerator.CrosshairFile), material, 20);
                crosshair.transform.localPosition = entrancePoint + new Vector3(2, 0, 0);
                crosshair.enabled = false;

                var serialized = new SerializedObject(stage);
                AssignArray(serialized, "_floors", views);
                Assign(serialized, "_hero", hero);
                Assign(serialized, "_crosshair", crosshair);
                Assign(serialized, "_swing", swing);
                Assign(serialized, "_spin", spin);
                Assign(serialized, "_laneTemplate", lane);
                Assign(serialized, "_projectileTemplate", projectile);
                Assign(serialized, "_groundTemplate", groundItem);
                Assign(serialized, "_heroStand", LoadSprite(AshenArtGenerator.HeroFile));
                Assign(serialized, "_heroDown", LoadSprite(AshenArtGenerator.HeroDownFile));
                Assign(serialized, "_grunt", foeSprites[0]);
                Assign(serialized, "_archer", foeSprites[1]);
                Assign(serialized, "_charger", foeSprites[2]);
                Assign(serialized, "_boss", foeSprites[3]);
                Assign(serialized, "_arrow", LoadSprite(AshenArtGenerator.ArrowFile));
                Assign(serialized, "_bolt", LoadSprite(AshenArtGenerator.BoltFile));
                Assign(serialized, "_potion", LoadSprite(AshenArtGenerator.PotionFile));
                var icons = new Sprite[AshenArtGenerator.SlotFiles.Length];
                for (int i = 0; i < icons.Length; i++) { icons[i] = LoadSprite(AshenArtGenerator.SlotFiles[i]); }
                AssignArray(serialized, "_slotIcons", icons);
                serialized.ApplyModifiedPropertiesWithoutUndo();

                var reason = stage.Mismatch(floors);
                if (reason != null) { throw new InvalidOperationException("生成した Stage が地図と一致しません：" + reason); }
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
