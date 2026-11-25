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
    /// シーンと Stage Prefab を生成する。既存の Stage は素材参照と Core のデータで決まる配置（タイトルの絵・街灯）だけ更新し、照合する。
    /// 実行：<c>unity run games/040-beat-em-up -- -executeMethod Game.Unity.Editor.SootSceneBuilder.Build</c>
    /// </summary>
    public static class SootSceneBuilder
    {
        private const string Root = "Assets/Game/Unity/";
        private const string Art = SootArtGenerator.ArtRoot;
        private const string ScenePath = "Assets/Scenes/SootAlley.unity";
        private const string StagePath = Art + "SootStage.prefab";
        private const string ObjectPrefix = "SootAlley";

        [MenuItem("Game/Rebuild Soot Alley Scene")]
        public static void Build()
        {
            SootArtGenerator.GenerateIfMissing();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            var stageText = Load<TextAsset>("Assets/Game/Data/Stage.txt");
            var movesText = Load<TextAsset>("Assets/Game/Data/Moves.txt");
            var settings = LoadOrCreate(Root + "SootSettings.asset", ScriptableObject.CreateInstance<SootSettings>);
            var config = settings.Validate();
            var stageData = StageData.Parse(stageText.text);
            stageData.Check(MoveTable.Parse(movesText.text), config);
            var stagePrefab = LoadStage(config, stageData);

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            settings = Load<SootSettings>(Root + "SootSettings.asset");
            var panel = LoadOrCreate(Root + "UI/SootPanelSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PanelSettings>();
                value.themeStyleSheet = Load<ThemeStyleSheet>(Root + "UI/SootTheme.tss");
                value.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                value.referenceResolution = new Vector2Int(1920, 1080);
                value.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                value.match = 0.5f;
                return value;
            });

            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name.StartsWith(ObjectPrefix, StringComparison.Ordinal) || root.GetComponent<SootStage>() != null)
                {
                    Object.DestroyImmediate(root);
                }
            }

            var stage = ((GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(StagePath))).GetComponent<SootStage>();
            var camera = Object.FindAnyObjectByType<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 108f;
            camera.transform.position = new Vector3((stageData.Sections[0].CameraX + (config.ViewWidth / 2)) / (float)Px.One, SootGame.CameraY, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = SootArtGenerator.Night;
            for (var i = camera.transform.childCount - 1; i >= 0; i--) { Object.DestroyImmediate(camera.transform.GetChild(i).gameObject); }

            var game = new GameObject(ObjectPrefix + "Game");
            var hud = game.AddComponent<PanelRenderer>();
            hud.panelSettings = panel;
            hud.visualTreeAsset = Load<VisualTreeAsset>(Root + "UI/Hud.uxml");
            var component = game.AddComponent<SootGame>();
            var serialized = new SerializedObject(component);
            Assign(serialized, "_settings", settings);
            Assign(serialized, "_moves", Load<TextAsset>("Assets/Game/Data/Moves.txt"));
            Assign(serialized, "_stageText", Load<TextAsset>("Assets/Game/Data/Stage.txt"));
            Assign(serialized, "_hudRenderer", hud);
            Assign(serialized, "_camera", camera);
            Assign(serialized, "_stage", stage);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<SootCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "SOOT ALLEY";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();

            var count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<SootStage>() != null) { count++; }
            }

            if (count != 1) { throw new InvalidOperationException($"Stage のインスタンスが {count} 個あります。"); }
            Debug.Log("[SootSceneBuilder] シーン生成完了");
            if (Application.isBatchMode) { EditorApplication.Exit(0); }
        }

        private static GameObject LoadStage(BrawlConfig config, StageData stageData)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(StagePath);
            if (existing != null)
            {
                var contents = PrefabUtility.LoadPrefabContents(StagePath);
                try
                {
                    var stage = contents.GetComponent<SootStage>();
                    var serialized = new SerializedObject(stage);
                    BindArt(serialized);
                    PlaceLamps(contents.transform, serialized, config, stageData, Load<Material>(Art + "Unlit.mat"));
                    stage.ShowTitle(config, stageData);
                    var mismatch = stage.Mismatch(config, stageData);
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
                var stage = root.AddComponent<SootStage>();
                var backdrop = Sprite(root.transform, "Backdrop", "Backdrop.png", material, -100);
                backdrop.transform.localPosition = SootStage.WorldOf(0, 0, config.ZMax, config);
                var ground = Sprite(root.transform, "Ground", "Ground.png", material, -90);
                ground.transform.localPosition = new Vector3(0f, -SootArtGenerator.GroundFront, 0f);
                var bodies = new Object[SootStage.Pool];
                var shadows = new Object[SootStage.Pool];
                for (var i = 0; i < SootStage.Pool; i++)
                {
                    shadows[i] = Sprite(root.transform, "Shadow " + i, "Shadow.png", material, SootStage.ShadowOrder);
                    bodies[i] = Sprite(root.transform, "Actor " + i, SootArtGenerator.FigureFile(0, SootStage.Stand), material, SootStage.BodyOrder + i);
                }

                var boxes = new GameObject("Boxes");
                boxes.transform.SetParent(root.transform, false);
                var filter = boxes.AddComponent<MeshFilter>();
                var renderer = boxes.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.sortingOrder = SootStage.BoxOrder;
                var serialized = new SerializedObject(stage);
                Assign(serialized, "_backdrop", backdrop);
                Assign(serialized, "_ground", ground);
                AssignArray(serialized, "_bodies", bodies);
                AssignArray(serialized, "_shadows", shadows);
                Assign(serialized, "_boxes", filter);
                Assign(serialized, "_boxRenderer", renderer);
                BindArt(serialized);
                PlaceLamps(root.transform, serialized, config, stageData, material);
                stage.ShowTitle(config, stageData);
                var mismatch = stage.Mismatch(config, stageData);
                if (mismatch != null) { throw new InvalidOperationException(mismatch); }
                return PrefabUtility.SaveAsPrefabAsset(root, StagePath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>区画ごとの街灯を作り直す（区画のデータが変わったら数と位置も変わる）。</summary>
        private static void PlaceLamps(Transform root, SerializedObject stage, BrawlConfig config, StageData stageData, Material material)
        {
            for (var i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i);
                if (child.name.StartsWith("Lamp", StringComparison.Ordinal)) { Object.DestroyImmediate(child.gameObject); }
            }

            var lamps = new Object[stageData.Sections.Count];
            for (var i = 0; i < lamps.Length; i++)
            {
                var lamp = Sprite(root, "Lamp " + (i + 1), "Lamp.png", material, -80);
                lamp.transform.localPosition = SootStage.WorldOf(SootStage.LampX(stageData.Sections[i], config), 0, config.ZMax, config);
                lamps[i] = lamp;
            }

            AssignArray(stage, "_lamps", lamps);
            stage.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BindArt(SerializedObject stage)
        {
            var fields = new[] { "_player", "_grunt", "_brute", "_kicker", "_boss" };
            for (var kind = 0; kind < fields.Length; kind++)
            {
                var sprites = new Object[SootStage.PoseCount];
                for (var pose = 0; pose < SootStage.PoseCount; pose++) { sprites[pose] = Load<Sprite>(Art + SootArtGenerator.FigureFile(kind, pose)); }
                AssignArray(stage, fields[kind], sprites);
            }

            Assign(stage, "_flare", Load<Sprite>(Art + "Flare.png"));
            Assign(stage, "_whirl", Load<Sprite>(Art + "Whirl.png"));
            Assign(stage, "_food", Load<Sprite>(Art + "Food.png"));
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
