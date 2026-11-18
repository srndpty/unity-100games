using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Unity.Editor
{
    /// <summary>
    /// Fishing のシーンと関連アセットを生成する。シーン構成はこのスクリプトを正とし、手で編集しない。
    /// バッチ実行: unity run games/007-fishing -- -executeMethod Game.Unity.Editor.FishingSceneBuilder.Build
    /// </summary>
    public static class FishingSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Fishing.unity";
        private const string TemplateScenePath = "Assets/Scenes/SampleScene.unity";
        private const string Root = "Assets/Game/Unity";
        private const string SettingsPath = Root + "/FishingSettings.asset";
        private const string PanelSettingsPath = Root + "/UI/FishingPanelSettings.asset";
        private const string ThemePath = Root + "/UI/FishingTheme.tss";
        private const string HudPath = Root + "/UI/Hud.uxml";
        private const string GameObjectName = "Fishing";

        [MenuItem("Game/Rebuild Fishing Scene")]
        public static void Build()
        {
            // 初回は雛形のシーン（カメラと URP の設定入り）を改名して使う。
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset(TemplateScenePath, ScenePath);
                if (!string.IsNullOrEmpty(error))
                {
                    throw new System.InvalidOperationException($"シーンを用意できません: {error}");
                }
            }

            // シーンを開くと未使用アセットがアンロードされ参照が切れるため、アセットは開いた後に読み込む。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = LoadOrCreate(SettingsPath, ScriptableObject.CreateInstance<FishingSettings>);
            var panelSettings = LoadOrCreate(PanelSettingsPath, CreatePanelSettings);
            var hud = LoadRequired<VisualTreeAsset>(HudPath);

            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == GameObjectName || root.name == "FishingStage")
                {
                    Object.DestroyImmediate(root);
                }
            }

            var camera = Object.FindAnyObjectByType<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.035f, .10f, .15f);
            camera.orthographic = true;

            var stage = (GameObject)PrefabUtility.InstantiatePrefab(FishingArtBuilder.LoadStage(settings.Game));
            var view = stage.GetComponent<FishingView>();
            view.Render(null, Game.Core.FishKind.Crucian, settings.Game);
            var game = new GameObject(GameObjectName);
            var hudRenderer = game.AddComponent<PanelRenderer>();
            hudRenderer.panelSettings = panelSettings;
            hudRenderer.visualTreeAsset = hud;
            var fishing = game.AddComponent<FishingGame>();
            var serialized = new SerializedObject(fishing);
            serialized.FindProperty("_settings").objectReferenceValue = settings;
            serialized.FindProperty("_hudRenderer").objectReferenceValue = hudRenderer;
            serialized.FindProperty("_camera").objectReferenceValue = camera;
            serialized.FindProperty("_view").objectReferenceValue = view;
            camera.orthographicSize = 8;
            camera.transform.position = new Vector3(0, 0, -10);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<FishingCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "STILLWATER";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();
            Debug.Log($"[FishingSceneBuilder] {ScenePath} を生成しました");
        }

        private static PanelSettings CreatePanelSettings()
        {
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.themeStyleSheet = LoadRequired<ThemeStyleSheet>(ThemePath);
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1920, 1080);
            panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panelSettings.match = 0.5f;
            return panelSettings;
        }

        private static T LoadOrCreate<T>(string path, System.Func<T> create)
            where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                return asset;
            }

            asset = create();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static T LoadRequired<T>(string path)
            where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new System.InvalidOperationException($"アセットが見つかりません: {path}");
            }

            return asset;
        }
    }
}
