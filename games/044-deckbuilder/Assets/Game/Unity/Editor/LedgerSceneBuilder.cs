using System;
using Game.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>
    /// シーンと Stage Prefab を生成する。Stage Prefab は既にあれば上書きせず、データと配置に合うかだけ確かめる。
    /// 実行：<c>unity run games/044-deckbuilder -- -executeMethod Game.Unity.Editor.LedgerSceneBuilder.Build</c>
    /// </summary>
    public static class LedgerSceneBuilder
    {
        private const string Root = "Assets/Game/Unity/";
        private const string Art = LedgerArtGenerator.ArtRoot;
        private const string DataRoot = "Assets/Game/Data/";
        private const string ScenePath = "Assets/Scenes/CinderLedger.unity";
        private const string StagePath = Art + "LedgerStage.prefab";
        private const string SettingsPath = Root + "LedgerSettings.asset";
        private const string ObjectPrefix = "Ledger";

        [MenuItem("Game/Rebuild Cinder Ledger Scene")]
        public static void Build()
        {
            LedgerArtGenerator.GenerateIfMissing();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            // シーンを開くと未使用アセットがアンロードされるので、アセットの読み込みはこの後に行う。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = LoadOrCreate(SettingsPath, () =>
            {
                var value = ScriptableObject.CreateInstance<LedgerSettings>();
                value.Cards = Load<TextAsset>(DataRoot + "Cards.txt");
                value.Enemies = Load<TextAsset>(DataRoot + "Enemies.txt");
                value.Encounters = Load<TextAsset>(DataRoot + "Encounters.txt");
                return value;
            });
            var data = settings.Validate();
            var panel = LoadOrCreate(Root + "UI/LedgerPanelSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PanelSettings>();
                value.themeStyleSheet = Load<ThemeStyleSheet>(Root + "UI/LedgerTheme.tss");
                value.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                value.referenceResolution = new Vector2Int(1920, 1080);
                value.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                value.match = 1f;
                return value;
            });

            // Prefab を作り直すと古いインスタンスが別名で残ることがあるので、前方一致で消す（018）。
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name.StartsWith(ObjectPrefix, StringComparison.Ordinal)) { Object.DestroyImmediate(root); }
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(LoadStage(data));
            instance.transform.position = Vector3.zero;
            var stage = instance.GetComponent<LedgerStage>();

            var camera = Object.FindAnyObjectByType<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5.4f;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.05f, .03f, .03f);

            var game = new GameObject(ObjectPrefix + "Game");
            var hud = game.AddComponent<PanelRenderer>();
            hud.panelSettings = panel;
            hud.visualTreeAsset = Load<VisualTreeAsset>(Root + "UI/Hud.uxml");
            var component = game.AddComponent<LedgerGame>();
            var serialized = new SerializedObject(component);
            Assign(serialized, "_settings", settings);
            Assign(serialized, "_hudRenderer", hud);
            Assign(serialized, "_camera", camera);
            Assign(serialized, "_stage", stage);
            AssignArray(serialized, "_cardIcons", Array.ConvertAll(LedgerArtGenerator.CardIds, id => LoadSprite(LedgerArtGenerator.CardFile(id))));
            AssignArray(serialized, "_intentIcons", Array.ConvertAll(LedgerArtGenerator.IntentFiles, LoadSprite));
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<LedgerCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "CINDER LEDGER";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();

            int count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<LedgerStage>() != null) { count++; }
            }

            if (count != 1) { throw new InvalidOperationException($"Stage のインスタンスが {count} 個あります。"); }
            Debug.Log("[LedgerSceneBuilder] シーン生成完了");
        }

        // 配置の正は Stage Prefab。既にあれば調整を上書きせず、データと配置に合うかだけ確かめる。
        private static GameObject LoadStage(GameData data)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(StagePath);
            if (existing != null)
            {
                var mismatch = existing.GetComponent<LedgerStage>().Mismatch(data);
                if (mismatch != null) { throw new InvalidOperationException($"{StagePath} がデータと合いません：{mismatch} Prefab を消して作り直してください。"); }
                return existing;
            }

            var material = LoadOrCreate(Art + "Unlit.mat", () => new Material(Shader.Find("Sprites/Default")));
            var root = new GameObject("LedgerStage");
            try
            {
                var stage = root.AddComponent<LedgerStage>();
                var background = Sprite(root.transform, "Background", LoadSprite(LedgerArtGenerator.BackgroundFile), material, -10);
                var player = Sprite(root.transform, "Player", LoadSprite(LedgerArtGenerator.PlayerFile), material, 2);
                player.transform.localPosition = LedgerStage.PlayerPosition;
                var marker = Sprite(root.transform, "Marker", LoadSprite(LedgerArtGenerator.MarkerFile), material, 1);
                marker.transform.localPosition = LedgerStage.SlotPosition(0);
                marker.enabled = false;
                var enemySprites = new Sprite[data.Enemies.Count];
                for (int i = 0; i < enemySprites.Length; i++) { enemySprites[i] = LoadSprite(LedgerArtGenerator.EnemyFile(data.Enemies[i].Id)); }
                var slots = new SpriteRenderer[LedgerStage.SlotCount];
                for (int i = 0; i < slots.Length; i++)
                {
                    slots[i] = Sprite(root.transform, $"Slot{i}", enemySprites[i], material, 2);
                    slots[i].transform.localPosition = LedgerStage.SlotPosition(i);
                }

                var serialized = new SerializedObject(stage);
                Assign(serialized, "_background", background);
                Assign(serialized, "_player", player);
                Assign(serialized, "_marker", marker);
                AssignArray(serialized, "_slots", slots);
                AssignArray(serialized, "_enemySprites", enemySprites);
                serialized.ApplyModifiedPropertiesWithoutUndo();

                var reason = stage.Mismatch(data);
                if (reason != null) { throw new InvalidOperationException("生成した Stage がデータと合いません：" + reason); }
                return PrefabUtility.SaveAsPrefabAsset(root, StagePath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

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
