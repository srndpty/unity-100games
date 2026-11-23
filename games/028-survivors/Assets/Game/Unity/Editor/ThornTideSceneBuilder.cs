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
    /// シーン・Stage Prefab・設定と敵・武器の定義アセットを生成する。既にあるアセットと Prefab は上書きしない
    /// （コードの既定値を変えたらアセットも直す。024 の知見）。
    /// 実行：<c>unity run games/028-survivors -- -executeMethod Game.Unity.Editor.ThornTideSceneBuilder.Build</c>
    /// </summary>
    public static class ThornTideSceneBuilder
    {
        private const string Root = "Assets/Game/Unity/";
        private const string Art = ThornTideArtGenerator.ArtRoot;
        private const string Data = Root + "Data/";
        private const string ScenePath = "Assets/Scenes/ThornTide.unity";
        private const string StagePath = Art + "ThornTideStage.prefab";
        private const string ObjectPrefix = "ThornTide";

        /// <summary>既定の敵（<see cref="TideConfig.Enemies"/> の並び）の表示名と絵の区画。</summary>
        private static readonly (string Name, int Cell)[] _enemyLooks = { ("影ダニ", 0), ("羽虫", 1), ("殻虫", 2), ("茨の番人", 0) };

        /// <summary>既定の武器（<see cref="TideConfig.Weapons"/> の並び）の表示名と説明。</summary>
        private static readonly (string Name, string Summary)[] _weaponLooks =
        {
            ("光の矢", "近い敵へ光の矢を放つ。\n強化で矢の数と貫通が増える。"),
            ("舞う葉刃", "自分の周りを葉の刃が回り、\n触れた敵を切る。"),
            ("棘の脈動", "一定の間隔で、\n周りの敵すべてに棘を伸ばす。"),
            ("蔦の鞭", "向いている左右へ蔦を振るう。\n強化で両側を打つ。"),
            ("雷の連鎖", "近い敵に落ちた雷が、\n次々と近くの敵へ飛び移る。"),
        };

        [MenuItem("Game/Rebuild Thorn Tide Scene")]
        public static void Build()
        {
            ThornTideArtGenerator.GenerateIfMissing();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var error = AssetDatabase.MoveAsset("Assets/Scenes/SampleScene.unity", ScenePath);
                if (!string.IsNullOrEmpty(error)) { throw new InvalidOperationException(error); }
            }

            // シーンを開くと未使用アセットがアンロードされるので、アセットの読み込みはこの後に行う。
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var settings = LoadSettings();
            var rules = settings.BuildRules();
            var panel = LoadOrCreate(Root + "UI/ThornTidePanelSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<PanelSettings>();
                value.themeStyleSheet = Load<ThemeStyleSheet>(Root + "UI/ThornTideTheme.tss");
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

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(LoadStage());
            instance.transform.position = Vector3.zero;
            var stage = instance.GetComponent<ThornTideStage>();

            var camera = Object.FindAnyObjectByType<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = rules.ViewHeightCells / 2f;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.06f, .1f, .06f);

            var game = new GameObject(ObjectPrefix + "Game");
            var hud = game.AddComponent<PanelRenderer>();
            hud.panelSettings = panel;
            hud.visualTreeAsset = Load<VisualTreeAsset>(Root + "UI/Hud.uxml");
            var component = game.AddComponent<ThornTideGame>();
            var serialized = new SerializedObject(component);
            Assign(serialized, "_settings", settings);
            Assign(serialized, "_hudRenderer", hud);
            Assign(serialized, "_camera", camera);
            Assign(serialized, "_stage", stage);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            game.AddComponent<ThornTideCaptureScenario>();

            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "100games";
            PlayerSettings.productName = "THORN TIDE";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = false;
            AssetDatabase.SaveAssets();

            var count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<ThornTideStage>() != null) { count++; }
            }

            if (count != 1) { throw new InvalidOperationException($"Stage のインスタンスが {count} 個あります。"); }
            Debug.Log("[ThornTideSceneBuilder] シーン生成完了");
        }

        /// <summary>設定と定義アセット。なければコードの既定値（<see cref="TideConfig"/>）から作る。</summary>
        private static ThornTideSettings LoadSettings()
        {
            var defaults = new TideConfig();
            Folder(Data.TrimEnd('/'));
            var enemies = new EnemyDefinition[defaults.Enemies.Length];
            for (var i = 0; i < enemies.Length; i++)
            {
                var def = defaults.Enemies[i];
                enemies[i] = LoadOrCreate($"{Data}Enemy-{def.Id}.asset", () =>
                {
                    var value = ScriptableObject.CreateInstance<EnemyDefinition>();
                    value.DisplayName = _enemyLooks[i].Name;
                    value.ArtCell = _enemyLooks[i].Cell;
                    value.Data = def.Copy();
                    return value;
                });
            }

            var weapons = new WeaponDefinition[defaults.Weapons.Length];
            for (var i = 0; i < weapons.Length; i++)
            {
                var def = defaults.Weapons[i];
                weapons[i] = LoadOrCreate($"{Data}Weapon-{def.Id}.asset", () =>
                {
                    var value = ScriptableObject.CreateInstance<WeaponDefinition>();
                    value.DisplayName = _weaponLooks[i].Name;
                    value.Summary = _weaponLooks[i].Summary;
                    value.Data = def.Copy();
                    return value;
                });
            }

            return LoadOrCreate(Root + "ThornTideSettings.asset", () =>
            {
                var value = ScriptableObject.CreateInstance<ThornTideSettings>();
                value.Rules = defaults.Copy();

                // 敵・武器の数値は定義アセットが正。設定の側には持たない（Inspector で二重に見えないように空にする）。
                value.Rules.Enemies = Array.Empty<EnemyDef>();
                value.Rules.Weapons = Array.Empty<WeaponDef>();
                value.Enemies = enemies;
                value.Weapons = weapons;
                return value;
            });
        }

        // 配置の正は Stage Prefab。既にあれば調整を上書きせず、描画物がそろっているかだけ確かめる。
        private static GameObject LoadStage()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(StagePath);
            if (existing != null)
            {
                var mismatch = existing.GetComponent<ThornTideStage>().Mismatch();
                if (mismatch != null) { throw new InvalidOperationException($"{StagePath} が正しくありません：{mismatch} Prefab を消して作り直してください。"); }
                return existing;
            }

            var material = LoadOrCreate(Art + "Unlit.mat", () => new Material(Shader.Find("Sprites/Default")));
            var swarmMaterial = LoadOrCreate(Art + "Swarm.mat", () => new Material(Shader.Find("Sprites/Default")) { mainTexture = Load<Texture2D>(Art + ThornTideArtGenerator.SwarmFile) });
            var root = new GameObject("ThornTideStage");
            try
            {
                var stage = root.AddComponent<ThornTideStage>();
                var floor = Sprite(root.transform, "Floor", ThornTideArtGenerator.FloorFile, material, -10);
                floor.drawMode = SpriteDrawMode.Tiled;
                floor.size = new Vector2(40, 34);
                var pulse = Sprite(root.transform, "Pulse", ThornTideArtGenerator.PulseFile, material, 2);
                pulse.enabled = false;

                var swarmObject = new GameObject("Swarm");
                swarmObject.transform.SetParent(root.transform, false);
                swarmObject.AddComponent<MeshFilter>();
                var swarmRenderer = swarmObject.AddComponent<MeshRenderer>();
                swarmRenderer.sharedMaterial = swarmMaterial;
                swarmRenderer.sortingOrder = 5;
                var swarm = swarmObject.AddComponent<QuadLayer>();

                var boss = Sprite(root.transform, "Boss", ThornTideArtGenerator.BossFile, material, 7);
                boss.enabled = false;
                var player = Sprite(root.transform, "Player", ThornTideArtGenerator.PlayerFile, material, 10);
                var lashRight = Sprite(root.transform, "LashA", ThornTideArtGenerator.LashFile, material, 11);
                lashRight.enabled = false;
                var lashLeft = Sprite(root.transform, "LashB", ThornTideArtGenerator.LashFile, material, 11);
                lashLeft.enabled = false;
                var blade = Sprite(root.transform, "Blade", ThornTideArtGenerator.BladeFile, material, 12);
                blade.transform.localPosition = new Vector3(1.6f, 0, 0);

                var arcObject = new GameObject("Arc");
                arcObject.transform.SetParent(root.transform, false);
                var arc = arcObject.AddComponent<LineRenderer>();
                arc.sharedMaterial = material;
                arc.useWorldSpace = false;
                arc.widthMultiplier = .14f;
                arc.numCapVertices = 2;
                arc.sortingOrder = 13;
                arc.positionCount = 0;
                arc.enabled = false;

                var serialized = new SerializedObject(stage);
                Assign(serialized, "_floor", floor);
                Assign(serialized, "_player", player);
                Assign(serialized, "_boss", boss);
                Assign(serialized, "_bladeTemplate", blade);
                Assign(serialized, "_pulse", pulse);
                Assign(serialized, "_arc", arc);
                Assign(serialized, "_swarm", swarm);
                var lashes = serialized.FindProperty("_lashes");
                lashes.arraySize = 2;
                lashes.GetArrayElementAtIndex(0).objectReferenceValue = lashRight;
                lashes.GetArrayElementAtIndex(1).objectReferenceValue = lashLeft;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                return PrefabUtility.SaveAsPrefabAsset(root, StagePath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
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

        private static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) { return; }
            var slash = path.LastIndexOf('/');
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
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
