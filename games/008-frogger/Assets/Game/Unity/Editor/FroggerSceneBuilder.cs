using System;
using System.IO;
using Game.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    public static class FroggerSceneBuilder
    {
        private const string Root = "Assets/Game/Unity/";
        private const string Art = Root + "Art/";
        private const string ScenePath = "Assets/Scenes/Frogger.unity";
        [MenuItem("Game/Rebuild Frogger Scene")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = LoadOrCreate(Root + "FroggerSettings.asset", ScriptableObject.CreateInstance<FroggerSettings>);
            settings.Validate();
            var panel = LoadOrCreate(Root + "UI/FroggerPanelSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PanelSettings>();
                value.themeStyleSheet = Load<ThemeStyleSheet>(Root + "UI/FroggerTheme.tss");
                value.scaleMode = PanelScaleMode.ScaleWithScreenSize; value.referenceResolution = new Vector2Int(1920, 1080);
                value.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight; value.match = .5f; return value;
            });
            foreach (var root in scene.GetRootGameObjects())
            { if (root.name == "Frogger" || root.name == "FroggerStage") { Object.DestroyImmediate(root); } }
            var stage = (GameObject)PrefabUtility.InstantiatePrefab(LoadStage(settings.Game));
            var view = stage.GetComponent<FroggerView>(); view.Render(null, settings.Game);
            var camera = Object.FindAnyObjectByType<Camera>(); camera.orthographic = true;
            camera.orthographicSize = 5.9f; camera.transform.position = new Vector3(0, .15f, -10);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.055f, .115f, .085f);
            var game = new GameObject("Frogger");
            var hud = game.AddComponent<PanelRenderer>(); hud.panelSettings = panel; hud.visualTreeAsset = Load<VisualTreeAsset>(Root + "UI/Hud.uxml");
            var component = game.AddComponent<FroggerGame>(); var serialized = new SerializedObject(component);
            Assign(serialized, "_settings", settings); Assign(serialized, "_hudRenderer", hud); Assign(serialized, "_camera", camera); Assign(serialized, "_view", view);
            serialized.ApplyModifiedPropertiesWithoutUndo(); game.AddComponent<FroggerCaptureScenario>();
            EditorSceneManager.SaveScene(scene); EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games"; PlayerSettings.productName = "LITTLE CROSSING";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed; PlayerSettings.defaultScreenWidth = 1280; PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false; AssetDatabase.SaveAssets(); Debug.Log("[FroggerSceneBuilder] シーン生成完了");
        }
        private static GameObject LoadStage(FroggerConfig config)
        {
            var path = Art + "FroggerStage.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (existing != null) { return existing; }
            foreach (var file in new[] { "Frog.png", "Car.png", "Log.png", "Board.jpg" }) { Import(file); }
            var squarePath = Art + "Mask.png";
            if (!File.Exists(squarePath))
            {
                var texture = new Texture2D(2, 2); texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white }); texture.Apply();
                File.WriteAllBytes(squarePath, texture.EncodeToPNG()); Object.DestroyImmediate(texture); AssetDatabase.ImportAsset(squarePath);
            }
            Import("Mask.png");
            var material = new Material(Shader.Find("Sprites/Default")); AssetDatabase.CreateAsset(material, Art + "Unlit.mat");
            var root = new GameObject("FroggerStage");
            try
            {
                var view = root.AddComponent<FroggerView>();
                var background = Sprite(root.transform, "Board", Load<Sprite>(Art + "Board.jpg"), material, -10);
                // 生成画像の内枠（幅約92.6%、高さ約89.9%）を11×9のプレイ領域へ合わせる。
                Scale(background, 11.88f, 10.01f);
                var maskObject = new GameObject("PlayfieldMask"); maskObject.transform.SetParent(root.transform, false);
                var mask = maskObject.AddComponent<SpriteMask>(); mask.sprite = Load<Sprite>(squarePath);
                maskObject.transform.localScale = new Vector3(11 / mask.sprite.bounds.size.x, 9 / mask.sprite.bounds.size.y, 1);
                var frog = Sprite(root.transform, "Frog", Load<Sprite>(Art + "Frog.png"), material, 10); Scale(frog, .76f, .76f);
                frog.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
                var serialized = new SerializedObject(view); Assign(serialized, "_frog", frog);
                var objects = serialized.FindProperty("_objects"); objects.arraySize = 24;
                for (var lane = 0; lane < 6; lane++)
                {
                    for (var id = 0; id < 4; id++)
                    {
                        var sprite = Sprite(root.transform, "Lane" + lane + "Object" + id, Load<Sprite>(Art + (lane < 3 ? "Car.png" : "Log.png")), material, 0);
                        sprite.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
                        objects.GetArrayElementAtIndex(lane * 4 + id).objectReferenceValue = sprite;
                    }
                }
                serialized.ApplyModifiedPropertiesWithoutUndo(); view.Render(null, config);
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
        }
        private static void Import(string file)
        {
            var path = Art + file; var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null) { throw new InvalidOperationException("画像がありません: " + path); }
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100; importer.alphaIsTransparency = true; importer.mipmapEnabled = false;
            importer.maxTextureSize = 2048; importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
        }
        private static SpriteRenderer Sprite(Transform parent, string name, Sprite sprite, Material material, int order)
        {
            var item = new GameObject(name); item.transform.SetParent(parent, false); var renderer = item.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite; renderer.sharedMaterial = material; renderer.sortingOrder = order; return renderer;
        }
        private static void Scale(SpriteRenderer renderer, float width, float height)
        { renderer.transform.localScale = new Vector3(width / renderer.sprite.bounds.size.x, height / renderer.sprite.bounds.size.y, 1); }
        private static void Assign(SerializedObject target, string field, Object value) => target.FindProperty(field).objectReferenceValue = value;
        private static T Load<T>(string path) where T : Object
        { return AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException("アセットがありません: " + path); }
        private static T LoadOrCreate<T>(string path, Func<T> factory) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path); if (asset != null) { return asset; }
            asset = factory(); AssetDatabase.CreateAsset(asset, path); return asset;
        }
    }
}
