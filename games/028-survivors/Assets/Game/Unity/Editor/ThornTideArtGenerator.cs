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
    /// 1 セル = 32 ピクセル（Core の 8 単位 = 1 ピクセル）。体の絵は既定の当たりの半径の大きさに描き（<see cref="ThornTideStage.CellArtRadius"/> など）、
    /// 描画は設定の半径に合わせて拡縮する。乱数は使わず、毎回同じ絵にする。
    /// </summary>
    public static class ThornTideArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";

        public const string FloorFile = "Floor.png";
        public const string PlayerFile = "Player.png";
        public const string BossFile = "Boss.png";
        public const string BladeFile = "Blade.png";
        public const string PulseFile = "Pulse.png";
        public const string LashFile = "Lash.png";
        public const string SwarmFile = "Swarm.png";

        private static readonly Color32 _outline = new(10, 14, 8, 255);

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtRoot);
            var created = new List<(string Path, bool Sprite)>();
            Save(created, FloorFile, 64, 64, Floor, true);
            Save(created, PlayerFile, 24, 24, Player, true);
            Save(created, BossFile, 88, 88, Boss, true);
            Save(created, BladeFile, 18, 18, Blade, true);
            Save(created, PulseFile, 64, 64, Pulse, true);
            Save(created, LashFile, 32, 32, Lash, true);
            Save(created, SwarmFile, QuadLayer.Cells * QuadLayer.CellPixels, QuadLayer.CellPixels, Swarm, false);
            AssetDatabase.Refresh();
            foreach (var (path, sprite) in created) { Import(path, sprite); }
        }

        private static void Save(List<(string, bool)> created, string file, int width, int height, Action<Canvas> paint, bool sprite)
        {
            var path = ArtRoot + file;
            if (File.Exists(path)) { return; }
            var canvas = new Canvas(width, height);
            paint(canvas);
            var texture = new Texture2D(canvas.Width, canvas.Height, TextureFormat.RGBA32, false);
            texture.SetPixels32(canvas.Pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            created.Add((path, sprite));
        }

        private static void Import(string path, bool sprite)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = sprite ? TextureImporterType.Sprite : TextureImporterType.Default;
            if (sprite)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = ThornTideArt.PixelsPerUnit;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)SpriteAlignment.Center;
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
            }

            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        // 床：夜の草原。深い緑の地に、明るい草の房と小石を散らす（2×2 セルで繰り返す）。
        private static void Floor(Canvas c)
        {
            c.Rect(0, 0, 64, 64, new Color32(22, 40, 24, 255));
            foreach (var (x, y) in new[] { (6, 9), (40, 14), (22, 34), (52, 46), (12, 54), (34, 58), (58, 4), (28, 20) })
            {
                c.Set(x, y, new Color32(54, 92, 46, 255));
                c.Set(x + 1, y + 1, new Color32(70, 118, 56, 255));
                c.Set(x - 1, y + 1, new Color32(54, 92, 46, 255));
                c.Set(x, y + 2, new Color32(84, 136, 64, 255));
            }

            foreach (var (x, y) in new[] { (18, 6), (46, 30), (4, 40), (60, 60) })
            {
                c.Rect(x, y, 2, 1, new Color32(52, 58, 50, 255));
            }

            c.Rect(0, 0, 64, 1, new Color32(26, 46, 28, 255));
            c.Rect(0, 0, 1, 64, new Color32(26, 46, 28, 255));
        }

        // 自機：フードをかぶった旅人（右向き）。半径 10 ピクセル（80 単位）。
        private static void Player(Canvas c)
        {
            c.Ellipse(12, 10, 7, 8, new Color32(70, 120, 200, 255));
            c.Ellipse(12, 15, 6, 6, new Color32(110, 160, 230, 255));
            c.Ellipse(14, 14, 3, 3, new Color32(250, 220, 180, 255));
            c.Set(15, 15, new Color32(30, 30, 40, 255));
            c.Rect(8, 3, 3, 3, new Color32(60, 50, 40, 255));
            c.Rect(13, 3, 3, 3, new Color32(60, 50, 40, 255));
            c.Rect(18, 9, 4, 2, new Color32(255, 240, 150, 255));
            c.Outline(_outline);
        }

        // ボス：茨の番人。紫の殻に棘の冠と赤い目。半径 40 ピクセル（320 単位）。
        private static void Boss(Canvas c)
        {
            for (var k = 0; k < 16; k++)
            {
                var angle = k * Mathf.PI * 2 / 16;
                for (var r = 30; r < 43; r++)
                {
                    c.Set(44 + (int)Mathf.Round(Mathf.Cos(angle) * r), 44 + (int)Mathf.Round(Mathf.Sin(angle) * r), new Color32(120, 170, 70, 255));
                }
            }

            c.Ellipse(44, 44, 34, 34, new Color32(90, 40, 110, 255));
            c.Ellipse(44, 46, 26, 24, new Color32(140, 70, 160, 255));
            c.Ellipse(34, 50, 5, 5, new Color32(255, 70, 70, 255));
            c.Ellipse(54, 50, 5, 5, new Color32(255, 70, 70, 255));
            c.Ellipse(34, 50, 2, 2, new Color32(255, 230, 200, 255));
            c.Ellipse(54, 50, 2, 2, new Color32(255, 230, 200, 255));
            c.Rect(34, 32, 20, 3, new Color32(40, 16, 50, 255));
            c.Outline(_outline);
        }

        // Orbit の刃：明るい緑の葉（回して描く）。半径 7.5 ピクセル（60 単位）。
        private static void Blade(Canvas c)
        {
            for (var y = 0; y < c.Height; y++)
            {
                for (var x = 0; x < c.Width; x++)
                {
                    var dx = x + .5f - 9;
                    var dy = y + .5f - 9;
                    var leaf = (dx * dx / 56f) + (dy * dy / 12f);
                    if (leaf <= 1) { c.Set(x, y, Math.Abs(dy) < 1 ? new Color32(220, 255, 180, 255) : new Color32(110, 220, 90, 255)); }
                }
            }

            c.Outline(_outline);
        }

        // Pulse の輪：半径 1 単位（31 ピクセル）の棘の輪。中は薄く塗る。
        private static void Pulse(Canvas c)
        {
            for (var y = 0; y < c.Height; y++)
            {
                for (var x = 0; x < c.Width; x++)
                {
                    var dx = x + .5f - 32;
                    var dy = y + .5f - 32;
                    var r = Mathf.Sqrt((dx * dx) + (dy * dy));
                    var spike = ((int)((Mathf.Atan2(dy, dx) + Mathf.PI) / (Mathf.PI * 2) * 24) % 2 == 0) ? 1.5f : 0f;
                    if (r <= 31 && r >= 28 - spike) { c.Set(x, y, new Color32(200, 255, 140, 200)); }
                    else if (r < 28) { c.Set(x, y, new Color32(150, 230, 90, 22)); }
                }
            }
        }

        // Lash の斬撃：右へ振る蔦の弧（1×1 単位を矩形に引き伸ばす）。
        private static void Lash(Canvas c)
        {
            for (var y = 0; y < c.Height; y++)
            {
                for (var x = 0; x < c.Width; x++)
                {
                    var dx = x + .5f;
                    var dy = y + .5f - 16;
                    var curve = 16 * (1 - Mathf.Pow((dx - 16) / 16, 2));
                    var d = Math.Abs(dy - (curve - 8));
                    if (d < 3 + (dx / 16)) { c.Set(x, y, new Color32(190, 255, 130, (byte)(150 + (dx * 3)))); }
                    if (d < 1.2f) { c.Set(x, y, new Color32(230, 255, 200, 255)); }
                }
            }
        }

        // 敵・宝石・弾のアトラス（横に 32 ピクセルの区画を並べる）。区画の中央に、既定の半径の大きさで描く。
        private static void Swarm(Canvas c)
        {
            // 0 影ダニ：黒紫の丸い虫と赤い目。半径 9 ピクセル（72 単位）。
            Cell(c, 0, s =>
            {
                s.Ellipse(16, 15, 9, 8, new Color32(70, 40, 90, 255));
                s.Ellipse(16, 17, 6, 4, new Color32(110, 70, 140, 255));
                s.Set(13, 17, new Color32(255, 80, 80, 255));
                s.Set(19, 17, new Color32(255, 80, 80, 255));
                s.Rect(9, 9, 2, 2, new Color32(50, 30, 60, 255));
                s.Rect(21, 9, 2, 2, new Color32(50, 30, 60, 255));
            });

            // 1 羽虫：黄土色の胴と透ける羽。半径 7 ピクセル（56 単位）。
            Cell(c, 1, s =>
            {
                s.Ellipse(11, 20, 5, 3, new Color32(220, 240, 255, 160));
                s.Ellipse(21, 20, 5, 3, new Color32(220, 240, 255, 160));
                s.Ellipse(16, 15, 4, 6, new Color32(200, 150, 60, 255));
                s.Rect(15, 11, 2, 1, new Color32(60, 40, 20, 255));
            });

            // 2 殻虫：硬い茶色の殻の大きな甲虫。半径 14 ピクセル（112 単位）。
            Cell(c, 2, s =>
            {
                s.Ellipse(16, 15, 14, 13, new Color32(110, 70, 40, 255));
                s.Ellipse(16, 16, 10, 10, new Color32(160, 110, 60, 255));
                s.Rect(15, 4, 2, 24, new Color32(80, 50, 30, 255));
                s.Ellipse(11, 22, 2, 2, new Color32(255, 200, 80, 255));
                s.Ellipse(21, 22, 2, 2, new Color32(255, 200, 80, 255));
            });

            // 3 宝石（小）：水色のひし形。半径 5 ピクセル（40 単位）。
            Cell(c, 3, s => Diamond(s, 5, new Color32(110, 220, 255, 255), new Color32(220, 250, 255, 255)));

            // 4 宝石（大）：緑のひし形。半径 7 ピクセル（56 単位）。
            Cell(c, 4, s => Diamond(s, 7, new Color32(120, 240, 120, 255), new Color32(230, 255, 220, 255)));

            // 5 光の矢の弾：白く光る玉。半径 5 ピクセル（40 単位）。
            Cell(c, 5, s =>
            {
                s.Ellipse(16, 16, 5, 5, new Color32(255, 240, 150, 255));
                s.Ellipse(16, 16, 3, 3, new Color32(255, 255, 240, 255));
            });
        }

        private static void Diamond(Canvas s, int radius, Color32 body, Color32 shine)
        {
            for (var y = 0; y < s.Height; y++)
            {
                for (var x = 0; x < s.Width; x++)
                {
                    var d = Math.Abs(x + .5f - 16) + Math.Abs(y + .5f - 16);
                    if (d <= radius) { s.Set(x, y, d <= radius / 2f ? shine : body); }
                }
            }
        }

        /// <summary>区画 <paramref name="cell"/> に 32×32 の絵を描いて輪郭を付け、アトラスへ写す。</summary>
        private static void Cell(Canvas atlas, int cell, Action<Canvas> paint)
        {
            var size = QuadLayer.CellPixels;
            var part = new Canvas(size, size);
            paint(part);
            part.Outline(_outline);
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++) { atlas.Set((cell * size) + x, y, part.Pixels[(y * size) + x]); }
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

            private bool Opaque(Color32[] source, int x, int y) =>
                x >= 0 && y >= 0 && x < Width && y < Height && source[(y * Width) + x].a > 200;
        }
    }
}
