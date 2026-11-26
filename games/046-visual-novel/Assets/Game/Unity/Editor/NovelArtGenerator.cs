#nullable enable
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
    /// 背景 5 枚（320×180）と立ち絵（人物 × 表情、64×112）をドット絵の PNG で作り、スプライトとして取り込む。
    /// 名前は <see cref="Scenario"/> の素材の表から決める（BG_{id}・Portrait_{人物}_{表情}）。既にある PNG は上書きしない。
    /// </summary>
    public static class NovelArtGenerator
    {
        public const string ArtDir = "Assets/Game/Unity/Art/";
        public const int PixelsPerUnit = 16;

        private static readonly Color32 Clear = new(0, 0, 0, 0);
        private static readonly Color32 Ink = new(24, 20, 28, 255);

        public static string BackgroundFile(string id) => $"BG_{id}.png";

        public static string PortraitFile(string character, string expression) => $"Portrait_{character}_{expression}.png";

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtDir);
            var created = false;
            var painters = new Dictionary<string, Action<Texture2D>>(StringComparer.Ordinal)
            {
                ["inn_hall"] = InnHall,
                ["lighthouse"] = Lighthouse,
                ["corridor"] = Corridor,
                ["storeroom"] = Storeroom,
                ["dawn_sea"] = DawnSea,
            };
            foreach (var id in Scenario.Backgrounds)
            {
                if (!painters.TryGetValue(id, out var paint)) { throw new InvalidOperationException($"背景 {id} の描き方がありません"); }
                created |= Write(BackgroundFile(id), 320, 180, paint);
            }

            foreach (var p in Scenario.Portraits)
            {
                var character = p.Key;
                var expression = p.Value;
                created |= Write(PortraitFile(character, expression), 64, 112, t => Person(t, character, expression));
            }

            if (created) { AssetDatabase.Refresh(); }
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtDir.TrimEnd('/') }))
            {
                Configure(AssetDatabase.GUIDToAssetPath(guid));
            }
        }

        private static bool Write(string file, int w, int h, Action<Texture2D> paint)
        {
            var path = ArtDir + file;
            if (File.Exists(path)) { return false; }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pixels = new Color32[w * h];
            for (int i = 0; i < pixels.Length; i++) { pixels[i] = Clear; }
            tex.SetPixels32(pixels);
            paint(tex);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            return true;
        }

        private static void Configure(string path)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) { return; }
            var dirty = false;
            if (importer.textureType != TextureImporterType.Sprite) { importer.textureType = TextureImporterType.Sprite; dirty = true; }
            if (importer.spriteImportMode != SpriteImportMode.Single) { importer.spriteImportMode = SpriteImportMode.Single; dirty = true; }
            if (!Mathf.Approximately(importer.spritePixelsPerUnit, PixelsPerUnit)) { importer.spritePixelsPerUnit = PixelsPerUnit; dirty = true; }
            if (importer.filterMode != FilterMode.Point) { importer.filterMode = FilterMode.Point; dirty = true; }
            if (importer.textureCompression != TextureImporterCompression.Uncompressed) { importer.textureCompression = TextureImporterCompression.Uncompressed; dirty = true; }
            if (importer.mipmapEnabled) { importer.mipmapEnabled = false; dirty = true; }
            if (dirty) { importer.SaveAndReimport(); }
        }

        // ---- 描く道具（y は下から） ----

        private static void Px(Texture2D t, int x, int y, Color32 c)
        {
            if (x >= 0 && y >= 0 && x < t.width && y < t.height) { t.SetPixel(x, y, c); }
        }

        private static void Rect(Texture2D t, int x0, int y0, int w, int h, Color32 c)
        {
            for (int y = Math.Max(0, y0); y < Math.Min(t.height, y0 + h); y++)
            {
                for (int x = Math.Max(0, x0); x < Math.Min(t.width, x0 + w); x++) { t.SetPixel(x, y, c); }
            }
        }

        private static void Ellipse(Texture2D t, float cx, float cy, float rx, float ry, Color32 c)
        {
            for (int y = Math.Max(0, (int)(cy - ry - 1)); y < Math.Min(t.height, (int)(cy + ry + 2)); y++)
            {
                for (int x = Math.Max(0, (int)(cx - rx - 1)); x < Math.Min(t.width, (int)(cx + rx + 2)); x++)
                {
                    var dx = (x + 0.5f - cx) / rx;
                    var dy = (y + 0.5f - cy) / ry;
                    if (dx * dx + dy * dy <= 1f) { t.SetPixel(x, y, c); }
                }
            }
        }

        /// <summary>多角形の内側を塗る（頂点は順に並べる）。</summary>
        private static void Poly(Texture2D t, Color32 c, params Vector2[] pts)
        {
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var p in pts)
            {
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }

            for (int y = Math.Max(0, (int)minY); y <= Math.Min(t.height - 1, (int)maxY); y++)
            {
                var sy = y + 0.5f;
                var xs = new List<float>();
                for (int i = 0; i < pts.Length; i++)
                {
                    var a = pts[i];
                    var b = pts[(i + 1) % pts.Length];
                    if ((a.y <= sy && b.y > sy) || (b.y <= sy && a.y > sy)) { xs.Add(a.x + (sy - a.y) / (b.y - a.y) * (b.x - a.x)); }
                }

                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    for (int x = Math.Max(0, Mathf.CeilToInt(xs[k] - 0.5f)); x <= Math.Min(t.width - 1, Mathf.FloorToInt(xs[k + 1] - 0.5f)); x++) { t.SetPixel(x, y, c); }
                }
            }
        }

        private static void Line(Texture2D t, int x0, int y0, int x1, int y1, Color32 c)
        {
            int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            var err = dx + dy;
            while (true)
            {
                Px(t, x0, y0, c);
                if (x0 == x1 && y0 == y1) { return; }
                var e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        /// <summary>縦のグラデーション（下の色 → 上の色）。</summary>
        private static void Gradient(Texture2D t, int y0, int y1, Color32 bottom, Color32 top)
        {
            for (int y = y0; y < y1; y++)
            {
                var c = Color32.Lerp(bottom, top, (y - y0) / (float)Math.Max(1, y1 - y0 - 1));
                Rect(t, 0, y, t.width, 1, c);
            }
        }

        /// <summary>斜めの雨筋（決まった並びで、毎回同じ絵になる）。</summary>
        private static void Rain(Texture2D t, Color32 c, int step)
        {
            for (int i = 0; i < t.width + t.height; i += step)
            {
                var x = i;
                var y = t.height - 1 - ((i * 37) % 23);
                for (int k = 0; k < 6; k++) { Px(t, x - k, y - k * 2, c); }
                for (int k = 0; k < 6; k++) { Px(t, x - 70 - k, y - 90 - k * 2, c); }
            }
        }

        private static Color32 C(int r, int g, int b, int a = 255) => new((byte)r, (byte)g, (byte)b, (byte)a);

        // ---- 背景 ----

        private static void InnHall(Texture2D t)
        {
            Gradient(t, 0, 180, C(70, 44, 32), C(104, 70, 48));
            for (int x = 0; x < 320; x += 24) { Rect(t, x, 46, 2, 134, C(84, 54, 38)); }
            Rect(t, 0, 0, 320, 44, C(58, 38, 28));
            for (int y = 4; y < 44; y += 8) { Rect(t, 0, y, 320, 1, C(46, 30, 22)); }
            Rect(t, 0, 44, 320, 4, C(40, 26, 20));

            // 窓（嵐の夜）
            Rect(t, 26, 78, 92, 78, C(52, 34, 24));
            Rect(t, 30, 82, 84, 70, C(18, 24, 42));
            Ellipse(t, 92, 132, 18, 6, C(30, 36, 56));
            Rect(t, 70, 82, 4, 70, C(52, 34, 24));
            Rect(t, 30, 115, 84, 3, C(52, 34, 24));
            for (int i = 0; i < 9; i++) { Line(t, 34 + i * 9, 150, 30 + i * 9, 140, C(70, 86, 120)); }
            Rect(t, 96, 120, 2, 14, C(200, 200, 190));
            Rect(t, 95, 133, 4, 3, C(140, 132, 90));

            // 暖炉
            Rect(t, 160, 44, 82, 74, C(96, 92, 88));
            for (int y = 48; y < 118; y += 9) { Rect(t, 160, y, 82, 1, C(76, 72, 70)); }
            Rect(t, 152, 114, 98, 6, C(70, 50, 36));
            Rect(t, 176, 44, 50, 44, C(30, 20, 16));
            Ellipse(t, 201, 54, 20, 12, C(200, 96, 36));
            Ellipse(t, 201, 54, 12, 8, C(250, 170, 70));
            Ellipse(t, 201, 52, 5, 4, C(255, 230, 150));

            // 柱時計
            Rect(t, 272, 44, 30, 120, C(62, 36, 24));
            Rect(t, 276, 48, 22, 70, C(40, 24, 16));
            Ellipse(t, 287, 140, 11, 11, C(228, 214, 180));
            Line(t, 287, 140, 287, 148, Ink);
            Line(t, 287, 140, 292, 140, Ink);
            Rect(t, 286, 60, 3, 40, C(180, 150, 70));
            Ellipse(t, 287, 60, 5, 5, C(210, 180, 90));

            // 天井の灯り
            Ellipse(t, 160, 176, 70, 22, C(150, 104, 64));
            Ellipse(t, 160, 172, 8, 6, C(255, 214, 140));

            // テーブル
            Rect(t, 60, 30, 120, 6, C(120, 80, 52));
            Rect(t, 68, 0, 6, 30, C(90, 60, 40));
            Rect(t, 166, 0, 6, 30, C(90, 60, 40));
            Rect(t, 100, 36, 8, 6, C(230, 226, 210));
            Rect(t, 130, 36, 8, 6, C(230, 226, 210));
        }

        private static void Lighthouse(Texture2D t)
        {
            Gradient(t, 40, 180, C(46, 54, 70), C(22, 26, 38));
            for (int i = 0; i < 7; i++) { Ellipse(t, 20 + i * 50, 150 + (i % 3) * 8, 40, 12, C(34, 38, 52)); }
            Gradient(t, 0, 46, C(12, 30, 36), C(28, 52, 60));
            for (int i = 0; i < 40; i++) { Rect(t, (i * 53) % 300, 6 + (i * 17) % 36, 10, 1, C(70, 100, 108)); }

            // 崖
            Poly(t, C(36, 34, 36), new Vector2(150, 0), new Vector2(320, 0), new Vector2(320, 80), new Vector2(260, 84), new Vector2(200, 70), new Vector2(170, 40));
            Poly(t, C(44, 64, 40), new Vector2(196, 70), new Vector2(262, 86), new Vector2(320, 82), new Vector2(320, 76), new Vector2(262, 80), new Vector2(200, 66));

            // 灯台
            Poly(t, C(226, 226, 220), new Vector2(222, 78), new Vector2(252, 78), new Vector2(246, 144), new Vector2(228, 144));
            Poly(t, C(170, 60, 52), new Vector2(225, 104), new Vector2(249, 104), new Vector2(248, 116), new Vector2(226, 116));
            Rect(t, 224, 144, 26, 4, C(60, 60, 64));
            Rect(t, 228, 148, 18, 12, C(150, 140, 90));
            Rect(t, 231, 150, 12, 8, C(90, 86, 70));
            Poly(t, C(60, 60, 64), new Vector2(226, 160), new Vector2(248, 160), new Vector2(237, 170));
            Rect(t, 234, 84, 6, 10, C(70, 60, 50));

            // 坂道
            Poly(t, C(70, 62, 54), new Vector2(170, 40), new Vector2(180, 40), new Vector2(226, 78), new Vector2(220, 78));
            Rain(t, C(120, 134, 156), 11);
        }

        private static void Corridor(Texture2D t)
        {
            Rect(t, 0, 0, 320, 180, C(30, 26, 30));
            Poly(t, C(54, 42, 34), new Vector2(0, 0), new Vector2(320, 0), new Vector2(190, 70), new Vector2(130, 70));
            for (int i = 1; i < 8; i++) { Line(t, 160 - i * 22, 0, 160 - i * 4, 70, C(44, 34, 28)); Line(t, 160 + i * 22, 0, 160 + i * 4, 70, C(44, 34, 28)); }
            Poly(t, C(62, 50, 46), new Vector2(0, 0), new Vector2(130, 70), new Vector2(130, 140), new Vector2(0, 180));
            Poly(t, C(58, 46, 42), new Vector2(320, 0), new Vector2(190, 70), new Vector2(190, 140), new Vector2(320, 180));
            Poly(t, C(40, 34, 36), new Vector2(0, 180), new Vector2(130, 140), new Vector2(190, 140), new Vector2(320, 180));
            Rect(t, 130, 70, 60, 70, C(22, 20, 26));

            // 扉（左に 2 枚、右に 1 枚。右の扉は少し開いて光が漏れる）
            Poly(t, C(86, 60, 42), new Vector2(20, 24), new Vector2(60, 46), new Vector2(60, 122), new Vector2(20, 136));
            Poly(t, C(86, 60, 42), new Vector2(84, 54), new Vector2(108, 64), new Vector2(108, 126), new Vector2(84, 130));
            Poly(t, C(86, 60, 42), new Vector2(250, 40), new Vector2(290, 22), new Vector2(290, 140), new Vector2(250, 126));
            Poly(t, C(236, 210, 150), new Vector2(246, 42), new Vector2(250, 40), new Vector2(250, 126), new Vector2(246, 124));
            Px(t, 54, 82, C(200, 180, 110));
            Px(t, 104, 94, C(200, 180, 110));
            Px(t, 256, 84, C(200, 180, 110));

            // 懐中電灯の丸い光
            for (int y = 0; y < 180; y++)
            {
                for (int x = 0; x < 320; x++)
                {
                    var d = Mathf.Sqrt((x - 160f) * (x - 160f) + (y - 70f) * (y - 70f) * 2f);
                    if (d > 120f)
                    {
                        var p = t.GetPixel(x, y);
                        t.SetPixel(x, y, p * 0.6f);
                    }
                }
            }
        }

        private static void Storeroom(Texture2D t)
        {
            Rect(t, 0, 0, 320, 180, C(50, 48, 46));
            for (int y = 40; y < 180; y += 14)
            {
                for (int x = (y / 14 % 2) * 14; x < 320; x += 28) { Rect(t, x, y, 26, 12, C(64, 60, 56)); }
            }

            Rect(t, 0, 0, 320, 40, C(38, 34, 30));
            for (int i = 0; i < 5; i++)
            {
                var x = 20 + i * 36;
                Rect(t, x, 30, 30, 46, C(104, 70, 40));
                Rect(t, x, 38, 30, 3, C(60, 60, 64));
                Rect(t, x, 64, 30, 3, C(60, 60, 64));
                Ellipse(t, x + 15, 76, 15, 4, C(80, 54, 32));
            }

            // 棚と縄
            Rect(t, 220, 100, 90, 4, C(90, 64, 40));
            Rect(t, 220, 140, 90, 4, C(90, 64, 40));
            Ellipse(t, 240, 112, 10, 8, C(170, 150, 100));
            Ellipse(t, 240, 112, 5, 4, C(110, 96, 64));
            Rect(t, 270, 104, 10, 18, C(70, 110, 120));
            Rect(t, 290, 104, 8, 14, C(150, 60, 50));

            // 布に包まれたレンズ
            Ellipse(t, 265, 50, 26, 18, C(214, 208, 190));
            Line(t, 245, 50, 285, 50, C(170, 164, 150));
            Ellipse(t, 265, 58, 6, 4, C(160, 200, 210));

            // ランタンの光
            Ellipse(t, 160, 150, 60, 30, C(92, 80, 60));
            Rect(t, 156, 140, 8, 12, C(250, 210, 120));
            Rect(t, 158, 152, 4, 6, C(60, 60, 64));
        }

        private static void DawnSea(Texture2D t)
        {
            Gradient(t, 60, 180, C(246, 176, 116), C(124, 160, 200));
            Ellipse(t, 200, 60, 22, 22, C(255, 226, 170));
            Gradient(t, 0, 60, C(40, 70, 96), C(110, 128, 150));
            for (int i = 0; i < 30; i++) { Rect(t, 170 + (i * 13) % 60, 8 + i * 2 % 50, 12 - (i % 5), 1, C(250, 210, 150)); }
            for (int i = 0; i < 6; i++) { Ellipse(t, 40 + i * 60, 140 + (i % 2) * 14, 30, 5, C(250, 214, 190)); }

            // 岬と灯台（遠く）
            Poly(t, C(52, 52, 60), new Vector2(0, 60), new Vector2(0, 92), new Vector2(40, 96), new Vector2(80, 70), new Vector2(96, 60));
            Rect(t, 26, 94, 8, 26, C(232, 228, 220));
            Rect(t, 25, 120, 10, 4, C(80, 80, 84));
            Rect(t, 27, 104, 6, 3, C(170, 60, 52));

            // 船
            Poly(t, C(60, 46, 40), new Vector2(118, 54), new Vector2(150, 54), new Vector2(146, 48), new Vector2(122, 48));
            Rect(t, 133, 54, 2, 16, C(60, 46, 40));
            Poly(t, C(230, 220, 200), new Vector2(135, 56), new Vector2(135, 68), new Vector2(146, 57));
        }

        // ---- 立ち絵 ----

        private sealed class Look
        {
            public Color32 Skin;
            public Color32 Hair;
            public Color32 Coat;
            public Color32 Accent;
            public bool LongHair;
            public bool Glasses;
            public bool Beard;
        }

        private static Look LookOf(string character) => character switch
        {
            "mio" => new Look { Skin = C(244, 214, 190), Hair = C(36, 40, 66), Coat = C(226, 216, 196), Accent = C(143, 195, 224), LongHair = true },
            "ren" => new Look { Skin = C(238, 206, 178), Hair = C(70, 46, 34), Coat = C(150, 104, 56), Accent = C(232, 228, 218), Glasses = true },
            "sosuke" => new Look { Skin = C(204, 160, 120), Hair = C(196, 196, 196), Coat = C(216, 176, 48), Accent = C(90, 90, 96), Beard = true },
            _ => throw new ArgumentException($"人物 {character} の姿がありません", nameof(character)),
        };

        private static void Person(Texture2D t, string character, string expression)
        {
            var l = LookOf(character);
            const float cx = 32f;

            // 体（肩から下）
            Poly(t, Ink, new Vector2(5, 0), new Vector2(59, 0), new Vector2(55, 46), new Vector2(42, 54), new Vector2(22, 54), new Vector2(9, 46));
            Poly(t, l.Coat, new Vector2(6, 0), new Vector2(58, 0), new Vector2(54, 45), new Vector2(42, 53), new Vector2(22, 53), new Vector2(10, 45));
            Poly(t, l.Accent, new Vector2(24, 53), new Vector2(40, 53), new Vector2(36, 40), new Vector2(28, 40));
            if (character == "mio") { Rect(t, 18, 44, 28, 6, l.Accent); }
            if (character == "sosuke") { Rect(t, 31, 0, 2, 42, C(150, 120, 30)); }
            if (character == "ren") { Line(t, 32, 40, 32, 0, C(110, 76, 40)); }

            // 首と頭
            Rect(t, 27, 52, 10, 8, l.Skin);
            if (l.LongHair) { Poly(t, l.Hair, new Vector2(14, 44), new Vector2(50, 44), new Vector2(48, 84), new Vector2(16, 84)); }
            Ellipse(t, cx, 76, 16, 19, Ink);
            Ellipse(t, cx, 76, 15, 18, l.Skin);
            Ellipse(t, cx, 88, 16, 10, l.Hair);
            Rect(t, 16, 80, 4, 10, l.Hair);
            Rect(t, 44, 80, 4, 10, l.Hair);
            if (!l.LongHair) { Ellipse(t, cx, 92, 15, 6, l.Hair); }
            if (l.Beard)
            {
                Ellipse(t, cx, 62, 11, 7, l.Hair);
                Ellipse(t, cx, 66, 6, 2, l.Skin);
            }

            Face(t, expression, l);
        }

        private static void Face(Texture2D t, string expression, Look l)
        {
            const int ly = 76;
            switch (expression)
            {
                case "calm":
                    Rect(t, 25, ly, 2, 3, Ink);
                    Rect(t, 37, ly, 2, 3, Ink);
                    Rect(t, 30, 66, 4, 1, C(150, 80, 70));
                    break;
                case "smile":
                    Px(t, 24, ly + 1, Ink); Px(t, 25, ly + 2, Ink); Px(t, 26, ly + 2, Ink); Px(t, 27, ly + 1, Ink);
                    Px(t, 36, ly + 1, Ink); Px(t, 37, ly + 2, Ink); Px(t, 38, ly + 2, Ink); Px(t, 39, ly + 1, Ink);
                    Px(t, 29, 67, C(150, 80, 70)); Rect(t, 30, 66, 4, 1, C(150, 80, 70)); Px(t, 34, 67, C(150, 80, 70));
                    Rect(t, 22, 71, 3, 1, C(240, 160, 150));
                    Rect(t, 39, 71, 3, 1, C(240, 160, 150));
                    break;
                case "worried":
                    Rect(t, 25, ly, 2, 3, Ink);
                    Rect(t, 37, ly, 2, 3, Ink);
                    Line(t, 23, ly + 5, 27, ly + 7, l.Hair);
                    Line(t, 41, ly + 5, 37, ly + 7, l.Hair);
                    Rect(t, 31, 65, 2, 2, C(150, 80, 70));
                    break;
                case "cold":
                    Rect(t, 24, ly + 1, 4, 1, Ink);
                    Rect(t, 36, ly + 1, 4, 1, Ink);
                    Rect(t, 23, ly + 4, 6, 1, l.Hair);
                    Rect(t, 35, ly + 4, 6, 1, l.Hair);
                    Rect(t, 31, 66, 5, 1, C(130, 70, 64));
                    break;
                default:
                    throw new ArgumentException($"表情 {expression} の描き方がありません", nameof(expression));
            }

            if (l.Glasses)
            {
                foreach (var gx in new[] { 26, 38 })
                {
                    Line(t, gx - 4, ly - 2, gx + 3, ly - 2, C(80, 80, 90));
                    Line(t, gx - 4, ly + 4, gx + 3, ly + 4, C(80, 80, 90));
                    Line(t, gx - 4, ly - 2, gx - 4, ly + 4, C(80, 80, 90));
                    Line(t, gx + 3, ly - 2, gx + 3, ly + 4, C(80, 80, 90));
                }

                Line(t, 29, ly + 2, 34, ly + 2, C(80, 80, 90));
            }
        }
    }
}
