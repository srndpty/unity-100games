using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>
    /// 道・障害物の模様のテクスチャ（PNG）と URP Lit のマテリアルを作る。既にあるファイル・マテリアルとインポート設定は上書きしない。
    /// 乱数は使わず、座標から決まる模様で毎回同じ絵にする。
    /// </summary>
    public static class CinderLanesArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";

        public const string Road = "Road";
        public const string Ash = "Ash";
        public const string Wall = "Wall";
        public const string Hurdle = "Hurdle";
        public const string Beam = "Beam";
        public const string Post = "Post";
        public const string Lamp = "Lamp";

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtRoot);
            var created = new List<string>();
            Save(created, Road, 96, 128, RoadPattern);
            Save(created, Ash, 32, 32, AshPattern);
            Save(created, Wall, 32, 32, WallPattern);
            Save(created, Hurdle, 32, 8, HurdlePattern);
            Save(created, Beam, 32, 32, BeamPattern);
            AssetDatabase.Refresh();
            foreach (var path in created) { Import(path); }

            MaterialOf(Road, Color.white, Color.black);
            MaterialOf(Ash, Color.white, Color.black);
            MaterialOf(Wall, Color.white, Color.black);
            MaterialOf(Hurdle, Color.white, new Color(.25f, .08f, 0));
            MaterialOf(Beam, Color.white, Color.black);
            MaterialOf(Post, new Color(.24f, .22f, .22f), Color.black);
            MaterialOf(Lamp, new Color(1f, .6f, .25f), new Color(2.4f, 1.1f, .3f));
            foreach (var name in new[] { Wall, Hurdle, Beam }) { PassedMaterialOf(name); }
            AssetDatabase.SaveAssets();
        }

        public static Material Material(string name) =>
            AssetDatabase.LoadAssetAtPath<Material>(ArtRoot + name + ".mat") ?? throw new InvalidOperationException("マテリアルがありません: " + name);

        private static void MaterialOf(string name, Color color, Color emission)
        {
            var path = ArtRoot + name + ".mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) { return; }
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtRoot + name + ".png");
            if (texture != null) { material.SetTexture("_BaseMap", texture); }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", .15f);
            if (emission != Color.black)
            {
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                material.SetColor("_EmissionColor", emission);
            }

            AssetDatabase.CreateAsset(material, path);
        }

        /// <summary>通り過ぎた障害物の半透明のマテリアル（元の模様を薄く残す）。</summary>
        public static string Passed(string name) => name + "Passed";

        private static void PassedMaterialOf(string name)
        {
            var path = ArtRoot + Passed(name) + ".mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) { return; }
            var material = new Material(Material(name));
            material.SetFloat("_Surface", 1);
            material.SetFloat("_Blend", 0);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.SetShaderPassEnabled("ShadowCaster", false);
            var color = material.GetColor("_BaseColor");
            material.SetColor("_BaseColor", new Color(color.r, color.g, color.b, .12f));
            AssetDatabase.CreateAsset(material, path);
        }

        private static void Save(List<string> created, string name, int width, int height, Func<int, int, Color32> paint)
        {
            var path = ArtRoot + name + ".png";
            if (File.Exists(path)) { return; }
            var pixels = new Color32[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++) { pixels[(y * width) + x] = paint(x, y); }
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            created.Add(path);
        }

        private static void Import(string path)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        // 座標から決まるまだら模様（0〜6）。
        private static int Speckle(int x, int y) => ((x * 73) + (y * 151) + (x * y * 7)) % 7;

        private static Color32 Shade(Color32 color, int amount) =>
            new((byte)Math.Clamp(color.r + amount, 0, 255), (byte)Math.Clamp(color.g + amount, 0, 255), (byte)Math.Clamp(color.b + amount, 0, 255), 255);

        // 道（1 区画 = 3 レーン × 8 m）：焦げた石畳、レーンの境に燃えさしの破線、両端に縁石。
        private static Color32 RoadPattern(int x, int y)
        {
            var stone = new Color32(46, 38, 36, 255);
            if (x < 2 || x >= 94) { return new Color32(120, 60, 34, 255); }
            if ((x == 31 || x == 32 || x == 63 || x == 64) && y % 32 < 18) { return new Color32(255, 140, 50, 255); }
            if (y % 16 == 0 || (x + ((y / 16) % 2 * 8)) % 16 == 0) { return Shade(stone, -14); }
            return Shade(stone, Speckle(x, y) * 3);
        }

        // 道の外：灰の積もった地面。
        private static Color32 AshPattern(int x, int y) => Shade(new Color32(54, 50, 52, 255), (Speckle(x, y) * 4) - 8);

        // 壁：黒ずんだ赤煉瓦。
        private static Color32 WallPattern(int x, int y)
        {
            var row = y / 8;
            var joint = y % 8 == 0 || (x + (row % 2 * 8)) % 16 == 0;
            return joint ? new Color32(30, 20, 20, 255) : Shade(new Color32(120, 46, 34, 255), (Speckle(x, y) * 3) - 9);
        }

        // 低い柵：橙と黒の縞。
        private static Color32 HurdlePattern(int x, int y) =>
            ((x / 4) + (y / 4)) % 2 == 0 ? new Color32(255, 140, 40, 255) : new Color32(36, 26, 20, 255);

        // 頭上の梁：黄と黒の斜めの縞。
        private static Color32 BeamPattern(int x, int y) =>
            ((x + y) / 6) % 2 == 0 ? new Color32(250, 210, 60, 255) : new Color32(30, 28, 24, 255);
    }
}
