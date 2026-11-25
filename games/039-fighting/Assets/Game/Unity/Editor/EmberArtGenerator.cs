using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>姿勢ごとのドット絵。既にあるファイルは上書きしない。</summary>
    public static class EmberArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";
        public const int PixelsPerUnit = 1;

        public static readonly Color32 Night = new(28, 18, 28, 255);

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtRoot);
            var created = new List<(string Path, Vector2 Pivot)>();
            var feet = new Vector2(0.5f, 0f);
            var center = new Vector2(0.5f, 0.5f);
            Save(created, "Backdrop.png", 480, 270, center, Backdrop);
            Save(created, "Ground.png", 480, 24, new Vector2(0.5f, 1f), Ground);
            Save(created, "Shot.png", 16, 16, center, Shot);
            for (var pose = 0; pose < EmberStage.PoseCount; pose++)
            {
                var index = pose;
                Save(created, "Cinder" + pose + ".png", 40, 76, feet, canvas => Fighter(canvas, index, ember: true));
                Save(created, "Kiln" + pose + ".png", 40, 76, feet, canvas => Fighter(canvas, index, ember: false));
            }

            AssetDatabase.Refresh();
            foreach (var (path, pivot) in created) { Import(path, pivot); }
            ReimportAll();
        }

        /// <summary>既存の絵も 1 枚のスプライトとして読み直す。自動分割で体の一部だけになるのを防ぐ。</summary>
        public static void ReimportAll()
        {
            var feet = new Vector2(0.5f, 0f);
            var center = new Vector2(0.5f, 0.5f);
            Import(ArtRoot + "Backdrop.png", center);
            Import(ArtRoot + "Ground.png", new Vector2(0.5f, 1f));
            Import(ArtRoot + "Shot.png", center);
            for (var pose = 0; pose < EmberStage.PoseCount; pose++)
            {
                Import(ArtRoot + "Cinder" + pose + ".png", feet);
                Import(ArtRoot + "Kiln" + pose + ".png", feet);
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

        private static void Import(string path, Vector2 pivot)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.filterMode = FilterMode.Point;
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

        private static void Fighter(Canvas canvas, int pose, bool ember)
        {
            var cloth = ember ? new Color32(190, 60, 30, 255) : new Color32(50, 80, 150, 255);
            var skin = new Color32(230, 180, 140, 255);
            var ink = new Color32(20, 12, 16, 255);
            if (pose == 6)
            {
                canvas.Rect(2, 8, 36, 12, cloth);
                canvas.Rect(28, 14, 10, 10, skin);
                return;
            }

            var body = pose == 2 || pose == 7 ? 20 : 34;
            var head = body + 12;
            canvas.Rect(12, 0, 6, pose == 3 ? 10 : 18, ink);
            canvas.Rect(22, 0, 6, pose == 1 ? 10 : 18, ink);
            canvas.Rect(8, 14, 24, body, cloth);
            canvas.Rect(12, head, 16, 14, skin);
            if (pose == 4) { canvas.Rect(26, 24, 12, 6, skin); }
            if (pose == 7) { canvas.Rect(10, 24, 20, 6, skin); }
            if (pose == 5) { canvas.Rect(8, 30, 8, 6, skin); }
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

            private void Dot(int x, int y, Color32 color)
            {
                if (x < 0 || y < 0 || x >= Width || y >= Height) { return; }
                Pixels[(y * Width) + x] = color;
            }
        }
    }
}
