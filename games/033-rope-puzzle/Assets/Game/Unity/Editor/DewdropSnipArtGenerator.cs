using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>
    /// 図形を組み合わせてドット絵の素材 PNG を描き出す。既にあるファイルとそのインポート設定は上書きしない。
    /// 1 単位（1 マス）= 32 ピクセル。夕暮れの庭：盤の内は明るい空、盤の外は暗い縁。円の絵（露玉・目標・トゲ）は直径 1 単位で描き、置くときに大きさを合わせる。
    /// 乱数は使わず、毎回同じ絵にする。
    /// </summary>
    public static class DewdropSnipArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";
        public const int PixelsPerUnit = 32;

        public const string BackgroundFile = "Background.png";
        public const string DewdropFile = "Dewdrop.png";
        public const string GoalFile = "Goal.png";
        public const string ThornFile = "Thorn.png";
        public const string AnchorFile = "Anchor.png";
        public const string WallFile = "Wall.png";
        public const string SnipFile = "Snip.png";

        /// <summary>背景は 20 × 11 単位（16:9 から 21:9 の画面まで覆う）。盤（16 × 9）は中央。</summary>
        public const int BackgroundWidth = 20 * PixelsPerUnit;
        public const int BackgroundHeight = 12 * PixelsPerUnit;

        private static readonly Color32 _outline = new(10, 18, 14, 255);

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtRoot);
            var created = new List<(string Path, bool Tiled)>();
            Save(created, BackgroundFile, BackgroundWidth, BackgroundHeight, false, Background);
            Save(created, DewdropFile, 32, 32, false, Dewdrop);
            Save(created, GoalFile, 32, 32, false, Goal);
            Save(created, ThornFile, 32, 32, false, Thorn);
            Save(created, AnchorFile, 16, 16, false, Anchor);
            Save(created, WallFile, 32, 32, true, Wall);
            Save(created, SnipFile, 32, 32, false, Snip);
            AssetDatabase.Refresh();
            foreach (var (path, tiled) in created) { Import(path, tiled); }
        }

        private static void Save(List<(string, bool)> created, string file, int width, int height, bool tiled, Action<Canvas> paint)
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
            created.Add((path, tiled));
        }

        private static void Import(string path, bool tiled)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = tiled ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        /// <summary>背景：盤の内は夕暮れの空（上ほど濃い）と遠くの丘、盤の外は暗い縁。</summary>
        private static void Background(Canvas c)
        {
            var left = (BackgroundWidth - (16 * PixelsPerUnit)) / 2;
            var bottom = (BackgroundHeight - (9 * PixelsPerUnit)) / 2;
            var right = left + (16 * PixelsPerUnit);
            var top = bottom + (9 * PixelsPerUnit);
            for (var y = 0; y < BackgroundHeight; y++)
            {
                for (var x = 0; x < BackgroundWidth; x++)
                {
                    var inside = x >= left && x < right && y >= bottom && y < top;
                    var t = Mathf.Floor(Mathf.Clamp01((float)(y - bottom) / (top - bottom)) * 8) / 8;
                    var sky = Color.Lerp(new Color(.98f, .78f, .55f), new Color(.38f, .42f, .66f), t);
                    c.Set(x, y, inside ? sky : Color.Lerp(sky, new Color(.06f, .08f, .1f), .82f));
                }
            }

            // 遠くの丘（盤の下の方）。
            for (var x = left; x < right; x++)
            {
                var h1 = bottom + 22 + Mathf.RoundToInt(10 * Mathf.Sin(x * .021f) + (6 * Mathf.Sin(x * .057f)));
                var h2 = bottom + 10 + Mathf.RoundToInt(6 * Mathf.Sin((x * .033f) + 1.7f));
                for (var y = bottom; y < h1; y++) { c.Set(x, y, new Color32(118, 140, 120, 255)); }
                for (var y = bottom; y < h2; y++) { c.Set(x, y, new Color32(84, 110, 88, 255)); }
            }

            // 盤の縁（ここから外へ出ると落ちた扱い）。
            var edge = new Color32(40, 52, 44, 255);
            c.Rect(left - 2, bottom - 2, right - left + 4, 2, edge);
            c.Rect(left - 2, bottom, 2, top - bottom, edge);
            c.Rect(right, bottom, 2, top - bottom, edge);
        }

        private static void Dewdrop(Canvas c)
        {
            c.Ellipse(16, 16, 15, 15, new Color32(110, 190, 240, 255));
            c.Ellipse(15, 17, 11, 11, new Color32(150, 214, 250, 255));
            c.Ellipse(11, 21, 4, 3, new Color32(240, 250, 255, 255));
            c.Outline(_outline);
        }

        /// <summary>目標：花の器（上が開いた花びら）。</summary>
        private static void Goal(Canvas c)
        {
            for (var i = 0; i < 6; i++)
            {
                var a = i * Mathf.PI / 3;
                c.Ellipse(16 + (9 * Mathf.Cos(a)), 16 + (9 * Mathf.Sin(a)), 6.5f, 6.5f, new Color32(236, 120, 160, 255));
            }

            c.Ellipse(16, 16, 8, 8, new Color32(255, 214, 110, 255));
            c.Ellipse(16, 16, 4, 4, new Color32(240, 170, 70, 255));
            c.Outline(_outline);
        }

        /// <summary>トゲ：棘の生えた暗い実。</summary>
        private static void Thorn(Canvas c)
        {
            var spike = new Color32(120, 60, 90, 255);
            for (var i = 0; i < 8; i++)
            {
                var a = i * Mathf.PI / 4;
                c.Line(16, 16, 16 + (15 * Mathf.Cos(a)), 16 + (15 * Mathf.Sin(a)), spike);
                c.Line(16.5f, 16, 16.5f + (14 * Mathf.Cos(a)), 16 + (14 * Mathf.Sin(a)), spike);
            }

            c.Ellipse(16, 16, 10, 10, new Color32(90, 40, 70, 255));
            c.Ellipse(13, 19, 3, 2, new Color32(170, 100, 140, 255));
            c.Outline(_outline);
        }

        private static void Anchor(Canvas c)
        {
            c.Ellipse(8, 8, 6, 6, new Color32(150, 110, 70, 255));
            c.Ellipse(7, 9, 3, 3, new Color32(210, 170, 120, 255));
            c.Outline(_outline);
        }

        /// <summary>壁・棚：苔の生えた石（タイル表示で繰り返す）。</summary>
        private static void Wall(Canvas c)
        {
            c.Rect(0, 0, 32, 32, new Color32(110, 104, 96, 255));
            var mortar = new Color32(80, 74, 68, 255);
            c.Rect(0, 15, 32, 2, mortar);
            c.Rect(15, 0, 2, 15, mortar);
            c.Rect(5, 17, 2, 15, mortar);
            c.Rect(25, 17, 2, 15, mortar);
            c.Rect(0, 28, 32, 4, new Color32(96, 140, 80, 255));
        }

        private static void Snip(Canvas c)
        {
            var white = new Color32(255, 255, 240, 255);
            c.Line(4, 16, 28, 16, white);
            c.Line(16, 4, 16, 28, white);
            c.Line(8, 8, 24, 24, white);
            c.Line(8, 24, 24, 8, white);
            c.Ellipse(16, 16, 4, 4, white);
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

            public void Set(int x, int y, Color color) => Set(x, y, (Color32)color);

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

            public void Line(float x0, float y0, float x1, float y1, Color32 color)
            {
                var steps = (int)Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0));
                for (var i = 0; i <= steps; i++)
                {
                    var t = steps == 0 ? 0 : (float)i / steps;
                    Set(Mathf.RoundToInt(Mathf.Lerp(x0, x1, t)), Mathf.RoundToInt(Mathf.Lerp(y0, y1, t)), color);
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
