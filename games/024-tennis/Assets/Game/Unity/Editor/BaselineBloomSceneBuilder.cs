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
    /// シーンとコートの Stage Prefab を生成する。Stage Prefab は既にあれば上書きせず、投影と一致するかだけ確かめる。
    /// 実行：<c>unity run games/024-tennis -- -executeMethod Game.Unity.Editor.BaselineBloomSceneBuilder.Build</c>
    /// </summary>
    public static class BaselineBloomSceneBuilder
    {
        private const string Root = "Assets/Game/Unity/";
        private const string Art = BaselineBloomArtGenerator.ArtRoot;
        private const string ScenePath = "Assets/Scenes/BaselineBloom.unity";
        private const string StagePath = Art + "BaselineBloomStage.prefab";
        private const string ObjectPrefix = "BaselineBloom";

        [MenuItem("Game/Rebuild Baseline Bloom Scene")]
        public static void Build()
        {
            BaselineBloomArtGenerator.GenerateIfMissing();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            // シーンを開くと未使用アセットがアンロードされるので、アセットの読み込みはこの後に行う。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = LoadOrCreate(Root + "BaselineBloomSettings.asset", ScriptableObject.CreateInstance<BaselineBloomSettings>);
            settings.Validate();
            var panel = LoadOrCreate(Root + "UI/BaselineBloomPanelSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PanelSettings>();
                value.themeStyleSheet = Load<ThemeStyleSheet>(Root + "UI/BaselineBloomTheme.tss");
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

            var stage = ((GameObject)PrefabUtility.InstantiatePrefab(LoadStage())).GetComponent<BaselineBloomStage>();

            var camera = Object.FindAnyObjectByType<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 9;
            camera.transform.position = new Vector3(0, 1, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = BaselineBloomArtGenerator.Hedge;
            for (var i = camera.transform.childCount - 1; i >= 0; i--) { Object.DestroyImmediate(camera.transform.GetChild(i).gameObject); }

            var game = new GameObject(ObjectPrefix + "Game");
            var hud = game.AddComponent<PanelRenderer>();
            hud.panelSettings = panel;
            hud.visualTreeAsset = Load<VisualTreeAsset>(Root + "UI/Hud.uxml");
            var component = game.AddComponent<BaselineBloomGame>();
            var serialized = new SerializedObject(component);
            Assign(serialized, "_settings", settings);
            Assign(serialized, "_hudRenderer", hud);
            Assign(serialized, "_camera", camera);
            Assign(serialized, "_stage", stage);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<BaselineBloomCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "BASELINE BLOOM";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();

            var count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<BaselineBloomStage>() != null) { count++; }
            }

            if (count != 1) { throw new InvalidOperationException($"Stage のインスタンスが {count} 個あります。"); }
            Debug.Log("[BaselineBloomSceneBuilder] シーン生成完了");
        }

        // 配置の正は Stage Prefab。既にあれば調整を上書きせず、投影と一致するかだけ確かめる。
        private static GameObject LoadStage()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(StagePath);
            if (existing != null)
            {
                var mismatch = existing.GetComponent<BaselineBloomStage>().Mismatch();
                if (mismatch != null) { throw new InvalidOperationException($"{StagePath} が投影と一致しません：{mismatch} Prefab を消して作り直してください。"); }
                return existing;
            }

            var material = Material();
            var root = new GameObject(ObjectPrefix + "Stage");
            try
            {
                var stage = root.AddComponent<BaselineBloomStage>();
                var court = Sprite(root.transform, "Court", BaselineBloomArtGenerator.CourtFile, material, -1000);
                var bounce = Sprite(root.transform, "BounceMark", BaselineBloomArtGenerator.BounceMarkFile, material, -900);
                bounce.enabled = false;
                var shadow = Sprite(root.transform, "Shadow", BaselineBloomArtGenerator.ShadowFile, material, -800);
                var net = Sprite(root.transform, "Net", BaselineBloomArtGenerator.NetFile, material, 0);
                net.transform.localPosition = CourtProjection.ToWorld(0, 0, 0);

                // 選手と球は最初のポイントの立ち位置に置いておく（プレイ前から Scene ビューで見える。実行時は毎回置き直す）。
                var config = new TennisConfig();
                var near = Sprite(root.transform, "Near", BaselineBloomArtGenerator.NearPoseFile(0), material, 0);
                var far = Sprite(root.transform, "Far", BaselineBloomArtGenerator.FarPoseFile(0), material, 0);
                var ball = Sprite(root.transform, "Ball", BaselineBloomArtGenerator.BallFile, material, 0);

                var nearPoses = new Object[BaselineBloomStage.PoseCount];
                var farPoses = new Object[BaselineBloomStage.PoseCount];
                for (var i = 0; i < BaselineBloomStage.PoseCount; i++)
                {
                    nearPoses[i] = LoadSprite(BaselineBloomArtGenerator.NearPoseFile(i));
                    farPoses[i] = LoadSprite(BaselineBloomArtGenerator.FarPoseFile(i));
                }

                var serialized = new SerializedObject(stage);
                Assign(serialized, "_court", court);
                Assign(serialized, "_net", net);
                Assign(serialized, "_near", near);
                Assign(serialized, "_far", far);
                Assign(serialized, "_ball", ball);
                Assign(serialized, "_shadow", shadow);
                Assign(serialized, "_bounceMark", bounce);
                AssignArray(serialized, "_nearPoses", nearPoses);
                AssignArray(serialized, "_farPoses", farPoses);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                stage.RenderInitial(config);
                return PrefabUtility.SaveAsPrefabAsset(root, StagePath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Material Material() => LoadOrCreate(Art + "Unlit.mat", () => new Material(Shader.Find("Sprites/Default")));

        private static Sprite LoadSprite(string file) => Load<Sprite>(Art + file);

        private static SpriteRenderer Sprite(Transform parent, string name, string file, Material material, int order)
        {
            var item = new GameObject(name);
            item.transform.SetParent(parent, false);
            var renderer = item.AddComponent<SpriteRenderer>();
            renderer.sprite = LoadSprite(file);
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
