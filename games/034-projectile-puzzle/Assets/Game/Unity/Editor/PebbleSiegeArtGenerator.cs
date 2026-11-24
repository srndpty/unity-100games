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
    /// 1 m = 32 ピクセル。夕方の丘の砦：空・遠くの山・草の地面。部材の絵（木・石・氷）は 1 m 角のタイルで描き、タイル表示で大きさを合わせる。
    /// 円の絵（弾）と灯籠は 1 m 角で描き、置くときに大きさを合わせる。乱数は使わず、毎回同じ絵にする。
    /// </summary>
    public static class PebbleSiegeArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";
        public const int PixelsPerUnit = 32;

        public const string BackgroundFile = "Background.png";
        public const string GroundFile = "Ground.png";
        public const string WoodFile = "Wood.png";
        public const string StoneFile = "Stone.png";
        public const string IceFile = "Ice.png";
        public const string LanternFile = "Lantern.png";
        public const string NormalFile = "ShotNormal.png";
        public const string HeavyFile = "ShotHeavy.png";
        public const string BombFile = "ShotBomb.png";
        public const string SlingshotFile = "Slingshot.png";
        public const string DotFile = "Dot.png";
        public const string BlastFile = "Blast.png";
        public const string PuffFile = "Puff.png";

        /// <summary>背景は 48 × 24 m（16:9 から 21:9 の画面まで覆う）。</summary>
        public const int BackgroundWidth = 48 * PixelsPerUnit;
        public const int BackgroundHeight = 24 * PixelsPerUnit;

        private static readonly Color32 _outline = new(24, 18, 16, 255);

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtRoot);
            var created = new List<(string Path, bool Tiled)>();
            Save(created, BackgroundFile, BackgroundWidth, BackgroundHeight, false, Background);
            Save(created, GroundFile, 32, 64, true, Ground);
            Save(created, WoodFile, 32, 32, true, Wood);
            Save(created, StoneFile, 32, 32, true, Stone);
            Save(created, IceFile, 32, 32, true, Ice);
            Save(created, LanternFile, 32, 32, false, Lantern);
            Save(created, NormalFile, 32, 32, false, c => Pebble(c, new Color32(176, 160, 140, 255), new Color32(214, 200, 178, 255)));
            Save(created, HeavyFile, 32, 32, false, c => Pebble(c, new Color32(92, 96, 112, 255), new Color32(140, 146, 164, 255)));
            Save(created, BombFile, 32, 32, false, Bomb);
            Save(created, SlingshotFile, 64, 80, false, Slingshot);
            Save(created, DotFile, 8, 8, false, c => c.Ellipse(4, 4, 3.5f, 3.5f, new Color32(255, 255, 255, 255)));
            Save(created, BlastFile, 64, 64, false, Blast);
            Save(created, PuffFile, 32, 32, false, Puff);
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

        /// <summary>背景：夕方の空（上ほど濃い、段階的）・雲・遠くの山。地面より下は描かない（地面の絵が覆う）。</summary>
        private static void Background(Canvas c)
        {
            for (var y = 0; y < BackgroundHeight; y++)
            {
                var t = Mathf.Floor((float)y / BackgroundHeight * 10) / 10;
                var sky = Color.Lerp(new Color(.98f, .80f, .58f), new Color(.30f, .40f, .66f), t);
                for (var x = 0; x < BackgroundWidth; x++) { c.Set(x, y, sky); }
            }

            var cloud = new Color32(255, 236, 214, 255);
            foreach (var (cx, cy) in new[] { (300, 600), (760, 640), (1180, 580), (1420, 680) })
            {
                c.Ellipse(cx, cy, 70, 16, cloud);
                c.Ellipse(cx + 40, cy + 12, 46, 16, cloud);
                c.Ellipse(cx - 46, cy + 6, 36, 12, cloud);
            }

            // 遠くの山（地面の高さは下から 4 m）。
            var ground = 4 * PixelsPerUnit;
            for (var x = 0; x < BackgroundWidth; x++)
            {
                var far = ground + 120 + Mathf.RoundToInt((50 * Mathf.Sin(x * .006f)) + (24 * Mathf.Sin((x * .017f) + 1.3f)));
                var near = ground + 50 + Mathf.RoundToInt(22 * Mathf.Sin((x * .011f) + .4f));
                for (var y = ground; y < far; y++) { c.Set(x, y, new Color32(150, 132, 160, 255)); }
                for (var y = ground; y < near; y++) { c.Set(x, y, new Color32(112, 126, 118, 255)); }
            }
        }

        /// <summary>地面：上の 1/4 m が草、下は土（1 × 2 m のタイル）。</summary>
        private static void Ground(Canvas c)
        {
            c.Rect(0, 0, 32, 64, new Color32(120, 88, 60, 255));
            for (var i = 0; i < 6; i++) { c.Rect((i * 11) % 30, 8 + (i * 9 % 40), 3, 2, new Color32(96, 70, 48, 255)); }
            c.Rect(0, 56, 32, 8, new Color32(104, 162, 76, 255));
            c.Rect(0, 55, 32, 1, new Color32(72, 118, 56, 255));
            for (var x = 2; x < 32; x += 6) { c.Rect(x, 63, 2, 1, new Color32(150, 200, 110, 255)); }
        }

        private static void Wood(Canvas c)
        {
            c.Rect(0, 0, 32, 32, new Color32(186, 132, 78, 255));
            var grain = new Color32(150, 100, 56, 255);
            c.Rect(0, 10, 32, 1, grain);
            c.Rect(0, 21, 32, 1, grain);
            c.Rect(6, 3, 8, 1, grain);
            c.Rect(18, 14, 9, 1, grain);
            c.Rect(4, 26, 10, 1, grain);
            Frame(c, new Color32(110, 70, 40, 255));
        }

        private static void Stone(Canvas c)
        {
            c.Rect(0, 0, 32, 32, new Color32(150, 150, 156, 255));
            var crack = new Color32(112, 112, 120, 255);
            c.Rect(0, 15, 32, 2, crack);
            c.Rect(15, 0, 2, 15, crack);
            c.Rect(7, 17, 2, 15, crack);
            c.Rect(24, 17, 2, 15, crack);
            c.Rect(3, 4, 4, 3, new Color32(176, 176, 182, 255));
            Frame(c, new Color32(84, 84, 92, 255));
        }

        private static void Ice(Canvas c)
        {
            c.Rect(0, 0, 32, 32, new Color32(170, 220, 240, 230));
            c.Line(4, 26, 12, 18, new Color32(236, 250, 255, 255));
            c.Line(18, 10, 26, 4, new Color32(236, 250, 255, 255));
            c.Line(6, 6, 9, 3, new Color32(236, 250, 255, 255));
            Frame(c, new Color32(90, 150, 180, 255));
        }

        /// <summary>灯籠（標的）：赤い紙の灯り。</summary>
        private static void Lantern(Canvas c)
        {
            c.Rect(10, 27, 12, 3, new Color32(60, 40, 30, 255));
            c.Rect(10, 2, 12, 3, new Color32(60, 40, 30, 255));
            c.Ellipse(16, 16, 13, 12, new Color32(220, 70, 60, 255));
            c.Ellipse(16, 16, 8, 8, new Color32(255, 190, 110, 255));
            c.Ellipse(16, 16, 4, 4, new Color32(255, 236, 170, 255));
            for (var y = 7; y < 26; y += 5) { c.Rect(4, y, 24, 1, new Color32(170, 46, 40, 255)); }
            c.Outline(_outline);
        }

        private static void Pebble(Canvas c, Color32 body, Color32 light)
        {
            c.Ellipse(16, 16, 15, 15, body);
            c.Ellipse(13, 19, 7, 6, light);
            c.Outline(_outline);
        }

        private static void Bomb(Canvas c)
        {
            c.Ellipse(16, 15, 14, 14, new Color32(52, 50, 58, 255));
            c.Ellipse(12, 19, 5, 4, new Color32(110, 108, 122, 255));
            c.Rect(14, 27, 4, 3, new Color32(140, 120, 90, 255));
            c.Rect(17, 30, 3, 2, new Color32(255, 180, 60, 255));
            c.Outline(_outline);
        }

        /// <summary>発射台：二股の木（2 × 2.5 m、下端が地面、二股の先が発射台の高さ）。</summary>
        private static void Slingshot(Canvas c)
        {
            var bark = new Color32(120, 82, 50, 255);
            c.Rect(28, 0, 8, 46, bark);
            c.Line(31, 44, 14, 76, bark);
            c.Line(33, 44, 16, 76, bark);
            c.Line(35, 44, 18, 76, bark);
            c.Line(33, 44, 50, 76, bark);
            c.Line(35, 44, 52, 76, bark);
            c.Line(37, 44, 54, 76, bark);
            c.Rect(12, 72, 8, 6, new Color32(90, 60, 36, 255));
            c.Rect(46, 72, 8, 6, new Color32(90, 60, 36, 255));
            c.Outline(_outline);
        }

        private static void Blast(Canvas c)
        {
            c.Ellipse(32, 32, 31, 31, new Color32(255, 150, 60, 160));
            c.Ellipse(32, 32, 22, 22, new Color32(255, 200, 90, 200));
            c.Ellipse(32, 32, 12, 12, new Color32(255, 246, 200, 230));
        }

        private static void Puff(Canvas c)
        {
            var dust = new Color32(236, 226, 210, 220);
            c.Ellipse(16, 14, 10, 8, dust);
            c.Ellipse(9, 18, 7, 6, dust);
            c.Ellipse(23, 19, 7, 6, dust);
            c.Ellipse(16, 22, 6, 5, dust);
        }

        /// <summary>タイルの縁（タイル表示で並べると 1 m ごとの継ぎ目になる）。</summary>
        private static void Frame(Canvas c, Color32 color)
        {
            c.Rect(0, 0, 32, 1, color);
            c.Rect(0, 31, 32, 1, color);
            c.Rect(0, 0, 1, 32, color);
            c.Rect(31, 0, 1, 32, color);
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
