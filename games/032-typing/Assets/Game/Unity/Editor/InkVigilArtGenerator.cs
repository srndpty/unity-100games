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
    /// 1 単位 = 32 ピクセル。夜の回廊：紫がかった石の床と柱、奥の灯り。敵は墨のような影 3 種（段ごと）。乱数は使わず、毎回同じ絵にする。
    /// </summary>
    public static class InkVigilArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";
        public const int PixelsPerUnit = 32;

        public const string CorridorFile = "Corridor.png";
        public const string NearLineFile = "NearLine.png";
        public const string KeeperFile = "Keeper.png";
        public const string FlashFile = "Flash.png";
        public const string PuffFile = "Puff.png";

        /// <summary>段ごとの敵の絵（2 コマずつ）。</summary>
        public static readonly string[] EnemyFiles = { "Inkling0.png", "Inkling1.png", "Shade0.png", "Shade1.png", "Wraith0.png", "Wraith1.png" };

        /// <summary>回廊は縦 9 単位（画面の高さ）で、21:9 まで覆える幅に描く。</summary>
        public const int CorridorWidth = 672;
        public const int CorridorHeight = 288;

        private const int EnemyWidth = 48;
        private const int EnemyHeight = 72;

        private static readonly Color32 _outline = new(6, 4, 12, 255);

        private enum Pivot
        {
            Center,
            Bottom,
        }

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtRoot);
            var created = new List<(string Path, Pivot Pivot)>();
            Save(created, CorridorFile, CorridorWidth, CorridorHeight, Pivot.Center, Corridor);
            Save(created, NearLineFile, 448, 6, Pivot.Center, NearLine);
            Save(created, KeeperFile, 32, 44, Pivot.Bottom, Keeper);
            Save(created, FlashFile, 4, 4, Pivot.Center, c => c.Rect(0, 0, 4, 4, new Color32(255, 255, 255, 255)));
            Save(created, PuffFile, 48, 48, Pivot.Center, Puff);
            for (var i = 0; i < EnemyFiles.Length; i++)
            {
                var tier = (i / 2) + 1;
                var frame = i % 2;
                Save(created, EnemyFiles[i], EnemyWidth, EnemyHeight, Pivot.Bottom, c => Enemy(c, tier, frame));
            }

            AssetDatabase.Refresh();
            foreach (var (path, pivot) in created) { Import(path, pivot); }
        }

        private static void Save(List<(string, Pivot)> created, string file, int width, int height, Pivot pivot, Action<Canvas> paint)
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

        private static void Import(string path, Pivot pivot)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)(pivot == Pivot.Bottom ? SpriteAlignment.BottomCenter : SpriteAlignment.Center);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        /// <summary>
        /// 回廊：消失点（<see cref="InkVigilPerspective.HorizonY"/>）へ向かう床の継ぎ目と柱、奥の灯り。
        /// 床の線は敵の列の境目と同じ遠近（<see cref="InkVigilPerspective.Foot"/>）で引く。
        /// </summary>
        private static void Corridor(Canvas c)
        {
            var cx = CorridorWidth / 2f;
            var cy = CorridorHeight / 2f;
            var horizon = cy + (InkVigilPerspective.HorizonY * PixelsPerUnit);

            // 天井と壁の暗がり、床の石。下ほど明るい。
            for (var y = 0; y < CorridorHeight; y++)
            {
                for (var x = 0; x < CorridorWidth; x++)
                {
                    Color color;
                    if (y >= horizon)
                    {
                        var t = Mathf.Floor((y - horizon) / (CorridorHeight - horizon) * 6) / 6;
                        color = Color.Lerp(new Color(.09f, .06f, .14f), new Color(.03f, .02f, .05f), t);
                    }
                    else
                    {
                        var t = Mathf.Floor((horizon - y) / horizon * 8) / 8;
                        color = Color.Lerp(new Color(.13f, .1f, .19f), new Color(.2f, .16f, .27f), t);
                    }

                    c.Set(x, y, color);
                }
            }

            // 奥の灯り（消失点の周り）。
            for (var r = 26; r >= 4; r -= 4)
            {
                var a = (byte)(70 - (r * 2));
                c.Ellipse(cx, horizon, r * 1.6f, r * .7f, new Color32(255, 210, 140, a), blend: true);
            }

            // 床の継ぎ目：奥行きの等間隔の横線と、列の境目から消失点へ向かう線。
            var seam = new Color32(52, 42, 74, 255);
            for (var i = 1; i <= 12; i++)
            {
                var distance = Game.Core.Enemy.Far * i / 12;
                var y = cy + (InkVigilPerspective.Foot(0, 1, distance).y * PixelsPerUnit);
                c.Rect(0, Mathf.RoundToInt(y), CorridorWidth, 1, seam);
            }

            for (var edge = -3; edge <= 3; edge++)
            {
                var nearX = cx + ((edge - .5f) * InkVigilPerspective.LaneSpacing * PixelsPerUnit);
                var nearY = cy + ((InkVigilPerspective.NearY - 1.5f) * PixelsPerUnit);
                c.Line(nearX, nearY, cx, horizon, seam);
            }

            // 左右の柱：遠近で細くなる。
            var pillar = new Color32(30, 24, 44, 255);
            var light = new Color32(70, 58, 96, 255);
            for (var i = 0; i < 5; i++)
            {
                var s = InkVigilPerspective.ScaleAt(i / 5f);
                var height = (int)(220 * s);
                var width = Math.Max(2, (int)(26 * s));
                foreach (var side in new[] { -1, 1 })
                {
                    var x = (int)(cx + (side * 9.2f * PixelsPerUnit * s)) - (width / 2);
                    var bottom = (int)(cy + ((InkVigilPerspective.HorizonY + ((InkVigilPerspective.NearY - InkVigilPerspective.HorizonY) * s)) * PixelsPerUnit));
                    c.Rect(x, bottom, width, height, pillar);
                    c.Rect(side < 0 ? x + width - Math.Max(1, width / 5) : x, bottom, Math.Max(1, width / 5), height, light);
                }
            }
        }

        // 手前の線：灯りの色の帯。中央が明るい。
        private static void NearLine(Canvas c)
        {
            for (var x = 0; x < c.Width; x++)
            {
                var t = 1f - (Mathf.Abs(x - (c.Width / 2f)) / (c.Width / 2f));
                var core = (byte)(120 + (120 * t));
                c.Set(x, 2, new Color32(255, 214, 110, core));
                c.Set(x, 3, new Color32(255, 214, 110, core));
                c.Set(x, 1, new Color32(255, 170, 80, (byte)(core / 3)));
                c.Set(x, 4, new Color32(255, 170, 80, (byte)(core / 3)));
            }
        }

        // 見張り：背中を向けて灯りを掲げる人影。
        private static void Keeper(Canvas c)
        {
            var cloak = new Color32(46, 40, 70, 255);
            var cloakLight = new Color32(70, 62, 104, 255);
            var hood = new Color32(38, 32, 58, 255);
            c.Rect(9, 0, 14, 24, cloak);
            c.Rect(7, 2, 18, 16, cloak);
            c.Rect(9, 2, 3, 20, cloakLight);
            c.Ellipse(16, 27, 6, 6, hood);
            c.Rect(22, 18, 3, 12, cloak);
            c.Rect(23, 30, 1, 3, new Color32(120, 110, 90, 255));
            c.Ellipse(23.5f, 36, 3.5f, 4, new Color32(255, 214, 110, 255));
            c.Ellipse(23.5f, 36, 2, 2.5f, new Color32(255, 250, 220, 255));
            c.Outline(_outline);
        }

        // 撃破の煙：墨が散る輪と滴。
        private static void Puff(Canvas c)
        {
            var ink = new Color32(170, 150, 230, 255);
            var pale = new Color32(230, 220, 255, 255);
            c.Ring(24, 24, 18, 3, ink);
            c.Ring(24, 24, 11, 2, pale);
            for (var i = 0; i < 8; i++)
            {
                var angle = i * Mathf.PI / 4;
                c.Ellipse(24 + (Mathf.Cos(angle) * 21), 24 + (Mathf.Sin(angle) * 21), 2.2f, 2.2f, i % 2 == 0 ? pale : ink);
            }
        }

        /// <summary>敵の影。段 1＝小さな墨玉、段 2＝頭巾の影、段 3＝角と灯りを持つ大きな影。frame で裾が揺れる。</summary>
        private static void Enemy(Canvas c, int tier, int frame)
        {
            var body = tier switch
            {
                1 => new Color32(40, 30, 64, 255),
                2 => new Color32(34, 22, 52, 255),
                _ => new Color32(26, 14, 40, 255),
            };
            var shade = tier switch
            {
                1 => new Color32(70, 56, 108, 255),
                2 => new Color32(66, 44, 98, 255),
                _ => new Color32(84, 40, 92, 255),
            };
            var eye = tier == 3 ? new Color32(255, 120, 90, 255) : new Color32(255, 230, 140, 255);
            var cx = EnemyWidth / 2f;
            var sway = frame == 0 ? -1 : 1;

            switch (tier)
            {
                case 1:
                    c.Ellipse(cx, 16, 13, 13, body);
                    c.Ellipse(cx - 4, 21, 5, 5, shade);
                    for (var i = 0; i < 4; i++) { c.Ellipse(cx - 9 + (i * 6) + (sway * (i % 2)), 4, 3, 4, body); }
                    c.Rect((int)cx - 6, 15, 3, 4, eye);
                    c.Rect((int)cx + 3, 15, 3, 4, eye);
                    break;
                case 2:
                    for (var y = 0; y < 50; y++)
                    {
                        var half = 8 + (y < 34 ? (34 - y) / 4 : 0);
                        c.Rect((int)(cx - half + (y < 8 ? sway * ((y / 3) % 2) : 0)), y, half * 2, 1, body);
                    }

                    c.Ellipse(cx, 50, 11, 11, body);
                    c.Ellipse(cx, 48, 7, 7, new Color32(8, 4, 14, 255));
                    c.Rect((int)cx - 5, 47, 3, 3, eye);
                    c.Rect((int)cx + 2, 47, 3, 3, eye);
                    c.Rect((int)cx - 12, 12, 3, 22, shade);
                    break;
                default:
                    for (var y = 0; y < 58; y++)
                    {
                        var half = 10 + (y < 40 ? (40 - y) / 3 : 0);
                        c.Rect((int)(cx - half + (y < 10 ? sway * ((y / 3) % 2) : 0)), y, half * 2, 1, body);
                    }

                    c.Ellipse(cx, 58, 12, 10, body);
                    c.Rect((int)cx - 12, 62, 3, 8, shade);
                    c.Rect((int)cx + 9, 62, 3, 8, shade);
                    c.Rect((int)cx - 6, 56, 4, 3, eye);
                    c.Rect((int)cx + 2, 56, 4, 3, eye);
                    c.Rect((int)cx + 13, 18, 2, 18, shade);
                    c.Ellipse(cx + 14, 14 + sway, 4, 5, new Color32(120, 220, 200, 255));
                    c.Ellipse(cx + 14, 14 + sway, 2, 2.5f, new Color32(220, 255, 245, 255));
                    break;
            }

            c.Outline(_outline);
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

            public void Ellipse(float cx, float cy, float rx, float ry, Color32 color, bool blend = false)
            {
                for (var py = (int)(cy - ry - 1); py <= (int)(cy + ry + 1); py++)
                {
                    for (var px = (int)(cx - rx - 1); px <= (int)(cx + rx + 1); px++)
                    {
                        var dx = (px + .5f - cx) / rx;
                        var dy = (py + .5f - cy) / ry;
                        if ((dx * dx) + (dy * dy) > 1) { continue; }
                        if (!blend) { Set(px, py, color); }
                        else if (px >= 0 && py >= 0 && px < Width && py < Height)
                        {
                            var under = Pixels[(py * Width) + px];
                            Set(px, py, Color32.Lerp(under, new Color32(color.r, color.g, color.b, 255), color.a / 255f));
                        }
                    }
                }
            }

            public void Ring(float cx, float cy, float radius, float thickness, Color32 color)
            {
                for (var py = 0; py < Height; py++)
                {
                    for (var px = 0; px < Width; px++)
                    {
                        var d = Mathf.Sqrt(((px + .5f - cx) * (px + .5f - cx)) + ((py + .5f - cy) * (py + .5f - cy)));
                        if (Mathf.Abs(d - radius) <= thickness / 2) { Set(px, py, color); }
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
