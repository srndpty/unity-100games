using System;
using System.Collections.Generic;
using System.IO;
using Game.Core;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>
    /// 図形を組み合わせてドット絵の素材 PNG を描き出す。既にあるファイルとそのインポート設定は上書きしない。
    /// ホールの地面はコースのデータ（1 m のマスの地面の種類）から 1 m = 4 ピクセルで描く（左下の角が基点）。
    /// 乱数は使わず、模様は座標の整数のハッシュで決める（毎回同じ絵）。
    /// </summary>
    public static class BreezewayArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";

        /// <summary>地面の絵の 1 m のピクセル数。</summary>
        public const int GroundPixelsPerMeter = 4;

        /// <summary>小物の絵の 1 単位（1 m）のピクセル数。</summary>
        public const int PixelsPerUnit = 16;

        public const string TreeFile = "Tree.png";
        public const string TeeFile = "Tee.png";
        public const string CupFile = "Cup.png";
        public const string FlagFile = "Flag.png";
        public const string BallFile = "Ball.png";
        public const string ShadowFile = "Shadow.png";
        public const string DotFile = "Dot.png";
        public const string MarkFile = "Mark.png";
        public const string ArrowFile = "Arrow.png";

        private static readonly Color32 _outline = new(16, 30, 16, 255);

        public static string GroundFile(int holeNumber) => $"Hole{holeNumber}.png";

        public static void GenerateIfMissing(Course course)
        {
            if (course == null) { throw new ArgumentNullException(nameof(course)); }
            Directory.CreateDirectory(ArtRoot);
            var created = new List<(string Path, Vector2 Pivot, int PixelsPerUnit)>();
            var center = new Vector2(.5f, .5f);
            foreach (var hole in course.Holes)
            {
                Save(created, GroundFile(hole.Number), hole.Width * GroundPixelsPerMeter, hole.Height * GroundPixelsPerMeter, Vector2.zero, GroundPixelsPerMeter, c => Ground(c, hole));
            }

            Save(created, TreeFile, 32, 32, center, PixelsPerUnit, Tree);
            Save(created, TeeFile, 16, 16, center, PixelsPerUnit, Tee);
            Save(created, CupFile, 16, 16, center, PixelsPerUnit, Cup);
            Save(created, FlagFile, 20, 48, new Vector2(2.5f / 20, 0), PixelsPerUnit, Flag);
            Save(created, BallFile, 12, 12, center, PixelsPerUnit, Ball);
            Save(created, ShadowFile, 12, 12, center, PixelsPerUnit, Shadow);
            Save(created, DotFile, 8, 8, center, PixelsPerUnit, Dot);
            Save(created, MarkFile, 16, 16, center, PixelsPerUnit, Mark);
            Save(created, ArrowFile, 16, 16, center, PixelsPerUnit, Arrow);
            AssetDatabase.Refresh();
            foreach (var (path, pivot, pixels) in created) { Import(path, pivot, pixels); }
        }

        private static void Save(List<(string, Vector2, int)> created, string file, int width, int height, Vector2 pivot, int pixelsPerUnit, Action<Canvas> paint)
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
            created.Add((path, pivot, pixelsPerUnit));
        }

        private static void Import(string path, Vector2 pivot, int pixelsPerUnit)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = pivot;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 4096;
            importer.SaveAndReimport();
        }

        private static uint Hash(int x, int y) => unchecked(((uint)x * 73856093u) ^ ((uint)y * 19349663u) ^ 0x9E3779B9u) * 2654435761u;

        /// <summary>地面：種類ごとの色と模様（フェアウェイの刈り目、グリーンの細い刈り目、ラフの草むら、砂、水面のさざ波）。種類の境目は暗く縁取る。</summary>
        private static void Ground(Canvas c, Hole hole)
        {
            const int p = GroundPixelsPerMeter;
            for (var y = 0; y < c.Height; y++)
            {
                for (var x = 0; x < c.Width; x++)
                {
                    var cx = x / p;
                    var cy = y / p;
                    var surface = hole.SurfaceOfCell(cx, cy);
                    var h = Hash(x, y);
                    Color32 color = surface switch
                    {
                        Surface.Rough => h % 9 == 0 ? new Color32(40, 92, 38, 255) : h % 13 == 0 ? new Color32(70, 132, 58, 255) : new Color32(54, 112, 48, 255),
                        Surface.Fairway => (cx / 6) % 2 == 0 ? new Color32(102, 176, 74, 255) : new Color32(92, 164, 68, 255),
                        Surface.Tee => (cx + cy) % 2 == 0 ? new Color32(116, 190, 88, 255) : new Color32(108, 182, 82, 255),
                        Surface.Green => (cx / 2) % 2 == 0 ? new Color32(120, 210, 104, 255) : new Color32(112, 200, 98, 255),
                        Surface.Bunker => h % 7 == 0 ? new Color32(204, 182, 128, 255) : new Color32(230, 212, 160, 255),
                        Surface.Water => (x + (2 * y)) % 23 == 0 || (x + (2 * y)) % 23 == 1 ? new Color32(96, 158, 214, 255) : new Color32(52, 112, 182, 255),
                        _ => (x / 2 + y / 2) % 2 == 0 ? new Color32(36, 58, 36, 255) : new Color32(32, 52, 32, 255),
                    };

                    // 種類の境目（マスの端のピクセルで、隣のマスの種類が違う）を暗くする。
                    var edge = (x % p == 0 && hole.SurfaceOfCell(cx - 1, cy) != surface) || (x % p == p - 1 && hole.SurfaceOfCell(cx + 1, cy) != surface) ||
                        (y % p == 0 && hole.SurfaceOfCell(cx, cy - 1) != surface) || (y % p == p - 1 && hole.SurfaceOfCell(cx, cy + 1) != surface);
                    if (edge)
                    {
                        color = surface == Surface.OutOfBounds ? new Color32(236, 236, 228, 255) : new Color32((byte)(color.r * 7 / 10), (byte)(color.g * 7 / 10), (byte)(color.b * 7 / 10), 255);
                    }

                    c.Set(x, y, color);
                }
            }
        }

        private static void Tree(Canvas c)
        {
            var mid = c.Width / 2f;
            c.Ellipse(mid, mid, mid - 1, mid - 1, new Color32(28, 78, 34, 255));
            c.Ellipse(mid - 2, mid + 2, (mid - 1) * .78f, (mid - 1) * .78f, new Color32(40, 104, 44, 255));
            c.Ellipse(mid - 4, mid + 4, (mid - 1) * .45f, (mid - 1) * .45f, new Color32(62, 138, 58, 255));
            c.Ellipse(mid - 6, mid + 6, 2.5f, 2.5f, new Color32(96, 170, 80, 255));
            c.Outline(_outline);
        }

        private static void Tee(Canvas c)
        {
            c.Ellipse(4, 8, 2.5f, 2.5f, new Color32(240, 240, 240, 255));
            c.Ellipse(12, 8, 2.5f, 2.5f, new Color32(240, 240, 240, 255));
            c.Outline(_outline);
        }

        private static void Cup(Canvas c)
        {
            c.Ellipse(8, 8, 7.5f, 7.5f, new Color32(250, 250, 250, 255));
            c.Ellipse(8, 8, 6f, 6f, new Color32(20, 24, 20, 255));
        }

        /// <summary>旗：左下（竿の根元）が基点。白い竿と赤い旗。</summary>
        private static void Flag(Canvas c)
        {
            c.Rect(1, 0, 3, 48, new Color32(245, 245, 240, 255));
            c.Rect(4, 32, 15, 14, new Color32(220, 50, 50, 255));
            c.Rect(4, 42, 15, 4, new Color32(255, 110, 100, 255));
            c.Outline(_outline);
        }

        private static void Ball(Canvas c)
        {
            c.Ellipse(6, 6, 5.5f, 5.5f, new Color32(236, 238, 240, 255));
            c.Ellipse(5, 7, 3.5f, 3.5f, new Color32(255, 255, 255, 255));
            c.Outline(new Color32(40, 40, 40, 255));
        }

        private static void Shadow(Canvas c) => c.Ellipse(6, 6, 5.5f, 4.5f, new Color32(0, 0, 0, 110));

        private static void Dot(Canvas c) => c.Ellipse(4, 4, 3.5f, 3.5f, new Color32(255, 255, 255, 255));

        private static void Mark(Canvas c)
        {
            c.Ring(8, 8, 7f, new Color32(255, 255, 255, 255));
            c.Ring(8, 8, 6f, new Color32(255, 255, 255, 255));
            c.Rect(7, 7, 2, 2, new Color32(255, 255, 255, 255));
        }

        /// <summary>右向きの矢印（傾きの低いほうへ）。</summary>
        private static void Arrow(Canvas c)
        {
            var color = new Color32(255, 255, 255, 220);
            c.Rect(1, 7, 10, 2, color);
            for (var i = 0; i < 5; i++)
            {
                c.Rect(10 + i, 3 + i, 1, 10 - (2 * i), color);
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

            public void Set(int x, int y, Color32 color)
            {
                if (x < 0 || y < 0 || x >= Width || y >= Height) { return; }
                Pixels[(y * Width) + x] = color;
            }

            public void Rect(int x, int y, int width, int height, Color32 color)
            {
                for (var py = y; py < y + height; py++)
                {
                    for (var px = x; px < x + width; px++) { Set(px, py, color); }
                }
            }

            public void Ellipse(float cx, float cy, float rx, float ry, Color32 color)
            {
                for (var py = (int)(cy - ry - 1); py <= (int)(cy + ry + 1); py++)
                {
                    for (var px = (int)(cx - rx - 1); px <= (int)(cx + rx + 1); px++)
                    {
                        var dx = (px + .5f - cx) / rx;
                        var dy = (py + .5f - cy) / ry;
                        if ((dx * dx) + (dy * dy) <= 1) { Set(px, py, color); }
                    }
                }
            }

            /// <summary>太さ 1 ピクセルの円周。</summary>
            public void Ring(float cx, float cy, float r, Color32 color)
            {
                var steps = Mathf.Max(16, (int)(r * 8));
                for (var i = 0; i < steps; i++)
                {
                    var a = i * 2 * Mathf.PI / steps;
                    Set(Mathf.FloorToInt(cx + (r * Mathf.Cos(a))), Mathf.FloorToInt(cy + (r * Mathf.Sin(a))), color);
                }
            }

            /// <summary>不透明な画素に隣り合う透明な画素を輪郭の色で塗る（上下左右）。</summary>
            public void Outline(Color32 color)
            {
                var source = (Color32[])Pixels.Clone();
                for (var y = 0; y < Height; y++)
                {
                    for (var x = 0; x < Width; x++)
                    {
                        if (source[(y * Width) + x].a != 0) { continue; }
                        if (Opaque(source, x - 1, y) || Opaque(source, x + 1, y) || Opaque(source, x, y - 1) || Opaque(source, x, y + 1))
                        {
                            Pixels[(y * Width) + x] = color;
                        }
                    }
                }
            }

            private bool Opaque(Color32[] source, int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && source[(y * Width) + x].a != 0;
        }
    }
}
