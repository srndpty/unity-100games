using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>
    /// 座標から決まる模様のテクスチャと URP のマテリアルを作る。既にあるものは上書きしない（調整を残す）。
    /// </summary>
    public static class HalcyonArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";

        public const string Ground = "Ground";
        public const string Runway = "Runway";
        public const string Hull = "Hull";
        public const string Bandit = "Bandit";
        public const string Canopy = "Canopy";
        public const string Engine = "Engine";
        public const string Missile = "Missile";
        public const string Smoke = "Smoke";
        public const string Explosion = "Explosion";
        public const string Tower = "Tower";
        public const string Beacon = "Beacon";
        public const string Gear = "Gear";

        /// <summary>地面の模様 1 枚の大きさ（m）。区画は 1 枚に 2×2 で 200 m。</summary>
        public const float GroundTile = 400f;

        /// <summary>滑走路の模様 1 枚の長さ（m）。</summary>
        public const float RunwayTile = 60f;

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtRoot);
            var created = new List<string>();
            Save(created, Ground, 128, 128, GroundPattern);
            Save(created, Runway, 32, 64, RunwayPattern);
            AssetDatabase.Refresh();
            foreach (var path in created) { Import(path); }

            MaterialOf(Ground, Color.white, Color.black, .05f);
            MaterialOf(Runway, Color.white, Color.black, .1f);
            MaterialOf(Hull, new Color(.62f, .7f, .78f), Color.black, .45f);
            MaterialOf(Bandit, new Color(.55f, .16f, .14f), Color.black, .4f);
            MaterialOf(Canopy, new Color(.08f, .12f, .18f), Color.black, .9f);
            MaterialOf(Engine, new Color(1f, .55f, .2f), new Color(3f, 1.2f, .3f), .2f);
            MaterialOf(Missile, new Color(.92f, .92f, .9f), Color.black, .5f);
            MaterialOf(Smoke, new Color(.86f, .87f, .9f), new Color(.25f, .25f, .27f), 0f);
            MaterialOf(Explosion, new Color(1f, .6f, .15f), new Color(4f, 1.6f, .3f), 0f);
            MaterialOf(Tower, new Color(.55f, .55f, .58f), Color.black, .1f);
            MaterialOf(Beacon, new Color(1f, .3f, .2f), new Color(2f, .4f, .2f), .2f);
            MaterialOf(Gear, new Color(.2f, .2f, .22f), Color.black, .3f);
            AssetDatabase.SaveAssets();
        }

        public static Material Material(string name) =>
            AssetDatabase.LoadAssetAtPath<Material>(ArtRoot + name + ".mat") ?? throw new InvalidOperationException("マテリアルがありません: " + name);

        private static void MaterialOf(string name, Color color, Color emission, float smoothness)
        {
            var path = ArtRoot + name + ".mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) { return; }
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtRoot + name + ".png");
            if (texture != null) { material.SetTexture("_BaseMap", texture); }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            if (emission != Color.black)
            {
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                material.SetColor("_EmissionColor", emission);
            }

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
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 8;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        /// <summary>畑の区画：近い 4 色の区画と、区画の境の細い道。遠くでちらつかないよう色の差は小さくする。</summary>
        private static Color32 GroundPattern(int x, int y)
        {
            var field = ((x / 64) + ((y / 64) * 3)) % 4;
            Color32 color = field switch
            {
                0 => new Color32(98, 126, 74, 255),
                1 => new Color32(110, 134, 78, 255),
                2 => new Color32(92, 118, 70, 255),
                _ => new Color32(124, 126, 84, 255),
            };
            if (x % 64 == 0 || y % 64 == 0) { color = new Color32(140, 136, 108, 255); }
            return Shade(color, Speckle(x, y));
        }

        /// <summary>滑走路：舗装と、中心の破線、両端の白線。</summary>
        private static Color32 RunwayPattern(int x, int y)
        {
            var color = Shade(new Color32(64, 66, 70, 255), Speckle(x, y) * 2);
            if (x <= 1 || x >= 30) { color = new Color32(230, 230, 230, 255); }
            if ((x == 15 || x == 16) && y % 32 < 18) { color = new Color32(235, 235, 235, 255); }
            return color;
        }

        private static int Speckle(int x, int y) => ((x * 73) + (y * 151) + (x * y * 7)) % 7;

        private static Color32 Shade(Color32 color, int amount) =>
            new((byte)Math.Clamp(color.r + amount, 0, 255), (byte)Math.Clamp(color.g + amount, 0, 255), (byte)Math.Clamp(color.b + amount, 0, 255), 255);
    }
}
