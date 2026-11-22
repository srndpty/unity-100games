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
    /// 1 セル = 32 ピクセル（Core の 8 単位 = 1 ピクセル）。どの絵も中央を基準点にし、向きのある絵は右（+x）向きに描く。
    /// 体の絵の直径は Core の当たりの円の直径にそろえる。乱数は使わず、毎回同じ絵にする。
    /// </summary>
    public static class HaloSiegeArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";
        public const int PixelsPerUnit = 32;

        public const string FloorFile = "Floor.png";
        public const string WallFile = "Wall.png";
        public const string ShipFile = "Ship.png";
        public const string ChaserFile = "Chaser.png";
        public const string BouncerFile = "Bouncer.png";
        public const string LancerFile = "Lancer.png";
        public const string BulletFile = "Bullet.png";
        public const string WarningFile = "Warning.png";
        public const string LaneFile = "Lane.png";
        public const string CrosshairFile = "Crosshair.png";

        private static readonly Color32 _outline = new(4, 6, 16, 255);

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtRoot);
            var created = new List<string>();
            Save(created, FloorFile, 32, 32, Floor);
            Save(created, WallFile, 32, 32, Wall);
            Save(created, ShipFile, 22, 22, Ship);
            Save(created, ChaserFile, 26, 26, Chaser);
            Save(created, BouncerFile, 28, 28, Bouncer);
            Save(created, LancerFile, 32, 32, Lancer);
            Save(created, BulletFile, 12, 10, Bullet);
            Save(created, WarningFile, 34, 34, Warning);
            Save(created, LaneFile, 32, 6, Lane);
            Save(created, CrosshairFile, 26, 26, Crosshair);
            AssetDatabase.Refresh();
            foreach (var path in created) { Import(path); }
        }

        private static void Save(List<string> created, string file, int width, int height, Action<Canvas> paint)
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
            created.Add(path);
        }

        private static void Import(string path)
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
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        // 床：夜の要塞の甲板。紺の地に細い格子と、4 隅の小さなリベット。
        private static void Floor(Canvas c)
        {
            c.Rect(0, 0, 32, 32, new Color32(12, 16, 36, 255));
            c.Rect(0, 0, 32, 1, new Color32(26, 36, 74, 255));
            c.Rect(0, 0, 1, 32, new Color32(26, 36, 74, 255));
            c.Rect(15, 15, 2, 2, new Color32(20, 28, 58, 255));
            foreach (var (x, y) in new[] { (3, 3), (28, 3), (3, 28), (28, 28) }) { c.Set(x, y, new Color32(32, 44, 86, 255)); }
        }

        // 外周の壁：光る縁取りの装甲板。
        private static void Wall(Canvas c)
        {
            c.Rect(0, 0, 32, 32, new Color32(30, 46, 104, 255));
            c.Rect(2, 2, 28, 28, new Color32(44, 70, 150, 255));
            c.Rect(4, 4, 24, 24, new Color32(36, 56, 124, 255));
            c.Rect(0, 15, 32, 2, new Color32(110, 170, 255, 255));
        }

        // 自機：水色の六角の船体と、照準の向き（右）へ伸びる砲身。直径 18 ピクセル（半径 72 単位）。
        private static void Ship(Canvas c)
        {
            c.Ellipse(10, 11, 8, 8, new Color32(70, 190, 240, 255));
            c.Ellipse(10, 11, 5, 5, new Color32(170, 240, 255, 255));
            c.Rect(14, 10, 7, 2, new Color32(230, 250, 255, 255));
            c.Ellipse(9, 11, 2, 2, new Color32(20, 60, 120, 255));
            c.Outline(_outline);
        }

        // Chaser：桃色のひし形。直径 24 ピクセル（半径 96 単位）。
        private static void Chaser(Canvas c)
        {
            for (var y = 0; y < c.Height; y++)
            {
                for (var x = 0; x < c.Width; x++)
                {
                    var d = Math.Abs(x + .5f - 13) + Math.Abs(y + .5f - 13);
                    if (d <= 12) { c.Set(x, y, d <= 6 ? new Color32(255, 170, 230, 255) : new Color32(230, 60, 170, 255)); }
                }
            }

            c.Rect(12, 12, 2, 2, new Color32(60, 10, 50, 255));
            c.Outline(_outline);
        }

        // Bouncer：緑のとげのある円（回して描く）。直径 26 ピクセル（半径 104 単位）。
        private static void Bouncer(Canvas c)
        {
            c.Ellipse(14, 14, 10, 10, new Color32(60, 210, 110, 255));
            c.Ellipse(14, 14, 6, 6, new Color32(160, 255, 180, 255));
            c.Rect(13, 1, 2, 4, new Color32(60, 210, 110, 255));
            c.Rect(13, 23, 2, 4, new Color32(60, 210, 110, 255));
            c.Rect(1, 13, 4, 2, new Color32(60, 210, 110, 255));
            c.Rect(23, 13, 4, 2, new Color32(60, 210, 110, 255));
            c.Ellipse(14, 14, 2, 2, new Color32(20, 70, 40, 255));
            c.Outline(_outline);
        }

        // Lancer：橙の矢じり（右向き）。直径 30 ピクセル（半径 120 単位）。
        private static void Lancer(Canvas c)
        {
            for (var y = 0; y < c.Height; y++)
            {
                for (var x = 1; x < 31; x++)
                {
                    var dx = x + .5f - 16;
                    var dy = Math.Abs(y + .5f - 16);
                    if (dy <= (15 - dx) * .5f && dy <= 13)
                    {
                        c.Set(x, y, dy <= (15 - dx) * .25f ? new Color32(255, 220, 140, 255) : new Color32(240, 130, 40, 255));
                    }
                }
            }

            c.Rect(6, 15, 4, 2, new Color32(90, 30, 10, 255));
            c.Outline(_outline);
        }

        // 自機の弾：黄色い光弾（右向きの楕円）。直径 8 ピクセル（半径 32 単位）。
        private static void Bullet(Canvas c)
        {
            c.Ellipse(6, 5, 5, 4, new Color32(255, 210, 80, 255));
            c.Ellipse(7, 5, 2.5f, 2, new Color32(255, 250, 220, 255));
        }

        // 出現の予告：白い輪（敵の種類の色で塗る）。
        private static void Warning(Canvas c)
        {
            for (var y = 0; y < c.Height; y++)
            {
                for (var x = 0; x < c.Width; x++)
                {
                    var dx = x + .5f - 17;
                    var dy = y + .5f - 17;
                    var r = Mathf.Sqrt((dx * dx) + (dy * dy));
                    if (r <= 16 && r >= 13.5f) { c.Set(x, y, new Color32(255, 255, 255, 230)); }
                    else if (r < 13.5f && ((x + y) % 4 == 0)) { c.Set(x, y, new Color32(255, 255, 255, 90)); }
                }
            }
        }

        // 突進の筋：長さ 1 セルの半透明の帯（突進の距離に伸ばす）。
        private static void Lane(Canvas c)
        {
            c.Rect(0, 1, 32, 4, new Color32(255, 150, 60, 70));
            c.Rect(0, 2, 32, 2, new Color32(255, 200, 120, 110));
        }

        // 照準：切れ目のある輪と中心の点。
        private static void Crosshair(Canvas c)
        {
            for (var y = 0; y < c.Height; y++)
            {
                for (var x = 0; x < c.Width; x++)
                {
                    var dx = x + .5f - 13;
                    var dy = y + .5f - 13;
                    var r = Mathf.Sqrt((dx * dx) + (dy * dy));
                    var gap = Math.Abs(dx) < 2 || Math.Abs(dy) < 2;
                    if (r <= 11 && r >= 9 && !gap) { c.Set(x, y, new Color32(255, 255, 255, 230)); }
                }
            }

            c.Rect(12, 12, 2, 2, new Color32(255, 255, 255, 255));
            c.Outline(new Color32(0, 0, 0, 160));
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
                x >= 0 && y >= 0 && x < Width && y < Height && source[(y * Width) + x].a != 0;
        }
    }
}
