using System;
using System.Collections.Generic;
using Game.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>
    /// シーンと Stage Prefab を生成する。Stage Prefab は既にあれば上書きせず、コースのデータと合っているかだけ確かめる。
    /// コースのデータを変えたら Prefab（と地面の絵）を消して作り直す。
    /// 実行：<c>unity run games/036-golf -- -executeMethod Game.Unity.Editor.BreezewaySceneBuilder.Build</c>
    /// </summary>
    public static class BreezewaySceneBuilder
    {
        public const string StagePath = Art + "BreezewayStage.prefab";
        public const string ScenePath = "Assets/Scenes/Breezeway.unity";

        private const string Root = "Assets/Game/Unity/";
        private const string Art = BreezewayArtGenerator.ArtRoot;
        private const string ObjectPrefix = "Breezeway";

        [MenuItem("Game/Rebuild Breezeway Scene")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            // シーンを開くと未使用アセットがアンロードされるので、アセットの読み込みはこの後に行う。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = LoadOrCreate(Root + "BreezewaySettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<BreezewaySettings>();
                value.Course = Load<TextAsset>("Assets/Game/Data/Course.txt");
                return value;
            });
            var course = settings.Validate();
            BreezewayArtGenerator.GenerateIfMissing(course);
            var panel = LoadOrCreate(Root + "UI/BreezewayPanelSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PanelSettings>();
                value.themeStyleSheet = Load<ThemeStyleSheet>(Root + "UI/BreezewayTheme.tss");
                value.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                value.referenceResolution = new Vector2Int(1920, 1080);
                value.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                value.match = 1f;
                return value;
            });

            // Prefab を作り直すと古いインスタンスが別名で残ることがあるので、前方一致で消す（018 の知見）。
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name.StartsWith(ObjectPrefix, StringComparison.Ordinal)) { Object.DestroyImmediate(root); }
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(LoadStage(course, settings.Rules));
            var stage = instance.GetComponent<BreezewayStage>();

            var camera = Object.FindAnyObjectByType<Camera>();
            camera.orthographic = true;
            var (center, size) = BreezewayStage.Frame(course.Holes[0], false, course.Holes[0].Tee, 16f / 9f);
            camera.orthographicSize = size;
            camera.transform.position = center;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.08f, .16f, .1f);
            for (var i = camera.transform.childCount - 1; i >= 0; i--) { Object.DestroyImmediate(camera.transform.GetChild(i).gameObject); }

            var game = new GameObject(ObjectPrefix);
            var hud = game.AddComponent<PanelRenderer>();
            hud.panelSettings = panel;
            hud.visualTreeAsset = Load<VisualTreeAsset>(Root + "UI/Hud.uxml");
            var component = game.AddComponent<BreezewayGame>();
            var serialized = new SerializedObject(component);
            Assign(serialized, "_settings", settings);
            Assign(serialized, "_hudRenderer", hud);
            Assign(serialized, "_camera", camera);
            Assign(serialized, "_stage", stage);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<BreezewayCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "BREEZEWAY LINKS";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();

            var count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<BreezewayStage>() != null) { count++; }
            }

            if (count != 1) { throw new InvalidOperationException($"Stage のインスタンスが {count} 個あります。"); }
            Debug.Log("[BreezewaySceneBuilder] シーン生成完了");
        }

        // 配置の正は Stage Prefab。既にあれば調整を上書きせず、コースのデータと合っているかだけ確かめる。
        private static GameObject LoadStage(Course course, GolfConfig config)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(StagePath);
            if (existing != null)
            {
                var mismatch = existing.GetComponent<BreezewayStage>().Mismatch(course, config);
                if (mismatch != null) { throw new InvalidOperationException($"{StagePath} がコースのデータと合いません：{mismatch} Prefab を消して作り直してください。"); }
                return existing;
            }

            var material = LoadOrCreate(Art + "Unlit.mat", () => new Material(Shader.Find("Sprites/Default")));
            var root = new GameObject("BreezewayStage");
            try
            {
                var stage = root.AddComponent<BreezewayStage>();
                var serialized = new SerializedObject(stage);
                var holes = serialized.FindProperty("_holes");
                holes.arraySize = course.Holes.Count;
                for (var i = 0; i < course.Holes.Count; i++)
                {
                    var hole = course.Holes[i];
                    var holeRoot = new GameObject("Hole" + hole.Number);
                    holeRoot.transform.SetParent(root.transform, false);
                    var ground = Sprite(holeRoot.transform, "Ground", BreezewayArtGenerator.GroundFile(hole.Number), material, -100);
                    var tee = Sprite(holeRoot.transform, "Tee", BreezewayArtGenerator.TeeFile, material, -50);
                    var cup = Sprite(holeRoot.transform, "Cup", BreezewayArtGenerator.CupFile, material, -40);
                    var trees = new Object[hole.Trees.Count];
                    for (var t = 0; t < trees.Length; t++) { trees[t] = Sprite(holeRoot.transform, "Tree" + t, BreezewayArtGenerator.TreeFile, material, 40); }
                    var slopes = new GameObject("Slopes");
                    slopes.transform.SetParent(holeRoot.transform, false);
                    var arrows = new Object[BreezewayStage.Arrows(hole).Count];
                    for (var a = 0; a < arrows.Length; a++) { arrows[a] = Sprite(slopes.transform, "Arrow" + a, BreezewayArtGenerator.ArrowFile, material, -30); }
                    slopes.SetActive(false);
                    holeRoot.SetActive(i == 0);

                    var element = holes.GetArrayElementAtIndex(i);
                    element.FindPropertyRelative("Root").objectReferenceValue = holeRoot;
                    element.FindPropertyRelative("Ground").objectReferenceValue = ground;
                    element.FindPropertyRelative("Tee").objectReferenceValue = tee;
                    element.FindPropertyRelative("Cup").objectReferenceValue = cup;
                    element.FindPropertyRelative("Slopes").objectReferenceValue = slopes;
                    AssignArray(element.FindPropertyRelative("Trees"), trees);
                    AssignArray(element.FindPropertyRelative("Arrows"), arrows);
                }

                var shadow = Sprite(root.transform, "Shadow", BreezewayArtGenerator.ShadowFile, material, 30);
                var predictedDots = new List<Object>();
                var trailDots = new List<Object>();
                for (var d = 0; d < BreezewayStage.DotCount; d++)
                {
                    predictedDots.Add(Hidden(Sprite(root.transform, "Predicted" + d, BreezewayArtGenerator.DotFile, material, 50)));
                    trailDots.Add(Hidden(Sprite(root.transform, "Trail" + d, BreezewayArtGenerator.DotFile, material, 52)));
                }

                var predictedLanding = Hidden(Sprite(root.transform, "PredictedLanding", BreezewayArtGenerator.MarkFile, material, 55));
                var actualLanding = Hidden(Sprite(root.transform, "ActualLanding", BreezewayArtGenerator.MarkFile, material, 56));
                actualLanding.color = new Color(1f, .62f, .2f, 1f);
                var flag = Sprite(root.transform, "Flag", BreezewayArtGenerator.FlagFile, material, 65);
                var ball = Sprite(root.transform, "Ball", BreezewayArtGenerator.BallFile, material, 70);

                Assign(serialized, "_ball", ball);
                Assign(serialized, "_shadow", shadow);
                Assign(serialized, "_flag", flag);
                Assign(serialized, "_predictedLanding", predictedLanding);
                Assign(serialized, "_actualLanding", actualLanding);
                AssignArray(serialized.FindProperty("_predictedDots"), predictedDots.ToArray());
                AssignArray(serialized.FindProperty("_trailDots"), trailDots.ToArray());
                serialized.ApplyModifiedPropertiesWithoutUndo();

                stage.Layout(course, config);

                // プレイ前の Scene ビューでも見えるよう、球と旗を 1 番ホールのティーとカップに置く。
                var first = course.Holes[0];
                ball.transform.localPosition = BreezewayStage.ToLocal(first.Tee);
                shadow.transform.localPosition = BreezewayStage.ToLocal(first.Tee);
                ball.transform.localScale = Vector3.one * 2;
                shadow.transform.localScale = Vector3.one * 2;
                flag.transform.localPosition = BreezewayStage.ToLocal(first.Cup);
                flag.transform.localScale = Vector3.one * 4;
                var mismatch = stage.Mismatch(course, config);
                if (mismatch != null) { throw new InvalidOperationException("作った Stage がコースのデータと合いません：" + mismatch); }
                return PrefabUtility.SaveAsPrefabAsset(root, StagePath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static SpriteRenderer Hidden(SpriteRenderer renderer)
        {
            renderer.enabled = false;
            return renderer;
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

        private static void AssignArray(SerializedProperty property, Object[] values)
        {
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
