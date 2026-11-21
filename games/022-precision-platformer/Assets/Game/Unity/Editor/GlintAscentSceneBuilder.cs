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
    public static class GlintAscentSceneBuilder
    {
        private const string Root = "Assets/Game/Unity/";
        private const string Art = GlintAscentArtGenerator.ArtRoot;
        private const string ScenePath = "Assets/Scenes/GlintAscent.unity";
        private const string ObjectPrefix = "GlintAscent";

        [MenuItem("Game/Rebuild Glint Ascent Scene")]
        public static void Build()
        {
            GlintAscentArtGenerator.GenerateIfMissing();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            // シーンを開くと未使用アセットがアンロードされるので、アセットの読み込みはこの後に行う。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = LoadOrCreate(Root + "GlintAscentSettings.asset", ScriptableObject.CreateInstance<GlintAscentSettings>);
            settings.Validate();
            var panel = LoadOrCreate(Root + "UI/GlintAscentPanelSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PanelSettings>();
                value.themeStyleSheet = Load<ThemeStyleSheet>(Root + "UI/GlintAscentTheme.tss");
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

            var rooms = Rooms.All();
            var stages = new GlintAscentStage[rooms.Count];
            for (var i = 0; i < rooms.Count; i++)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(LoadStage(i, rooms[i]));
                instance.SetActive(i == 0);
                stages[i] = instance.GetComponent<GlintAscentStage>();
            }

            var camera = Object.FindAnyObjectByType<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = (Room.Rows + GlintAscentGame.HudBandTiles) / 2;
            camera.transform.position = new Vector3(Room.Columns / 2f, (Room.Rows + GlintAscentGame.HudBandTiles) / 2, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.06f, .07f, .18f);
            for (var i = camera.transform.childCount - 1; i >= 0; i--) { Object.DestroyImmediate(camera.transform.GetChild(i).gameObject); }

            // 背景はカメラの子にして、いつも画面全体を覆う。
            var backdrop = new GameObject("Backdrop").AddComponent<SpriteRenderer>();
            backdrop.transform.SetParent(camera.transform, false);
            backdrop.transform.localPosition = new Vector3(0, 0, 20);
            backdrop.sprite = Load<Sprite>(Art + GlintAscentArtGenerator.BackdropFile);
            backdrop.sharedMaterial = Material();
            backdrop.sortingOrder = -100;

            var game = new GameObject(ObjectPrefix);
            var hud = game.AddComponent<PanelRenderer>();
            hud.panelSettings = panel;
            hud.visualTreeAsset = Load<VisualTreeAsset>(Root + "UI/Hud.uxml");
            var component = game.AddComponent<GlintAscentGame>();
            var serialized = new SerializedObject(component);
            Assign(serialized, "_settings", settings);
            Assign(serialized, "_hudRenderer", hud);
            Assign(serialized, "_camera", camera);
            AssignArray(serialized, "_stages", stages);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<GlintAscentCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "GLINT ASCENT";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();

            var count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<GlintAscentStage>() != null) { count++; }
            }

            if (count != rooms.Count) { throw new InvalidOperationException($"Stage のインスタンスが {count} 個あります（部屋は {rooms.Count} 個）。"); }
            Debug.Log("[GlintAscentSceneBuilder] シーン生成完了");
        }

        // 配置の正は Stage Prefab。既にあれば調整を上書きせず、地図と一致するかだけ確かめる。
        private static GameObject LoadStage(int index, Room room)
        {
            var path = $"{Art}GlintAscentStage{index + 1:00}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                var mismatch = existing.GetComponent<GlintAscentStage>().Mismatch(room);
                if (mismatch != null)
                {
                    throw new InvalidOperationException($"{path} が Core の部屋（{room.Name}）と一致しません：{mismatch} Prefab を消して作り直してください。");
                }

                return existing;
            }

            var material = Material();
            var root = new GameObject($"GlintAscentStage{index + 1:00}");
            try
            {
                var stage = root.AddComponent<GlintAscentStage>();
                var grid = new GameObject("Grid").AddComponent<Grid>();
                grid.transform.SetParent(root.transform, false);
                var ground = Tilemap(grid.transform, "Ground", 0, material);
                var spikes = Tilemap(grid.transform, "Spikes", 1, material);
                var exit = Tilemap(grid.transform, "Exit", 2, material);
                var rockTop = TileAsset("RockTop", GlintAscentArtGenerator.RockTopFile);
                var rock = TileAsset("Rock", GlintAscentArtGenerator.RockFile);
                var spike = TileAsset("Spike", GlintAscentArtGenerator.SpikeFile);
                var light = TileAsset("Exit", GlintAscentArtGenerator.ExitFile);

                var map = room.Map;
                for (var row = 0; row < map.Height; row++)
                {
                    for (var column = 0; column < map.Width; column++)
                    {
                        var cell = new Vector3Int(column, row, 0);
                        switch (map[column, row])
                        {
                            case TileKind.Solid:
                                ground.SetTile(cell, row + 1 < map.Height && map.IsSolid(column, row + 1) ? rock : rockTop);
                                break;
                            case TileKind.Spike:
                                spikes.SetTile(cell, spike);
                                break;
                            case TileKind.Exit:
                                exit.SetTile(cell, light);
                                break;
                        }
                    }
                }

                var player = Sprite(root.transform, "Player", GlintAscentArtGenerator.StandFile, material, 10);
                var start = room.StartBox;
                player.transform.localPosition = new Vector3((float)start.CenterX / Units.Tile, (float)start.Bottom / Units.Tile, 0);

                var serialized = new SerializedObject(stage);
                serialized.FindProperty("_roomIndex").intValue = index;
                Assign(serialized, "_ground", ground);
                Assign(serialized, "_spikes", spikes);
                Assign(serialized, "_exit", exit);
                Assign(serialized, "_player", player);
                Assign(serialized, "_stand", LoadSprite(GlintAscentArtGenerator.StandFile));
                AssignArray(serialized, "_run", new Object[] { LoadSprite(GlintAscentArtGenerator.Run0File), LoadSprite(GlintAscentArtGenerator.Run1File) });
                Assign(serialized, "_rise", LoadSprite(GlintAscentArtGenerator.RiseFile));
                Assign(serialized, "_fall", LoadSprite(GlintAscentArtGenerator.FallFile));
                Assign(serialized, "_dash", LoadSprite(GlintAscentArtGenerator.DashFile));
                Assign(serialized, "_slide", LoadSprite(GlintAscentArtGenerator.SlideFile));
                Assign(serialized, "_dead", LoadSprite(GlintAscentArtGenerator.DeadFile));
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
