#nullable enable
using System;
using System.IO;
using Game.Core;
using Game.Unity.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>
    /// EMBERLOCK HOLLOW のデータアセット・Stage Prefab・UI 設定・シーンを一括生成・検証するエディタスクリプト。
    /// 実行: <c>unity run games/041-jrpg -- -executeMethod Game.Unity.Editor.EmberlockSceneBuilder.Build</c>
    /// </summary>
    public static class EmberlockSceneBuilder
    {
        private const string UnityRoot = "Assets/Game/Unity/";
        private const string DataRoot = "Assets/Game/Data/";
        private const string ArtRoot = EmberlockArtGenerator.ArtDir;
        private const string PrefabRoot = UnityRoot + "Prefabs/";
        private const string ScenePath = "Assets/Scenes/EmberlockHollow.unity";
        private const string ObjectPrefix = "Emberlock";

        [MenuItem("Game/Rebuild Emberlock Hollow Scene")]
        public static void Build()
        {
            EmberlockArtGenerator.GenerateIfMissing();
            EnsureDirectory(DataRoot);
            EnsureDirectory(DataRoot + "Classes");
            EnsureDirectory(DataRoot + "Spells");
            EnsureDirectory(DataRoot + "Items");
            EnsureDirectory(DataRoot + "Equips");
            EnsureDirectory(DataRoot + "Enemies");
            EnsureDirectory(DataRoot + "Groups");
            EnsureDirectory(DataRoot + "Encounters");
            EnsureDirectory(DataRoot + "Shops");
            EnsureDirectory(DataRoot + "Maps");
            EnsureDirectory(PrefabRoot);
            EnsureDirectory(UnityRoot + "Tiles");

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null &&
                AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/SampleScene.unity") != null)
            {
                var moveErr = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(moveErr))
                {
                    throw new InvalidOperationException(moveErr);
                }
            }

            CreateOrVerifyDataAssets();
            CreateOrVerifyStagePrefabs();

            // シーンを開いてからアセットを読む（OpenScene によるアンロードで fileID 0 になるのを防ぐ）
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = LoadRequired<EmberlockSettings>(UnityRoot + "EmberlockSettings.asset");
            var database = LoadRequired<GameDatabase>(DataRoot + "GameDatabase.asset");
            var validatedData = database.BuildValidated(settings);

            var panelSettings = LoadOrCreateAsset(UnityRoot + "UI/EmberlockPanelSettings.asset", () =>
            {
                var ps = ScriptableObject.CreateInstance<PanelSettings>();
                ps.themeStyleSheet = LoadRequired<ThemeStyleSheet>(UnityRoot + "UI/EmberlockTheme.tss");
                ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                ps.referenceResolution = new Vector2Int(1920, 1080);
                ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                ps.match = 0.5f;
                return ps;
            });

            foreach (var rootObj in scene.GetRootGameObjects())
            {
                if (rootObj.name.StartsWith(ObjectPrefix, StringComparison.Ordinal) ||
                    rootObj.GetComponent<EmberlockGame>() != null ||
                    rootObj.GetComponent<EmberlockMapStage>() != null ||
                    rootObj.GetComponent<EmberlockBattleStage>() != null)
                {
                    Object.DestroyImmediate(rootObj);
                }
            }

            var worldRoot = new GameObject(ObjectPrefix + "World");
            var mapStages = new Object[validatedData.Maps.Count];
            for (var i = 0; i < validatedData.Maps.Count; i++)
            {
                var map = validatedData.Maps[i];
                var prefab = LoadRequired<GameObject>($"{PrefabRoot}Stage_{map.Id}.prefab");
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, worldRoot.transform);
                var stageComp = instance.GetComponent<EmberlockMapStage>();
                var mismatch = stageComp.Mismatch(map);
                if (mismatch != null)
                {
                    throw new InvalidOperationException($"Stage_{map.Id} 検証失敗: {mismatch}");
                }

                stageComp.ShowStaticPreview(validatedData);
                instance.SetActive(string.Equals(map.Id, validatedData.Rules.StartMapId, StringComparison.Ordinal));
                mapStages[i] = stageComp;
            }

            var battlePrefab = LoadRequired<GameObject>(PrefabRoot + "BattleStage.prefab");
            var battleInstance = (GameObject)PrefabUtility.InstantiatePrefab(battlePrefab, worldRoot.transform);
            var battleStage = battleInstance.GetComponent<EmberlockBattleStage>();
            var battleMismatch = battleStage.Mismatch();
            if (battleMismatch != null)
            {
                throw new InvalidOperationException($"BattleStage 検証失敗: {battleMismatch}");
            }

            battleInstance.SetActive(false);

            var camera = Object.FindAnyObjectByType<Camera>();
            if (camera == null)
            {
                var camObj = new GameObject("Main Camera");
                camObj.tag = "MainCamera";
                camera = camObj.AddComponent<Camera>();
            }

            camera.orthographic = true;
            camera.orthographicSize = 6.6f;
            var startMap = validatedData.GetMap(validatedData.Rules.StartMapId);
            camera.transform.position = new Vector3(startMap.Width * 0.5f, startMap.Height * 0.5f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.06f, 0.11f, 1f);

            var gameObj = new GameObject(ObjectPrefix + "Game");
            var hud = gameObj.AddComponent<PanelRenderer>();
            hud.panelSettings = panelSettings;
            hud.visualTreeAsset = LoadRequired<VisualTreeAsset>(UnityRoot + "UI/Hud.uxml");
            var gameComp = gameObj.AddComponent<EmberlockGame>();
            var so = new SerializedObject(gameComp);
            so.FindProperty("_settings").objectReferenceValue = settings;
            so.FindProperty("_database").objectReferenceValue = database;
            so.FindProperty("_hudRenderer").objectReferenceValue = hud;
            so.FindProperty("_camera").objectReferenceValue = camera;
            AssignObjectArray(so.FindProperty("_mapStages"), mapStages);
            so.FindProperty("_battleStage").objectReferenceValue = battleStage;
            so.ApplyModifiedPropertiesWithoutUndo();

            gameObj.AddComponent<EmberlockCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, enabled: true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "EMBERLOCK HOLLOW";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();

            var gameRoots = 0;
            foreach (var r in scene.GetRootGameObjects())
            {
                if (r.GetComponent<EmberlockGame>() != null)
                {
                    gameRoots++;
                }
            }

            if (gameRoots != 1)
            {
                throw new InvalidOperationException($"EmberlockGame の数が不正です: {gameRoots}");
            }

            Debug.Log($"[EmberlockSceneBuilder] シーン生成・検証完了 (Fingerprint=0x{validatedData.Fingerprint:X8})");
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
        }

        private static void CreateOrVerifyDataAssets()
        {
            var raw = DefaultGameData.CreateRaw();
            var settings = LoadOrCreateAsset(UnityRoot + "EmberlockSettings.asset", ScriptableObject.CreateInstance<EmberlockSettings>);
            settings.Validate();

            var classes = new ClassAsset[raw.Classes.Length];
            for (var i = 0; i < raw.Classes.Length; i++)
            {
                var def = raw.Classes[i];
                classes[i] = LoadOrCreateAsset($"{DataRoot}Classes/Class_{def.Id}.asset", () =>
                {
                    var a = ScriptableObject.CreateInstance<ClassAsset>();
                    a.Def = def.Copy();
                    return a;
                });
            }

            var spells = new SpellAsset[raw.Spells.Length];
            for (var i = 0; i < raw.Spells.Length; i++)
            {
                var def = raw.Spells[i];
                spells[i] = LoadOrCreateAsset($"{DataRoot}Spells/Spell_{def.Id}.asset", () =>
                {
                    var a = ScriptableObject.CreateInstance<SpellAsset>();
                    a.Def = def.Copy();
                    return a;
                });
            }

            var items = new ItemAsset[raw.Items.Length];
            for (var i = 0; i < raw.Items.Length; i++)
            {
                var def = raw.Items[i];
                items[i] = LoadOrCreateAsset($"{DataRoot}Items/Item_{def.Id}.asset", () =>
                {
                    var a = ScriptableObject.CreateInstance<ItemAsset>();
                    a.Def = def.Copy();
                    return a;
                });
            }

            var equips = new EquipAsset[raw.Equips.Length];
            for (var i = 0; i < raw.Equips.Length; i++)
            {
                var def = raw.Equips[i];
                equips[i] = LoadOrCreateAsset($"{DataRoot}Equips/Equip_{def.Id}.asset", () =>
                {
                    var a = ScriptableObject.CreateInstance<EquipAsset>();
                    a.Def = def.Copy();
                    return a;
                });
            }

            var enemies = new EnemyAsset[raw.Enemies.Length];
            for (var i = 0; i < raw.Enemies.Length; i++)
            {
                var def = raw.Enemies[i];
                var spritePath = ArtRoot + EnemySpriteFileName(def.Id);
                var spr = LoadRequired<Sprite>(spritePath);
                enemies[i] = LoadOrCreateAsset($"{DataRoot}Enemies/Enemy_{def.Id}.asset", () =>
                {
                    var a = ScriptableObject.CreateInstance<EnemyAsset>();
                    a.Def = def.Copy();
                    a.BattleSprite = spr;
                    return a;
                });
                if (enemies[i].BattleSprite == null)
                {
                    enemies[i].BattleSprite = spr;
                    EditorUtility.SetDirty(enemies[i]);
                }
            }

            var groups = new EnemyGroupAsset[raw.EnemyGroups.Length];
            for (var i = 0; i < raw.EnemyGroups.Length; i++)
            {
                var def = raw.EnemyGroups[i];
                groups[i] = LoadOrCreateAsset($"{DataRoot}Groups/Group_{def.Id}.asset", () =>
                {
                    var a = ScriptableObject.CreateInstance<EnemyGroupAsset>();
                    a.Def = def.Copy();
                    return a;
                });
            }

            var encounters = new EncounterAsset[raw.EncounterTables.Length];
            for (var i = 0; i < raw.EncounterTables.Length; i++)
            {
                var def = raw.EncounterTables[i];
                encounters[i] = LoadOrCreateAsset($"{DataRoot}Encounters/Encounter_{def.Id}.asset", () =>
                {
                    var a = ScriptableObject.CreateInstance<EncounterAsset>();
                    a.Def = def.Copy();
                    return a;
                });
            }

            var shops = new ShopAsset[raw.Shops.Length];
            for (var i = 0; i < raw.Shops.Length; i++)
            {
                var def = raw.Shops[i];
                shops[i] = LoadOrCreateAsset($"{DataRoot}Shops/Shop_{def.Id}.asset", () =>
                {
                    var a = ScriptableObject.CreateInstance<ShopAsset>();
                    a.Def = def.Copy();
                    return a;
                });
            }

            var maps = new MapAsset[raw.Maps.Length];
            for (var i = 0; i < raw.Maps.Length; i++)
            {
                var def = raw.Maps[i];
                var txtPath = $"{DataRoot}Maps/Grid_{def.Id}.txt";
                if (!File.Exists(txtPath))
                {
                    File.WriteAllText(txtPath, def.GridText + "\n");
                    AssetDatabase.ImportAsset(txtPath);
                }

                var gridTextAsset = LoadRequired<TextAsset>(txtPath);
                maps[i] = LoadOrCreateAsset($"{DataRoot}Maps/Map_{def.Id}.asset", () =>
                {
                    var m = ScriptableObject.CreateInstance<MapAsset>();
                    m.Id = def.Id;
                    m.MapName = def.Name;
                    m.IsSafeZone = def.IsSafeZone;
                    m.EncounterTableId = def.EncounterTableId;
                    m.GridFile = gridTextAsset;
                    m.Portals = def.Copy().Portals;
                    m.Chests = def.Copy().Chests;
                    m.Doors = def.Copy().Doors;
                    m.Npcs = def.Copy().Npcs;
                    m.Boss = def.Copy().Boss;
                    return m;
                });
            }

            var db = LoadOrCreateAsset(DataRoot + "GameDatabase.asset", () =>
            {
                var d = ScriptableObject.CreateInstance<GameDatabase>();
                d.BaseRules = raw.Rules.Copy();
                d.ExpTable = raw.ExpTable.Copy();
                d.Classes = classes;
                d.Spells = spells;
                d.Items = items;
                d.Equips = equips;
                d.Enemies = enemies;
                d.EnemyGroups = groups;
                d.EncounterTables = encounters;
                d.Shops = shops;
                d.Maps = maps;
                return d;
            });

            db.BuildValidated(settings);
            AssetDatabase.SaveAssets();
        }

        private static void CreateOrVerifyStagePrefabs()
        {
            var settings = LoadRequired<EmberlockSettings>(UnityRoot + "EmberlockSettings.asset");
            var db = LoadRequired<GameDatabase>(DataRoot + "GameDatabase.asset");
            var data = db.BuildValidated(settings);

            var wallTile = MakeOrLoadTile("WallTile.asset", "TileWall.png");
            var floorTile = MakeOrLoadTile("FloorTile.asset", "TileFloor.png");
            var grassTile = MakeOrLoadTile("GrassTile.asset", "TileGrass.png");
            var bridgeTile = MakeOrLoadTile("BridgeTile.asset", "TileBridge.png");
            var stairsTile = MakeOrLoadTile("StairsTile.asset", "TileStairs.png");
            var waterTile = MakeOrLoadTile("WaterTile.asset", "TileWater.png");

            var chestClosed = LoadRequired<Sprite>(ArtRoot + "ChestClosed.png");
            var chestOpen = LoadRequired<Sprite>(ArtRoot + "ChestOpen.png");
            var doorClosed = LoadRequired<Sprite>(ArtRoot + "DoorClosed.png");
            var bossFieldSprite = LoadRequired<Sprite>(ArtRoot + "BossField.png");
            var heroSprites = new Object[]
            {
                LoadRequired<Sprite>(ArtRoot + "HeroDown0.png"),
                LoadRequired<Sprite>(ArtRoot + "HeroDown1.png"),
                LoadRequired<Sprite>(ArtRoot + "HeroLeft0.png"),
                LoadRequired<Sprite>(ArtRoot + "HeroLeft1.png"),
                LoadRequired<Sprite>(ArtRoot + "HeroRight0.png"),
                LoadRequired<Sprite>(ArtRoot + "HeroRight1.png"),
                LoadRequired<Sprite>(ArtRoot + "HeroUp0.png"),
                LoadRequired<Sprite>(ArtRoot + "HeroUp1.png"),
            };

            for (var m = 0; m < data.Maps.Count; m++)
            {
                var map = data.Maps[m];
                var prefabPath = $"{PrefabRoot}Stage_{map.Id}.prefab";
                var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (existing != null)
                {
                    var checkStage = existing.GetComponent<EmberlockMapStage>();
                    if (checkStage != null && checkStage.Mismatch(map) == null)
                    {
                        continue;
                    }
                }

                var root = new GameObject($"Stage_{map.Id}");
                try
                {
                    var grid = root.AddComponent<Grid>();
                    grid.cellSize = Vector3.one;
                    var stage = root.AddComponent<EmberlockMapStage>();

                    var tmObj = new GameObject("Terrain");
                    tmObj.transform.SetParent(root.transform, worldPositionStays: false);
                    var tilemap = tmObj.AddComponent<Tilemap>();
                    var tmRenderer = tmObj.AddComponent<TilemapRenderer>();
                    tmRenderer.sortingOrder = -20;

                    for (var y = 0; y < map.Height; y++)
                    {
                        for (var x = 0; x < map.Width; x++)
                        {
                            var terrain = map.TerrainAt(x, y);
                            var tile = terrain switch
                            {
                                TileTerrain.Wall => wallTile,
                                TileTerrain.Floor => floorTile,
                                TileTerrain.Grass => grassTile,
                                TileTerrain.Bridge => bridgeTile,
                                TileTerrain.Stairs => stairsTile,
                                TileTerrain.Water => waterTile,
                                TileTerrain.LockedDoor => floorTile,
                                _ => wallTile,
                            };
                            tilemap.SetTile(EmberlockMapStage.TileCell(x, y, map.Height), tile);
                        }
                    }

                    var chestObjs = new Object[map.Chests.Count];
                    for (var i = 0; i < map.Chests.Count; i++)
                    {
                        var c = map.Chests[i];
                        chestObjs[i] = CreateChildSprite(root.transform, $"Chest_{i}", chestClosed, EmberlockMapStage.CellCenter(c.X, c.Y, map.Height), 5);
                    }

                    var doorObjs = new Object[map.Doors.Count];
                    for (var i = 0; i < map.Doors.Count; i++)
                    {
                        var d = map.Doors[i];
                        doorObjs[i] = CreateChildSprite(root.transform, $"Door_{i}", doorClosed, EmberlockMapStage.CellCenter(d.X, d.Y, map.Height), 6);
                    }

                    var npcObjs = new Object[map.Npcs.Count];
                    for (var i = 0; i < map.Npcs.Count; i++)
                    {
                        var n = map.Npcs[i];
                        var npcSprite = LoadRequired<Sprite>(ArtRoot + NpcSpriteFileName(n.Facility));
                        npcObjs[i] = CreateChildSprite(root.transform, $"Npc_{n.Id}", npcSprite, EmberlockMapStage.CellCenter(n.X, n.Y, map.Height), 8);
                    }

                    SpriteRenderer? bossRenderer = null;
                    if (map.Boss.Enabled)
                    {
                        bossRenderer = CreateChildSprite(
                            root.transform,
                            "BossField",
                            bossFieldSprite,
                            EmberlockMapStage.CellCenter(map.Boss.BossTileX, map.Boss.BossTileY, map.Height),
                            9);
                    }

                    var heroRenderer = CreateChildSprite(
                        root.transform,
                        "Hero",
                        (Sprite)heroSprites[0],
                        EmberlockMapStage.CellCenter(data.Rules.StartX, data.Rules.StartY, map.Height),
                        15);

                    var so = new SerializedObject(stage);
                    so.FindProperty("_mapId").stringValue = map.Id;
                    so.FindProperty("_terrainTilemap").objectReferenceValue = tilemap;
                    so.FindProperty("_wallTile").objectReferenceValue = wallTile;
                    so.FindProperty("_floorTile").objectReferenceValue = floorTile;
                    so.FindProperty("_grassTile").objectReferenceValue = grassTile;
                    so.FindProperty("_bridgeTile").objectReferenceValue = bridgeTile;
                    so.FindProperty("_stairsTile").objectReferenceValue = stairsTile;
                    so.FindProperty("_waterTile").objectReferenceValue = waterTile;
                    AssignObjectArray(so.FindProperty("_chests"), chestObjs);
                    so.FindProperty("_chestClosedSprite").objectReferenceValue = chestClosed;
                    so.FindProperty("_chestOpenSprite").objectReferenceValue = chestOpen;
                    AssignObjectArray(so.FindProperty("_doors"), doorObjs);
                    so.FindProperty("_doorClosedSprite").objectReferenceValue = doorClosed;
                    AssignObjectArray(so.FindProperty("_npcs"), npcObjs);
                    so.FindProperty("_bossRenderer").objectReferenceValue = bossRenderer;
                    so.FindProperty("_heroRenderer").objectReferenceValue = heroRenderer;
                    AssignObjectArray(so.FindProperty("_heroSprites"), heroSprites);
                    so.ApplyModifiedPropertiesWithoutUndo();

                    stage.ShowStaticPreview(data);
                    var mismatch = stage.Mismatch(map);
                    if (mismatch != null)
                    {
                        throw new InvalidOperationException($"Stage_{map.Id} 生成エラー: {mismatch}");
                    }

                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                }
                finally
                {
                    Object.DestroyImmediate(root);
                }
            }

            // BattleStage Prefab
            var battlePrefabPath = PrefabRoot + "BattleStage.prefab";
            var existingBattle = AssetDatabase.LoadAssetAtPath<GameObject>(battlePrefabPath);
            if (existingBattle == null || existingBattle.GetComponent<EmberlockBattleStage>()?.Mismatch() != null)
            {
                var root = new GameObject("BattleStage");
                try
                {
                    var battle = root.AddComponent<EmberlockBattleStage>();
                    var backdrop = CreateChildSprite(root.transform, "Backdrop", LoadRequired<Sprite>(ArtRoot + "BattleBackdrop.png"), Vector3.zero, -50);
                    var slots = new Object[EmberlockBattleStage.MaxEnemySlots];
                    var defaultEnemySprite = LoadRequired<Sprite>(ArtRoot + "EnemySlime.png");
                    for (var i = 0; i < slots.Length; i++)
                    {
                        slots[i] = CreateChildSprite(root.transform, $"EnemySlot_{i}", defaultEnemySprite, new Vector3((i - 1.5f) * 2.5f, 0.4f, 0f), 10 + i);
                    }

                    var arrow = CreateChildSprite(root.transform, "TargetArrow", LoadRequired<Sprite>(ArtRoot + "TargetArrow.png"), new Vector3(0f, 2f, 0f), 30);

                    var so = new SerializedObject(battle);
                    so.FindProperty("_backdrop").objectReferenceValue = backdrop;
                    AssignObjectArray(so.FindProperty("_enemySlots"), slots);
                    so.FindProperty("_targetArrow").objectReferenceValue = arrow;
                    so.ApplyModifiedPropertiesWithoutUndo();

                    PrefabUtility.SaveAsPrefabAsset(root, battlePrefabPath);
                }
                finally
                {
                    Object.DestroyImmediate(root);
                }
            }

            AssetDatabase.SaveAssets();
        }

        private static Tile MakeOrLoadTile(string tileFileName, string spriteFileName)
        {
            var sprite = LoadRequired<Sprite>(ArtRoot + spriteFileName);
            var tile = LoadOrCreateAsset($"{UnityRoot}Tiles/{tileFileName}", () =>
            {
                var t = ScriptableObject.CreateInstance<Tile>();
                t.sprite = sprite;
                t.colliderType = Tile.ColliderType.None;
                return t;
            });
            if (tile.sprite == null)
            {
                tile.sprite = sprite;
                EditorUtility.SetDirty(tile);
            }

            return tile;
        }

        private static SpriteRenderer CreateChildSprite(Transform parent, string name, Sprite sprite, Vector3 localPos, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = sortingOrder;
            return sr;
        }

        private static string EnemySpriteFileName(string enemyId) => enemyId switch
        {
            "soot_slime" => "EnemySlime.png",
            "cinder_bat" => "EnemyBat.png",
            "ember_hound" => "EnemyHound.png",
            "hollow_acolyte" => "EnemyAcolyte.png",
            "iron_sentinel" => "EnemySentinel.png",
            "boss_valug" => "EnemyBossValug.png",
            _ => "EnemySlime.png",
        };

        private static string NpcSpriteFileName(FacilityKind facility) => facility switch
        {
            FacilityKind.Inn => "NpcInn.png",
            FacilityKind.Church => "NpcPriest.png",
            FacilityKind.Shop => "NpcMerchant.png",
            _ => "NpcTown.png",
        };

        private static void AssignObjectArray(SerializedProperty prop, Object[] values)
        {
            prop.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        private static T LoadOrCreateAsset<T>(string path, Func<T> factory)
            where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                return existing;
            }

            EnsureDirectory(Path.GetDirectoryName(path) ?? "Assets");
            var created = factory();
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

        private static T LoadRequired<T>(string path)
            where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new InvalidOperationException($"必須アセットが見つかりません: {path}");
            }

            return asset;
        }

        private static void EnsureDirectory(string assetPath)
        {
            var normalized = assetPath.Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrEmpty(normalized) || AssetDatabase.IsValidFolder(normalized))
            {
                return;
            }

            var slash = normalized.LastIndexOf('/');
            if (slash > 0)
            {
                var parent = normalized.Substring(0, slash);
                EnsureDirectory(parent);
                var leaf = normalized.Substring(slash + 1);
                if (!AssetDatabase.IsValidFolder(normalized))
                {
                    AssetDatabase.CreateFolder(parent, leaf);
                }
            }
        }
    }
}
