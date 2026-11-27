#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Game.Core.Geometry;
using Game.Core.Mechanisms;
using Game.Core.Rooms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>
    /// STARGAZER'S STUDY の素材・Stage Prefab・UI 設定・設定アセット・シーンを作り、検証する。
    /// Stage Prefab は Core の部屋の宣言（<see cref="StudyRoom"/>）から作り、部品ごとの物の位置・大きさ・BoxCollider は Core の箱と同じにする。
    /// 既にある Prefab と設定アセットは、宣言と一致する限り上書きしない（手で調整した値を残す）。
    /// 実行：<c>unity run games/048-escape-room -- -executeMethod Game.Unity.Editor.EscapeSceneBuilder.Build</c>
    /// </summary>
    public static class EscapeSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Stargazer.unity";
        public const string SettingsPath = UnityRoot + "EscapeSettings.asset";
        public const string PrefabPath = UnityRoot + "Prefabs/StudyStage.prefab";
        private const string UnityRoot = "Assets/Game/Unity/";
        private const string Prefix = "Escape";

        [MenuItem("Game/Rebuild Stargazer Scene")]
        public static void Build()
        {
            var def = StudyRoom.Create();
            var error = RoomBuildCheck.CheckRoom(def);
            if (error != null) { throw new InvalidOperationException(error); }
            EscapeArtGenerator.GenerateIfMissing();
            EnsureFolder(UnityRoot + "Prefabs");
            EnsureFolder(UnityRoot + "UI");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null && AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/SampleScene.unity") != null)
            {
                var moved = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(moved)) { throw new InvalidOperationException(moved); }
            }

            WriteTextIfMissing(UnityRoot + "UI/EscapeTheme.tss", "@import url(\"unity-theme://default\");\n");
            WriteTextIfMissing(
                UnityRoot + "UI/Hud.uxml",
                "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">\n    <ui:VisualElement name=\"hud\" style=\"flex-grow: 1; position: absolute; left: 0; right: 0; top: 0; bottom: 0;\" />\n</ui:UXML>\n");
            AssetDatabase.Refresh();

            var settings = LoadOrCreate(SettingsPath, ScriptableObject.CreateInstance<EscapeSettings>);
            settings.Validate();
            BuildStagePrefab(def);
            LoadOrCreate(UnityRoot + "UI/EscapePanelSettings.asset", () =>
            {
                var ps = ScriptableObject.CreateInstance<PanelSettings>();
                ps.themeStyleSheet = Required<ThemeStyleSheet>(UnityRoot + "UI/EscapeTheme.tss");
                ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                ps.referenceResolution = new Vector2Int(1920, 1080);
                ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                ps.match = 1f;
                return ps;
            });
            AssetDatabase.SaveAssets();

            // シーンを開いてからアセットを読む（OpenScene のアンロードで参照が切れるのを防ぐ）。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            settings = Required<EscapeSettings>(SettingsPath);
            var panelSettings = Required<PanelSettings>(UnityRoot + "UI/EscapePanelSettings.asset");

            // Prefab を作り直すと古いインスタンスが別名で残ることがあるので、前方一致で消す（018）。
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name.StartsWith(Prefix, StringComparison.Ordinal) || root.name.StartsWith("StudyStage", StringComparison.Ordinal)
                    || root.GetComponent<EscapeGame>() != null || root.GetComponent<StudyStage>() != null)
                {
                    Object.DestroyImmediate(root);
                }
            }

            var stageObj = (GameObject)PrefabUtility.InstantiatePrefab(Required<GameObject>(PrefabPath));
            stageObj.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var stage = stageObj.GetComponent<StudyStage>();
            var mismatch = stage.Mismatch(def);
            if (mismatch != null) { throw new InvalidOperationException("StudyStage: " + mismatch); }

            var camera = Object.FindAnyObjectByType<Camera>();
            if (camera == null)
            {
                var camObj = new GameObject("Main Camera") { tag = "MainCamera" };
                camera = camObj.AddComponent<Camera>();
            }

            camera.orthographic = false;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 30f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f, 0.02f, 0.04f, 1f);
            StageMath.Apply(camera, def.Views[def.WallViews[0]].Pose);

            // 月明かり（窓の方から弱く）と、天井のランプ（暖かい点光源）。
            var light = Object.FindAnyObjectByType<Light>();
            if (light != null)
            {
                light.transform.rotation = Quaternion.Euler(35f, 200f, 0f);
                light.color = new Color(0.55f, 0.62f, 0.85f);
                light.intensity = 0.35f;
                light.shadows = LightShadows.None;
            }

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.3f, 0.27f, 0.24f);
            RenderSettings.fog = false;

            var backdrop = GameObject.CreatePrimitive(PrimitiveType.Quad);
            backdrop.name = Prefix + "HeldBackdrop";
            Object.DestroyImmediate(backdrop.GetComponent<Collider>());
            backdrop.transform.localScale = new Vector3(3f, 1.3f, 1f);
            var backdropRenderer = backdrop.GetComponent<MeshRenderer>();
            backdropRenderer.sharedMaterial = EscapeArtGenerator.Material(EscapeArtGenerator.Black);
            backdropRenderer.shadowCastingMode = ShadowCastingMode.Off;
            backdrop.SetActive(false);

            var gameObj = new GameObject(Prefix + "Game");
            var hud = gameObj.AddComponent<PanelRenderer>();
            hud.panelSettings = panelSettings;
            hud.visualTreeAsset = Required<VisualTreeAsset>(UnityRoot + "UI/Hud.uxml");
            var game = gameObj.AddComponent<EscapeGame>();
            var so = new SerializedObject(game);
            so.FindProperty("_settings").objectReferenceValue = settings;
            so.FindProperty("_hudRenderer").objectReferenceValue = hud;
            so.FindProperty("_camera").objectReferenceValue = camera;
            so.FindProperty("_stage").objectReferenceValue = stage;
            so.FindProperty("_heldBackdrop").objectReferenceValue = backdrop.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
            gameObj.AddComponent<EscapeCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "STARGAZER'S STUDY";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();

            var games = 0;
            var stages = 0;
            foreach (var r in scene.GetRootGameObjects())
            {
                if (r.GetComponent<EscapeGame>() != null) { games++; }
                if (r.GetComponent<StudyStage>() != null) { stages++; }
            }

            if (games != 1 || stages != 1) { throw new InvalidOperationException($"EscapeGame が {games}・StudyStage が {stages} です。"); }
            Debug.Log("[EscapeSceneBuilder] シーンを作り、検証しました。");
            if (Application.isBatchMode) { EditorApplication.Exit(0); }
        }

        /// <summary>Stage の Prefab の中身を作る。部品ごとの物は Core の箱の中心に置き、大きさを箱に合わせ、見た目は箱の中の単位の空間で組む。</summary>
        public static GameObject CreateStage(RoomDef def)
        {
            if (def == null) { throw new ArgumentNullException(nameof(def)); }
            var b = new StageBuilder(def);
            return b.Build();
        }

        private static void BuildStagePrefab(RoomDef def)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null && existing.GetComponent<StudyStage>()?.Mismatch(def) == null) { return; }
            var root = CreateStage(def);
            try
            {
                var mismatch = root.GetComponent<StudyStage>().Mismatch(def);
                if (mismatch != null) { throw new InvalidOperationException("StudyStage を作れません: " + mismatch); }
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static T LoadOrCreate<T>(string path, Func<T> factory)
            where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) { return existing; }
            EnsureFolder(Path.GetDirectoryName(path)!.Replace('\\', '/'));
            var created = factory();
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

        internal static T Required<T>(string path)
            where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            return asset != null ? asset : throw new InvalidOperationException($"アセットがありません: {path}");
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) { return; }
            var parent = Path.GetDirectoryName(path)!.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static void WriteTextIfMissing(string path, string text)
        {
            if (File.Exists(path)) { return; }
            File.WriteAllText(path, text);
        }

        /// <summary>Stage の組み立て。見た目の部品は箱の中の単位の空間（-0.5〜0.5）で置く。</summary>
        private sealed class StageBuilder
        {
            private readonly RoomDef _def;
            private readonly GameObject _root;
            private readonly Transform _partsRoot;
            private readonly Transform _decor;
            private readonly Transform[] _parts;
            private readonly Object?[] _installed;
            private readonly List<Object> _dialDigits = new();
            private readonly List<Object> _rings = new();
            private readonly List<Object> _ringSymbols = new();
            private readonly List<Object> _gems = new();
            private readonly List<Object> _stars = new();
            private readonly Object[] _heldModels;
            private readonly Object[] _clues;
            private readonly List<Object> _cylinderDigits = new();
            private readonly List<Object> _discSymbols = new();
            private Transform? _hourHand;
            private Transform? _minuteHand;
            private Transform? _bookHour;
            private Transform? _bookMinute;
            private Transform? _doorHinge;
            private GameObject? _sky;

            public StageBuilder(RoomDef def)
            {
                _def = def;
                _root = new GameObject("StudyStage");
                _partsRoot = Child(_root.transform, "Parts");
                _decor = Child(_root.transform, "Decor");
                _parts = new Transform[def.Parts.Count];
                _installed = new Object?[def.Mechanisms.Count];
                _heldModels = new Object[def.Examinables.Count];
                _clues = new Object[def.Examinables.Count];
            }

            public GameObject Build()
            {
                var stage = _root.AddComponent<StudyStage>();
                foreach (var p in _def.Parts) { _parts[p.Index] = PartObject(p); }
                Shell();
                Clock();
                Installed();
                Stars();
                HeldModels();

                var so = new SerializedObject(stage);
                Assign(so.FindProperty("_parts"), _parts);
                Assign(so.FindProperty("_installed"), _installed);
                Assign(so.FindProperty("_dialDigits"), _dialDigits);
                Assign(so.FindProperty("_rings"), _rings);
                Assign(so.FindProperty("_ringSymbols"), _ringSymbols);
                so.FindProperty("_hourHand").objectReferenceValue = _hourHand;
                so.FindProperty("_minuteHand").objectReferenceValue = _minuteHand;
                Assign(so.FindProperty("_gems"), _gems);
                Assign(so.FindProperty("_stars"), _stars);
                Assign(so.FindProperty("_heldModels"), _heldModels);
                Assign(so.FindProperty("_clues"), _clues);
                Assign(so.FindProperty("_cylinderDigits"), _cylinderDigits);
                Assign(so.FindProperty("_discSymbols"), _discSymbols);
                so.FindProperty("_bookHour").objectReferenceValue = _bookHour;
                so.FindProperty("_bookMinute").objectReferenceValue = _bookMinute;
                so.FindProperty("_doorHinge").objectReferenceValue = _doorHinge;
                so.FindProperty("_sky").objectReferenceValue = _sky;
                var digits = new Object[10];
                for (int d = 0; d < 10; d++) { digits[d] = EscapeArtGenerator.Material(EscapeArtGenerator.DigitName(d)); }
                Assign(so.FindProperty("_digitMaterials"), digits);
                var symbols = new Object[RingLock.Constellations.Count];
                for (int c = 0; c < symbols.Length; c++) { symbols[c] = EscapeArtGenerator.Material(EscapeArtGenerator.SymbolName(c)); }
                Assign(so.FindProperty("_symbolMaterials"), symbols);
                var gems = new Object[SequenceLock.GemCount];
                var lit = new Object[SequenceLock.GemCount];
                for (int g = 0; g < gems.Length; g++)
                {
                    gems[g] = EscapeArtGenerator.Material(EscapeArtGenerator.GemName(g, false));
                    lit[g] = EscapeArtGenerator.Material(EscapeArtGenerator.GemName(g, true));
                }

                Assign(so.FindProperty("_gemMaterials"), gems);
                Assign(so.FindProperty("_gemLitMaterials"), lit);
                var icons = new Object[_def.Items.Count];
                for (int i = 0; i < icons.Length; i++) { icons[i] = EscapeArtGenerator.Icon(i); }
                Assign(so.FindProperty("_icons"), icons);
                so.ApplyModifiedPropertiesWithoutUndo();
                return _root;
            }

            /// <summary>部品の物：Core の箱の中心・大きさ・照合専用の BoxCollider と、箱の中の見た目。</summary>
            private Transform PartObject(PartDef p)
            {
                var go = new GameObject(p.Id);
                go.transform.SetParent(_partsRoot, false);
                go.transform.localPosition = StageMath.Center(p.Box);
                go.transform.localScale = StageMath.Size(p.Box);
                go.AddComponent<BoxCollider>();
                var t = go.transform;
                var wall = _def.Views[p.View].Wall;
                switch (p.Kind)
                {
                    case PartKind.Slot:
                        SlotVisual(t, p, wall);
                        break;
                    case PartKind.Keyhole:
                        Cube(t, "Plate", EscapeArtGenerator.Brass, Vector3.zero, Vector3.one);
                        FrontCube(t, wall, "Slot", EscapeArtGenerator.Black, new Vector3(0.25f, 0.6f, 0.2f), 0f);
                        break;
                    case PartKind.Socket:
                        Cube(t, "Rim", EscapeArtGenerator.Brass, Vector3.zero, Vector3.one);
                        FrontCube(t, wall, "Hole", EscapeArtGenerator.Black, new Vector3(0.6f, 0.6f, 0.2f), 0f);
                        break;
                    case PartKind.Pickup:
                        PickupVisual(t, p);
                        break;
                    case PartKind.Examinable:
                        ExaminableVisual(t, p);
                        break;
                    default:
                        BodyVisual(t, p, wall);
                        break;
                }

                return t;
            }

            private void SlotVisual(Transform t, PartDef p, int wall)
            {
                switch (_def.Mechanisms[p.Mechanism].Kind)
                {
                    case MechanismKind.Dial:
                        Cube(t, "Wheel", EscapeArtGenerator.Brass, Vector3.zero, new Vector3(0.95f, 1f, 1f));
                        _dialDigits.Add(FrontQuad(t, wall, "Digit", EscapeArtGenerator.DigitName(0), new Vector2(0.8f, 0.7f), Vector2.zero));
                        break;
                    case MechanismKind.Rings:
                        {
                            var ring = Primitive(t, "Ring", PrimitiveType.Cylinder, EscapeArtGenerator.Brass, Vector3.zero, new Vector3(1f, 0.45f, 1f));
                            for (int k = 0; k < 4; k++)
                            {
                                var notch = Primitive(ring, "Notch" + k, PrimitiveType.Cube, EscapeArtGenerator.DarkWood, Vector3.zero, new Vector3(0.12f, 1.1f, 0.12f));
                                var a = k * Mathf.PI / 2f;
                                notch.localPosition = new Vector3(Mathf.Cos(a) * 0.5f, 0f, Mathf.Sin(a) * 0.5f);
                            }

                            _rings.Add(ring);
                            _ringSymbols.Add(FrontQuad(t, wall, "Symbol", EscapeArtGenerator.SymbolName(0), new Vector2(0.34f, 0.9f), Vector2.zero, 0.53f));
                            break;
                        }

                    case MechanismKind.Clock:
                        Primitive(t, "Knob", PrimitiveType.Sphere, EscapeArtGenerator.Brass, Vector3.zero, Vector3.one);
                        break;
                    default:
                        _gems.Add(Primitive(t, "Gem", PrimitiveType.Sphere, EscapeArtGenerator.GemName(p.Slot, false), Vector3.zero, Vector3.one).GetComponent<Renderer>());
                        break;
                }
            }

            private void PickupVisual(Transform t, PartDef p)
            {
                switch (_def.Items[p.Item].Id)
                {
                    case "star_disc":
                        Primitive(t, "Disc", PrimitiveType.Cylinder, EscapeArtGenerator.Navy, Vector3.zero, new Vector3(1f, 0.5f, 1f));
                        break;
                    case "gear":
                        Primitive(t, "Gear", PrimitiveType.Cylinder, EscapeArtGenerator.Brass, Vector3.zero, new Vector3(1f, 0.5f, 1f));
                        Primitive(t, "Teeth", PrimitiveType.Cube, EscapeArtGenerator.Brass, Vector3.zero, new Vector3(1.2f, 0.9f, 0.2f));
                        Primitive(t, "Teeth2", PrimitiveType.Cube, EscapeArtGenerator.Brass, Vector3.zero, new Vector3(0.2f, 0.9f, 1.2f));
                        break;
                    case "handle":
                        Cube(t, "Bar", EscapeArtGenerator.Iron, new Vector3(0f, 0f, 0f), new Vector3(1f, 1f, 0.3f));
                        Cube(t, "Shaft", EscapeArtGenerator.Iron, new Vector3(0f, 0f, 0.3f), new Vector3(0.3f, 1f, 0.7f));
                        break;
                    default:
                        {
                            var metal = _def.Items[p.Item].Id == "exit_key" ? EscapeArtGenerator.Silver : EscapeArtGenerator.Brass;
                            Primitive(t, "Bow", PrimitiveType.Cylinder, metal, new Vector3(-0.3f, 0f, 0f), new Vector3(0.4f, 0.5f, 0.6f));
                            Cube(t, "Shaft", metal, new Vector3(0.15f, 0f, 0f), new Vector3(0.7f, 0.6f, 0.15f));
                            Cube(t, "Bit", metal, new Vector3(0.4f, 0f, 0.15f), new Vector3(0.15f, 0.6f, 0.3f));
                            break;
                        }
                }
            }

            private void ExaminableVisual(Transform t, PartDef p)
            {
                switch (_def.Examinables[p.Examinable].Id)
                {
                    case "cylinder":
                        Primitive(t, "Tube", PrimitiveType.Cylinder, EscapeArtGenerator.Brass, Vector3.zero, new Vector3(1f, 0.5f, 1f));
                        break;
                    case "disc":
                        Primitive(t, "Disc", PrimitiveType.Cylinder, EscapeArtGenerator.Navy, Vector3.zero, new Vector3(1f, 0.5f, 1f)).localRotation = Quaternion.Euler(0f, 0f, 90f);
                        Primitive(t, "Rim", PrimitiveType.Cylinder, EscapeArtGenerator.Brass, new Vector3(-0.1f, 0f, 0f), new Vector3(1.04f, 0.45f, 1.04f)).localRotation = Quaternion.Euler(0f, 0f, 90f);
                        break;
                    default:
                        Cube(t, "Cover", EscapeArtGenerator.Leather, Vector3.zero, Vector3.one);
                        Cube(t, "Pages", EscapeArtGenerator.Paper, new Vector3(0.05f, 0f, 0f), new Vector3(0.92f, 0.94f, 0.9f));
                        Cube(t, "Band", EscapeArtGenerator.Brass, new Vector3(-0.01f, 0.3f, 0f), new Vector3(1.0f, 0.06f, 1.02f));
                        break;
                }
            }

            private void BodyVisual(Transform t, PartDef p, int wall)
            {
                string material = EscapeArtGenerator.Wood;
                var id = p.Id;
                if (id.StartsWith("clock.", StringComparison.Ordinal) || id.StartsWith("cabinet.", StringComparison.Ordinal)) { material = EscapeArtGenerator.DarkWood; }
                if (id.StartsWith("safe.", StringComparison.Ordinal)) { material = EscapeArtGenerator.Iron; }
                if (id.StartsWith("globe.", StringComparison.Ordinal)) { material = EscapeArtGenerator.Brass; }
                if (id == "globe.stand") { material = EscapeArtGenerator.DarkWood; }
                if (id == "shelf") { material = EscapeArtGenerator.DarkWood; }
                switch (id)
                {
                    case "window":
                        Cube(t, "Frame", EscapeArtGenerator.DarkWood, Vector3.zero, Vector3.one);
                        FrontCube(t, wall, "Glass", EscapeArtGenerator.Night, new Vector3(0.92f, 0.94f, 0.4f), 0.1f);
                        Cube(t, "Mullion", EscapeArtGenerator.DarkWood, new Vector3(0f, 0f, -0.3f), new Vector3(0.03f, 1f, 0.6f));
                        Cube(t, "Transom", EscapeArtGenerator.DarkWood, new Vector3(0f, 0.15f, -0.3f), new Vector3(1f, 0.03f, 0.6f));
                        return;
                    case "telescope":
                        {
                            var tube = Primitive(t, "Tube", PrimitiveType.Cylinder, EscapeArtGenerator.Brass, new Vector3(0f, 0.3f, 0f), new Vector3(0.35f, 0.42f, 0.35f));
                            tube.localRotation = Quaternion.Euler(55f, 0f, 0f);
                            Cube(t, "LegA", EscapeArtGenerator.DarkWood, new Vector3(-0.3f, -0.2f, 0f), new Vector3(0.06f, 0.65f, 0.06f));
                            Cube(t, "LegB", EscapeArtGenerator.DarkWood, new Vector3(0.3f, -0.2f, 0f), new Vector3(0.06f, 0.65f, 0.06f));
                            Cube(t, "LegC", EscapeArtGenerator.DarkWood, new Vector3(0f, -0.2f, 0.35f), new Vector3(0.06f, 0.65f, 0.06f));
                            return;
                        }

                    case "door.slab":
                        // 扉の見た目は蝶番で回すので、部品の物（拡大した箱）の外に置く（拡大の中で回すと歪む）。
                        DoorLeaf(p);
                        return;
                    case "clock.upper":
                        Cube(t, "Case", material, Vector3.zero, Vector3.one);
                        return;
                    case "clock.door":
                    case "clock.open":
                        Cube(t, "Door", EscapeArtGenerator.DarkWood, Vector3.zero, Vector3.one);
                        return;
                    case "globe.lid":
                    case "globe.lidOpen":
                        Primitive(t, "Dome", PrimitiveType.Sphere, EscapeArtGenerator.Brass, Vector3.zero, new Vector3(1f, 1.6f, 1f));
                        return;
                    case "globe.base":
                        Primitive(t, "Plate", PrimitiveType.Cylinder, EscapeArtGenerator.Brass, Vector3.zero, new Vector3(1f, 0.5f, 1f));
                        return;
                    case "desk.front":
                        Cube(t, "Front", material, Vector3.zero, Vector3.one);
                        FrontCube(t, wall, "Pull", EscapeArtGenerator.Brass, new Vector3(0.2f, 0.12f, 1f), 0.4f);
                        return;
                    default:
                        if (id.StartsWith("shelf.book", StringComparison.Ordinal))
                        {
                            Cube(t, "Book", EscapeArtGenerator.BookColors[(id[id.Length - 1] - '0') % EscapeArtGenerator.BookColors.Length], Vector3.zero, Vector3.one);
                            return;
                        }

                        Cube(t, "Body", material, Vector3.zero, Vector3.one);
                        return;
                }
            }

            private void DoorLeaf(PartDef p)
            {
                var box = p.Box;
                var hinge = Child(_decor, "DoorHinge");
                hinge.localPosition = StageMath.ToWorld(new Vec3i(box.Max.X, 0, box.Max.Z));
                var width = box.Size.X * StageMath.MetersPerUnit;
                var leaf = Primitive(hinge, "Leaf", PrimitiveType.Cube, EscapeArtGenerator.Wood, new Vector3(-width / 2f, box.Size.Y * StageMath.MetersPerUnit / 2f, -box.Size.Z * StageMath.MetersPerUnit / 2f), StageMath.Size(box));
                Primitive(leaf, "Panel", PrimitiveType.Cube, EscapeArtGenerator.DarkWood, new Vector3(0f, 0.15f, 0.55f), new Vector3(0.8f, 0.3f, 0.2f));
                Primitive(leaf, "Panel2", PrimitiveType.Cube, EscapeArtGenerator.DarkWood, new Vector3(0f, -0.25f, 0.55f), new Vector3(0.8f, 0.35f, 0.2f));
                _doorHinge = hinge;

                // 扉の向こう（部屋の外）の明るい夜明けの空。扉が開くと見える。
                // 閉じている間は扉の板の中に隠れる位置（壁の面の 2 mm 手前）。
                var outside = Primitive(_decor, "Outside", PrimitiveType.Quad, EscapeArtGenerator.Lamp, StageMath.ToWorld(new Vec3i((box.Min.X + box.Max.X) / 2, 1050, box.Min.Z + 2)), new Vector3(width, 2.1f, 1f));
                outside.localRotation = Quaternion.Euler(0f, 180f, 0f);
            }

            /// <summary>部屋の壁・床・天井・敷物・ランプ（当たりはない）。</summary>
            private void Shell()
            {
                var b = _def.Bounds;
                var size = StageMath.Size(b);
                var c = StageMath.Center(b);
                Tiled(Primitive(_decor, "Floor", PrimitiveType.Cube, EscapeArtGenerator.Floor, new Vector3(c.x, -0.05f, c.z), new Vector3(size.x, 0.1f, size.z)), 2f, 2f);
                Primitive(_decor, "Ceiling", PrimitiveType.Cube, EscapeArtGenerator.Ceiling, new Vector3(c.x, size.y + 0.05f, c.z), new Vector3(size.x, 0.1f, size.z));
                Tiled(Primitive(_decor, "WallNorth", PrimitiveType.Cube, EscapeArtGenerator.Wall, new Vector3(c.x, c.y, (b.Max.Z * StageMath.MetersPerUnit) + 0.05f), new Vector3(size.x, size.y, 0.1f)), 6f, 4f);
                Tiled(Primitive(_decor, "WallSouth", PrimitiveType.Cube, EscapeArtGenerator.Wall, new Vector3(c.x, c.y, (b.Min.Z * StageMath.MetersPerUnit) - 0.05f), new Vector3(size.x, size.y, 0.1f)), 6f, 4f);
                Tiled(Primitive(_decor, "WallEast", PrimitiveType.Cube, EscapeArtGenerator.Wall, new Vector3((b.Max.X * StageMath.MetersPerUnit) + 0.05f, c.y, c.z), new Vector3(0.1f, size.y, size.z)), 6f, 4f);
                Tiled(Primitive(_decor, "WallWest", PrimitiveType.Cube, EscapeArtGenerator.Wall, new Vector3((b.Min.X * StageMath.MetersPerUnit) - 0.05f, c.y, c.z), new Vector3(0.1f, size.y, size.z)), 6f, 4f);
                Primitive(_decor, "Rug", PrimitiveType.Cube, EscapeArtGenerator.Rug, new Vector3(0f, 0.005f, 0f), new Vector3(2.2f, 0.01f, 1.6f));
                Primitive(_decor, "LampCord", PrimitiveType.Cube, EscapeArtGenerator.Black, new Vector3(0f, size.y - 0.25f, 0f), new Vector3(0.02f, 0.5f, 0.02f));
                Primitive(_decor, "LampShade", PrimitiveType.Sphere, EscapeArtGenerator.Lamp, new Vector3(0f, size.y - 0.55f, 0f), new Vector3(0.3f, 0.22f, 0.3f));
                var lampLight = new GameObject("LampLight").AddComponent<Light>();
                lampLight.transform.SetParent(_decor, false);
                lampLight.transform.localPosition = new Vector3(0f, size.y - 0.7f, 0f);
                lampLight.type = LightType.Point;
                lampLight.color = new Color(1f, 0.82f, 0.6f);
                lampLight.intensity = 6f;
                lampLight.range = 9f;
                lampLight.shadows = LightShadows.None;
            }

            /// <summary>柱時計の文字盤と針（Core の部品ではない飾り。針は値で回す）。</summary>
            private void Clock()
            {
                var axle = _def.Parts[_def.SocketOf(MechanismOf(MechanismKind.Clock))];
                var center = StageMath.Center(axle.Box);
                var face = Primitive(_decor, "ClockFace", PrimitiveType.Quad, EscapeArtGenerator.ClockFace, new Vector3(center.x, center.y, (axle.Box.Min.Z * StageMath.MetersPerUnit) + 0.002f), new Vector3(0.38f, 0.38f, 1f));
                face.localRotation = Quaternion.Euler(0f, 180f, 0f);

                // 南の壁の柱時計は +z を向く。針の根元を 180° 回し、見る向きが局所の +z になるようにする（z の負の角で時計回り）。
                var hands = Child(_decor, "ClockHands");
                hands.localPosition = new Vector3(center.x, center.y, axle.Box.Max.Z * StageMath.MetersPerUnit);
                hands.localRotation = Quaternion.Euler(0f, 180f, 0f);
                _hourHand = Hand(hands, "HourHand", 0.1f, 0.016f, 0.004f);
                _minuteHand = Hand(hands, "MinuteHand", 0.15f, 0.01f, 0.008f);
            }

            private static Transform Hand(Transform root, string name, float length, float width, float lift)
            {
                var pivot = Child(root, name);
                pivot.localPosition = new Vector3(0f, 0f, -lift);
                Primitive(pivot, "Hand", PrimitiveType.Cube, EscapeArtGenerator.Black, new Vector3(0f, length / 2f, 0f), new Vector3(width, length, 0.004f));
                return pivot;
            }

            /// <summary>持ち物をはめた・差した見た目（仕掛けが Locked でなくなったら出す）。</summary>
            private void Installed()
            {
                foreach (var m in _def.Mechanisms)
                {
                    var socket = _def.SocketOf(m.Index);
                    if (socket < 0) { continue; }
                    var box = _def.Parts[socket].Box;
                    var wall = _def.Views[_def.Parts[socket].View].Wall;
                    var front = StageMath.ToWorld(box.FrontCenter(wall)) - (Forward(wall) * 0.02f);
                    var holder = Child(_decor, "Installed " + m.Id);
                    holder.localPosition = front;
                    holder.localRotation = Quaternion.Euler(0f, wall * 90f, 0f);
                    switch (m.Kind)
                    {
                        case MechanismKind.KeyLock:
                            {
                                var metal = m.Item >= 0 && _def.Items[m.Item].Id == "exit_key" ? EscapeArtGenerator.Silver : EscapeArtGenerator.Brass;
                                Primitive(holder, "Bow", PrimitiveType.Cylinder, metal, new Vector3(0f, 0f, -0.04f), new Vector3(0.05f, 0.005f, 0.05f)).localRotation = Quaternion.Euler(90f, 0f, 0f);
                                Primitive(holder, "Shaft", PrimitiveType.Cube, metal, new Vector3(0f, 0f, -0.015f), new Vector3(0.01f, 0.01f, 0.03f));
                                break;
                            }

                        case MechanismKind.Clock:
                            Primitive(holder, "Gear", PrimitiveType.Cylinder, EscapeArtGenerator.Brass, new Vector3(0f, 0f, -0.006f), new Vector3(0.09f, 0.004f, 0.09f)).localRotation = Quaternion.Euler(90f, 0f, 0f);
                            break;
                        case MechanismKind.Sequence:
                            Primitive(holder, "Shaft", PrimitiveType.Cube, EscapeArtGenerator.Iron, new Vector3(0f, 0f, -0.04f), new Vector3(0.03f, 0.03f, 0.08f));
                            Primitive(holder, "Bar", PrimitiveType.Cube, EscapeArtGenerator.Iron, new Vector3(0f, 0f, -0.08f), new Vector3(0.03f, 0.16f, 0.03f));
                            break;
                        default:
                            Object.DestroyImmediate(holder.gameObject);
                            continue;
                    }

                    holder.gameObject.SetActive(false);
                    _installed[m.Index] = holder.gameObject;
                }
            }

            /// <summary>望遠鏡の視野の星（宝石の色）。望遠鏡をのぞいている間だけ、答えの順に光る。</summary>
            private void Stars()
            {
                ViewDef? view = null;
                foreach (var v in _def.Views)
                {
                    if (v.Reveals >= 0 && _def.Mechanisms[v.Reveals].Kind == MechanismKind.Sequence) { view = v; }
                }

                if (view == null) { throw new InvalidOperationException("望遠鏡の視点がありません"); }
                var eye = StageMath.ToWorld(view.Pose.Eye);
                var offsets = new[] { new Vector3(-0.3f, 0.12f, 0f), new Vector3(-0.08f, -0.1f, 0f), new Vector3(0.14f, 0.15f, 0f), new Vector3(0.3f, -0.04f, 0f) };
                for (int g = 0; g < SequenceLock.GemCount; g++)
                {
                    var star = Primitive(_decor, "Star" + g, PrimitiveType.Sphere, EscapeArtGenerator.StarName(g), eye + (Vector3.forward * 0.6f) + offsets[g], Vector3.one * 0.055f);
                    star.gameObject.SetActive(false);
                    _stars.Add(star.gameObject);
                }

                // 背景の暗い星（望遠鏡をのぞいている間だけ出す。位置は決まった式で散らす）。
                var sky = Child(_decor, "Sky");
                for (int i = 0; i < 28; i++)
                {
                    var x = (((i * 37) + 11) % 89 / 88f) - 0.5f;
                    var y = (((i * 53) + 7) % 61 / 60f) - 0.5f;
                    Primitive(sky, "Dim" + i, PrimitiveType.Sphere, EscapeArtGenerator.StarDim, eye + (Vector3.forward * 0.62f) + new Vector3(x * 0.9f, y * 0.5f, 0f), Vector3.one * (0.005f + (i % 3 * 0.002f)));
                }

                sky.gameObject.SetActive(false);
                _sky = sky.gameObject;
            }

            /// <summary>手に取った物の模型（カメラの前に置く）と、手がかりの面（向いているときだけ出す）。番号は Core の手に取る物の番号。</summary>
            private void HeldModels()
            {
                var held = Child(_root.transform, "Held");
                foreach (var e in _def.Examinables)
                {
                    var model = Child(held, "Held " + e.Id);
                    var clue = Child(model, "Clue");
                    switch (e.Id)
                    {
                        case "cylinder":
                            Primitive(model, "Tube", PrimitiveType.Cylinder, EscapeArtGenerator.Brass, Vector3.zero, new Vector3(0.12f, 0.1f, 0.12f));
                            for (int i = 0; i < DialLock.Digits; i++)
                            {
                                // 底（-y）に 3 桁。見る向き（ピッチ 90° 後）で左から読めるように並べる。
                                var q = Primitive(clue, "Digit" + i, PrimitiveType.Quad, EscapeArtGenerator.DigitName(0), new Vector3((i - 1) * 0.034f, -0.1015f, 0f), new Vector3(0.03f, 0.045f, 1f));
                                q.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                                _cylinderDigits.Add(q.GetComponent<Renderer>());
                            }

                            break;
                        case "disc":
                            Primitive(model, "Disc", PrimitiveType.Cylinder, EscapeArtGenerator.Navy, Vector3.zero, new Vector3(0.22f, 0.006f, 0.22f)).localRotation = Quaternion.Euler(90f, 0f, 0f);
                            Primitive(model, "Rim", PrimitiveType.Cylinder, EscapeArtGenerator.Brass, Vector3.zero, new Vector3(0.23f, 0.005f, 0.23f)).localRotation = Quaternion.Euler(90f, 0f, 0f);
                            for (int i = 0; i < RingLock.RingCount; i++)
                            {
                                // 裏（+z）に上・中・下の 3 星座。
                                var q = Primitive(clue, "Symbol" + i, PrimitiveType.Quad, EscapeArtGenerator.SymbolName(0), new Vector3(0f, (1 - i) * 0.058f, 0.0075f), new Vector3(0.055f, 0.055f, 1f));
                                q.localRotation = Quaternion.Euler(0f, 180f, 0f);
                                _discSymbols.Add(q.GetComponent<Renderer>());
                            }

                            break;
                        default:
                            {
                                Primitive(model, "Cover", PrimitiveType.Cube, EscapeArtGenerator.Leather, Vector3.zero, new Vector3(0.16f, 0.22f, 0.04f));
                                Primitive(model, "Pages", PrimitiveType.Cube, EscapeArtGenerator.Paper, new Vector3(0.006f, 0f, 0f), new Vector3(0.152f, 0.212f, 0.034f));
                                Primitive(model, "Title", PrimitiveType.Cube, EscapeArtGenerator.Brass, new Vector3(0f, 0.05f, -0.0205f), new Vector3(0.1f, 0.02f, 0.002f));

                                // 裏表紙（+z）に小さな文字盤と針。
                                var face = Primitive(clue, "Face", PrimitiveType.Quad, EscapeArtGenerator.ClockFace, new Vector3(0f, 0f, 0.0205f), new Vector3(0.11f, 0.11f, 1f));
                                face.localRotation = Quaternion.Euler(0f, 180f, 0f);
                                var pivotRoot = Child(clue, "Hands");
                                pivotRoot.localPosition = new Vector3(0f, 0f, 0.0215f);
                                pivotRoot.localRotation = Quaternion.Euler(0f, 180f, 0f);
                                _bookHour = Child(pivotRoot, "Hour");
                                Primitive(_bookHour, "Hand", PrimitiveType.Cube, EscapeArtGenerator.Black, new Vector3(0f, 0.016f, -0.001f), new Vector3(0.006f, 0.032f, 0.001f));
                                _bookMinute = Child(pivotRoot, "Minute");
                                Primitive(_bookMinute, "Hand", PrimitiveType.Cube, EscapeArtGenerator.Black, new Vector3(0f, 0.024f, -0.0015f), new Vector3(0.004f, 0.048f, 0.001f));
                                break;
                            }
                    }

                    clue.gameObject.SetActive(false);
                    model.gameObject.SetActive(false);
                    _heldModels[e.Index] = model.gameObject;
                    _clues[e.Index] = clue.gameObject;
                }
            }

            private int MechanismOf(MechanismKind kind)
            {
                foreach (var m in _def.Mechanisms)
                {
                    if (m.Kind == kind) { return m.Index; }
                }

                throw new InvalidOperationException($"{kind} の仕掛けがありません");
            }

            private static Vector3 Forward(int wall) => Quaternion.Euler(0f, wall * 90f, 0f) * Vector3.forward;

            /// <summary>箱の手前の面（見る壁の向きの反対側）に貼る板（単位の空間）。</summary>
            private static Renderer FrontQuad(Transform parent, int wall, string name, string material, Vector2 size, Vector2 offset, float depth = 0.51f)
            {
                var q = Primitive(parent, name, PrimitiveType.Quad, material, Vector3.zero, new Vector3(size.x, size.y, 1f));
                q.localRotation = Quaternion.Euler(0f, wall * 90f, 0f);
                var f = Forward(wall);
                var right = Quaternion.Euler(0f, wall * 90f, 0f) * Vector3.right;
                q.localPosition = (-f * depth) + (right * offset.x) + (Vector3.up * offset.y);
                return q.GetComponent<Renderer>();
            }

            /// <summary>箱の手前の面から少し出る小さな箱（単位の空間、深さは手前へ）。</summary>
            private static void FrontCube(Transform parent, int wall, string name, string material, Vector3 size, float up)
            {
                var f = Forward(wall);
                var depth = size.z;
                var scale = (wall & 1) == 0 ? new Vector3(size.x, size.y, depth) : new Vector3(depth, size.y, size.x);
                Primitive(parent, name, PrimitiveType.Cube, material, (-f * (0.5f + (depth * 0.5f) - 0.01f)) + (Vector3.up * up), scale);
            }

            private static Transform Cube(Transform parent, string name, string material, Vector3 position, Vector3 scale) =>
                Primitive(parent, name, PrimitiveType.Cube, material, position, scale);

            private static Transform Primitive(Transform parent, string name, PrimitiveType shape, string material, Vector3 position, Vector3 scale)
            {
                var item = GameObject.CreatePrimitive(shape);
                item.name = name;
                Object.DestroyImmediate(item.GetComponent<Collider>());
                item.transform.SetParent(parent, false);
                item.transform.localPosition = position;
                item.transform.localScale = scale;
                var renderer = item.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = EscapeArtGenerator.Material(material);
                renderer.shadowCastingMode = ShadowCastingMode.On;
                return item.transform;
            }

            private static void Tiled(Transform part, float x, float y)
            {
                var renderer = part.GetComponent<MeshRenderer>();
                var name = renderer.sharedMaterial.name + "Tiled" + part.name;
                var path = EscapeArtGenerator.ArtRoot + name + ".mat";
                var instance = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (instance == null)
                {
                    instance = new Material(renderer.sharedMaterial) { name = name };
                    instance.SetTextureScale("_BaseMap", new Vector2(x, y));
                    AssetDatabase.CreateAsset(instance, path);
                }

                renderer.sharedMaterial = instance;
            }

            private static Transform Child(Transform parent, string name)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                return go.transform;
            }

            private static void Assign(SerializedProperty prop, IReadOnlyList<Object?> values)
            {
                prop.arraySize = values.Count;
                for (int i = 0; i < values.Count; i++) { prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i]; }
            }
        }
    }
}
