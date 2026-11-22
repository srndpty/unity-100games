using System;
using System.Collections.Generic;
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
    /// シーンと 6 部屋の Stage Prefab を生成する。Stage Prefab は Core の地図から作り、既にあれば上書きせず地図と一致するかだけ確かめる。
    /// 実行：<c>unity run games/025-topdown-action -- -executeMethod Game.Unity.Editor.EmberVaultSceneBuilder.Build</c>
    /// </summary>
    public static class EmberVaultSceneBuilder
    {
        private const string Root = "Assets/Game/Unity/";
        private const string Art = EmberVaultArtGenerator.ArtRoot;
        private const string ScenePath = "Assets/Scenes/EmberVault.unity";
        private const string ObjectPrefix = "EmberVault";

        [MenuItem("Game/Rebuild Ember Vault Scene")]
        public static void Build()
        {
            EmberVaultArtGenerator.GenerateIfMissing();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            // シーンを開くと未使用アセットがアンロードされるので、アセットの読み込みはこの後に行う。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = LoadOrCreate(Root + "EmberVaultSettings.asset", ScriptableObject.CreateInstance<EmberVaultSettings>);
            settings.Validate();
            var panel = LoadOrCreate(Root + "UI/EmberVaultPanelSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PanelSettings>();
                value.themeStyleSheet = Load<ThemeStyleSheet>(Root + "UI/EmberVaultTheme.tss");
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

            var world = Rooms.Create();
            var stages = new EmberVaultStage[world.Rooms.Count];
            for (var i = 0; i < world.Rooms.Count; i++)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(LoadStage(i, world.Rooms[i]));
                instance.transform.position = EmberVaultGame.RoomOrigin(world.Rooms[i]);
                instance.SetActive(i == world.StartRoom);
                stages[i] = instance.GetComponent<EmberVaultStage>();
            }

            var camera = Object.FindAnyObjectByType<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = (Room.Rows + EmberVaultGame.HudBandTiles) / 2;
            var view = EmberVaultGame.RoomView(world.Rooms[world.StartRoom]);
            camera.transform.position = new Vector3(view.x, view.y, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.05f, .03f, .04f);
            for (var i = camera.transform.childCount - 1; i >= 0; i--) { Object.DestroyImmediate(camera.transform.GetChild(i).gameObject); }

            // 背景はカメラの子にして、いつも画面全体を覆う。
            var backdrop = new GameObject("Backdrop").AddComponent<SpriteRenderer>();
            backdrop.transform.SetParent(camera.transform, false);
            backdrop.transform.localPosition = new Vector3(0, 0, 20);
            backdrop.sprite = Load<Sprite>(Art + EmberVaultArtGenerator.BackdropFile);
            backdrop.sharedMaterial = Material();
            backdrop.sortingOrder = -100;

            var game = new GameObject(ObjectPrefix);
            var hud = game.AddComponent<PanelRenderer>();
            hud.panelSettings = panel;
            hud.visualTreeAsset = Load<VisualTreeAsset>(Root + "UI/Hud.uxml");
            var component = game.AddComponent<EmberVaultGame>();
            var serialized = new SerializedObject(component);
            Assign(serialized, "_settings", settings);
            Assign(serialized, "_hudRenderer", hud);
            Assign(serialized, "_camera", camera);
            AssignArray(serialized, "_stages", stages);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<EmberVaultCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "EMBER VAULT";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();

            var count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<EmberVaultStage>() != null) { count++; }
            }

            if (count != world.Rooms.Count) { throw new InvalidOperationException($"Stage のインスタンスが {count} 個あります（部屋は {world.Rooms.Count} 個）。"); }
            Debug.Log("[EmberVaultSceneBuilder] シーン生成完了");
        }

        // 配置の正は Stage Prefab。既にあれば調整を上書きせず、地図と一致するかだけ確かめる。
        private static GameObject LoadStage(int index, Room room)
        {
            var path = $"{Art}EmberVaultStage{index + 1:00}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                var mismatch = existing.GetComponent<EmberVaultStage>().Mismatch(room);
                if (mismatch != null) { throw new InvalidOperationException($"{path} が Core の部屋（{room.Name}）と一致しません：{mismatch} Prefab を消して作り直してください。"); }
                return existing;
            }

            var material = Material();
            var root = new GameObject($"EmberVaultStage{index + 1:00}");
            try
            {
                var stage = root.AddComponent<EmberVaultStage>();
                var grid = new GameObject("Grid").AddComponent<Grid>();
                grid.transform.SetParent(root.transform, false);
                var floor = Tilemap(grid.transform, "Floor", 0, material);
                var walls = Tilemap(grid.transform, "Walls", 1, material);
                var doors = Tilemap(grid.transform, "Doors", 1, material);
                var floorTile = TileAsset("Floor", EmberVaultArtGenerator.FloorFile);
                var threshold = TileAsset("Threshold", EmberVaultArtGenerator.ThresholdFile);
                var wall = TileAsset("Wall", EmberVaultArtGenerator.WallFile);
                var wallFace = TileAsset("WallTop", EmberVaultArtGenerator.WallTopFile);
                var smallDoor = TileAsset("SmallDoor", EmberVaultArtGenerator.SmallDoorFile);
                var bossDoor = TileAsset("BossDoor", EmberVaultArtGenerator.BossDoorFile);

                var map = room.Map;
                for (var row = 0; row < map.Height; row++)
                {
                    for (var column = 0; column < map.Width; column++)
                    {
                        var cell = new Vector3Int(column, row, 0);
                        var kind = map[column, row];
                        floor.SetTile(cell, TileMap.IsDoorTile(kind) ? threshold : floorTile);
                        switch (kind)
                        {
                            case TileKind.Wall:
                                // 下が壁でない壁は手前の面に影を付ける。
                                walls.SetTile(cell, row > 0 && map[column, row - 1] != TileKind.Wall ? wallFace : wall);
                                break;
                            case TileKind.SmallDoor:
                                doors.SetTile(cell, smallDoor);
                                break;
                            case TileKind.BossDoor:
                                doors.SetTile(cell, bossDoor);
                                break;
                        }
                    }
                }

                var closedChests = new Object[] { LoadSprite(EmberVaultArtGenerator.ChestFile), LoadSprite(EmberVaultArtGenerator.BossChestFile) };
                var chests = new List<Object>();
                foreach (var chest in room.Chests)
                {
                    var item = Sprite(root.transform, $"Chest{chests.Count}{chest.Content}", (Sprite)closedChests[(int)chest.Content], material, 3);
                    item.transform.localPosition = EmberVaultStage.CellCenter(chest.Cell);
                    chests.Add(item);
                }

                SpriteRenderer reward = null;
                if (room.Reward is Cell rewardCell)
                {
                    reward = Sprite(root.transform, "RewardKey", LoadSprite(EmberVaultArtGenerator.KeyFile), material, 4);
                    reward.transform.localPosition = EmberVaultStage.CellCenter(rewardCell);
                    reward.enabled = false;
                }

                SpriteRenderer treasure = null;
                if (room.Boss is Cell bossCell)
                {
                    treasure = Sprite(root.transform, "Treasure", LoadSprite(EmberVaultArtGenerator.TreasureFile), material, 4);
                    treasure.transform.localPosition = EmberVaultStage.CellCenter(bossCell);
                    treasure.enabled = false;
                }

                var enemySprites = new Dictionary<EnemyKind, Sprite>
                {
                    [EnemyKind.Slime] = LoadSprite(EmberVaultArtGenerator.Slime0File),
                    [EnemyKind.Spitter] = LoadSprite(EmberVaultArtGenerator.SpitterFile),
                    [EnemyKind.Charger] = LoadSprite(EmberVaultArtGenerator.ChargerFile),
                    [EnemyKind.Boss] = LoadSprite(EmberVaultArtGenerator.BossFile),
                };
                var enemies = new List<Object>();
                foreach (var spawn in room.Spawns)
                {
                    var enemy = Sprite(root.transform, $"Enemy{enemies.Count}{spawn.Kind}", enemySprites[spawn.Kind], material, spawn.Kind == EnemyKind.Boss ? 7 : 6);
                    enemy.transform.localPosition = EmberVaultStage.Center(spawn.Cell.Centered(Enemy.SizeOf(spawn.Kind)));
                    enemies.Add(enemy);
                }

                var hero = Sprite(root.transform, "Hero", LoadSprite(EmberVaultArtGenerator.HeroUp0File), material, 10);
                if (room.Start is Cell start) { hero.transform.localPosition = EmberVaultStage.Center(start.Centered(Hero.Size)); }
                else
                {
                    // 開始位置のない部屋では、最初の到着位置に置いておく（描画のときに試合の位置へ動かす）。
                    foreach (Side side in Enum.GetValues(typeof(Side)))
                    {
                        if (room.Door(side) == DoorKind.None) { continue; }
                        hero.transform.localPosition = EmberVaultStage.Center(room.Arrival(side));
                        break;
                    }

                    hero.enabled = false;
                }

                var sword = Sprite(root.transform, "Sword", LoadSprite(EmberVaultArtGenerator.SlashFile), material, 11);
                sword.enabled = false;
                var shot = Sprite(root.transform, "Shot", LoadSprite(EmberVaultArtGenerator.ShotFile), material, 12);
                shot.enabled = false;

                var serialized = new SerializedObject(stage);
                serialized.FindProperty("_roomIndex").intValue = index;
                Assign(serialized, "_floor", floor);
                Assign(serialized, "_walls", walls);
                Assign(serialized, "_doors", doors);
                Assign(serialized, "_smallDoorTile", smallDoor);
                Assign(serialized, "_bossDoorTile", bossDoor);
                AssignArray(serialized, "_chests", chests.ToArray());
                AssignArray(serialized, "_chestClosed", closedChests);
                Assign(serialized, "_chestOpen", LoadSprite(EmberVaultArtGenerator.ChestOpenFile));
                Assign(serialized, "_reward", reward);
                Assign(serialized, "_treasure", treasure);
                AssignArray(serialized, "_enemies", enemies.ToArray());
                AssignArray(serialized, "_slime", new Object[] { LoadSprite(EmberVaultArtGenerator.Slime0File), LoadSprite(EmberVaultArtGenerator.Slime1File) });
                Assign(serialized, "_spitter", LoadSprite(EmberVaultArtGenerator.SpitterFile));
                Assign(serialized, "_charger", LoadSprite(EmberVaultArtGenerator.ChargerFile));
                Assign(serialized, "_chargerCharge", LoadSprite(EmberVaultArtGenerator.ChargerChargeFile));
                Assign(serialized, "_boss", LoadSprite(EmberVaultArtGenerator.BossFile));
                Assign(serialized, "_bossCharge", LoadSprite(EmberVaultArtGenerator.BossChargeFile));
                Assign(serialized, "_bossStun", LoadSprite(EmberVaultArtGenerator.BossStunFile));
                Assign(serialized, "_hero", hero);
                AssignArray(serialized, "_heroDown", new Object[] { LoadSprite(EmberVaultArtGenerator.HeroDown0File), LoadSprite(EmberVaultArtGenerator.HeroDown1File) });
                AssignArray(serialized, "_heroUp", new Object[] { LoadSprite(EmberVaultArtGenerator.HeroUp0File), LoadSprite(EmberVaultArtGenerator.HeroUp1File) });
                AssignArray(serialized, "_heroSide", new Object[] { LoadSprite(EmberVaultArtGenerator.HeroSide0File), LoadSprite(EmberVaultArtGenerator.HeroSide1File) });
                Assign(serialized, "_heroRoll", LoadSprite(EmberVaultArtGenerator.HeroRollFile));
                Assign(serialized, "_heroHurt", LoadSprite(EmberVaultArtGenerator.HeroHurtFile));
                Assign(serialized, "_sword", sword);
                Assign(serialized, "_shotTemplate", shot);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                return PrefabUtility.SaveAsPrefabAsset(root, path);
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

        private static void AssignArray(SerializedObject target, string field, Object[] values)
        {
            var property = target.FindProperty(field);
            property.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++) { property.GetArrayElementAtIndex(i).objectReferenceValue = values[i]; }
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
