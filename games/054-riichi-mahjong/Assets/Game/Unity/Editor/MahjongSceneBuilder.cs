using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UIElements;

namespace Game.Unity.Editor
{
    public static class MahjongSceneBuilder
    {
        private const string Root = "Assets/Game/Unity/";
        private const string Scene = "Assets/Scenes/Mahjong.unity";

        [MenuItem("Game/Rebuild Mahjong Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var camera = UnityEngine.Object.FindAnyObjectByType<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.09f, 0.19f, 0.16f);
            var input = new GameObject("EventSystem");
            input.AddComponent<EventSystem>();
            input.AddComponent<InputSystemUIInputModule>();
            var table = new GameObject("Mahjong Table");
            var panel = table.AddComponent<PanelRenderer>();
            panel.panelSettings = LoadOrCreate<PanelSettings>("MahjongPanel.asset");
            panel.panelSettings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(Root + "Mahjong.tss");
            panel.panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.panelSettings.referenceResolution = new Vector2Int(1280, 720);
            panel.panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.panelSettings.match = 0f;
            EditorUtility.SetDirty(panel.panelSettings);
            panel.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Root + "Hud.uxml");
            var game = new SerializedObject(table.AddComponent<MahjongGame>());
            game.FindProperty("_panel").objectReferenceValue = panel;
            game.FindProperty("_settings").objectReferenceValue = LoadOrCreate<MahjongSettings>("MahjongSettings.asset");
            var atlas = Root + "MahjongTiles.png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(atlas);
            if (importer.npotScale != TextureImporterNPOTScale.None ||
                importer.mipmapEnabled || importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            game.FindProperty("_tileAtlas").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(atlas);
            game.ApplyModifiedPropertiesWithoutUndo();
            table.AddComponent<MahjongCapture>();
            if (!EditorSceneManager.SaveScene(scene, Scene)) { throw new InvalidOperationException("シーンを保存できません。"); }

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Scene, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "Riichi Mahjong";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;
            AssetDatabase.SaveAssets();
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var fullPath = Root + path;
            var asset = AssetDatabase.LoadAssetAtPath<T>(fullPath);
            if (asset != null) { return asset; }

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, fullPath);
            return asset;
        }
    }
}
