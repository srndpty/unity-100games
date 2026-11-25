using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>舞台と技エフェクト。既にあるファイルは上書きしない。</summary>
    public static class EmberArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";
        public static readonly string[] Poses = { "Guard", "Guard", "Crouch", "Crouch", "Strike", "Recoil", "Down", "Crouch" };

        public static readonly Color32 Night = new(28, 18, 28, 255);

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtRoot);
            var created = new List<(string Path, Vector2 Pivot)>();
            var center = new Vector2(0.5f, 0.5f);
            Save(created, "Backdrop.png", 480, 270, center, Backdrop);
            Save(created, "Ground.png", 480, 24, new Vector2(0.5f, 1f), Ground);
            Save(created, "Shot.png", 16, 16, center, Shot);
            Save(created, "Flare.png", 48, 48, center, Flare);

            AssetDatabase.Refresh();
            foreach (var (path, pivot) in created) { Import(path, pivot); }
            var feet = new Vector2(0.5f, 0f);
            foreach (var character in new[] { "Judoka", "Veteran" })
            {
                foreach (var pose in new HashSet<string>(Poses))
                {
                    // ダウンの原画は縮小して書き出しているため、ワールド上で他の姿と体格を揃える。
                    var pixelsPerUnit = pose == "Down" ? 2 : 4;
                    Import(ArtRoot + character + pose + ".png", feet, pixelsPerUnit);
                }
            }
        }

        private static void Save(List<(string, Vector2)> created, string file, int width, int height, Vector2 pivot, System.Action<Canvas> paint)
        {
            var path = ArtRoot + file;
            if (File.Exists(path)) { return; }
            var canvas = new Canvas(width, height);
            paint(canvas);
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(canvas.Pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            created.Add((path, pivot));
        }

        private static void Import(string path, Vector2 pivot, int pixelsPerUnit = 1)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.filterMode = pixelsPerUnit == 1 ? FilterMode.Point : FilterMode.Bilinear;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = pivot;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        private static void Backdrop(Canvas canvas)
        {
            for (var y = 0; y < canvas.Height; y++)
            {
                var t = y / (float)canvas.Height;
                var color = new Color32((byte)(40 + (20 * t)), (byte)(18 + (28 * t)), (byte)(36 + (10 * t)), 255);
                canvas.Rect(0, y, canvas.Width, 1, color);
            }
        }

        private static void Ground(Canvas canvas)
        {
            canvas.Rect(0, 0, canvas.Width, canvas.Height, new Color32(48, 32, 36, 255));
            canvas.Rect(0, canvas.Height - 4, canvas.Width, 4, new Color32(180, 90, 50, 255));
        }

        private static void Shot(Canvas canvas) => canvas.Ellipse(8, 8, 7, 7, new Color32(255, 150, 40, 255));

        private static void Flare(Canvas canvas)
        {
            for (var y = 0; y < canvas.Height; y++)
            {
                for (var x = 0; x < canvas.Width; x++)
                {
                    var dx = (x - 23.5f) / 24f;
                    var dy = (y - 23.5f) / 24f;
                    var radius = Mathf.Sqrt(dx * dx + dy * dy);
                    var angle = Mathf.Atan2(dy, dx);
                    var ray = Mathf.Pow(Mathf.Abs(Mathf.Cos(3f * angle)), 10f);
                    var alpha = Mathf.Clamp01(1f - radius / (0.24f + 0.76f * ray));
                    canvas.Dot(x, y, new Color32(255, 255, 255, (byte)(255f * Mathf.Sqrt(alpha))));
                }
            }
        }

        private sealed class Canvas
        {
            public Canvas(int width, int height)
            {
                Width = width;
                Height = height;
                Pixels = new Color32[width * height];
            }

            public int Width { get; }

            public int Height { get; }

            public Color32[] Pixels { get; }

            public void Rect(int x, int y, int width, int height, Color32 color)
            {
                for (var row = y; row < y + height; row++)
                {
                    for (var col = x; col < x + width; col++) { Dot(col, row, color); }
                }
            }

            public void Ellipse(int cx, int cy, int rx, int ry, Color32 color)
            {
                for (var y = cy - ry; y <= cy + ry; y++)
                {
                    for (var x = cx - rx; x <= cx + rx; x++)
                    {
                        var nx = (x - cx) / (float)rx;
                        var ny = (y - cy) / (float)ry;
                        if ((nx * nx) + (ny * ny) <= 1f) { Dot(x, y, color); }
                    }
                }
            }

            public void Dot(int x, int y, Color32 color)
            {
                if (x < 0 || y < 0 || x >= Width || y >= Height) { return; }
                Pixels[(y * Width) + x] = color;
            }
        }
    }
}
