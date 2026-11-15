using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Unity.Editor
{
    /// <summary>
    /// Invaders のシーンと関連アセットを生成する。シーン構成はこのスクリプトを正とし、手で編集しない。
    /// バッチ実行: unity run games/004-space-invaders -- -executeMethod Game.Unity.Editor.InvadersSceneBuilder.Build
    /// </summary>
    public static class InvadersSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Invaders.unity";
        private const string TemplateScenePath = "Assets/Scenes/SampleScene.unity";
        private const string Root = "Assets/Game/Unity";
        private const string SettingsPath = Root + "/InvadersSettings.asset";
        private const string PanelSettingsPath = Root + "/UI/InvadersPanelSettings.asset";
        private const string ThemePath = Root + "/UI/InvadersTheme.tss";
        private const string HudPath = Root + "/UI/Hud.uxml";
        private const string GameObjectName = "Invaders";

        [MenuItem("Game/Rebuild Invaders Scene")]
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
            var settings = LoadOrCreate(SettingsPath, ScriptableObject.CreateInstance<InvadersSettings>);
            var panelSettings = LoadOrCreate(PanelSettingsPath, CreatePanelSettings);
            var hud = LoadRequired<VisualTreeAsset>(HudPath);

            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == GameObjectName)
                {
                    Object.DestroyImmediate(root);
                }
            }

            var camera = Object.FindAnyObjectByType<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.04f, 0.05f, 0.09f);
            camera.orthographic = true;

            var game = new GameObject(GameObjectName);
            var hudRenderer = game.AddComponent<PanelRenderer>();
            hudRenderer.panelSettings = panelSettings;
            hudRenderer.visualTreeAsset = hud;
            var snake = game.AddComponent<InvadersGame>();
            var serialized = new SerializedObject(snake);
            serialized.FindProperty("_settings").objectReferenceValue = settings;
            serialized.FindProperty("_hudRenderer").objectReferenceValue = hudRenderer;
            serialized.FindProperty("_camera").objectReferenceValue = camera;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<InvadersCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log($"[InvadersSceneBuilder] {ScenePath} を生成しました");
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
