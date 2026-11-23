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
    /// シーンと Stage Prefab を生成する。設定アセット・Stage Prefab が既にあれば上書きせず、Prefab が設定と一致するかだけ確かめる。
    /// 実行：<c>unity run games/027-bullet-hell -- -executeMethod Game.Unity.Editor.LanternVeilSceneBuilder.Build</c>
    /// </summary>
    public static class LanternVeilSceneBuilder
    {
        private const string Root = "Assets/Game/Unity/";
        private const string Art = LanternVeilArtGenerator.ArtRoot;
        private const string ScenePath = "Assets/Scenes/LanternVeil.unity";
        private const string StagePath = Art + "LanternVeilStage.prefab";
        private const string ObjectPrefix = "LanternVeil";

        [MenuItem("Game/Rebuild Lantern Veil Scene")]
        public static void Build()
        {
            LanternVeilArtGenerator.GenerateIfMissing();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            // シーンを開くと未使用アセットがアンロードされるので、アセットの読み込みはこの後に行う。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = LoadOrCreate(Root + "LanternVeilSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<LanternVeilSettings>();
                value.Boss1 = Load<TextAsset>("Assets/Game/Patterns/boss1.txt");
                value.Boss2 = Load<TextAsset>("Assets/Game/Patterns/boss2.txt");
                return value;
            });
            settings.Validate();
            var panel = LoadOrCreate(Root + "UI/LanternVeilPanelSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PanelSettings>();
                value.themeStyleSheet = Load<ThemeStyleSheet>(Root + "UI/LanternVeilTheme.tss");
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

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(LoadStage(settings));
            instance.transform.position = Vector3.zero;
            var stage = instance.GetComponent<LanternVeilStage>();

            var camera = Object.FindAnyObjectByType<Camera>();
            camera.orthographic = true;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.05f, .02f, .05f);
            LanternVeilGame.Frame(camera);

            var game = new GameObject(ObjectPrefix + "Game");
            var hud = game.AddComponent<PanelRenderer>();
            hud.panelSettings = panel;
            hud.visualTreeAsset = Load<VisualTreeAsset>(Root + "UI/Hud.uxml");
            var component = game.AddComponent<LanternVeilGame>();
            var serialized = new SerializedObject(component);
            Assign(serialized, "_settings", settings);
            Assign(serialized, "_hudRenderer", hud);
            Assign(serialized, "_camera", camera);
            Assign(serialized, "_stage", stage);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<LanternVeilCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "LANTERN VEIL";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();

            var count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<LanternVeilStage>() != null) { count++; }
            }

            if (count != 1) { throw new InvalidOperationException($"Stage のインスタンスが {count} 個あります。"); }
            Debug.Log("[LanternVeilSceneBuilder] シーン生成完了");
        }

        // 配置の正は Stage Prefab。既にあれば調整を上書きせず、設定と一致するかだけ確かめる。
        private static GameObject LoadStage(LanternVeilSettings settings)
        {
            var rules = settings.Rules;
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(StagePath);
            if (existing != null)
            {
                var mismatch = existing.GetComponent<LanternVeilStage>().Mismatch(rules);
                if (mismatch != null) { throw new InvalidOperationException($"{StagePath} が設定と一致しません：{mismatch} Prefab を消して作り直してください。"); }
                return existing;
            }

            var material = LoadOrCreate(Art + "Unlit.mat", () => new Material(Shader.Find("Sprites/Default")));
            var bulletMaterial = LoadOrCreate(Art + "Bullets.mat", () => new Material(Shader.Find("Sprites/Default")) { mainTexture = Load<Texture2D>(Art + LanternVeilArtGenerator.BulletsFile) });
            var root = new GameObject("LanternVeilStage");
            try
            {
                var stage = root.AddComponent<LanternVeilStage>();
                var field = LanternVeilStage.FieldSize;
                var background = Sprite(root.transform, "Background", LanternVeilArtGenerator.FieldFile, material, 0);
                background.transform.localPosition = field / 2;

                var hpBar = Sprite(root.transform, "HpBar", LanternVeilArtGenerator.HpBarFile, material, LanternVeilStage.HpBarOrder);
                hpBar.transform.localPosition = new Vector3(0, field.y - .25f, 0);
                hpBar.transform.localScale = new Vector3(field.x, 1, 1);

                var boss = Sprite(root.transform, "Boss", LanternVeilArtGenerator.Boss1File, material, 5);
                boss.transform.localPosition = LanternVeilStage.Local(rules.BossHome);

                var shot = Sprite(root.transform, "Shot", LanternVeilArtGenerator.ShotFile, material, 8);
                shot.enabled = false;

                var player = Sprite(root.transform, "Player", LanternVeilArtGenerator.PlayerFile, material, 10);
                player.transform.localPosition = LanternVeilStage.Local(rules.PlayerStart);
                var hitbox = Sprite(root.transform, "Hitbox", LanternVeilArtGenerator.HitboxFile, material, 25);
                hitbox.transform.localPosition = player.transform.localPosition;
                hitbox.enabled = false;

                var bullets = new GameObject("Bullets");
                bullets.transform.SetParent(root.transform, false);
                bullets.AddComponent<MeshFilter>();
                var bulletRenderer = bullets.AddComponent<MeshRenderer>();
                bulletRenderer.sharedMaterial = bulletMaterial;
                bulletRenderer.sortingOrder = LanternVeilStage.BulletOrder;
                var layer = bullets.AddComponent<BulletLayer>();

                var ring = Sprite(root.transform, "BombRing", LanternVeilArtGenerator.BombRingFile, material, 22);
                ring.enabled = false;

                // 領域の外を覆う幕（左下を基準にした 1 単位の白い四角をカメラの背景色で塗って広げる）。
                var curtainColor = new Color(.05f, .02f, .05f);
                var curtains = LanternVeilStage.CurtainLayout();
                var curtainRenderers = new SpriteRenderer[curtains.Length];
                for (var i = 0; i < curtains.Length; i++)
                {
                    var (name, position, size) = curtains[i];
                    var curtain = Sprite(root.transform, name, LanternVeilArtGenerator.PixelFile, material, LanternVeilStage.CurtainOrder);
                    curtain.transform.localPosition = position;
                    curtain.transform.localScale = new Vector3(size.x, size.y, 1);
                    curtain.color = curtainColor;
                    curtainRenderers[i] = curtain;
                }

                var serialized = new SerializedObject(stage);
                Assign(serialized, "_background", background);
                Assign(serialized, "_player", player);
                Assign(serialized, "_hitbox", hitbox);
                Assign(serialized, "_boss", boss);
                Assign(serialized, "_hpBar", hpBar);
                Assign(serialized, "_bombRing", ring);
                Assign(serialized, "_shotTemplate", shot);
                Assign(serialized, "_bullets", layer);
                var sprites = serialized.FindProperty("_bossSprites");
                sprites.arraySize = VeilConfig.BossCount;
                sprites.GetArrayElementAtIndex(0).objectReferenceValue = LoadSprite(LanternVeilArtGenerator.Boss1File);
                sprites.GetArrayElementAtIndex(1).objectReferenceValue = LoadSprite(LanternVeilArtGenerator.Boss2File);
                var curtainArray = serialized.FindProperty("_curtains");
                curtainArray.arraySize = curtainRenderers.Length;
                for (var i = 0; i < curtainRenderers.Length; i++) { curtainArray.GetArrayElementAtIndex(i).objectReferenceValue = curtainRenderers[i]; }
                serialized.ApplyModifiedPropertiesWithoutUndo();

                var mismatch = stage.Mismatch(rules);
                if (mismatch != null) { throw new InvalidOperationException("作った Stage が設定と一致しません：" + mismatch); }
                return PrefabUtility.SaveAsPrefabAsset(root, StagePath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

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
