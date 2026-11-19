using System;
using System.Collections.Generic;
using Game.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    public static class SevenLanternsSceneBuilder
    {
        private const string Root = "Assets/Game/Unity/";
        private const string Art = SevenLanternsArtGenerator.ArtRoot;
        private const string ScenePath = "Assets/Scenes/SevenLanterns.unity";
        private const int BackdropOrder = -20;
        private const int SlotOrder = -10;

        // 札の中の重なり順。裏は表の上に重ねて、伏せた札では表を隠す。
        private const int FaceOrder = 0;
        private const int InkOrder = 1;
        private const int BackOrder = 5;
        private const int HighlightOrder = 6;

        private const float CornerX = .34f;
        private const float CornerRankY = .52f;
        private const float CornerSuitY = .25f;
        private const float CornerRankScale = .44f;
        private const float CornerSuitScale = .24f;
        private const float CenterSuitScale = .56f;

        // 卓の座標（TableLayout）を画面の左側いっぱいに広げる。右側は HUD。
        private const float TableScale = 1.25f;

        private static readonly Vector3 _tableOffset = new(1.2f, -.45f, 0);
        private static readonly Color _highlightColor = new(1f, .74f, .22f);

        [MenuItem("Game/Rebuild Seven Lanterns Scene")]
        public static void Build()
        {
            SevenLanternsArtGenerator.GenerateIfMissing();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            // シーンを開くと未使用アセットがアンロードされるので、アセットの読み込みはこの後に行う。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = LoadOrCreate(Root + "SevenLanternsSettings.asset", ScriptableObject.CreateInstance<SevenLanternsSettings>);
            settings.Validate();
            var panel = LoadOrCreate(Root + "UI/SevenLanternsPanelSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PanelSettings>();
                value.themeStyleSheet = Load<ThemeStyleSheet>(Root + "UI/SevenLanternsTheme.tss");
                value.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                value.referenceResolution = new Vector2Int(1920, 1080);
                value.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                value.match = .5f;
                return value;
            });

            // Prefab を作り直した後は、古いインスタンスが「(Missing Prefab ...)」の付いた名前で残るので前方一致で消す。
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "SevenLanterns" || root.name.StartsWith("SevenLanternsStage", StringComparison.Ordinal)) { Object.DestroyImmediate(root); }
            }

            var stage = (GameObject)PrefabUtility.InstantiatePrefab(LoadStage());
            var view = stage.GetComponentInChildren<SevenLanternsTableView>();

            var camera = Object.FindAnyObjectByType<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5.4f;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.04f, .08f, .12f);

            var game = new GameObject("SevenLanterns");
            var hud = game.AddComponent<PanelRenderer>();
            hud.panelSettings = panel;
            hud.visualTreeAsset = Load<VisualTreeAsset>(Root + "UI/Hud.uxml");
            var component = game.AddComponent<SevenLanternsGame>();
            var serialized = new SerializedObject(component);
            Assign(serialized, "_settings", settings);
            Assign(serialized, "_hudRenderer", hud);
            Assign(serialized, "_camera", camera);
            Assign(serialized, "_view", view);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<SevenLanternsCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "SEVEN LANTERNS";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();
            Debug.Log("[SevenLanternsSceneBuilder] シーン生成完了");
        }

        // 配置の正は Stage Prefab。既にあれば調整を上書きしない。
        private static GameObject LoadStage()
        {
            var path = Art + "SevenLanternsStage.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) { return existing; }

            var material = LoadOrCreate(Art + "Unlit.mat", () => new Material(Shader.Find("Sprites/Default")));
            var root = new GameObject("SevenLanternsStage");
            try
            {
                var backdrop = Sprite(root.transform, "Backdrop", SevenLanternsArtGenerator.BackdropFile, material, BackdropOrder);
                backdrop.transform.localScale = new Vector3(20, 11.5f, 1);
                BuildTable(root.transform, material);
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // 13 の枠と 52 枚の札。プレイ前から Scene ビューで見えるよう、飾りの配札で並べておく。
        private static void BuildTable(Transform parent, Material material)
        {
            var table = new GameObject("Table").transform;
            table.SetParent(parent, false);
            table.localPosition = _tableOffset;
            table.localScale = new Vector3(TableScale, TableScale, 1);

            var slots = new List<Object>();
            var slotGroup = Group(table, "Slots");
            foreach (var pile in PileId.All) { slots.Add(Sprite(slotGroup, "Slot-" + pile, SevenLanternsArtGenerator.SlotFile, material, SlotOrder)); }

            var cards = new List<Object>();
            var groups = new List<Object>();
            var backs = new List<Object>();
            var highlights = new List<Object>();
            var cardGroup = Group(table, "Cards");
            for (var id = 0; id < Card.DeckSize; id++)
            {
                var card = Card.FromId(id);
                var item = new GameObject("Card-" + card).transform;
                item.SetParent(cardGroup, false);
                groups.Add(item.gameObject.AddComponent<SortingGroup>());
                cards.Add(item);
                BuildCard(item, card, material, backs, highlights);
            }

            var view = table.gameObject.AddComponent<SevenLanternsTableView>();
            var serialized = new SerializedObject(view);
            AssignArray(serialized, "_cards", cards);
            AssignArray(serialized, "_groups", groups);
            AssignArray(serialized, "_backs", backs);
            AssignArray(serialized, "_highlights", highlights);
            AssignArray(serialized, "_slots", slots);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            view.Arrange();
            view.Render(Deal.Create(0, 1), null, null, default, 0, 0);
        }

        // 1 枚の札：表の紙、左上と右下（逆さ）のランクと小さい印、中央の大きい印、裏、選んだときの縁。
        private static void BuildCard(Transform item, Card card, Material material, List<Object> backs, List<Object> highlights)
        {
            var ink = Ink(card.Suit);
            Sprite(item, "Face", SevenLanternsArtGenerator.FaceFile, material, FaceOrder);
            var rank = SevenLanternsArtGenerator.RankFile(card.Rank);
            var suit = SevenLanternsArtGenerator.SuitFile(card.Suit);
            Mark(item, "RankTop", rank, material, ink, new Vector2(-CornerX, CornerRankY), CornerRankScale, false);
            Mark(item, "SuitTop", suit, material, ink, new Vector2(-CornerX, CornerSuitY), CornerSuitScale, false);
            Mark(item, "RankBottom", rank, material, ink, new Vector2(CornerX, -CornerRankY), CornerRankScale, true);
            Mark(item, "SuitBottom", suit, material, ink, new Vector2(CornerX, -CornerSuitY), CornerSuitScale, true);
            Mark(item, "Center", suit, material, ink, Vector2.zero, CenterSuitScale, false);

            backs.Add(Sprite(item, "Back", SevenLanternsArtGenerator.BackFile, material, BackOrder));
            var highlight = Sprite(item, "Highlight", SevenLanternsArtGenerator.HighlightFile, material, HighlightOrder);
            highlight.color = _highlightColor;
            highlight.enabled = false;
            highlights.Add(highlight);
        }

        // 赤は太陽（朱）と炎（紅）、黒は月（藍）と波（深緑）。同じ色の組でも見分けられるよう少しずらす。
        private static Color Ink(Suit suit) => suit switch
        {
            Suit.Sun => new Color(.84f, .32f, .08f),
            Suit.Flame => new Color(.76f, .1f, .16f),
            Suit.Moon => new Color(.14f, .18f, .4f),
            _ => new Color(.06f, .26f, .3f),
        };

        private static void Mark(Transform parent, string name, string file, Material material, Color color, Vector2 position, float scale, bool upsideDown)
        {
            var renderer = Sprite(parent, name, file, material, InkOrder);
            renderer.color = color;
            renderer.transform.localPosition = position;
            renderer.transform.localScale = new Vector3(scale, scale, 1);
            if (upsideDown) { renderer.transform.localRotation = Quaternion.Euler(0, 0, 180); }
        }

        private static Transform Group(Transform parent, string name)
        {
            var group = new GameObject(name).transform;
            group.SetParent(parent, false);
            return group;
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
