using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UIElements;

namespace Game.Unity.Editor
{
    public static class CanyonSceneBuilder
    {
        private const string Root = "Assets/Game/Unity/";
        private const string Scene = "Assets/Scenes/Canyon.unity";

        [MenuItem("Game/Rebuild Canyon Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var camera = UnityEngine.Object.FindAnyObjectByType<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.04f, 0.07f, 0.11f);
            var input = new GameObject("EventSystem");
            input.AddComponent<EventSystem>();
            input.AddComponent<InputSystemUIInputModule>();
            var gameObject = new GameObject("Canyon Game");
            var panel = gameObject.AddComponent<PanelRenderer>();
            panel.panelSettings = LoadOrCreate<PanelSettings>(Root + "UI/CanyonPanel.asset");
            panel.panelSettings.themeStyleSheet = Require<ThemeStyleSheet>("UI/Canyon.tss");
            panel.panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.panelSettings.referenceResolution = new Vector2Int(1280, 720);
            panel.panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.panelSettings.match = 1f;
            EditorUtility.SetDirty(panel.panelSettings);
            panel.visualTreeAsset = Require<VisualTreeAsset>("UI/Hud.uxml");

            var game = new SerializedObject(gameObject.AddComponent<CanyonGame>());
            game.FindProperty("_panel").objectReferenceValue = panel;
            game.FindProperty("_settings").objectReferenceValue = LoadOrCreate<CanyonSettings>(Root + "CanyonSettings.asset");
            game.FindProperty("_style").objectReferenceValue = Require<StyleSheet>("UI/Canyon.uss");
            game.FindProperty("_terrain").objectReferenceValue = Require<Texture2D>("Art/Terrain.png");
            game.FindProperty("_atlas").objectReferenceValue = Require<Texture2D>("Art/Atlas.png");
            game.ApplyModifiedPropertiesWithoutUndo();
            gameObject.AddComponent<CanyonCapture>();
            if (!EditorSceneManager.SaveScene(scene, Scene)) { throw new InvalidOperationException("シーンを保存できません。"); }

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Scene, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "Canyon Gate";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();
        }

        private static T Require<T>(string path) where T : UnityEngine.Object =>
            AssetDatabase.LoadAssetAtPath<T>(Root + path) ?? throw new InvalidOperationException(path);

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) { return asset; }
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
