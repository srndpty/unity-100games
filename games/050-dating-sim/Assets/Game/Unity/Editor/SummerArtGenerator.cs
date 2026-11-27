#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>
    /// 背景 7 枚（320×180）と立ち絵（人物 × 表情、64×112）をドット絵の PNG で作り、スプライトとして取り込む。
    /// 名前は台本の背景・人物・表情の表から決める（BG_{id}・Portrait_{人物}_{表情}）。既にある PNG は上書きしない。
    /// </summary>
    public static class SummerArtGenerator
    {
        public const string ArtDir = "Assets/Game/Unity/Art/";
        public const int PixelsPerUnit = 16;

        private static readonly Color32 Clear = new(0, 0, 0, 0);
        private static readonly Color32 Ink = new(30, 24, 36, 255);

        public static string BackgroundFile(string id) => $"BG_{id}.png";

        public static string PortraitFile(string character, string face) => $"Portrait_{character}_{face}.png";

        public static void GenerateIfMissing()
        {
            var book = SummerBuildCheck.LoadBook();
            Directory.CreateDirectory(ArtDir);
            var created = false;
            var painters = new Dictionary<string, Action<Texture2D>>(StringComparer.Ordinal)
            {
                ["room"] = Room,
                ["town"] = Town,
                ["club"] = Club,
                ["library"] = Library,
                ["roof"] = Roof,
                ["festival"] = Festival,
                ["meteor"] = Meteor,
            };
            foreach (var bg in book.Backgrounds)
            {
                if (!painters.TryGetValue(bg.Id, out var paint)) { throw new InvalidOperationException($"背景 {bg.Id} の描き方がありません"); }
                created |= Write(BackgroundFile(bg.Id), 320, 180, paint);
            }

            foreach (var (character, face) in SummerStage.PortraitsOf(book))
            {
                created |= Write(PortraitFile(character, face), 64, 112, t => Person(t, character, face));
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

        /// <summary>決まった並びの星（毎回同じ絵になる）。</summary>
        private static void Stars(Texture2D t, int y0, int count, Color32 c)
        {
            for (int i = 0; i < count; i++)
            {
                var x = (i * 97 + 13) % t.width;
                var y = y0 + (i * 53 + 7) % (t.height - y0);
                Px(t, x, y, c);
                if (i % 7 == 0)
                {
                    Px(t, x + 1, y, c);
                    Px(t, x - 1, y, c);
                    Px(t, x, y + 1, c);
                    Px(t, x, y - 1, c);
                }
            }
        }

        private static Color32 C(int r, int g, int b, int a = 255) => new((byte)r, (byte)g, (byte)b, (byte)a);

        // ---- 背景 ----

        private static void Room(Texture2D t)
        {
            Gradient(t, 0, 180, C(214, 196, 170), C(236, 222, 200));
            Rect(t, 0, 0, 320, 40, C(170, 128, 92));
            for (int x = 0; x < 320; x += 32) { Rect(t, x, 0, 1, 40, C(150, 110, 80)); }

            // 窓（夏の青空と入道雲）
            Rect(t, 196, 70, 100, 86, C(250, 246, 236));
            Gradient(t, 74, 152, C(150, 206, 240), C(70, 150, 226));
            Rect(t, 0, 74, 200, 78, C(214, 196, 170));
            Rect(t, 292, 74, 28, 78, C(226, 210, 186));
            Ellipse(t, 230, 100, 22, 14, C(255, 255, 255));
            Ellipse(t, 250, 108, 18, 16, C(255, 255, 255));
            Ellipse(t, 268, 98, 16, 10, C(250, 250, 250));
            Rect(t, 244, 74, 3, 78, C(250, 246, 236));
            Rect(t, 196, 112, 100, 3, C(250, 246, 236));

            // 星図のポスター
            Rect(t, 120, 96, 50, 60, C(28, 40, 80));
            for (int i = 0; i < 18; i++) { Px(t, 124 + (i * 17) % 42, 100 + (i * 11) % 52, C(240, 240, 200)); }
            Line(t, 130, 140, 146, 128, C(140, 160, 220));
            Line(t, 146, 128, 160, 146, C(140, 160, 220));

            // 机とスタンド
            Rect(t, 20, 60, 120, 6, C(140, 96, 60));
            Rect(t, 26, 40, 6, 20, C(110, 76, 48));
            Rect(t, 128, 40, 6, 20, C(110, 76, 48));
            Rect(t, 40, 66, 30, 4, C(240, 236, 224));
            Rect(t, 44, 70, 22, 3, C(90, 140, 200));
            Line(t, 110, 66, 116, 92, C(70, 70, 80));
            Poly(t, C(250, 214, 110), new Vector2(108, 92), new Vector2(124, 92), new Vector2(118, 84));

            // ベッド
            Rect(t, 160, 20, 160, 30, C(120, 170, 210));
            Rect(t, 160, 46, 160, 6, C(236, 240, 246));
            Rect(t, 290, 50, 28, 14, C(250, 250, 250));
        }

        private static void Town(Texture2D t)
        {
            Gradient(t, 60, 180, C(170, 220, 246), C(80, 150, 226));
            Ellipse(t, 60, 150, 30, 10, C(255, 255, 255));
            Ellipse(t, 250, 160, 40, 9, C(250, 250, 250));
            Rect(t, 0, 0, 320, 40, C(120, 120, 126));
            Rect(t, 0, 38, 320, 4, C(200, 196, 186));
            for (int x = 0; x < 320; x += 40) { Rect(t, x + 10, 18, 20, 2, C(236, 236, 230)); }

            // 店並び
            var colors = new[] { C(214, 160, 120), C(190, 200, 176), C(226, 206, 160), C(176, 186, 214), C(220, 176, 176) };
            for (int i = 0; i < 5; i++)
            {
                var x = i * 64;
                var h = 80 + (i % 3) * 16;
                Rect(t, x, 42, 62, h, colors[i]);
                Rect(t, x + 6, 50, 50, 30, C(70, 90, 110));
                Rect(t, x + 8, 52, 46, 26, C(150, 190, 210));
                for (int k = 0; k < 6; k++) { Rect(t, x + 2 + k * 10, 82, 10, 8, k % 2 == 0 ? C(230, 80, 70) : C(250, 250, 240)); }
                Rect(t, x + 10, 96, 40, 10, C(60, 50, 50));
                Rect(t, x + 12, 98, 36, 6, C(250, 236, 200));
            }

            // 電柱と電線
            Rect(t, 150, 42, 4, 120, C(110, 100, 90));
            Rect(t, 140, 150, 24, 3, C(110, 100, 90));
            Line(t, 0, 156, 150, 152, C(60, 60, 70));
            Line(t, 154, 152, 320, 158, C(60, 60, 70));
        }

        private static void Club(Texture2D t)
        {
            Gradient(t, 0, 180, C(190, 186, 170), C(214, 210, 196));
            Rect(t, 0, 0, 320, 36, C(130, 110, 90));

            // 窓
            Rect(t, 200, 90, 100, 70, C(240, 240, 236));
            Gradient(t, 94, 156, C(240, 200, 150), C(130, 170, 220));
            Rect(t, 0, 94, 204, 62, C(190, 186, 170));
            Rect(t, 296, 94, 24, 62, C(204, 200, 186));
            Rect(t, 248, 94, 3, 62, C(240, 240, 236));

            // 黒板と星座の図
            Rect(t, 20, 80, 150, 80, C(110, 90, 60));
            Rect(t, 24, 84, 142, 72, C(40, 70, 56));
            var pts = new[] { (40, 140), (62, 128), (84, 136), (104, 118), (128, 126), (148, 104) };
            for (int i = 0; i + 1 < pts.Length; i++) { Line(t, pts[i].Item1, pts[i].Item2, pts[i + 1].Item1, pts[i + 1].Item2, C(220, 230, 220)); }
            foreach (var (x, y) in pts) { Rect(t, x - 1, y - 1, 3, 3, C(255, 250, 200)); }
            Rect(t, 40, 96, 40, 2, C(230, 230, 230));
            Rect(t, 40, 102, 28, 2, C(230, 230, 230));

            // 望遠鏡（三脚）
            Line(t, 236, 36, 256, 76, C(60, 60, 70));
            Line(t, 276, 36, 256, 76, C(60, 60, 70));
            Line(t, 256, 36, 256, 76, C(60, 60, 70));
            Poly(t, C(240, 240, 246), new Vector2(226, 74), new Vector2(286, 98), new Vector2(282, 108), new Vector2(222, 84));
            Rect(t, 282, 98, 8, 12, C(60, 60, 70));

            // 机と星座早見盤
            Rect(t, 40, 36, 120, 8, C(150, 110, 70));
            Ellipse(t, 100, 46, 18, 4, C(40, 50, 110));
            Ellipse(t, 100, 46, 12, 3, C(80, 100, 180));
        }

        private static void Library(Texture2D t)
        {
            Rect(t, 0, 0, 320, 180, C(206, 190, 160));
            Rect(t, 0, 0, 320, 30, C(150, 116, 80));

            // 高い窓と差す光
            for (int k = 0; k < 2; k++)
            {
                var x = 120 + k * 70;
                Rect(t, x, 90, 50, 80, C(250, 246, 226));
                Gradient(t, 94, 166, C(200, 230, 246), C(150, 200, 240));
                Rect(t, x + 24, 90, 2, 80, C(220, 210, 180));
            }

            Rect(t, 0, 94, 120, 72, C(206, 190, 160));
            Rect(t, 170, 94, 20, 72, C(206, 190, 160));
            Rect(t, 240, 94, 80, 72, C(206, 190, 160));
            Poly(t, C(236, 226, 196, 255), new Vector2(120, 90), new Vector2(170, 90), new Vector2(200, 30), new Vector2(110, 30));

            // 本棚（左右）
            var spines = new[] { C(160, 60, 60), C(60, 90, 150), C(60, 130, 90), C(200, 160, 70), C(120, 80, 130), C(210, 120, 80) };
            foreach (var x0 in new[] { 0, 250 })
            {
                Rect(t, x0, 30, 70, 150, C(120, 84, 54));
                for (int shelf = 0; shelf < 5; shelf++)
                {
                    var y = 36 + shelf * 28;
                    Rect(t, x0 + 4, y + 22, 62, 3, C(96, 64, 40));
                    for (int b = 0; b < 10; b++)
                    {
                        var h = 14 + (b * 7 + shelf * 3) % 7;
                        Rect(t, x0 + 6 + b * 6, y + 22 - h, 5, h, spines[(b + shelf) % spines.Length]);
                    }
                }
            }

            // 机と本
            Rect(t, 90, 40, 140, 6, C(170, 130, 90));
            Rect(t, 100, 20, 6, 20, C(130, 96, 64));
            Rect(t, 214, 20, 6, 20, C(130, 96, 64));
            Rect(t, 140, 46, 30, 4, C(250, 246, 236));
            Rect(t, 154, 46, 2, 4, C(160, 60, 60));
        }

        private static void Roof(Texture2D t)
        {
            Gradient(t, 40, 180, C(40, 50, 100), C(10, 14, 40));
            Stars(t, 60, 90, C(230, 236, 255));
            Ellipse(t, 250, 146, 14, 14, C(255, 246, 210));
            Ellipse(t, 256, 150, 12, 12, C(30, 38, 84));

            // 遠くの町の灯り
            Rect(t, 0, 40, 320, 16, C(26, 30, 56));
            for (int i = 0; i < 40; i++) { Px(t, (i * 41) % 320, 44 + (i * 13) % 10, C(250, 210, 120)); }

            // 屋上の床とフェンス
            Rect(t, 0, 0, 320, 40, C(90, 92, 104));
            for (int x = 0; x < 320; x += 40) { Line(t, x, 0, x + 20, 40, C(80, 82, 94)); }
            Rect(t, 0, 40, 320, 2, C(150, 154, 170));
            Rect(t, 0, 80, 320, 2, C(150, 154, 170));
            for (int x = 0; x < 320; x += 6) { Line(t, x, 42, x + 6, 80, C(110, 114, 130)); Line(t, x + 6, 42, x, 80, C(110, 114, 130)); }
            for (int x = 0; x < 320; x += 48) { Rect(t, x, 40, 3, 44, C(150, 154, 170)); }

            // 給水塔
            Rect(t, 20, 80, 50, 40, C(150, 160, 176));
            Ellipse(t, 45, 120, 25, 6, C(170, 180, 196));
            Rect(t, 24, 40, 4, 40, C(120, 128, 140));
            Rect(t, 62, 40, 4, 40, C(120, 128, 140));
        }

        private static void Festival(Texture2D t)
        {
            Gradient(t, 50, 180, C(60, 40, 90), C(16, 18, 48));
            Stars(t, 120, 30, C(220, 220, 250));

            // 花火
            for (int k = 0; k < 2; k++)
            {
                var cx = 70 + k * 170;
                var cy = 150 - k * 10;
                var col = k == 0 ? C(255, 190, 120) : C(160, 220, 255);
                for (int a = 0; a < 16; a++)
                {
                    var ang = a * Mathf.PI / 8f;
                    Line(t, cx, cy, cx + (int)(Mathf.Cos(ang) * 20), cy + (int)(Mathf.Sin(ang) * 20), col);
                }
            }

            // 屋台
            Rect(t, 0, 0, 320, 30, C(60, 50, 50));
            for (int i = 0; i < 4; i++)
            {
                var x = 10 + i * 80;
                Rect(t, x, 30, 64, 40, C(120, 70, 50));
                for (int k = 0; k < 8; k++) { Rect(t, x + k * 8, 70, 8, 10, k % 2 == 0 ? C(220, 60, 60) : C(250, 240, 230)); }
                Rect(t, x + 6, 36, 52, 20, C(250, 220, 160));
            }

            // 提灯の列
            Line(t, 0, 120, 320, 110, C(40, 30, 30));
            for (int i = 0; i < 12; i++)
            {
                var x = 12 + i * 27;
                var y = 116 - i * 10 / 12;
                Ellipse(t, x, y - 8, 6, 8, C(240, 110, 60));
                Ellipse(t, x, y - 8, 3, 5, C(255, 200, 120));
            }
        }

        private static void Meteor(Texture2D t)
        {
            Gradient(t, 0, 180, C(30, 40, 90), C(4, 6, 24));
            Stars(t, 30, 160, C(236, 240, 255));

            // 天の川
            for (int i = 0; i < 400; i++)
            {
                var x = (i * 37) % 320;
                var y = 40 + x * 120 / 320 + ((i * 29) % 30) - 15;
                Px(t, x, y, C(120, 130, 190));
            }

            // 流れ星
            for (int k = 0; k < 7; k++)
            {
                var x = 40 + k * 41;
                var y = 170 - (k * 23) % 60;
                Line(t, x, y, x - 22, y - 14, C(170, 190, 255));
                Line(t, x, y, x - 10, y - 6, C(255, 255, 255));
                Px(t, x, y, C(255, 255, 230));
            }

            // 丘のシルエット
            Poly(t, C(10, 14, 26), new Vector2(0, 0), new Vector2(320, 0), new Vector2(320, 30), new Vector2(240, 46), new Vector2(160, 38), new Vector2(80, 50), new Vector2(0, 36));
            Rect(t, 196, 40, 3, 16, C(10, 14, 26));
            Poly(t, C(10, 14, 26), new Vector2(188, 54), new Vector2(208, 62), new Vector2(206, 66), new Vector2(186, 58));
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
        }

        private static Look LookOf(string character) => character switch
        {
            "sumi" => new Look { Skin = C(246, 220, 200), Hair = C(30, 34, 58), Coat = C(236, 240, 250), Accent = C(90, 130, 200), LongHair = true, Glasses = true },
            "shiori" => new Look { Skin = C(248, 222, 198), Hair = C(120, 74, 46), Coat = C(240, 180, 150), Accent = C(250, 240, 220) },
            _ => throw new ArgumentException($"人物 {character} の姿がありません", nameof(character)),
        };

        private static void Person(Texture2D t, string character, string face)
        {
            var l = LookOf(character);
            const float cx = 32f;

            // 体（肩から下）
            Poly(t, Ink, new Vector2(5, 0), new Vector2(59, 0), new Vector2(55, 46), new Vector2(42, 54), new Vector2(22, 54), new Vector2(9, 46));
            Poly(t, l.Coat, new Vector2(6, 0), new Vector2(58, 0), new Vector2(54, 45), new Vector2(42, 53), new Vector2(22, 53), new Vector2(10, 45));
            Poly(t, l.Accent, new Vector2(24, 53), new Vector2(40, 53), new Vector2(34, 40), new Vector2(30, 40));
            if (character == "sumi") { Poly(t, C(200, 60, 70), new Vector2(29, 44), new Vector2(35, 44), new Vector2(34, 30), new Vector2(30, 30)); }
            if (character == "shiori")
            {
                for (int y = 4; y < 40; y += 8) { Px(t, 32, y, C(250, 240, 220)); }
                Rect(t, 31, 0, 2, 44, C(220, 150, 120));
            }

            // 首と頭
            Rect(t, 27, 52, 10, 8, l.Skin);
            if (l.LongHair)
            {
                // 長い髪は肩の左右に垂らし、胸元（リボン）は隠さない。
                Poly(t, l.Hair, new Vector2(12, 34), new Vector2(23, 40), new Vector2(23, 84), new Vector2(15, 84));
                Poly(t, l.Hair, new Vector2(41, 40), new Vector2(52, 34), new Vector2(49, 84), new Vector2(41, 84));
            }
            else { Poly(t, l.Hair, new Vector2(15, 62), new Vector2(49, 62), new Vector2(49, 84), new Vector2(15, 84)); }
            Ellipse(t, cx, 76, 16, 19, Ink);
            Ellipse(t, cx, 76, 15, 18, l.Skin);
            Ellipse(t, cx, 88, 16, 10, l.Hair);
            Rect(t, 16, 78, 4, 12, l.Hair);
            Rect(t, 44, 78, 4, 12, l.Hair);
            if (character == "shiori")
            {
                // 前髪と髪留め（星）
                Rect(t, 18, 84, 28, 4, l.Hair);
                Rect(t, 42, 86, 4, 4, C(255, 220, 90));
                Px(t, 44, 91, C(255, 220, 90));
            }

            Face(t, face, l);
        }

        private static void Face(Texture2D t, string face, Look l)
        {
            const int ly = 76;
            var mouth = C(170, 90, 80);
            var blush = C(246, 160, 160);
            switch (face)
            {
                case "normal":
                    Rect(t, 25, ly, 2, 4, Ink);
                    Rect(t, 37, ly, 2, 4, Ink);
                    Rect(t, 30, 66, 4, 1, mouth);
                    break;
                case "smile":
                    Px(t, 24, ly + 1, Ink); Px(t, 25, ly + 2, Ink); Px(t, 26, ly + 2, Ink); Px(t, 27, ly + 1, Ink);
                    Px(t, 36, ly + 1, Ink); Px(t, 37, ly + 2, Ink); Px(t, 38, ly + 2, Ink); Px(t, 39, ly + 1, Ink);
                    Px(t, 29, 67, mouth); Rect(t, 30, 66, 4, 1, mouth); Px(t, 34, 67, mouth);
                    Rect(t, 22, 71, 3, 1, blush);
                    Rect(t, 39, 71, 3, 1, blush);
                    break;
                case "shy":
                    Rect(t, 24, ly, 4, 1, Ink);
                    Rect(t, 36, ly, 4, 1, Ink);
                    Rect(t, 21, 70, 5, 2, blush);
                    Rect(t, 38, 70, 5, 2, blush);
                    Rect(t, 31, 66, 2, 1, mouth);
                    break;
                default:
                    throw new ArgumentException($"表情 {face} の描き方がありません", nameof(face));
            }

            if (l.Glasses)
            {
                foreach (var gx in new[] { 26, 38 })
                {
                    Line(t, gx - 4, ly - 2, gx + 3, ly - 2, C(70, 70, 90));
                    Line(t, gx - 4, ly + 5, gx + 3, ly + 5, C(70, 70, 90));
                    Line(t, gx - 4, ly - 2, gx - 4, ly + 5, C(70, 70, 90));
                    Line(t, gx + 3, ly - 2, gx + 3, ly + 5, C(70, 70, 90));
                }

                Line(t, 29, ly + 2, 34, ly + 2, C(70, 70, 90));
            }
        }
    }
}
