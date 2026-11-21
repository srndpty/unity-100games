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
    public static class BrambleDashSceneBuilder
    {
        private const string Root = "Assets/Game/Unity/";
        private const string Art = BrambleDashArtGenerator.ArtRoot;
        private const string ScenePath = "Assets/Scenes/BrambleDash.unity";
        private const string ObjectPrefix = "BrambleDash";

        [MenuItem("Game/Rebuild Bramble Dash Scene")]
        public static void Build()
        {
            BrambleDashArtGenerator.GenerateIfMissing();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            // シーンを開くと未使用アセットがアンロードされるので、アセットの読み込みはこの後に行う。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = LoadOrCreate(Root + "BrambleDashSettings.asset", ScriptableObject.CreateInstance<BrambleDashSettings>);
            settings.Validate();
            var panel = LoadOrCreate(Root + "UI/BrambleDashPanelSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PanelSettings>();
                value.themeStyleSheet = Load<ThemeStyleSheet>(Root + "UI/BrambleDashTheme.tss");
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

            var levels = Levels.All();
            var stages = new BrambleDashStage[levels.Length];
            for (var i = 0; i < levels.Length; i++)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(LoadStage(i, levels[i]));
                instance.SetActive(i == 0);
                stages[i] = instance.GetComponent<BrambleDashStage>();
            }

            var camera = Object.FindAnyObjectByType<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = BrambleDashGame.ViewHalfHeight;
            camera.transform.position = new Vector3(13.5f, 7.5f, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.46f, .66f, .86f);
            for (var i = camera.transform.childCount - 1; i >= 0; i--) { Object.DestroyImmediate(camera.transform.GetChild(i).gameObject); }

            // 背景はカメラの子にして、いつも画面全体を覆う。
            var backdrop = new GameObject("Backdrop").AddComponent<SpriteRenderer>();
            backdrop.transform.SetParent(camera.transform, false);
            backdrop.transform.localPosition = new Vector3(0, 0, 20);
            backdrop.sprite = Load<Sprite>(Art + BrambleDashArtGenerator.BackdropFile);
            backdrop.sharedMaterial = Material();
            backdrop.sortingOrder = -100;

            var game = new GameObject(ObjectPrefix);
            var hud = game.AddComponent<PanelRenderer>();
            hud.panelSettings = panel;
            hud.visualTreeAsset = Load<VisualTreeAsset>(Root + "UI/Hud.uxml");
            var component = game.AddComponent<BrambleDashGame>();
            var serialized = new SerializedObject(component);
            Assign(serialized, "_settings", settings);
            Assign(serialized, "_hudRenderer", hud);
            Assign(serialized, "_camera", camera);
            AssignArray(serialized, "_stages", stages);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<BrambleDashCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "BRAMBLE DASH";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();

            var count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<BrambleDashStage>() != null) { count++; }
            }

            if (count != levels.Length) { throw new InvalidOperationException($"Stage のインスタンスが {count} 個あります（面は {levels.Length} 個）。"); }
            Debug.Log("[BrambleDashSceneBuilder] シーン生成完了");
        }

        // 配置の正は Stage Prefab。既にあれば調整を上書きせず、地図と一致するかだけ確かめる。
        private static GameObject LoadStage(int index, Level level)
        {
            var path = $"{Art}BrambleDashStage{index + 1}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                var mismatch = existing.GetComponent<BrambleDashStage>().Mismatch(level);
                if (mismatch != null)
                {
                    throw new InvalidOperationException($"{path} が Core の地図（{level.Name}）と一致しません：{mismatch} Prefab を消して作り直してください。");
                }

                return existing;
            }

            var material = Material();
            var root = new GameObject($"BrambleDashStage{index + 1}");
            try
            {
                var stage = root.AddComponent<BrambleDashStage>();
                var grid = new GameObject("Grid").AddComponent<Grid>();
                grid.transform.SetParent(root.transform, false);
                var ground = Tilemap(grid.transform, "Ground", 0, material);
                var goal = Tilemap(grid.transform, "Goal", 2, material);
                var grass = TileAsset("Grass", BrambleDashArtGenerator.GrassFile);
                var dirt = TileAsset("Dirt", BrambleDashArtGenerator.DirtFile);
                var pole = TileAsset("Pole", BrambleDashArtGenerator.PoleFile);

                var map = level.Map;
                var flagTop = Vector2Int.zero;
                for (var row = 0; row < map.Height; row++)
                {
                    for (var column = 0; column < map.Width; column++)
                    {
                        var cell = new Vector3Int(column, row, 0);
                        switch (map[column, row])
                        {
                            case TileKind.Solid:
                                ground.SetTile(cell, map.IsSolid(column, row + 1) ? dirt : grass);
                                break;
                            case TileKind.Goal:
                                goal.SetTile(cell, pole);
                                if (row >= flagTop.y) { flagTop = new Vector2Int(column, row); }
                                break;
                        }
                    }
                }

                var flag = Sprite(root.transform, "Flag", BrambleDashArtGenerator.FlagFile, material, 3);
                flag.transform.localPosition = new Vector3(flagTop.x + .5f, flagTop.y + .3f, 0);

                var enemies = new GameObject("Enemies").transform;
                enemies.SetParent(root.transform, false);
                var renderers = new SpriteRenderer[level.Enemies.Count];
                for (var i = 0; i < renderers.Length; i++)
                {
                    var spawn = level.Enemies[i];
                    var file = spawn.Kind switch
                    {
                        EnemyKind.Walker => BrambleDashArtGenerator.Walker0File,
                        EnemyKind.Hopper => BrambleDashArtGenerator.HopperFile,
                        _ => BrambleDashArtGenerator.Thorn0File,
                    };
                    renderers[i] = Sprite(enemies, $"{i:00}-{spawn.Kind}", file, material, 5);
                    renderers[i].transform.localPosition = new Vector3(spawn.Column + .5f, spawn.Row, 0);
                }

                var player = Sprite(root.transform, "Player", BrambleDashArtGenerator.PlayerStandFile, material, 10);
                player.transform.localPosition = new Vector3(level.StartColumn + .5f, level.StartRow, 0);

                var serialized = new SerializedObject(stage);
                serialized.FindProperty("_levelIndex").intValue = index;
                Assign(serialized, "_ground", ground);
                Assign(serialized, "_player", player);
                AssignArray(serialized, "_enemies", renderers);
                Assign(serialized, "_playerStand", LoadSprite(BrambleDashArtGenerator.PlayerStandFile));
                AssignArray(serialized, "_playerRun", new Object[] { LoadSprite(BrambleDashArtGenerator.PlayerRun0File), LoadSprite(BrambleDashArtGenerator.PlayerRun1File) });
                Assign(serialized, "_playerJump", LoadSprite(BrambleDashArtGenerator.PlayerJumpFile));
                Assign(serialized, "_playerMiss", LoadSprite(BrambleDashArtGenerator.PlayerMissFile));
                AssignArray(serialized, "_walker", new Object[] { LoadSprite(BrambleDashArtGenerator.Walker0File), LoadSprite(BrambleDashArtGenerator.Walker1File) });
                Assign(serialized, "_walkerSquashed", LoadSprite(BrambleDashArtGenerator.WalkerSquashedFile));
                Assign(serialized, "_hopper", LoadSprite(BrambleDashArtGenerator.HopperFile));
                Assign(serialized, "_hopperAir", LoadSprite(BrambleDashArtGenerator.HopperAirFile));
                Assign(serialized, "_hopperSquashed", LoadSprite(BrambleDashArtGenerator.HopperSquashedFile));
                AssignArray(serialized, "_thorn", new Object[] { LoadSprite(BrambleDashArtGenerator.Thorn0File), LoadSprite(BrambleDashArtGenerator.Thorn1File) });
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
