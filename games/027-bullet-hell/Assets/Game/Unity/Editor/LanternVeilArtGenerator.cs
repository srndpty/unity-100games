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
    /// 絵の 1 ピクセル = プレイ領域の 1 px（<see cref="LanternVeilStage.PixelsPerUnit"/> ピクセルで 1 ワールド単位）。
    /// 弾の絵は当たりの円より少し大きく描く（弾幕 STG の慣例：見た目より当たりが小さい）。乱数は使わず、毎回同じ絵にする。
    /// </summary>
    public static class LanternVeilArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";

        public const string FieldFile = "Field.png";
        public const string PlayerFile = "Player.png";
        public const string HitboxFile = "Hitbox.png";
        public const string Boss1File = "Boss1.png";
        public const string Boss2File = "Boss2.png";
        public const string ShotFile = "Shot.png";
        public const string BulletsFile = "Bullets.png";
        public const string BombRingFile = "BombRing.png";
        public const string HpBarFile = "HpBar.png";
        public const string PixelFile = "Pixel.png";

        private static readonly Color32 _outline = new(10, 4, 8, 255);

        /// <summary>弾の色（<see cref="BulletColor"/> の順）：縁の色と芯の色。</summary>
        private static readonly (Color32 Rim, Color32 Core)[] _bulletColors =
        {
            (new Color32(235, 50, 60, 255), new Color32(255, 220, 220, 255)),
            (new Color32(250, 140, 40, 255), new Color32(255, 236, 200, 255)),
            (new Color32(240, 220, 50, 255), new Color32(255, 255, 220, 255)),
            (new Color32(70, 210, 90, 255), new Color32(220, 255, 220, 255)),
            (new Color32(60, 210, 230, 255), new Color32(220, 255, 255, 255)),
            (new Color32(70, 110, 250, 255), new Color32(220, 230, 255, 255)),
            (new Color32(180, 80, 240, 255), new Color32(244, 220, 255, 255)),
            (new Color32(220, 220, 230, 255), new Color32(255, 255, 255, 255)),
        };

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtRoot);
            var created = new List<(string Path, SpriteAlignment Alignment)>();
            Save(created, FieldFile, Field.WidthPx, Field.HeightPx, Background, SpriteAlignment.Center);
            Save(created, PlayerFile, 20, 24, Player, SpriteAlignment.Center);
            Save(created, HitboxFile, 9, 9, Hitbox, SpriteAlignment.Center);
            Save(created, Boss1File, 64, 64, Warden, SpriteAlignment.Center);
            Save(created, Boss2File, 72, 72, Matron, SpriteAlignment.Center);
            Save(created, ShotFile, 6, 14, Shot, SpriteAlignment.Center);
            Save(created, BulletsFile, BulletLayer.Columns * BulletLayer.CellPixels, BulletLayer.Rows * BulletLayer.CellPixels, Bullets, SpriteAlignment.Center);
            Save(created, BombRingFile, 64, 64, BombRing, SpriteAlignment.Center);
            Save(created, HpBarFile, 16, 4, HpBar, SpriteAlignment.LeftCenter);
            Save(created, PixelFile, 16, 16, c => c.Rect(0, 0, 16, 16, new Color32(255, 255, 255, 255)), SpriteAlignment.BottomLeft);
            AssetDatabase.Refresh();
            foreach (var (path, alignment) in created) { Import(path, alignment); }
        }

        private static void Save(List<(string, SpriteAlignment)> created, string file, int width, int height, Action<Canvas> paint, SpriteAlignment alignment)
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
            created.Add((path, alignment));
        }

        private static void Import(string path, SpriteAlignment alignment)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = LanternVeilStage.PixelsPerUnit;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)alignment;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        // プレイ領域の背景：夜の紙の帳。濃い紫紺の地に、障子の格子と、ところどころ灯の滲み。
        private static void Background(Canvas c)
        {
            for (var y = 0; y < c.Height; y++)
            {
                for (var x = 0; x < c.Width; x++)
                {
                    // 上ほど少し明るい縦のグラデーション。
                    var t = (float)y / c.Height;
                    var color = new Color32((byte)(18 + (14 * t)), (byte)(10 + (6 * t)), (byte)(26 + (16 * t)), 255);
                    if (x % 48 == 0 || y % 64 == 0) { color = new Color32((byte)(color.r + 10), (byte)(color.g + 6), (byte)(color.b + 12), 255); }
                    c.Set(x, y, color);
                }
            }

            foreach (var (cx, cy) in new[] { (72, 360), (300, 300), (150, 160), (330, 90), (40, 60) })
            {
                for (var y = cy - 24; y <= cy + 24; y++)
                {
                    for (var x = cx - 24; x <= cx + 24; x++)
                    {
                        var d = Mathf.Sqrt(((x - cx) * (x - cx)) + ((y - cy) * (y - cy))) / 24f;
                        if (d > 1 || x < 0 || y < 0 || x >= c.Width || y >= c.Height) { continue; }
                        var p = c.Pixels[(y * c.Width) + x];
                        var glow = (1 - d) * 14;
                        c.Set(x, y, new Color32((byte)(p.r + glow), (byte)(p.g + (glow * .6f)), p.b, 255));
                    }
                }
            }

            // 縁：細い朱の線。
            c.Rect(0, 0, c.Width, 2, new Color32(170, 70, 50, 255));
            c.Rect(0, c.Height - 2, c.Width, 2, new Color32(170, 70, 50, 255));
            c.Rect(0, 0, 2, c.Height, new Color32(170, 70, 50, 255));
            c.Rect(c.Width - 2, 0, 2, c.Height, new Color32(170, 70, 50, 255));
        }

        // 自機：灯を抱えた小さな紙の鳥（上向き）。
        private static void Player(Canvas c)
        {
            for (var y = 2; y < 22; y++)
            {
                var half = (22 - y) * .45f;
                for (var x = 0; x < c.Width; x++)
                {
                    if (Math.Abs(x + .5f - 10) <= half) { c.Set(x, y, y < 8 ? new Color32(200, 210, 240, 255) : new Color32(245, 245, 255, 255)); }
                }
            }

            c.Rect(3, 4, 4, 3, new Color32(180, 190, 230, 255));
            c.Rect(13, 4, 4, 3, new Color32(180, 190, 230, 255));
            c.Ellipse(10, 11, 3, 3.5f, new Color32(255, 190, 80, 255));
            c.Ellipse(10, 11, 1.5f, 2, new Color32(255, 250, 210, 255));
            c.Outline(_outline);
        }

        // 当たり判定の点：白い芯に赤い縁（半径 2.5 px の円より少し大きい）。
        private static void Hitbox(Canvas c)
        {
            c.Ellipse(4.5f, 4.5f, 4, 4, new Color32(230, 40, 60, 255));
            c.Ellipse(4.5f, 4.5f, 2.6f, 2.6f, new Color32(255, 255, 255, 255));
        }

        // ボス 1：燠火の番人。丸い提灯の体と、2 つの目と、下に垂れる房。
        private static void Warden(Canvas c)
        {
            c.Ellipse(32, 34, 24, 22, new Color32(210, 80, 40, 255));
            c.Ellipse(32, 34, 20, 18, new Color32(250, 150, 60, 255));
            for (var y = 14; y <= 54; y += 8) { c.Rect(10, y, 44, 1, new Color32(190, 70, 36, 255)); }
            c.Rect(22, 54, 20, 5, new Color32(60, 30, 26, 255));
            c.Rect(22, 9, 20, 5, new Color32(60, 30, 26, 255));
            c.Rect(29, 1, 2, 8, new Color32(240, 200, 90, 255));
            c.Rect(33, 1, 2, 8, new Color32(240, 200, 90, 255));
            c.Ellipse(24, 36, 3, 4, new Color32(255, 250, 220, 255));
            c.Ellipse(40, 36, 3, 4, new Color32(255, 250, 220, 255));
            c.Ellipse(24, 35, 1.5f, 2, new Color32(60, 10, 10, 255));
            c.Ellipse(40, 35, 1.5f, 2, new Color32(60, 10, 10, 255));
            c.Outline(_outline);
        }

        // ボス 2：帳の女主人。紫の大きな翅と、白い面。
        private static void Matron(Canvas c)
        {
            c.Ellipse(20, 44, 18, 22, new Color32(110, 50, 170, 255));
            c.Ellipse(52, 44, 18, 22, new Color32(110, 50, 170, 255));
            c.Ellipse(22, 22, 14, 14, new Color32(140, 70, 200, 255));
            c.Ellipse(50, 22, 14, 14, new Color32(140, 70, 200, 255));
            c.Ellipse(20, 46, 8, 10, new Color32(200, 150, 240, 255));
            c.Ellipse(52, 46, 8, 10, new Color32(200, 150, 240, 255));
            c.Ellipse(36, 36, 8, 22, new Color32(40, 20, 60, 255));
            c.Ellipse(36, 46, 7, 8, new Color32(240, 236, 246, 255));
            c.Rect(32, 46, 2, 2, new Color32(120, 20, 60, 255));
            c.Rect(38, 46, 2, 2, new Color32(120, 20, 60, 255));
            c.Outline(_outline);
        }

        // ショット：上向きの淡い水色の針。
        private static void Shot(Canvas c)
        {
            c.Rect(2, 0, 2, 14, new Color32(140, 230, 255, 200));
            c.Rect(1, 3, 4, 8, new Color32(200, 245, 255, 230));
        }

        // 弾のアトラス：行が形（small・rice・orb・large）、列が色。どの絵も区画の中央が弾の中心で、右（+x）向き。
        private static void Bullets(Canvas c)
        {
            var cell = BulletLayer.CellPixels;
            var center = cell / 2f;
            for (var row = 0; row < BulletLayer.Rows; row++)
            {
                for (var column = 0; column < BulletLayer.Columns; column++)
                {
                    var (rim, core) = _bulletColors[column];
                    var ox = column * cell;
                    var oy = row * cell;
                    switch ((BulletShape)row)
                    {
                        case BulletShape.Small:
                            c.Ellipse(ox + center, oy + center, 4, 4, rim);
                            c.Ellipse(ox + center, oy + center, 2.2f, 2.2f, core);
                            break;
                        case BulletShape.Rice:
                            c.Ellipse(ox + center, oy + center, 6.5f, 3.2f, rim);
                            c.Ellipse(ox + center, oy + center, 4f, 1.6f, core);
                            break;
                        case BulletShape.Orb:
                            c.Ellipse(ox + center, oy + center, 6.5f, 6.5f, new Color32(rim.r, rim.g, rim.b, 120));
                            c.Ellipse(ox + center, oy + center, 5.2f, 5.2f, rim);
                            c.Ellipse(ox + center, oy + center, 3f, 3f, core);
                            break;
                        default:
                            c.Ellipse(ox + center, oy + center, 11.5f, 11.5f, rim);
                            c.Ellipse(ox + center, oy + center, 8.5f, 8.5f, core);
                            break;
                    }
                }
            }
        }

        // ボムの輪：白い輪（描画で広げながら薄くする）。
        private static void BombRing(Canvas c)
        {
            c.Ellipse(32, 32, 31, 31, new Color32(255, 230, 180, 200), ring: 3);
            c.Ellipse(32, 32, 27, 27, new Color32(255, 200, 120, 90), ring: 3);
        }

        // 体力のバー：橙の帯（左端を基準に横へ伸ばす）。
        private static void HpBar(Canvas c)
        {
            c.Rect(0, 0, 16, 4, new Color32(240, 120, 50, 255));
            c.Rect(0, 2, 16, 1, new Color32(255, 210, 140, 255));
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

            /// <summary>楕円を塗る。<paramref name="ring"/> が正なら、縁からその太さの輪だけを塗る。</summary>
            public void Ellipse(float cx, float cy, float rx, float ry, Color32 color, float ring = 0)
            {
                for (var py = (int)(cy - ry - 1); py <= (int)(cy + ry + 1); py++)
                {
                    for (var px = (int)(cx - rx - 1); px <= (int)(cx + rx + 1); px++)
                    {
                        var dx = (px + .5f - cx) / rx;
                        var dy = (py + .5f - cy) / ry;
                        var d = (dx * dx) + (dy * dy);
                        if (d > 1) { continue; }
                        if (ring > 0)
                        {
                            var ix = (px + .5f - cx) / Math.Max(.01f, rx - ring);
                            var iy = (py + .5f - cy) / Math.Max(.01f, ry - ring);
                            if ((ix * ix) + (iy * iy) < 1) { continue; }
                        }

                        Set(px, py, color);
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
