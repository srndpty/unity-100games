using System;
using Game.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>
    /// シーンと Stage Prefab（地面・滑走路・目印の塔・機体 4 機・ミサイル・爆発・煙の見本）を生成する。
    /// Stage Prefab と設定アセットは既にあれば上書きせず、Prefab は設定と一致するかだけ確かめる。
    /// 実行：<c>unity run games/038-flight -- -executeMethod Game.Unity.Editor.HalcyonSceneBuilder.Build</c>
    /// </summary>
    public static class HalcyonSceneBuilder
    {
        private const string Root = "Assets/Game/Unity/";
        private const string Art = HalcyonArtGenerator.ArtRoot;
        private const string ScenePath = "Assets/Scenes/HalcyonSortie.unity";
        private const string StagePath = Art + "HalcyonStage.prefab";
        private const string ObjectPrefix = "Halcyon";

        /// <summary>空とフォグの色。</summary>
        public static readonly Color Sky = new(.55f, .72f, .9f);

        [MenuItem("Game/Rebuild Halcyon Sortie Scene")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            // シーンを開くと未使用アセットがアンロードされるので、アセットの生成と読み込みはこの後に行う。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            HalcyonArtGenerator.GenerateIfMissing();
            var settings = LoadOrCreate(Root + "HalcyonSettings.asset", ScriptableObject.CreateInstance<HalcyonSettings>);
            var (_, mission) = settings.Validate();
            var panel = LoadOrCreate(Root + "UI/HalcyonPanelSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PanelSettings>();
                value.themeStyleSheet = Load<ThemeStyleSheet>(Root + "UI/HalcyonTheme.tss");
                value.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                value.referenceResolution = new Vector2Int(1920, 1080);
                value.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                value.match = .5f;
                return value;
            });

            // Prefab を作り直すと古いインスタンスが別名で残ることがあるので、前方一致で消す（018 の知見）。
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name.StartsWith(ObjectPrefix, StringComparison.Ordinal)) { Object.DestroyImmediate(root); }
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(LoadStage(mission));
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var stage = instance.GetComponent<HalcyonStage>();

            var camera = Object.FindAnyObjectByType<Camera>();
            camera.orthographic = false;
            camera.fieldOfView = 60;
            camera.nearClipPlane = 1f;
            camera.farClipPlane = 20000f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Sky;

            var light = Object.FindAnyObjectByType<Light>();
            light.transform.rotation = Quaternion.Euler(50, -30, 0);
            light.color = new Color(1f, .96f, .88f);
            light.intensity = 1.3f;

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.5f, .56f, .64f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Sky;
            RenderSettings.fogStartDistance = 2500f;
            RenderSettings.fogEndDistance = 14000f;

            var game = new GameObject(ObjectPrefix + "Game");
            var hud = game.AddComponent<PanelRenderer>();
            hud.panelSettings = panel;
            hud.visualTreeAsset = Load<VisualTreeAsset>(Root + "UI/Hud.uxml");
            var component = game.AddComponent<HalcyonGame>();
            var serialized = new SerializedObject(component);
            Assign(serialized, "_settings", settings);
            Assign(serialized, "_hudRenderer", hud);
            Assign(serialized, "_camera", camera);
            Assign(serialized, "_stage", stage);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<HalcyonCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "HALCYON SORTIE";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();

            var count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<HalcyonStage>() != null) { count++; }
            }

            if (count != 1) { throw new InvalidOperationException($"Stage のインスタンスが {count} 個あります。"); }
            Debug.Log("[HalcyonSceneBuilder] シーン生成完了");
        }

        // 配置の正は Stage Prefab。既にあれば調整を上書きせず、設定と一致するかだけ確かめる。
        private static GameObject LoadStage(MissionConfig mission)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(StagePath);
            if (existing != null)
            {
                var mismatch = existing.GetComponent<HalcyonStage>().Mismatch(mission);
                if (mismatch != null) { throw new InvalidOperationException($"{StagePath} が設定と一致しません：{mismatch} Prefab を消して作り直してください。"); }
                return existing;
            }

            var root = new GameObject("HalcyonStage");
            try
            {
                var stage = root.AddComponent<HalcyonStage>();
                var arena = mission.ArenaHalfWidth;

                // 地面：区域より広い板。模様は 100 m ごと。
                var groundSize = arena * 2.5f;
                var ground = Part(root.transform, "Ground", PrimitiveType.Plane, HalcyonArtGenerator.Ground, Vector3.zero, new Vector3(groundSize / 10f, 1f, groundSize / 10f), shadows: false);
                Tile(ground, HalcyonArtGenerator.Ground, groundSize / HalcyonArtGenerator.GroundTile, groundSize / HalcyonArtGenerator.GroundTile);

                // 滑走路：Core の滑走路と同じ中心・大きさ（照合も同じ式）。上面は地面の 0.1 m 上。
                var runwayConfig = mission.Runway;
                var runwaySize = new Vector3(runwayConfig.HalfWidth * 2 / (float)Units.PerMeter, .2f, runwayConfig.Length / (float)Units.PerMeter);
                var runway = Part(root.transform, "Runway", PrimitiveType.Cube, HalcyonArtGenerator.Runway, HalcyonStage.RunwayCenter(runwayConfig, 0f), runwaySize, shadows: false);
                Tile(runway, HalcyonArtGenerator.Runway, 1f, runwaySize.z / HalcyonArtGenerator.RunwayTile);

                // 目印の塔：1.5 km ごとの格子（滑走路の近くは除く）。高さは位置から決める。
                var towers = new GameObject("Towers");
                towers.transform.SetParent(root.transform, false);
                for (var x = -arena; x <= arena; x += 1500)
                {
                    for (var z = -arena; z <= arena; z += 1500)
                    {
                        if (Mathf.Abs(x) < 400 && z < (runwayConfig.EndZ / Units.PerMeter) + 600) { continue; }
                        var height = 30f + (((x * 7) + (z * 13) + 100000) % 70);
                        Part(towers.transform, $"Tower {x} {z}", PrimitiveType.Cube, HalcyonArtGenerator.Tower, new Vector3(x, height / 2f, z), new Vector3(16f, height, 16f), shadows: true);
                        Part(towers.transform, $"Beacon {x} {z}", PrimitiveType.Sphere, HalcyonArtGenerator.Beacon, new Vector3(x, height + 3f, z), Vector3.one * 6f, shadows: false);
                    }
                }

                var aircraft = new Transform[HalcyonStage.AircraftCount];
                for (var i = 0; i < aircraft.Length; i++)
                {
                    aircraft[i] = Plane(root.transform, i == 0 ? "Player" : "Enemy " + i, i == 0 ? HalcyonArtGenerator.Hull : HalcyonArtGenerator.Bandit);
                    aircraft[i].localPosition = i == 0
                        ? new Vector3(runwayConfig.X / (float)Units.PerMeter, 2f, runwayConfig.StartZ / (float)Units.PerMeter)
                        : new Vector3((i - 2) * mission.EnemySpacing, mission.EnemyAltitude, mission.EnemyCenterZ);
                }

                var missile = new GameObject("MissileTemplate");
                missile.transform.SetParent(root.transform, false);
                Part(missile.transform, "Body", PrimitiveType.Cylinder, HalcyonArtGenerator.Missile, Vector3.zero, new Vector3(.3f, 1.6f, .3f), shadows: false).localRotation = Quaternion.Euler(90, 0, 0);
                Part(missile.transform, "Flame", PrimitiveType.Sphere, HalcyonArtGenerator.Engine, new Vector3(0, 0, -1.8f), new Vector3(.5f, .5f, 1.2f), shadows: false);
                missile.transform.localPosition = new Vector3(0, 6f, runwayConfig.StartZ / (float)Units.PerMeter);

                var explosion = Part(root.transform, "ExplosionTemplate", PrimitiveType.Sphere, HalcyonArtGenerator.Explosion, new Vector3(30f, 20f, runwayConfig.StartZ / (float)Units.PerMeter), Vector3.one * HalcyonStage.ExplosionSize, shadows: false);

                var trails = new GameObject("Trails");
                trails.transform.SetParent(root.transform, false);
                var trail = trails.AddComponent<HalcyonTrails>();
                var puff = Part(trails.transform, "PuffTemplate", PrimitiveType.Sphere, HalcyonArtGenerator.Smoke, new Vector3(-30f, 10f, runwayConfig.StartZ / (float)Units.PerMeter), Vector3.one * 3f, shadows: false);
                var trailSerialized = new SerializedObject(trail);
                Assign(trailSerialized, "_puffTemplate", puff.gameObject);
                trailSerialized.ApplyModifiedPropertiesWithoutUndo();

                var serialized = new SerializedObject(stage);
                var list = serialized.FindProperty("_aircraft");
                list.arraySize = aircraft.Length;
                for (var i = 0; i < aircraft.Length; i++) { list.GetArrayElementAtIndex(i).objectReferenceValue = aircraft[i]; }
                Assign(serialized, "_missileTemplate", missile);
                Assign(serialized, "_explosionTemplate", explosion.gameObject);
                Assign(serialized, "_trails", trail);
                Assign(serialized, "_runway", runway);
                Assign(serialized, "_ground", ground);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                return PrefabUtility.SaveAsPrefabAsset(root, StagePath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>プリミティブで組んだ機体（全長約 15 m、中心は Core の機体の位置、機首は +z）。</summary>
        private static Transform Plane(Transform parent, string name, string paint)
        {
            var plane = new GameObject(name);
            plane.transform.SetParent(parent, false);
            var model = new GameObject("Model");
            model.transform.SetParent(plane.transform, false);
            var m = model.transform;
            Part(m, "Fuselage", PrimitiveType.Capsule, paint, Vector3.zero, new Vector3(1.5f, 7.5f, 1.5f), shadows: true).localRotation = Quaternion.Euler(90, 0, 0);
            Part(m, "Wing", PrimitiveType.Cube, paint, new Vector3(0, -.1f, -.8f), new Vector3(12f, .25f, 3.4f), shadows: true);
            Part(m, "Tailplane", PrimitiveType.Cube, paint, new Vector3(0, .1f, -6f), new Vector3(5f, .2f, 1.6f), shadows: true);
            Part(m, "Fin", PrimitiveType.Cube, paint, new Vector3(0, 1.4f, -5.9f), new Vector3(.25f, 2.6f, 2f), shadows: true);
            Part(m, "Canopy", PrimitiveType.Sphere, HalcyonArtGenerator.Canopy, new Vector3(0, .75f, 3f), new Vector3(1f, .8f, 2.4f), shadows: false);
            Part(m, "Engine", PrimitiveType.Sphere, HalcyonArtGenerator.Engine, new Vector3(0, 0, -7.3f), new Vector3(.6f, .6f, .35f), shadows: false);
            Part(m, "NoseGear", PrimitiveType.Cylinder, HalcyonArtGenerator.Gear, new Vector3(0, -1.35f, 4f), new Vector3(.25f, .6f, .25f), shadows: false);
            Part(m, "GearL", PrimitiveType.Cylinder, HalcyonArtGenerator.Gear, new Vector3(-1.6f, -1.35f, -1f), new Vector3(.3f, .6f, .3f), shadows: false);
            Part(m, "GearR", PrimitiveType.Cylinder, HalcyonArtGenerator.Gear, new Vector3(1.6f, -1.35f, -1f), new Vector3(.3f, .6f, .3f), shadows: false);
            return plane.transform;
        }

        private static void Tile(Transform part, string material, float x, float y)
        {
            var renderer = part.GetComponent<MeshRenderer>();
            var instance = new Material(HalcyonArtGenerator.Material(material)) { name = material + "Tiled" };
            instance.SetTextureScale("_BaseMap", new Vector2(x, y));
            var path = Art + material + "Tiled.mat";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(instance, path);
            renderer.sharedMaterial = instance;
        }

        private static Transform Part(Transform parent, string name, PrimitiveType shape, string material, Vector3 position, Vector3 scale, bool shadows)
        {
            var item = GameObject.CreatePrimitive(shape);
            item.name = name;
            Object.DestroyImmediate(item.GetComponent<Collider>());
            item.transform.SetParent(parent, false);
            item.transform.localPosition = position;
            item.transform.localScale = scale;
            var renderer = item.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = HalcyonArtGenerator.Material(material);
            renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return item.transform;
        }

        private static void Assign(SerializedObject target, string field, Object value) =>
            target.FindProperty(field).objectReferenceValue = value;

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
