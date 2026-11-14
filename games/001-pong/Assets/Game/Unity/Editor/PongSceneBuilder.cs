using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Unity.Editor
{
    /// <summary>
    /// Pong のシーンと関連アセットを生成する。シーン構成はこのスクリプトを正とし、手で編集しない。
    /// バッチ実行: unity run games/001-pong -- -executeMethod Game.Unity.Editor.PongSceneBuilder.Build
    /// </summary>
    public static class PongSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Pong.unity";
        private const string Root = "Assets/Game/Unity";
        private const string SettingsPath = Root + "/PongSettings.asset";
        private const string PanelSettingsPath = Root + "/UI/PongPanelSettings.asset";
        private const string ThemePath = Root + "/UI/PongTheme.tss";
        private const string HudPath = Root + "/UI/Hud.uxml";
        private const string GameObjectName = "Pong";

        [MenuItem("Game/Rebuild Pong Scene")]
        public static void Build()
        {
            // シーンを開くと未使用アセットがアンロードされ参照が切れるため、アセットは開いた後に読み込む。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = LoadOrCreate(SettingsPath, ScriptableObject.CreateInstance<PongSettings>);
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
            camera.backgroundColor = Color.black;
            camera.orthographicSize = 5f;

            var game = new GameObject(GameObjectName);
            var document = game.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = hud;
            var pong = game.AddComponent<PongGame>();
            var serialized = new SerializedObject(pong);
            serialized.FindProperty("_settings").objectReferenceValue = settings;
            serialized.FindProperty("_hudDocument").objectReferenceValue = document;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<PongCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log($"[PongSceneBuilder] {ScenePath} を生成しました");
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
