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
    /// シーンと Stage Prefab を生成する。既存の Stage は素材参照だけ更新し、Core の開始位置と照合する。
    /// 実行：<c>unity run games/039-fighting -- -executeMethod Game.Unity.Editor.EmberSceneBuilder.Build</c>
    /// </summary>
    public static class EmberSceneBuilder
    {
        private const string Root = "Assets/Game/Unity/";
        private const string Art = EmberArtGenerator.ArtRoot;
        private const string ScenePath = "Assets/Scenes/EmberDuel.unity";
        private const string StagePath = Art + "EmberStage.prefab";
        private const string ObjectPrefix = "EmberDuel";

        [MenuItem("Game/Rebuild Ember Duel Scene")]
        public static void Build()
        {
            EmberArtGenerator.GenerateIfMissing();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = LoadOrCreate(Root + "EmberSettings.asset", ScriptableObject.CreateInstance<EmberSettings>);
            settings.Validate();
            var panel = LoadOrCreate(Root + "UI/EmberPanelSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PanelSettings>();
                value.themeStyleSheet = Load<ThemeStyleSheet>(Root + "UI/EmberTheme.tss");
                value.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                value.referenceResolution = new Vector2Int(1920, 1080);
                value.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                value.match = 0.5f;
                return value;
            });

            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name.StartsWith(ObjectPrefix, StringComparison.Ordinal) || root.GetComponent<EmberStage>() != null)
                {
                    Object.DestroyImmediate(root);
                }
            }

            var stage = ((GameObject)PrefabUtility.InstantiatePrefab(LoadStage())).GetComponent<EmberStage>();
            var camera = Object.FindAnyObjectByType<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 150f;
            camera.transform.position = new Vector3(240f, 130f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = EmberArtGenerator.Night;
            for (var i = camera.transform.childCount - 1; i >= 0; i--) { Object.DestroyImmediate(camera.transform.GetChild(i).gameObject); }

            var game = new GameObject(ObjectPrefix + "Game");
            var hud = game.AddComponent<PanelRenderer>();
            hud.panelSettings = panel;
            hud.visualTreeAsset = Load<VisualTreeAsset>(Root + "UI/Hud.uxml");
            var component = game.AddComponent<EmberGame>();
            var serialized = new SerializedObject(component);
            Assign(serialized, "_settings", settings);
            Assign(serialized, "_moves", Load<TextAsset>("Assets/Game/Data/Moves.txt"));
            Assign(serialized, "_hudRenderer", hud);
            Assign(serialized, "_camera", camera);
            Assign(serialized, "_stage", stage);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<EmberCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "EMBER DUEL";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();

            var count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<EmberStage>() != null) { count++; }
            }

            if (count != 1) { throw new InvalidOperationException($"Stage のインスタンスが {count} 個あります。"); }
            Debug.Log("[EmberSceneBuilder] シーン生成完了");
            if (Application.isBatchMode) { EditorApplication.Exit(0); }
        }

        private static GameObject LoadStage()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(StagePath);
            if (existing != null)
            {
                var contents = PrefabUtility.LoadPrefabContents(StagePath);
                try
                {
                    var stage = contents.GetComponent<EmberStage>();
                    BindArt(new SerializedObject(stage));
                    stage.Left.sprite = stage.Portrait(0);
                    stage.Right.sprite = stage.Portrait(1);
                    var mismatch = stage.Mismatch(new DuelConfig());
                    if (mismatch != null) { throw new InvalidOperationException(StagePath + " が一致しません：" + mismatch); }
                    return PrefabUtility.SaveAsPrefabAsset(contents, StagePath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }

            var material = LoadOrCreate(Art + "Unlit.mat", () => new Material(Shader.Find("Sprites/Default")));
            var root = new GameObject(ObjectPrefix + "Stage");
            try
            {
                var stage = root.AddComponent<EmberStage>();
                var backdrop = Sprite(root.transform, "Backdrop", "Backdrop.png", material, -100);
                var ground = Sprite(root.transform, "Ground", "Ground.png", material, -50);
                backdrop.transform.localPosition = new Vector3(240f, 135f, 0f);
                ground.transform.localPosition = new Vector3(240f, 0f, 0f);
                var left = Sprite(root.transform, "Left", "JudokaGuard.png", material, 10);
                var right = Sprite(root.transform, "Right", "VeteranGuard.png", material, 10);
                var shot0 = Sprite(root.transform, "Shot0", "Shot.png", material, 20);
                var shot1 = Sprite(root.transform, "Shot1", "Shot.png", material, 20);
                var boxes = new GameObject("Boxes");
                boxes.transform.SetParent(root.transform, false);
                var filter = boxes.AddComponent<MeshFilter>();
                var renderer = boxes.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.sortingOrder = 40;
                var serialized = new SerializedObject(stage);
                Assign(serialized, "_left", left);
                Assign(serialized, "_right", right);
                Assign(serialized, "_shot0", shot0);
                Assign(serialized, "_shot1", shot1);
                Assign(serialized, "_ground", ground);
                Assign(serialized, "_backdrop", backdrop);
                Assign(serialized, "_boxes", filter);
                Assign(serialized, "_boxRenderer", renderer);
                BindArt(serialized);
                stage.ShowTitle(new DuelConfig());
                var mismatch = stage.Mismatch(new DuelConfig());
                if (mismatch != null) { throw new InvalidOperationException(mismatch); }
                return PrefabUtility.SaveAsPrefabAsset(root, StagePath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void BindArt(SerializedObject stage)
        {
            var cinder = new Object[EmberStage.PoseCount];
            var kiln = new Object[EmberStage.PoseCount];
            for (var i = 0; i < EmberStage.PoseCount; i++)
            {
                var pose = EmberArtGenerator.Poses[i];
                cinder[i] = Load<Sprite>(Art + "Judoka" + pose + ".png");
                kiln[i] = Load<Sprite>(Art + "Veteran" + pose + ".png");
            }

            AssignArray(stage, "_cinder", cinder);
            AssignArray(stage, "_kiln", kiln);
            Assign(stage, "_flare", Load<Sprite>(Art + "Flare.png"));
            stage.ApplyModifiedPropertiesWithoutUndo();
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
