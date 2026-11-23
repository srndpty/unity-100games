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
    /// シーンと Stage Prefab を生成する。Stage Prefab は Core の施設から作り、既にあれば上書きせず施設と一致するかだけ確かめる。
    /// 実行：<c>unity run games/030-stealth -- -executeMethod Game.Unity.Editor.HushWardSceneBuilder.Build</c>
    /// </summary>
    public static class HushWardSceneBuilder
    {
        public const string StagePath = Art + "HushWardStage.prefab";
        private const string Root = "Assets/Game/Unity/";
        private const string Art = HushWardArtGenerator.ArtRoot;
        private const string ScenePath = "Assets/Scenes/HushWard.unity";
        private const string ObjectPrefix = "HushWard";

        [MenuItem("Game/Rebuild Hush Ward Scene")]
        public static void Build()
        {
            HushWardArtGenerator.GenerateIfMissing();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            // シーンを開くと未使用アセットがアンロードされるので、アセットの読み込みはこの後に行う。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = LoadOrCreate(Root + "HushWardSettings.asset", ScriptableObject.CreateInstance<HushWardSettings>);
            settings.Validate();
            var panel = LoadOrCreate(Root + "UI/HushWardPanelSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PanelSettings>();
                value.themeStyleSheet = Load<ThemeStyleSheet>(Root + "UI/HushWardTheme.tss");
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

            var facility = Facility.Ward();
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(LoadStage(facility));
            var stage = instance.GetComponent<HushWardStage>();

            var camera = Object.FindAnyObjectByType<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = facility.Map.Height / 2f;
            camera.transform.position = new Vector3(facility.Map.Width / 2f, facility.Map.Height / 2f, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.02f, .03f, .05f);
            for (var i = camera.transform.childCount - 1; i >= 0; i--) { Object.DestroyImmediate(camera.transform.GetChild(i).gameObject); }

            // 背景はカメラの子にして、いつも画面全体を覆う。
            var backdrop = new GameObject("Backdrop").AddComponent<SpriteRenderer>();
            backdrop.transform.SetParent(camera.transform, false);
            backdrop.transform.localPosition = new Vector3(0, 0, 20);
            backdrop.sprite = Load<Sprite>(Art + HushWardArtGenerator.BackdropFile);
            backdrop.sharedMaterial = Material();
            backdrop.sortingOrder = -100;

            var game = new GameObject(ObjectPrefix);
            var hud = game.AddComponent<PanelRenderer>();
            hud.panelSettings = panel;
            hud.visualTreeAsset = Load<VisualTreeAsset>(Root + "UI/Hud.uxml");
            var component = game.AddComponent<HushWardGame>();
            var serialized = new SerializedObject(component);
            Assign(serialized, "_settings", settings);
            Assign(serialized, "_hudRenderer", hud);
            Assign(serialized, "_camera", camera);
            Assign(serialized, "_stage", stage);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<HushWardCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "HUSH WARD";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();

            var count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<HushWardStage>() != null) { count++; }
            }

            if (count != 1) { throw new InvalidOperationException($"Stage のインスタンスが {count} 個あります。"); }
            Debug.Log("[HushWardSceneBuilder] シーン生成完了");
        }

        // 配置の正は Stage Prefab。既にあれば調整を上書きせず、施設と一致するかだけ確かめる。
        private static GameObject LoadStage(Facility facility)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(StagePath);
            if (existing != null)
            {
                var mismatch = existing.GetComponent<HushWardStage>().Mismatch(facility);
                if (mismatch != null) { throw new InvalidOperationException($"{StagePath} が Core の施設と一致しません：{mismatch} Prefab を消して作り直してください。"); }
                return existing;
            }

            var material = Material();
            var root = new GameObject("HushWardStage");
            try
            {
                var stage = root.AddComponent<HushWardStage>();
                var grid = new GameObject("Grid").AddComponent<Grid>();
                grid.transform.SetParent(root.transform, false);
                var floor = Tilemap(grid.transform, "Floor", 0, material);
                var noise = Tilemap(grid.transform, "Noise", 1, material);
                var walls = Tilemap(grid.transform, "Walls", 3, material);
                var crates = Tilemap(grid.transform, "Crates", 3, material);
                var floorTile = TileAsset("Floor", HushWardArtGenerator.FloorFile);
                var exitTile = TileAsset("Exit", HushWardArtGenerator.ExitFile);
                var wall = TileAsset("Wall", HushWardArtGenerator.WallFile);
                var wallFace = TileAsset("WallFace", HushWardArtGenerator.WallFaceFile);
                var crate = TileAsset("Crate", HushWardArtGenerator.CrateFile);
                var noiseTile = TileAsset("Noise", HushWardArtGenerator.NoiseFile);

                var map = facility.Map;
                for (var row = 0; row < map.Height; row++)
                {
                    for (var column = 0; column < map.Width; column++)
                    {
                        var cell = new Vector3Int(column, row, 0);
                        var kind = map[column, row];
                        floor.SetTile(cell, kind == TileKind.Exit ? exitTile : floorTile);
                        if (kind == TileKind.Wall)
                        {
                            // 下が壁でない壁は手前の面を明るくする。
                            walls.SetTile(cell, row > 0 && map[column, row - 1] != TileKind.Wall ? wallFace : wall);
                        }
                        else if (kind == TileKind.Crate)
                        {
                            crates.SetTile(cell, crate);
                        }
                    }
                }

                var objective = Sprite(root.transform, "Objective", LoadSprite(HushWardArtGenerator.ObjectiveFile), material, 4);
                objective.transform.localPosition = HushWardStage.CellCenter(facility.Objective);

                var intruderDown = new Object[] { LoadSprite(HushWardArtGenerator.IntruderDown0File), LoadSprite(HushWardArtGenerator.IntruderDown1File) };
                var intruderUp = new Object[] { LoadSprite(HushWardArtGenerator.IntruderUp0File), LoadSprite(HushWardArtGenerator.IntruderUp1File) };
                var intruderSide = new Object[] { LoadSprite(HushWardArtGenerator.IntruderSide0File), LoadSprite(HushWardArtGenerator.IntruderSide1File) };
                var intruder = Sprite(root.transform, "Intruder", (Sprite)intruderUp[0], material, 7);
                intruder.transform.localPosition = HushWardStage.CellCenter(facility.Exit);
                var carried = Sprite(intruder.transform, "Carried", LoadSprite(HushWardArtGenerator.CarriedFile), material, 8);
                carried.transform.localPosition = new Vector3(0, .62f, 0);
                carried.enabled = false;

                var guards = new Object[facility.Guards.Count];
                for (var i = 0; i < guards.Length; i++) { guards[i] = GuardView(root.transform, facility.Guards[i], material); }

                var serialized = new SerializedObject(stage);
                Assign(serialized, "_floor", floor);
                Assign(serialized, "_walls", walls);
                Assign(serialized, "_crates", crates);
                Assign(serialized, "_noise", noise);
                Assign(serialized, "_floorTile", floorTile);
                Assign(serialized, "_exitTile", exitTile);
                Assign(serialized, "_noiseTile", noiseTile);
                Assign(serialized, "_objective", objective);
                Assign(serialized, "_intruder", intruder);
                Assign(serialized, "_carried", carried);
                AssignArray(serialized, "_intruderDown", intruderDown);
                AssignArray(serialized, "_intruderUp", intruderUp);
                AssignArray(serialized, "_intruderSide", intruderSide);
                Assign(serialized, "_intruderSneak", LoadSprite(HushWardArtGenerator.IntruderSneakFile));
                AssignArray(serialized, "_guards", guards);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                return PrefabUtility.SaveAsPrefabAsset(root, StagePath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>警備員 1 人の描画の部品（体・扇・記号・ゲージ・詳しい表示の印と巡回路）。</summary>
        private static HushWardGuardView GuardView(Transform parent, GuardPlan plan, Material material)
        {
            var item = new GameObject($"Guard{plan.Name}");
            item.transform.SetParent(parent, false);
            item.transform.localPosition = HushWardStage.CellCenter(plan.Start);
            var view = item.AddComponent<HushWardGuardView>();

            var cone = new GameObject("Cone");
            cone.transform.SetParent(item.transform, false);
            var filter = cone.AddComponent<MeshFilter>();
            var coneRenderer = cone.AddComponent<MeshRenderer>();
            coneRenderer.sharedMaterial = material;
            coneRenderer.sortingOrder = 2;

            var down = new Object[] { LoadSprite(HushWardArtGenerator.GuardDown0File), LoadSprite(HushWardArtGenerator.GuardDown1File) };
            var body = Sprite(item.transform, "Body", (Sprite)down[0], material, 6);
            var mark = Sprite(item.transform, "Mark", LoadSprite(HushWardArtGenerator.QuestionFile), material, 12);
            mark.transform.localPosition = new Vector3(0, .95f, 0);
            mark.enabled = false;
            var gaugeBack = Sprite(item.transform, "GaugeBack", LoadSprite(HushWardArtGenerator.GaugeBackFile), material, 11);
            gaugeBack.transform.localPosition = new Vector3(0, .58f, 0);
            gaugeBack.enabled = false;
            var gaugeFill = Sprite(item.transform, "GaugeFill", LoadSprite(HushWardArtGenerator.GaugeFillFile), material, 12);
            gaugeFill.transform.localPosition = new Vector3(0, .58f, 0);
            gaugeFill.enabled = false;
            var goal = Sprite(item.transform, "DebugGoal", LoadSprite(HushWardArtGenerator.GoalFile), material, 13);
            goal.enabled = false;
            var noticed = Sprite(item.transform, "DebugNoticed", LoadSprite(HushWardArtGenerator.NoticedFile), material, 13);
            noticed.enabled = false;

            var route = new GameObject("DebugRoute").AddComponent<LineRenderer>();
            route.transform.SetParent(item.transform, false);
            route.useWorldSpace = true;
            route.sharedMaterial = material;
            route.widthMultiplier = .07f;
            route.startColor = new Color(.45f, .85f, 1f, .8f);
            route.endColor = new Color(.45f, .85f, 1f, .8f);
            route.sortingOrder = 8;
            route.positionCount = 0;
            route.enabled = false;

            var serialized = new SerializedObject(view);
            Assign(serialized, "_body", body);
            Assign(serialized, "_cone", filter);
            Assign(serialized, "_coneRenderer", coneRenderer);
            Assign(serialized, "_mark", mark);
            Assign(serialized, "_gaugeBack", gaugeBack);
            Assign(serialized, "_gaugeFill", gaugeFill);
            Assign(serialized, "_goal", goal);
            Assign(serialized, "_noticed", noticed);
            Assign(serialized, "_route", route);
            AssignArray(serialized, "_down", down);
            AssignArray(serialized, "_up", new Object[] { LoadSprite(HushWardArtGenerator.GuardUp0File), LoadSprite(HushWardArtGenerator.GuardUp1File) });
            AssignArray(serialized, "_side", new Object[] { LoadSprite(HushWardArtGenerator.GuardSide0File), LoadSprite(HushWardArtGenerator.GuardSide1File) });
            Assign(serialized, "_question", LoadSprite(HushWardArtGenerator.QuestionFile));
            Assign(serialized, "_exclaim", LoadSprite(HushWardArtGenerator.ExclaimFile));
            Assign(serialized, "_searching", LoadSprite(HushWardArtGenerator.SearchingFile));
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return view;
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
