using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>
    /// シーンと Stage Prefab を生成する。Stage Prefab は既にあれば上書きせず、部品がそろっているかだけ確かめる。
    /// 実行：<c>unity run games/032-typing -- -executeMethod Game.Unity.Editor.InkVigilSceneBuilder.Build</c>
    /// </summary>
    public static class InkVigilSceneBuilder
    {
        public const string StagePath = Art + "InkVigilStage.prefab";
        private const string Root = "Assets/Game/Unity/";
        private const string Art = InkVigilArtGenerator.ArtRoot;
        private const string ScenePath = "Assets/Scenes/InkVigil.unity";
        private const string WordsPath = "Assets/Game/Data/Words.txt";
        private const string ObjectPrefix = "InkVigil";

        [MenuItem("Game/Rebuild Ink Vigil Scene")]
        public static void Build()
        {
            InkVigilArtGenerator.GenerateIfMissing();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            // シーンを開くと未使用アセットがアンロードされるので、アセットの読み込みはこの後に行う。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = LoadOrCreate(Root + "InkVigilSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<InkVigilSettings>();
                value.Words = Load<TextAsset>(WordsPath);
                return value;
            });
            settings.Validate();
            var panel = LoadOrCreate(Root + "UI/InkVigilPanelSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PanelSettings>();
                value.themeStyleSheet = Load<ThemeStyleSheet>(Root + "UI/InkVigilTheme.tss");
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
            var stage = instance.GetComponent<InkVigilStage>();

            var camera = Object.FindAnyObjectByType<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 4.5f;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.03f, .02f, .05f);
            for (var i = camera.transform.childCount - 1; i >= 0; i--) { Object.DestroyImmediate(camera.transform.GetChild(i).gameObject); }

            var game = new GameObject(ObjectPrefix);
            var hud = game.AddComponent<PanelRenderer>();
            hud.panelSettings = panel;
            hud.visualTreeAsset = Load<VisualTreeAsset>(Root + "UI/Hud.uxml");
            var component = game.AddComponent<InkVigilGame>();
            var serialized = new SerializedObject(component);
            Assign(serialized, "_settings", settings);
            Assign(serialized, "_hudRenderer", hud);
            Assign(serialized, "_camera", camera);
            Assign(serialized, "_stage", stage);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<InkVigilCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "INK VIGIL";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();

            var count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<InkVigilStage>() != null) { count++; }
            }

            if (count != 1) { throw new InvalidOperationException($"Stage のインスタンスが {count} 個あります。"); }
            Debug.Log("[InkVigilSceneBuilder] シーン生成完了");
        }

        // 配置の正は Stage Prefab。既にあれば調整を上書きせず、部品がそろっているかだけ確かめる。
        private static GameObject LoadStage()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(StagePath);
            if (existing != null)
            {
                var mismatch = existing.GetComponent<InkVigilStage>().Mismatch();
                if (mismatch != null) { throw new InvalidOperationException($"{StagePath} が欠けています：{mismatch} Prefab を消して作り直してください。"); }
                return existing;
            }

            var material = Material();
            var root = new GameObject("InkVigilStage");
            try
            {
                var stage = root.AddComponent<InkVigilStage>();
                var corridor = Sprite(root.transform, "Corridor", LoadSprite(InkVigilArtGenerator.CorridorFile), material, -100);
                var nearLine = Sprite(root.transform, "NearLine", LoadSprite(InkVigilArtGenerator.NearLineFile), material, 50);
                nearLine.transform.localPosition = new Vector3(0, InkVigilPerspective.NearY, 0);
                var keeper = Sprite(root.transform, "Keeper", LoadSprite(InkVigilArtGenerator.KeeperFile), material, 900);
                keeper.transform.localPosition = InkVigilStage.KeeperPosition;
                var flash = Sprite(root.transform, "Flash", LoadSprite(InkVigilArtGenerator.FlashFile), material, 1000);
                flash.transform.localScale = new Vector3(200, 100, 1);
                flash.enabled = false;
                var puff = Sprite(root.transform, "Puff", LoadSprite(InkVigilArtGenerator.PuffFile), material, 500);
                puff.enabled = false;

                var tierSprites = new Object[InkVigilArtGenerator.EnemyFiles.Length];
                for (var i = 0; i < tierSprites.Length; i++) { tierSprites[i] = LoadSprite(InkVigilArtGenerator.EnemyFiles[i]); }
                var enemies = new Object[InkVigilStage.MaxLanes];
                for (var i = 0; i < enemies.Length; i++)
                {
                    var enemy = Sprite(root.transform, $"Enemy{i}", (Sprite)tierSprites[0], material, 100);
                    enemy.enabled = false;
                    enemies[i] = enemy;
                }

                var serialized = new SerializedObject(stage);
                Assign(serialized, "_corridor", corridor);
                Assign(serialized, "_nearLine", nearLine);
                Assign(serialized, "_keeper", keeper);
                Assign(serialized, "_flash", flash);
                Assign(serialized, "_puff", puff);
                AssignArray(serialized, "_enemies", enemies);
                AssignArray(serialized, "_tierSprites", tierSprites);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                return PrefabUtility.SaveAsPrefabAsset(root, StagePath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
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
