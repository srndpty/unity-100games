using System;
using System.Collections.Generic;
using Game.Core;
using TileMotion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UIElements;
using Box = TileMotion.Box;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>
    /// シーンと 5 部屋の Stage Prefab を生成する。Stage Prefab は Core の地図から作り、既にあれば上書きせず地図と一致するかだけ確かめる。
    /// 実行：<c>unity run games/023-metroidvania -- -executeMethod Game.Unity.Editor.MossDepthsSceneBuilder.Build</c>
    /// </summary>
    public static class MossDepthsSceneBuilder
    {
        private const string Root = "Assets/Game/Unity/";
        private const string Art = MossDepthsArtGenerator.ArtRoot;
        private const string ScenePath = "Assets/Scenes/MossDepths.unity";
        private const string ObjectPrefix = "MossDepths";

        [MenuItem("Game/Rebuild Mosslight Depths Scene")]
        public static void Build()
        {
            MossDepthsArtGenerator.GenerateIfMissing();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            // シーンを開くと未使用アセットがアンロードされるので、アセットの読み込みはこの後に行う。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = LoadOrCreate(Root + "MossDepthsSettings.asset", ScriptableObject.CreateInstance<MossDepthsSettings>);
            settings.Validate();
            var panel = LoadOrCreate(Root + "UI/MossDepthsPanelSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PanelSettings>();
                value.themeStyleSheet = Load<ThemeStyleSheet>(Root + "UI/MossDepthsTheme.tss");
                value.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                value.referenceResolution = new Vector2Int(1920, 1080);
                value.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                value.match = .5f;
                return value;
            });

            // Prefab を作り直すと古いインスタンスが別名で残ることがあるので、前方一致で消す。
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name.StartsWith(ObjectPrefix, StringComparison.Ordinal)) { Object.DestroyImmediate(root); }
            }

            var world = Rooms.Create();
            var stages = new MossDepthsStage[world.Rooms.Count];
            for (var i = 0; i < world.Rooms.Count; i++)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(LoadStage(i, world.Rooms[i]));
                instance.SetActive(i == world.StartRoom);
                stages[i] = instance.GetComponent<MossDepthsStage>();
            }

            var camera = Object.FindAnyObjectByType<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = (Room.Rows + MossDepthsGame.HudBandTiles) / 2;
            camera.transform.position = new Vector3(Room.Columns / 2f, (Room.Rows + MossDepthsGame.HudBandTiles) / 2, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.04f, .07f, .08f);
            for (var i = camera.transform.childCount - 1; i >= 0; i--) { Object.DestroyImmediate(camera.transform.GetChild(i).gameObject); }

            // 背景はカメラの子にして、いつも画面全体を覆う。
            var backdrop = new GameObject("Backdrop").AddComponent<SpriteRenderer>();
            backdrop.transform.SetParent(camera.transform, false);
            backdrop.transform.localPosition = new Vector3(0, 0, 20);
            backdrop.sprite = Load<Sprite>(Art + MossDepthsArtGenerator.BackdropFile);
            backdrop.sharedMaterial = Material();
            backdrop.sortingOrder = -100;

            var game = new GameObject(ObjectPrefix);
            var hud = game.AddComponent<PanelRenderer>();
            hud.panelSettings = panel;
            hud.visualTreeAsset = Load<VisualTreeAsset>(Root + "UI/Hud.uxml");
            var component = game.AddComponent<MossDepthsGame>();
            var serialized = new SerializedObject(component);
            Assign(serialized, "_settings", settings);
            Assign(serialized, "_hudRenderer", hud);
            Assign(serialized, "_camera", camera);
            AssignArray(serialized, "_stages", stages);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<MossDepthsCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "MOSSLIGHT DEPTHS";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();

            var count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<MossDepthsStage>() != null) { count++; }
            }

            if (count != world.Rooms.Count) { throw new InvalidOperationException($"Stage のインスタンスが {count} 個あります（部屋は {world.Rooms.Count} 個）。"); }
            Debug.Log("[MossDepthsSceneBuilder] シーン生成完了");
        }

        // 配置の正は Stage Prefab。既にあれば調整を上書きせず、地図と一致するかだけ確かめる。
        private static GameObject LoadStage(int index, Room room)
        {
            var path = $"{Art}MossDepthsStage{index + 1:00}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                var mismatch = existing.GetComponent<MossDepthsStage>().Mismatch(room);
                if (mismatch != null)
                {
                    throw new InvalidOperationException($"{path} が Core の部屋（{room.Name}）と一致しません：{mismatch} Prefab を消して作り直してください。");
                }

                return existing;
            }

            var material = Material();
            var root = new GameObject($"MossDepthsStage{index + 1:00}");
            try
            {
                var stage = root.AddComponent<MossDepthsStage>();
                var grid = new GameObject("Grid").AddComponent<Grid>();
                grid.transform.SetParent(root.transform, false);
                var doors = Tilemap(grid.transform, "Doors", 0, material);
                var ground = Tilemap(grid.transform, "Ground", 1, material);
                var cracked = Tilemap(grid.transform, "Cracked", 2, material);
                var mossTop = TileAsset("MossTop", MossDepthsArtGenerator.MossTopFile);
                var rock = TileAsset("Rock", MossDepthsArtGenerator.RockFile);
                var crackedTile = TileAsset("Cracked", MossDepthsArtGenerator.CrackedFile);
                var door = TileAsset("Door", MossDepthsArtGenerator.DoorFile);

                var map = room.Map;
                for (var row = 0; row < map.Height; row++)
                {
                    for (var column = 0; column < map.Width; column++)
                    {
                        var cell = new Vector3Int(column, row, 0);
                        switch (map[column, row])
                        {
                            case TileKind.Solid:
                                var covered = row + 1 < map.Height && map[column, row + 1] != TileKind.Empty && map[column, row + 1] != TileKind.Door;
                                ground.SetTile(cell, covered ? rock : mossTop);
                                break;
                            case TileKind.Cracked:
                                cracked.SetTile(cell, crackedTile);
                                break;
                            case TileKind.Door:
                                doors.SetTile(cell, door);
                                break;
                        }
                    }
                }

                var statues = new List<Object>();
                foreach (var cell in room.Statues)
                {
                    var statue = Sprite(root.transform, $"Statue{statues.Count}", MossDepthsArtGenerator.StatueFile, material, 3);
                    statue.transform.localPosition = new Vector3(cell.Column + .5f, cell.Row, 0);
                    statues.Add(statue);
                }

                var pickups = new List<Object>();
                foreach (var pickup in room.Pickups)
                {
                    var file = pickup.Kind switch
                    {
                        PickupKind.DoubleJump => MossDepthsArtGenerator.JumpOrbFile,
                        PickupKind.BreakShot => MossDepthsArtGenerator.BreakOrbFile,
                        _ => MossDepthsArtGenerator.FruitFile,
                    };
                    var item = Sprite(root.transform, $"Pickup{pickups.Count}{pickup.Kind}", file, material, 4);
                    item.transform.localPosition = new Vector3(pickup.Cell.Column + .5f, pickup.Cell.Row + .5f, 0);
                    pickups.Add(item);
                }

                var walkers = new List<Object>();
                foreach (var cell in room.Walkers)
                {
                    var walker = Sprite(root.transform, $"Walker{walkers.Count}", MossDepthsArtGenerator.Walk0File, material, 6);
                    PlaceFeet(walker, Room.PlaceOnTile(cell, Walker.Width, Walker.Height));
                    walkers.Add(walker);
                }

                SpriteRenderer boss = null;
                if (room.Boss is Cell bossCell)
                {
                    boss = Sprite(root.transform, "Boss", MossDepthsArtGenerator.BossIdleFile, material, 7);
                    PlaceFeet(boss, Room.PlaceOnTile(bossCell, Boss.Width, Boss.Height));
                }

                var player = Sprite(root.transform, "Player", MossDepthsArtGenerator.StandFile, material, 10);
                if (room.Start is Cell start) { PlaceFeet(player, Room.PlaceOnTile(start, Player.Width, Player.Height)); }
                else
                {
                    // 開始位置のない部屋では、最初の到着位置に置いておく（描画のときに試合の位置へ動かす）。
                    foreach (Side side in Enum.GetValues(typeof(Side)))
                    {
                        if (!room.HasDoor(side)) { continue; }
                        PlaceFeet(player, Room.PlaceOnTile(room.Arrival(side), Player.Width, Player.Height));
                        break;
                    }

                    player.enabled = false;
                }

                var shots = new List<Object>();
                for (var i = 0; i < ExplorerConfig.MaxShotsLimit; i++)
                {
                    var shot = Sprite(root.transform, $"Shot{i}", MossDepthsArtGenerator.ShotFile, material, 11);
                    shot.enabled = false;
                    shots.Add(shot);
                }

                var bullet = Sprite(root.transform, "Bullet", MossDepthsArtGenerator.BulletFile, material, 12);
                bullet.enabled = false;

                var serialized = new SerializedObject(stage);
                serialized.FindProperty("_roomIndex").intValue = index;
                Assign(serialized, "_ground", ground);
                Assign(serialized, "_cracked", cracked);
                Assign(serialized, "_doors", doors);
                Assign(serialized, "_crackedTile", crackedTile);
                Assign(serialized, "_player", player);
                Assign(serialized, "_stand", LoadSprite(MossDepthsArtGenerator.StandFile));
                AssignArray(serialized, "_run", new Object[] { LoadSprite(MossDepthsArtGenerator.Run0File), LoadSprite(MossDepthsArtGenerator.Run1File) });
                Assign(serialized, "_rise", LoadSprite(MossDepthsArtGenerator.RiseFile));
                Assign(serialized, "_fall", LoadSprite(MossDepthsArtGenerator.FallFile));
                Assign(serialized, "_hurt", LoadSprite(MossDepthsArtGenerator.HurtFile));
                AssignArray(serialized, "_statues", statues.ToArray());
                AssignArray(serialized, "_pickups", pickups.ToArray());
                AssignArray(serialized, "_walkers", walkers.ToArray());
                AssignArray(serialized, "_walk", new Object[] { LoadSprite(MossDepthsArtGenerator.Walk0File), LoadSprite(MossDepthsArtGenerator.Walk1File) });
                Assign(serialized, "_boss", boss);
                Assign(serialized, "_bossIdle", LoadSprite(MossDepthsArtGenerator.BossIdleFile));
                Assign(serialized, "_bossCharge", LoadSprite(MossDepthsArtGenerator.BossChargeFile));
                Assign(serialized, "_bossStunned", LoadSprite(MossDepthsArtGenerator.BossStunnedFile));
                AssignArray(serialized, "_shots", shots.ToArray());
                Assign(serialized, "_shot", LoadSprite(MossDepthsArtGenerator.ShotFile));
                Assign(serialized, "_breakShot", LoadSprite(MossDepthsArtGenerator.BreakShotFile));
                Assign(serialized, "_bulletTemplate", bullet);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void PlaceFeet(SpriteRenderer renderer, Box body)
        {
            renderer.transform.localPosition = new Vector3((float)body.CenterX / Units.Tile, (float)body.Bottom / Units.Tile, 0);
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

        private static SpriteRenderer Sprite(Transform parent, string name, string file, Material material, int order)
        {
            var item = new GameObject(name);
            item.transform.SetParent(parent, false);
            var renderer = item.AddComponent<SpriteRenderer>();
            renderer.sprite = LoadSprite(file);
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
