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
    /// シーンと Stage Prefab を生成する。既存の Prefab は上書きせず、中心線と一致するかだけ確かめる。
    /// 実行：<c>unity run games/037-kart-racing -- -executeMethod Game.Unity.Editor.ThistleSceneBuilder.Build</c>
    /// </summary>
    public static class ThistleSceneBuilder
    {
        private const string Root = "Assets/Game/Unity/";
        private const string ScenePath = "Assets/Scenes/ThistleCircuit.unity";
        private const string StagePath = ThistleArtGenerator.ArtRoot + "ThistleStage.prefab";
        private const string Prefix = "Thistle";

        [MenuItem("Game/Rebuild Thistle Circuit")]
        public static void Build()
        {
            ThistleArtGenerator.GenerateIfMissing();
            Directory.CreateDirectory(Root + "UI");
            if (!File.Exists(Root + "UI/Hud.uxml"))
            {
                File.WriteAllText(Root + "UI/Hud.uxml", "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\"><ui:VisualElement /></ui:UXML>\n");
            }

            AssetDatabase.Refresh();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = LoadOrCreate(Root + "ThistleSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<ThistleSettings>();
                value.Track = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Data/Track.txt");
                return value;
            });
            settings.Validate();

            var panel = LoadOrCreate(Root + "UI/ThistlePanel.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PanelSettings>();
                value.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                value.referenceResolution = new Vector2Int(1920, 1080);
                value.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                value.match = 0.5f;
                return value;
            });

            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name.StartsWith(Prefix, StringComparison.Ordinal)) { Object.DestroyImmediate(root); }
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(LoadStage(settings));
            var stage = instance.GetComponent<ThistleStage>();
            var camera = Object.FindAnyObjectByType<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.16f, 0.28f, 0.18f);
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

        private static GameObject LoadStage(ThistleSettings settings)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(StagePath);
            if (existing != null)
            {
                var track = Track.Parse(settings.Track.text, settings.Validate());
                var mismatch = existing.GetComponent<ThistleStage>().Mismatch(track);
                if (mismatch != null) { throw new InvalidOperationException(StagePath + " がコースと一致しません：" + mismatch); }
                return existing;
            }

            var rules = settings.Validate();
            var parsed = Track.Parse(settings.Track.text, rules);
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            var material = new Material(shader);
            if (material.HasProperty("_BaseColor")) { material.SetColor("_BaseColor", new Color(0.28f, 0.30f, 0.28f)); }
            AssetDatabase.CreateAsset(material, ThistleArtGenerator.ArtRoot + "Unlit.mat");
            var root = new GameObject("ThistleStage");
            try
            {
                root.AddComponent<ThistleStage>();
                var road = new GameObject("Road");
                road.transform.SetParent(root.transform, false);
                var mesh = Road(parsed);
                AssetDatabase.CreateAsset(mesh, ThistleArtGenerator.ArtRoot + "Road.asset");
                road.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = road.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                for (var i = 0; i < parsed.StationCount; i++)
                {
                    var marker = new GameObject("Marker-" + i);
                    marker.transform.SetParent(root.transform, false);
                    marker.transform.localPosition = ThistleLayout.At(parsed.StationPosition(i));
                }

                for (var i = 0; i < KartConfig.KartCount; i++)
                {
                    var kart = new GameObject("Kart-" + i);
                    kart.transform.SetParent(root.transform, false);
                    var sprite = kart.AddComponent<SpriteRenderer>();
                    sprite.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ThistleArtGenerator.ArtRoot + "kart-" + i + ".png");
                    sprite.sortingOrder = 10;
                }

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, StagePath);
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Mesh Road(Track track)
        {
            var count = track.StationCount;
            var vertices = new Vector3[count * 2];
            var colors = new Color[count * 2];
            var triangles = new int[count * 6];
            var asphalt = new Color(0.28f, 0.30f, 0.28f);
            for (var i = 0; i < count; i++)
            {
                vertices[i * 2] = ThistleLayout.At(track.Left(i));
                vertices[(i * 2) + 1] = ThistleLayout.At(track.Right(i));
                colors[i * 2] = asphalt;
                colors[(i * 2) + 1] = asphalt;
                var next = (i + 1) % count;
                var t = i * 6;
                triangles[t] = i * 2;
                triangles[t + 1] = next * 2;
                triangles[t + 2] = (i * 2) + 1;
                triangles[t + 3] = (i * 2) + 1;
                triangles[t + 4] = next * 2;
                triangles[t + 5] = (next * 2) + 1;
            }

            var mesh = new Mesh { name = "Road" };
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static T LoadOrCreate<T>(string path, Func<T> create)
            where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) { return existing; }
            var created = create();
            AssetDatabase.CreateAsset(created, path);
            return created;
        }
    }
}
