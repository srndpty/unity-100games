using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    public static class TideClaimSceneBuilder
    {
        private const string Root = "Assets/Game/Unity/";
        private const string Art = TideClaimArtGenerator.ArtRoot;
        private const string ScenePath = "Assets/Scenes/TideClaim.unity";
        private const string StagePath = Art + "TideClaimStage.prefab";
        private const string ObjectPrefix = "TideClaim";

        /// <summary>盤は画面の左寄りに置き、右の 4 分の 1 を HUD に空ける。</summary>
        private const float StageCenterX = -2.6f;
        private const float StageScale = .95f;

        [MenuItem("Game/Rebuild Tide Claim Scene")]
        public static void Build()
        {
            TideClaimArtGenerator.GenerateIfMissing();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            // シーンを開くと未使用アセットがアンロードされるので、アセットの読み込みはこの後に行う。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = LoadOrCreate(Root + "TideClaimSettings.asset", ScriptableObject.CreateInstance<TideClaimSettings>);
            settings.Validate();
            var panel = LoadOrCreate(Root + "UI/TideClaimPanelSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PanelSettings>();
                value.themeStyleSheet = Load<ThemeStyleSheet>(Root + "UI/TideClaimTheme.tss");
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

            var stage = (GameObject)PrefabUtility.InstantiatePrefab(LoadStage());
            var view = stage.GetComponent<TideClaimFieldView>();

            var camera = Object.FindAnyObjectByType<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 6;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.06f, .09f, .13f);

            var game = new GameObject(ObjectPrefix);
            var hud = game.AddComponent<PanelRenderer>();
            hud.panelSettings = panel;
            hud.visualTreeAsset = Load<VisualTreeAsset>(Root + "UI/Hud.uxml");
            var component = game.AddComponent<TideClaimGame>();
            var serialized = new SerializedObject(component);
            Assign(serialized, "_settings", settings);
            Assign(serialized, "_hudRenderer", hud);
            Assign(serialized, "_camera", camera);
            Assign(serialized, "_view", view);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<TideClaimCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "TIDE CLAIM";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();

            var stages = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<TideClaimFieldView>() != null) { stages++; }
            }

            if (stages != 1) { throw new InvalidOperationException($"盤のインスタンスが {stages} 個あります。"); }
            Debug.Log("[TideClaimSceneBuilder] シーン生成完了");
        }

        // 配置の正は Stage Prefab。既にあれば調整を上書きしない。
        private static GameObject LoadStage()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(StagePath);
            if (existing != null) { return existing; }

            var material = LoadOrCreate(Art + "Unlit.mat", () => new Material(Shader.Find("Sprites/Default")));
            var root = new GameObject("TideClaimStage");
            try
            {
                root.transform.position = new Vector3(StageCenterX, 0, 0);
                root.transform.localScale = Vector3.one * StageScale;
                var view = root.AddComponent<TideClaimFieldView>();

                // 背景は画面の中央に置く。16:10〜21:9 の画面で端が見えないよう少し大きくする。
                var backdrop = Sprite(root.transform, "Backdrop", TideClaimArtGenerator.BackdropFile, material, -20);
                backdrop.transform.localPosition = new Vector3(-StageCenterX / StageScale, 0, 0);
                backdrop.transform.localScale = new Vector3(1.35f / StageScale, 1.15f / StageScale, 1);

                var width = TideClaimSettings.BoardWidth * TideClaimFieldView.CellSize;
                var height = TideClaimSettings.BoardHeight * TideClaimFieldView.CellSize;
                var frame = Sprite(root.transform, "Frame", TideClaimArtGenerator.FrameFile, material, -10);
                frame.color = new Color(.12f, .16f, .18f, .9f);
                frame.transform.localScale = new Vector3(width + .5f, height + .5f, 1);

                var field = Sprite(root.transform, "Field", TideClaimArtGenerator.FieldFile, material, 0);
                var eddy = Sprite(root.transform, "Eddy", TideClaimArtGenerator.EddyFile, material, 5);
                eddy.transform.localScale = Vector3.one * .6f;
                var player = Sprite(root.transform, "Player", TideClaimArtGenerator.PlayerFile, material, 10);
                player.transform.localPosition = new Vector3(.1f, (.5f - (TideClaimSettings.BoardHeight / 2f)) * TideClaimFieldView.CellSize, 0);
                player.transform.localScale = Vector3.one * .36f;

                var serialized = new SerializedObject(view);
                Assign(serialized, "_field", field);
                Assign(serialized, "_player", player);
                Assign(serialized, "_enemy", eddy);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                return PrefabUtility.SaveAsPrefabAsset(root, StagePath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
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
