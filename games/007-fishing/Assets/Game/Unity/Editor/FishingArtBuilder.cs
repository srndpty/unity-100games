using System;
using System.IO;
using System.Linq;
using Game.Core;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace Game.Unity.Editor
{
    public static class FishingArtBuilder
    {
        public const string PrefabPath = "Assets/Game/Unity/Art/FishingStage.prefab";
        private const string Art = "Assets/Game/Unity/Art/";
        [MenuItem("Game/Reimport Fishing Background")]
        public static void ReimportBackground()
        {
            ImportSingle("Lakeside.jpg", new Vector2(.5f, .5f));
            var stage = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                stage.transform.Find("Lakeside").GetComponent<SpriteRenderer>().sprite = Load<Sprite>("Lakeside.jpg");
                PrefabUtility.SaveAsPrefabAsset(stage, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(stage); }
            FishingSceneBuilder.Build();
        }
        public static GameObject LoadStage(FishingConfig config)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab != null) { return prefab; }
            ImportSingle("Lakeside.jpg", new Vector2(.5f, .5f));
            ImportSingle("Angler.png", new Vector2(.5f, 0));
            ImportFish();
            CreateDisc();
            var material = new Material(Shader.Find("Sprites/Default"));
            AssetDatabase.CreateAsset(material, Art + "FishingLine.mat");
            var root = new GameObject("FishingStage");
            try
            {
                var view = root.AddComponent<FishingView>();
                var background = Sprite(root.transform, "Lakeside", Load<Sprite>("Lakeside.jpg"), -10);
                background.transform.localScale = new Vector3(28 / background.sprite.bounds.size.x, 16 / background.sprite.bounds.size.y, 1);
                var angler = Sprite(root.transform, "Angler", Load<Sprite>("Angler.png"), 5);
                angler.transform.localPosition = new Vector3(-8.2f, 2.12f);
                angler.transform.localScale = Vector3.one * (4.5f / angler.sprite.bounds.size.y);
                var hand = Anchor(root.transform, "RodHand", new Vector2(-7.65f, 4.92f));
                var tip = Anchor(root.transform, "RodTip", new Vector2(-4.7f, 6.3f));
                var sprites = AssetDatabase.LoadAllAssetsAtPath(Art + "FishAtlas.png").OfType<Sprite>().ToArray();
                var names = new[] { "Crucian", "Bass", "Carp" };
                var fishSprites = Array.ConvertAll(names, name => Array.Find(sprites, sprite => sprite.name == name));
                var fish = Sprite(root.transform, "Fish", fishSprites[0], 2);
                var floating = Sprite(root.transform, "Float", Load<Sprite>("Disc.png"), 4);
                floating.color = new Color(1, .43f, .15f); floating.transform.localScale = new Vector3(.16f, .33f, 1);
                var bait = Sprite(root.transform, "Bait", Load<Sprite>("Disc.png"), 4);
                bait.color = new Color(1, .84f, .43f); bait.transform.localScale = Vector3.one * .12f;
                var rod = Line(root.transform, "Rod", material, new Color(.30f, .19f, .10f), .045f, 6);
                var line = Line(root.transform, "Line", material, new Color(.92f, .95f, .84f, .85f), .022f, 3);
                var underwater = Line(root.transform, "UnderwaterLine", material, new Color(.89f, .96f, .87f, .65f), .018f, 3);
                var ripple = Line(root.transform, "BiteRipple", material, new Color(.98f, .99f, .86f, .8f), .025f, 3);
                var serialized = new SerializedObject(view);
                Assign(serialized, "_fish", fish); Assign(serialized, "_angler", angler.transform);
                Assign(serialized, "_rodHand", hand); Assign(serialized, "_rodTip", tip);
                Assign(serialized, "_float", floating); Assign(serialized, "_bait", bait);
                Assign(serialized, "_rod", rod); Assign(serialized, "_line", line);
                Assign(serialized, "_underLine", underwater); Assign(serialized, "_ripple", ripple);
                serialized.FindProperty("_surfaceY").floatValue = 1.43f;
                serialized.FindProperty("_shoreX").floatValue = -5.2f;
                var array = serialized.FindProperty("_fishSprites"); array.arraySize = 3;
                for (var i = 0; i < 3; i++) { array.GetArrayElementAtIndex(i).objectReferenceValue = fishSprites[i]; }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                view.Render(null, FishKind.Crucian, config, false);
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        private static void Assign(SerializedObject target, string name, UnityEngine.Object value)
            => target.FindProperty(name).objectReferenceValue = value;
        private static T Load<T>(string name) where T : UnityEngine.Object
            => AssetDatabase.LoadAssetAtPath<T>(Art + name) ?? throw new InvalidOperationException("素材がありません: " + name);
        private static void ImportSingle(string file, Vector2 pivot)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(Art + file);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100;
            var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
            settings.spritePivot = pivot; settings.spriteAlignment = (int)SpriteAlignment.Custom;
            importer.SetTextureSettings(settings);
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.alphaIsTransparency = true; importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }
        private static void ImportFish()
        {
            var path = Art + "FishAtlas.png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = 100; importer.alphaIsTransparency = true; importer.isReadable = true;
            importer.mipmapEnabled = false; importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048; importer.SaveAndReimport();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            var names = new[] { "Crucian", "Bass", "Carp" };
            var factory = new SpriteDataProviderFactories(); factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer); provider.InitSpriteEditorDataProvider();
            var rects = new SpriteRect[3];
            for (var i = 0; i < 3; i++)
            {
                var bottom = texture.height * (2 - i) / 3; var top = texture.height * (3 - i) / 3;
                var minX = texture.width; var maxX = 0; var minY = top; var maxY = bottom;
                for (var y = bottom; y < top; y++)
                {
                    for (var x = 0; x < texture.width; x++)
                    {
                        if (texture.GetPixel(x, y).a < .04f) { continue; }
                        minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                    }
                }
                rects[i] = new SpriteRect { name = names[i], rect = new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1), pivot = new Vector2(.02f, .43f), alignment = SpriteAlignment.Custom, spriteID = new GUID("007f150000000000000000000000000" + i) };
            }
            provider.SetSpriteRects(rects);
            provider.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(Array.ConvertAll(rects, rect => new SpriteNameFileIdPair(rect.name, rect.spriteID)));
            provider.Apply(); importer.isReadable = false; importer.SaveAndReimport();
        }
        private static void CreateDisc()
        {
            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            for (var y = 0; y < 64; y++)
            { for (var x = 0; x < 64; x++) { texture.SetPixel(x, y, new Vector2(x - 31.5f, y - 31.5f).sqrMagnitude <= 31 * 31 ? Color.white : Color.clear); } }
            texture.Apply(); File.WriteAllBytes(Art + "Disc.png", texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(Art + "Disc.png"); ImportSingle("Disc.png", new Vector2(.5f, .5f));
            var importer = (TextureImporter)AssetImporter.GetAtPath(Art + "Disc.png"); importer.spritePixelsPerUnit = 64; importer.SaveAndReimport();
        }
        private static SpriteRenderer Sprite(Transform parent, string name, Sprite sprite, int order)
        {
            var item = new GameObject(name); item.transform.SetParent(parent, false);
            var renderer = item.AddComponent<SpriteRenderer>(); renderer.sprite = sprite; renderer.sortingOrder = order; return renderer;
        }
        private static Transform Anchor(Transform parent, string name, Vector2 position)
        { var item = new GameObject(name); item.transform.SetParent(parent, false); item.transform.localPosition = position; return item.transform; }
        private static LineRenderer Line(Transform parent, string name, Material material, Color color, float width, int order)
        {
            var item = new GameObject(name); item.transform.SetParent(parent, false);
            var line = item.AddComponent<LineRenderer>(); line.sharedMaterial = material; line.startColor = line.endColor = color;
            line.startWidth = line.endWidth = width; line.sortingOrder = order; line.numCapVertices = 4; line.useWorldSpace = true; return line;
        }
    }
}
