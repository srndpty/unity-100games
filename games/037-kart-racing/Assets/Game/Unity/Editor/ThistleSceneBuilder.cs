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
    /// <summary>
    /// シーンと Stage Prefab を生成する。既存の Prefab は上書きせず、コースと一致するかだけ確かめる
    /// （作り直すときは Prefab・Circuit.asset・Ground.mat を消してから実行する）。
    /// 実行：<c>unity run games/037-kart-racing -- -executeMethod Game.Unity.Editor.ThistleSceneBuilder.Build</c>
    /// </summary>
    public static class ThistleSceneBuilder
    {
        private const string Root = "Assets/Game/Unity/";
        private const string ScenePath = "Assets/Scenes/ThistleCircuit.unity";
        private const string StagePath = ThistleArtGenerator.ArtRoot + "ThistleStage.prefab";
        private const string CircuitPath = ThistleArtGenerator.ArtRoot + "Circuit.asset";
        private const string MaterialPath = ThistleArtGenerator.ArtRoot + "Ground.mat";
        private const string Prefix = "Thistle";

        [MenuItem("Game/Rebuild Thistle Circuit")]
        public static void Build()
        {
            ThistleArtGenerator.Generate();
            AssetDatabase.Refresh();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = AssetDatabase.LoadAssetAtPath<ThistleSettings>(Root + "ThistleSettings.asset");
            if (settings == null) { throw new InvalidOperationException("ThistleSettings.asset がありません。"); }
            var rules = settings.Validate();
            var track = Track.Parse(settings.Track.text, rules);

            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(Root + "UI/ThistlePanel.asset");
            if (panel == null) { throw new InvalidOperationException("ThistlePanel.asset がありません。"); }

            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name.StartsWith(Prefix, StringComparison.Ordinal)) { Object.DestroyImmediate(root); }
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(LoadStage(track));
            var stage = instance.GetComponent<ThistleStage>();
            var camera = Object.FindAnyObjectByType<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = ThistlePalette.Field;
            camera.transform.position = new Vector3(0f, 0f, -20f);

            var gameObject = new GameObject(Prefix + "Game");
            var hud = gameObject.AddComponent<PanelRenderer>();
            hud.panelSettings = panel;
            hud.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Root + "UI/Hud.uxml");
            var game = gameObject.AddComponent<ThistleGame>();
            var serialized = new SerializedObject(game);
            serialized.FindProperty("_settings").objectReferenceValue = settings;
            serialized.FindProperty("_hudRenderer").objectReferenceValue = hud;
            serialized.FindProperty("_camera").objectReferenceValue = camera;
            serialized.FindProperty("_stage").objectReferenceValue = stage;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            gameObject.AddComponent<ThistleCaptureScenario>();

            var stages = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<ThistleStage>() != null) { stages++; }
            }

            if (stages != 1) { throw new InvalidOperationException($"Stage が {stages} 個あります。"); }

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "THISTLE CIRCUIT";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();
            Debug.Log("[ThistleSceneBuilder] シーン生成完了");
        }

        private static GameObject LoadStage(Track track)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(StagePath);
            if (existing != null)
            {
                var mismatch = existing.GetComponent<ThistleStage>().Mismatch(track);
                if (mismatch != null) { throw new InvalidOperationException(StagePath + " がコースと一致しません：" + mismatch); }
                return existing;
            }

            if (File.Exists(CircuitPath) || File.Exists(MaterialPath))
            {
                throw new InvalidOperationException("Prefab がないのに Circuit.asset か Ground.mat が残っています。消してから作り直してください。");
            }

            var material = new Material(Shader.Find("Thistle/Ground")) { name = "Ground" };
            AssetDatabase.CreateAsset(material, MaterialPath);
            var mesh = ThistleTrackMesh.Build(track);
            AssetDatabase.CreateAsset(mesh, CircuitPath);

            var root = new GameObject("ThistleStage");
            try
            {
                root.AddComponent<ThistleStage>();
                var circuit = new GameObject(ThistleStage.CircuitName);
                circuit.transform.SetParent(root.transform, false);
                circuit.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = circuit.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.sortingOrder = -10;

                var smoke = new GameObject(ThistleStage.SmokeName);
                smoke.transform.SetParent(root.transform, false);
                var puffs = new SerializedObject(smoke.AddComponent<ThistleSmoke>());
                puffs.FindProperty("_puff").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(ThistleArtGenerator.SmokePath);
                puffs.ApplyModifiedPropertiesWithoutUndo();

                for (var i = KartConfig.KartCount - 1; i >= 0; i--)
                {
                    var kart = new GameObject(ThistleStage.KartName(i));
                    kart.transform.SetParent(root.transform, false);
                    var sprite = kart.AddComponent<SpriteRenderer>();
                    sprite.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ThistleArtGenerator.KartPath(i));
                    sprite.sortingOrder = i == 0 ? 11 : 10;
                }

                return PrefabUtility.SaveAsPrefabAsset(root, StagePath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
