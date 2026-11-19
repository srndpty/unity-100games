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
    public static class SporeBloomSceneBuilder
    {
        private const string Root = "Assets/Game/Unity/";
        private const string Art = SporeBloomArtGenerator.ArtRoot;
        private const string ScenePath = "Assets/Scenes/SporeBloom.unity";
        private const float FieldX = 8.5f;
        private const float FieldY = -.5f;
        private const float NextX = 4.7f;
        private const float FrameThickness = .3f;
        private const int BackdropOrder = -20;
        private const int PanelOrder = -10;
        private const int BedOrder = -9;
        private const int FrameOrder = -8;
        private const int CellOrder = 0;
        private const int ActiveOrder = 1;

        private static readonly Color _panelColor = new(.04f, .08f, .06f, .8f);
        private static readonly Color _frameColor = new(.29f, .47f, .33f);

        [MenuItem("Game/Rebuild Spore Bloom Scene")]
        public static void Build()
        {
            SporeBloomArtGenerator.GenerateIfMissing();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            // シーンを開くと未使用アセットがアンロードされるので、アセットの読み込みはこの後に行う。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = LoadOrCreate(Root + "SporeBloomSettings.asset", ScriptableObject.CreateInstance<SporeBloomSettings>);
            settings.Validate();
            var panel = LoadOrCreate(Root + "UI/SporeBloomPanelSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PanelSettings>();
                value.themeStyleSheet = Load<ThemeStyleSheet>(Root + "UI/SporeBloomTheme.tss");
                value.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                value.referenceResolution = new Vector2Int(1920, 1080);
                value.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                value.match = .5f;
                return value;
            });

            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "SporeBloom" || root.name == "SporeBloomStage") { Object.DestroyImmediate(root); }
            }

            var stage = (GameObject)PrefabUtility.InstantiatePrefab(LoadStage());
            var view = stage.GetComponent<SporeBloomView>();

            var camera = Object.FindAnyObjectByType<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 8f;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.03f, .05f, .04f);

            var game = new GameObject("SporeBloom");
            var hud = game.AddComponent<PanelRenderer>();
            hud.panelSettings = panel;
            hud.visualTreeAsset = Load<VisualTreeAsset>(Root + "UI/Hud.uxml");
            var component = game.AddComponent<SporeBloomGame>();
            var serialized = new SerializedObject(component);
            Assign(serialized, "_settings", settings);
            Assign(serialized, "_hudRenderer", hud);
            Assign(serialized, "_camera", camera);
            Assign(serialized, "_view", view);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<SporeBloomCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "SPORE BLOOM";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();
            Debug.Log("[SporeBloomSceneBuilder] シーン生成完了");
        }

        // 配置の正は Stage Prefab。既にあれば調整を上書きしない。
        private static GameObject LoadStage()
        {
            var path = Art + "SporeBloomStage.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) { return existing; }

            var material = LoadOrCreate(Art + "Unlit.mat", () => new Material(Shader.Find("Sprites/Default")));
            var root = new GameObject("SporeBloomStage");
            try
            {
                var view = root.AddComponent<SporeBloomView>();
                var backdrop = Sprite(root.transform, "Backdrop", SporeBloomArtGenerator.BackdropFile, material, BackdropOrder);
                backdrop.transform.localScale = new Vector3(30, 17, 1);

                var player = BuildField(root.transform, "PlayerField", -FieldX, NextX, material);
                var cpu = BuildField(root.transform, "CpuField", FieldX, -NextX, material);

                var serialized = new SerializedObject(view);
                Assign(serialized, "_player", player);
                Assign(serialized, "_cpu", cpu);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // 苗床、枠、次のペアの台。プレイ前から Scene ビューで見える。
        private static SporeFieldView BuildField(Transform parent, string name, float x, float nextOffset, Material material)
        {
            var field = new GameObject(name).transform;
            field.SetParent(parent, false);
            field.localPosition = new Vector3(x, FieldY, 0);

            var bed = Sprite(field, "Bed", SporeBloomArtGenerator.BedTileFile, material, BedOrder);
            bed.drawMode = SpriteDrawMode.Tiled;
            bed.size = new Vector2(Board.Width, Board.VisibleHeight);

            var halfWidth = (Board.Width + FrameThickness) / 2f;
            var halfHeight = (Board.VisibleHeight + FrameThickness) / 2f;
            Block(field, "FrameLeft", material, FrameOrder, _frameColor, new Vector3(-halfWidth, 0, 0), new Vector3(FrameThickness, Board.VisibleHeight + (FrameThickness * 2), 1));
            Block(field, "FrameRight", material, FrameOrder, _frameColor, new Vector3(halfWidth, 0, 0), new Vector3(FrameThickness, Board.VisibleHeight + (FrameThickness * 2), 1));
            Block(field, "FrameBottom", material, FrameOrder, _frameColor, new Vector3(0, -halfHeight, 0), new Vector3(Board.Width, FrameThickness, 1));
            Block(field, "FrameTop", material, FrameOrder, _frameColor, new Vector3(0, halfHeight, 0), new Vector3(Board.Width, FrameThickness, 1));
            Block(field, "NextPanel", material, PanelOrder, _panelColor, new Vector3(nextOffset, 3.2f, 0), new Vector3(2.6f, 5.2f, 1));

            var firstSpore = SporeBloomArtGenerator.SporeFile(Cell.Ember);
            var cells = Pool(field, "Cells", firstSpore, material, CellOrder, Board.Width * Board.Height);
            var active = Pool(field, "Active", firstSpore, material, ActiveOrder, 2);
            var next = Pool(field, "Next", firstSpore, material, ActiveOrder, SporeFieldView.PreviewPairs * 2);
            var anchors = new List<Object>();
            for (var i = 0; i < SporeFieldView.PreviewPairs; i++)
            {
                anchors.Add(Anchor(field, "NextAnchor" + i, new Vector3(nextOffset, 4.3f - (i * 2.3f), 0)));
            }

            var sprites = new List<Object>();
            foreach (var cell in SporeBloomArtGenerator.SporeCells) { sprites.Add(Load<Sprite>(Art + SporeBloomArtGenerator.SporeFile(cell))); }

            var view = field.gameObject.AddComponent<SporeFieldView>();
            var serialized = new SerializedObject(view);
            AssignArray(serialized, "_cells", cells);
            AssignArray(serialized, "_active", active);
            AssignArray(serialized, "_next", next);
            AssignArray(serialized, "_nextAnchors", anchors);
            AssignArray(serialized, "_sprites", sprites);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            view.Arrange();
            return view;
        }

        private static void Block(Transform parent, string name, Material material, int order, Color color, Vector3 position, Vector3 scale)
        {
            var block = Sprite(parent, name, SporeBloomArtGenerator.SolidFile, material, order);
            block.color = color;
            block.transform.localPosition = position;
            block.transform.localScale = scale;
        }

        private static Transform Anchor(Transform parent, string name, Vector3 position)
        {
            var anchor = new GameObject(name).transform;
            anchor.SetParent(parent, false);
            anchor.localPosition = position;
            return anchor;
        }

        private static List<Object> Pool(Transform parent, string name, string file, Material material, int order, int count)
        {
            var group = new GameObject(name).transform;
            group.SetParent(parent, false);
            var items = new List<Object>();
            for (var i = 0; i < count; i++)
            {
                var item = Sprite(group, name + i, file, material, order);
                item.enabled = false;
                items.Add(item);
            }

            return items;
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

        private static void AssignArray(SerializedObject target, string field, List<Object> values)
        {
            var property = target.FindProperty(field);
            property.arraySize = values.Count;
            for (var i = 0; i < values.Count; i++) { property.GetArrayElementAtIndex(i).objectReferenceValue = values[i]; }
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
