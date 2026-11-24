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
    /// 実行：<c>unity run games/033-rope-puzzle -- -executeMethod Game.Unity.Editor.DewdropSnipSceneBuilder.Build</c>
    /// </summary>
    public static class DewdropSnipSceneBuilder
    {
        public const string StagePath = Art + "DewdropSnipStage.prefab";
        public const string ScenePath = "Assets/Scenes/DewdropSnip.unity";

        /// <summary>カメラの中心（盤の中心から少し上：上の帯の HUD が盤に重ならないよう、上に余白を取る）。</summary>
        public static readonly Vector3 CameraPosition = new(0, .45f, -10);

        public const float CameraSize = 5f;

        private const string Root = "Assets/Game/Unity/";
        private const string Art = DewdropSnipArtGenerator.ArtRoot;
        private const string ObjectPrefix = "DewdropSnip";

        [MenuItem("Game/Rebuild Dewdrop Snip Scene")]
        public static void Build()
        {
            DewdropSnipArtGenerator.GenerateIfMissing();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            // シーンを開くと未使用アセットがアンロードされるので、アセットの読み込みはこの後に行う。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = LoadOrCreate(Root + "DewdropSnipSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<DewdropSnipSettings>();
                value.Puzzles = Load<TextAsset>("Assets/Game/Data/Puzzles.txt");
                value.Solutions = Load<TextAsset>("Assets/Game/Data/Solutions.txt");
                return value;
            });
            settings.Validate();
            var panel = LoadOrCreate(Root + "UI/DewdropSnipPanelSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PanelSettings>();
                value.themeStyleSheet = Load<ThemeStyleSheet>(Root + "UI/DewdropSnipTheme.tss");
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
            var stage = instance.GetComponent<DewdropSnipStage>();

            var camera = Object.FindAnyObjectByType<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = CameraSize;
            camera.transform.position = CameraPosition;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.05f, .07f, .08f);
            for (var i = camera.transform.childCount - 1; i >= 0; i--) { Object.DestroyImmediate(camera.transform.GetChild(i).gameObject); }

            var game = new GameObject(ObjectPrefix);
            var hud = game.AddComponent<PanelRenderer>();
            hud.panelSettings = panel;
            hud.visualTreeAsset = Load<VisualTreeAsset>(Root + "UI/Hud.uxml");
            var component = game.AddComponent<DewdropSnipGame>();
            var serialized = new SerializedObject(component);
            Assign(serialized, "_settings", settings);
            Assign(serialized, "_hudRenderer", hud);
            Assign(serialized, "_camera", camera);
            Assign(serialized, "_stage", stage);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<DewdropSnipCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "DEWDROP SNIP";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();

            var count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<DewdropSnipStage>() != null) { count++; }
            }

            if (count != 1) { throw new InvalidOperationException($"Stage のインスタンスが {count} 個あります。"); }
            Debug.Log("[DewdropSnipSceneBuilder] シーン生成完了");
        }

        // 配置の正は Stage Prefab。既にあれば調整を上書きせず、部品がそろっているかだけ確かめる。
        private static GameObject LoadStage()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(StagePath);
            if (existing != null)
            {
                var mismatch = existing.GetComponent<DewdropSnipStage>().Mismatch();
                if (mismatch != null) { throw new InvalidOperationException($"{StagePath} が欠けています：{mismatch} Prefab を消して作り直してください。"); }
                return existing;
            }

            var material = Material();
            var root = new GameObject("DewdropSnipStage");
            try
            {
                var stage = root.AddComponent<DewdropSnipStage>();
                var background = Sprite(root.transform, "Background", DewdropSnipArtGenerator.BackgroundFile, material, -100);
                var anchors = Parts(root.transform, "Anchor", DewdropSnipArtGenerator.AnchorFile, material, 30, DewdropSnipStage.MaxRopes);
                var walls = Parts(root.transform, "Wall", DewdropSnipArtGenerator.WallFile, material, 10, DewdropSnipStage.MaxWalls);
                foreach (SpriteRenderer wall in walls)
                {
                    wall.drawMode = SpriteDrawMode.Tiled;
                    wall.tileMode = SpriteTileMode.Continuous;
                    wall.size = new Vector2(2, .5f);
                }

                var spikes = Parts(root.transform, "Spike", DewdropSnipArtGenerator.ThornFile, material, 20, DewdropSnipStage.MaxSpikes);
                var goal = Sprite(root.transform, "Goal", DewdropSnipArtGenerator.GoalFile, material, 15);
                goal.transform.localPosition = new Vector3(0, -3, 0);
                goal.transform.localScale = new Vector3(1.25f, 1.25f, 1);

                var ropes = new GameObject("Ropes");
                ropes.transform.SetParent(root.transform, false);
                var filter = ropes.AddComponent<MeshFilter>();
                var meshRenderer = ropes.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = material;
                meshRenderer.sortingOrder = 25;

                var body = Sprite(root.transform, "Body", DewdropSnipArtGenerator.DewdropFile, material, 40);
                body.transform.localScale = new Vector3(.69f, .69f, 1);
                var snip = Sprite(root.transform, "Snip", DewdropSnipArtGenerator.SnipFile, material, 60);
                snip.enabled = false;

                var trailObject = new GameObject("Trail");
                trailObject.transform.SetParent(root.transform, false);
                var trail = trailObject.AddComponent<LineRenderer>();
                trail.sharedMaterial = material;
                trail.useWorldSpace = false;
                trail.widthMultiplier = .08f;
                trail.numCapVertices = 2;
                trail.sortingOrder = 70;
                trail.colorGradient = new Gradient
                {
                    colorKeys = new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                    alphaKeys = new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(.85f, 1) },
                };
                trail.positionCount = 0;
                trail.enabled = false;

                var serialized = new SerializedObject(stage);
                Assign(serialized, "_background", background);
                Assign(serialized, "_body", body);
                Assign(serialized, "_goal", goal);
                Assign(serialized, "_snip", snip);
                AssignArray(serialized, "_anchors", anchors);
                AssignArray(serialized, "_walls", walls);
                AssignArray(serialized, "_spikes", spikes);
                Assign(serialized, "_ropes", filter);
                Assign(serialized, "_trail", trail);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                return PrefabUtility.SaveAsPrefabAsset(root, StagePath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Material Material() => LoadOrCreate(Art + "Unlit.mat", () => new Material(Shader.Find("Sprites/Default")));

        /// <summary>置き場を <paramref name="count"/> 個並べる（Scene ビューで見えるよう、横に並べておく）。</summary>
        private static Object[] Parts(Transform parent, string name, string file, Material material, int order, int count)
        {
            var parts = new Object[count];
            for (var i = 0; i < count; i++)
            {
                var part = Sprite(parent, name + i, file, material, order);
                part.transform.localPosition = new Vector3(-6 + (i * 1.3f), name == "Anchor" ? 4 : name == "Wall" ? -1 : -4, 0);
                parts[i] = part;
            }

            return parts;
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
