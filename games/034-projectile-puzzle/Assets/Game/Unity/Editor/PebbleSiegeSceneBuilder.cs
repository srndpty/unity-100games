using System;
using Game.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Material = UnityEngine.Material;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>
    /// シーンと Stage Prefab を生成する。Stage Prefab と設定アセットは既にあれば上書きせず、部品がそろっているかだけ確かめる。
    /// 物理は Tick ごとに進めるので、Physics2D の simulationMode を Script にする。
    /// 実行：<c>unity run games/034-projectile-puzzle -- -executeMethod Game.Unity.Editor.PebbleSiegeSceneBuilder.Build</c>
    /// </summary>
    public static class PebbleSiegeSceneBuilder
    {
        public const string StagePath = Art + "PebbleSiegeStage.prefab";
        public const string ScenePath = "Assets/Scenes/PebbleSiege.unity";

        /// <summary>カメラの中心（盤の中心から少し下：地面の下 1 m と、上の帯の HUD の余白を取る）。</summary>
        public static readonly Vector3 CameraPosition = new(Field.Width / 2, 8.6f, -10);

        public const float CameraSize = 9.6f;

        private const string Root = "Assets/Game/Unity/";
        private const string Art = PebbleSiegeArtGenerator.ArtRoot;
        private const string ObjectPrefix = "PebbleSiege";

        [MenuItem("Game/Rebuild Pebble Siege Scene")]
        public static void Build()
        {
            PebbleSiegeArtGenerator.GenerateIfMissing();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            Physics2D.simulationMode = SimulationMode2D.Script;

            // シーンを開くと未使用アセットがアンロードされるので、アセットの読み込みはこの後に行う。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = LoadOrCreate(Root + "PebbleSiegeSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PebbleSiegeSettings>();
                value.Stages = Load<TextAsset>("Assets/Game/Data/Stages.txt");
                value.Solutions = Load<TextAsset>("Assets/Game/Data/Solutions.txt");
                return value;
            });
            settings.Validate();
            var panel = LoadOrCreate(Root + "UI/PebbleSiegePanelSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PanelSettings>();
                value.themeStyleSheet = Load<ThemeStyleSheet>(Root + "UI/PebbleSiegeTheme.tss");
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

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(LoadStage());
            instance.transform.position = Vector3.zero;
            var stage = instance.GetComponent<PebbleSiegeStage>();

            var camera = Object.FindAnyObjectByType<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = CameraSize;
            camera.transform.position = CameraPosition;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.30f, .40f, .66f);
            for (var i = camera.transform.childCount - 1; i >= 0; i--) { Object.DestroyImmediate(camera.transform.GetChild(i).gameObject); }

            var game = new GameObject(ObjectPrefix);
            var hud = game.AddComponent<PanelRenderer>();
            hud.panelSettings = panel;
            hud.visualTreeAsset = Load<VisualTreeAsset>(Root + "UI/Hud.uxml");
            var component = game.AddComponent<PebbleSiegeGame>();
            var serialized = new SerializedObject(component);
            Assign(serialized, "_settings", settings);
            Assign(serialized, "_hudRenderer", hud);
            Assign(serialized, "_camera", camera);
            Assign(serialized, "_stage", stage);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<PebbleSiegeCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "PEBBLE SIEGE";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();

            var count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<PebbleSiegeStage>() != null) { count++; }
            }

            if (count != 1) { throw new InvalidOperationException($"Stage のインスタンスが {count} 個あります。"); }
            Debug.Log("[PebbleSiegeSceneBuilder] シーン生成完了");
        }

        // 配置の正は Stage Prefab。既にあれば調整を上書きせず、部品がそろっているかだけ確かめる。
        private static GameObject LoadStage()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(StagePath);
            if (existing != null)
            {
                var mismatch = existing.GetComponent<PebbleSiegeStage>().Mismatch();
                if (mismatch != null) { throw new InvalidOperationException($"{StagePath} が欠けています：{mismatch} Prefab を消して作り直してください。"); }
                return existing;
            }

            var material = Material();
            var root = new GameObject("PebbleSiegeStage");
            try
            {
                var stage = root.AddComponent<PebbleSiegeStage>();
                var background = Sprite(root.transform, "Background", PebbleSiegeArtGenerator.BackgroundFile, material, -100);
                background.transform.localPosition = new Vector3(Field.Width / 2, 8, 0);
                var ground = Sprite(root.transform, "Ground", PebbleSiegeArtGenerator.GroundFile, material, -50);
                Tile(ground, new Vector2(Field.Width + (2 * PebbleSiegeWorld.GroundMargin), 2));
                ground.transform.localPosition = new Vector3(Field.Width / 2, -1, 0);

                var slingshot = Sprite(root.transform, "Slingshot", PebbleSiegeArtGenerator.SlingshotFile, material, 20);
                slingshot.transform.localPosition = new Vector3(4, 1.3f, 0);
                var loaded = Sprite(root.transform, "Loaded", PebbleSiegeArtGenerator.NormalFile, material, 30);
                loaded.transform.localPosition = new Vector3(4, 2.5f, 0);
                loaded.transform.localScale = new Vector3(.64f, .64f, 1);

                var bandObject = new GameObject("Band");
                bandObject.transform.SetParent(root.transform, false);
                var band = bandObject.AddComponent<LineRenderer>();
                band.sharedMaterial = material;
                band.useWorldSpace = false;
                band.widthMultiplier = .12f;
                band.sortingOrder = 25;
                band.startColor = band.endColor = new Color(.36f, .2f, .12f);
                band.positionCount = 3;
                band.SetPositions(new[] { new Vector3(3.45f, 2.55f, 0), new Vector3(4, 2.5f, 0), new Vector3(4.55f, 2.55f, 0) });

                var dots = new Object[PebbleSiegeStage.MaxDots];
                for (var i = 0; i < dots.Length; i++)
                {
                    var dot = Sprite(root.transform, "Dot" + i, PebbleSiegeArtGenerator.DotFile, material, 40);
                    dot.transform.localPosition = new Vector3(5 + (i * .5f), 3 + (i * .2f) - (i * i * .01f), 0);
                    dots[i] = dot;
                }

                var blast = Sprite(root.transform, "Blast", PebbleSiegeArtGenerator.BlastFile, material, 60);
                blast.transform.localPosition = new Vector3(-6, 12, 0);
                blast.enabled = false;
                var puffs = new Object[PebbleSiegeStage.MaxPuffs];
                for (var i = 0; i < puffs.Length; i++)
                {
                    var puff = Sprite(root.transform, "Puff" + i, PebbleSiegeArtGenerator.PuffFile, material, 55);
                    puff.transform.localPosition = new Vector3(-6 + (i * .4f), 10, 0);
                    puff.enabled = false;
                    puffs[i] = puff;
                }

                // 部材と弾の見本：盤の下（カメラの外）に並べ、Scene ビューで見えるようにする。
                var wood = Template(root.transform, "WoodTemplate", PebbleSiegeArtGenerator.WoodFile, material, 10, new Vector3(2, -5, 0), true);
                var stone = Template(root.transform, "StoneTemplate", PebbleSiegeArtGenerator.StoneFile, material, 10, new Vector3(4, -5, 0), true);
                var ice = Template(root.transform, "IceTemplate", PebbleSiegeArtGenerator.IceFile, material, 10, new Vector3(6, -5, 0), true);
                var lantern = Template(root.transform, "LanternTemplate", PebbleSiegeArtGenerator.LanternFile, material, 12, new Vector3(8, -5, 0), false);
                var normal = Template(root.transform, "NormalShotTemplate", PebbleSiegeArtGenerator.NormalFile, material, 30, new Vector3(10, -5, 0), false);
                var heavy = Template(root.transform, "HeavyShotTemplate", PebbleSiegeArtGenerator.HeavyFile, material, 30, new Vector3(12, -5, 0), false);
                var bomb = Template(root.transform, "BombShotTemplate", PebbleSiegeArtGenerator.BombFile, material, 30, new Vector3(14, -5, 0), false);

                var serialized = new SerializedObject(stage);
                Assign(serialized, "_background", background);
                Assign(serialized, "_ground", ground);
                Assign(serialized, "_slingshot", slingshot);
                Assign(serialized, "_loaded", loaded);
                Assign(serialized, "_band", band);
                Assign(serialized, "_blast", blast);
                AssignArray(serialized, "_dots", dots);
                AssignArray(serialized, "_puffs", puffs);
                Assign(serialized, "_wood", wood);
                Assign(serialized, "_stone", stone);
                Assign(serialized, "_ice", ice);
                Assign(serialized, "_lantern", lantern);
                Assign(serialized, "_normal", normal);
                Assign(serialized, "_heavy", heavy);
                Assign(serialized, "_bomb", bomb);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                return PrefabUtility.SaveAsPrefabAsset(root, StagePath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Material Material() => LoadOrCreate(Art + "Unlit.mat", () => new Material(Shader.Find("Sprites/Default")));

        private static SpriteRenderer Template(Transform parent, string name, string file, Material material, int order, Vector3 position, bool tiled)
        {
            var template = Sprite(parent, name, file, material, order);
            template.transform.localPosition = position;
            if (tiled) { Tile(template, Vector2.one); }
            return template;
        }

        private static void Tile(SpriteRenderer renderer, Vector2 size)
        {
            renderer.drawMode = SpriteDrawMode.Tiled;
            renderer.tileMode = SpriteTileMode.Continuous;
            renderer.size = size;
        }

        private static SpriteRenderer Sprite(Transform parent, string name, string file, Material material, int order)
        {
            var item = new GameObject(name);
            item.transform.SetParent(parent, false);
            var renderer = item.AddComponent<SpriteRenderer>();
            renderer.sprite = Load<Sprite>(Art + file);
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
