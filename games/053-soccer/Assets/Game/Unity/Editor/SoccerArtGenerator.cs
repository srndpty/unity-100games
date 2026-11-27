#nullable enable
using System;
using System.IO;
using Game.Core;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>
    /// コート（38 × 26 m、1 m = 20 px）・選手・向きの印・球・影・操作中の輪・目標の印をドット絵の PNG で作り、スプライトとして取り込む。
    /// コートの線はルールの寸法（<see cref="Pitch"/>）から描く。既にある PNG は上書きしない。
    /// </summary>
    public static class SoccerArtGenerator
    {
        public const string ArtDir = "Assets/Game/Unity/Art/";
        public const int PixelsPerUnit = 20;

        /// <summary>コートの絵の大きさ（m）。コートの外に 3 m の余白。</summary>
        public const int FieldMetersX = 38;
        public const int FieldMetersY = 26;

        public const string PitchFile = "Pitch.png";
        public const string PlayerFile = "Player.png";
        public const string FacingFile = "Facing.png";
        public const string BallFile = "Ball.png";
        public const string ShadowFile = "Shadow.png";
        public const string RingFile = "Ring.png";
        public const string TargetFile = "Target.png";

        private static readonly Color32 Clear = new(0, 0, 0, 0);
        private static readonly Color32 White = new(245, 245, 240, 255);

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtDir);
            var created = false;
            created |= Write(PitchFile, FieldMetersX * PixelsPerUnit, FieldMetersY * PixelsPerUnit, PaintPitch);
            created |= Write(PlayerFile, 16, 16, t => Disc(t, 8f, 8f, 7.5f, White, new Color32(30, 30, 30, 255)));
            created |= Write(FacingFile, 6, 6, t =>
            {
                for (int y = 0; y < 6; y++)
                {
                    for (int x = 0; x < 6; x++) { if (x <= 5 - Math.Abs(y - 2.5f) * 1.6f) { t.SetPixel(x, y, White); } }
                }
            });
            created |= Write(BallFile, 6, 6, t =>
            {
                Disc(t, 3f, 3f, 2.9f, White, new Color32(40, 40, 40, 255));
                t.SetPixel(3, 3, new Color32(40, 40, 40, 255));
            });
            created |= Write(ShadowFile, 8, 4, t => Ellipse(t, 4f, 2f, 3.9f, 1.9f, new Color32(0, 0, 0, 110)));
            created |= Write(RingFile, 24, 24, t => Ring(t, 12f, 12f, 11.5f, 9.5f, new Color32(255, 230, 90, 255)));
            created |= Write(TargetFile, 10, 10, t =>
            {
                for (int y = 0; y < 10; y++)
                {
                    for (int x = 0; x < 10; x++)
                    {
                        var d = Math.Abs(x - 4.5f) + Math.Abs(y - 4.5f);
                        if (d <= 4.6f && d >= 2.6f) { t.SetPixel(x, y, White); }
                    }
                }
            });

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

        // ---- コート ----

        /// <summary>コートの m を絵の px へ（左下が原点、中心がコートの中心）。</summary>
        private static int Px(float meters, int sizeMeters) => Mathf.RoundToInt((meters + (sizeMeters / 2f)) * PixelsPerUnit);

        private static void PaintPitch(Texture2D t)
        {
            var hl = Pitch.HalfLength / 1000f;
            var hw = Pitch.HalfWidth / 1000f;
            for (int y = 0; y < t.height; y++)
            {
                for (int x = 0; x < t.width; x++)
                {
                    var mx = (x / (float)PixelsPerUnit) - (FieldMetersX / 2f);
                    var my = (y / (float)PixelsPerUnit) - (FieldMetersY / 2f);
                    var inside = Math.Abs(mx) <= hl && Math.Abs(my) <= hw;
                    Color32 c;
                    if (inside)
                    {
                        var stripe = ((int)Math.Floor((mx + hl) / 2f)) % 2 == 0;
                        c = stripe ? new Color32(56, 140, 64, 255) : new Color32(66, 152, 72, 255);
                    }
                    else
                    {
                        c = new Color32(40, 92, 52, 255);
                    }

                    t.SetPixel(x, y, c);
                }
            }

            // 線（幅 2 px = 0.1 m）
            HLine(t, -hl, hl, -hw);
            HLine(t, -hl, hl, hw);
            VLine(t, -hl, -hw, hw);
            VLine(t, hl, -hw, hw);
            VLine(t, 0f, -hw, hw);
            Circle(t, 0f, 0f, Pitch.ExclusionRadius / 1000f);
            Disc(t, Px(0f, FieldMetersX), Px(0f, FieldMetersY), 3f, White, White);

            // GK の範囲（ゴールの中心からの半円）とゴール（ネットの奥行き・ポスト）。
            var area = Pitch.KeeperAreaRadius / 1000f;
            var gw = Pitch.GoalHalfWidth / 1000f;
            var depth = Pitch.GoalDepth / 1000f;
            foreach (var side in new[] { -1, 1 })
            {
                var gx = side * hl;
                for (int i = 0; i <= 360; i++)
                {
                    var a = i * Mathf.Deg2Rad / 2f - (Mathf.PI / 2f);
                    var px = gx - (side * Mathf.Cos(a) * area);
                    var py = Mathf.Sin(a) * area;
                    Dot(t, px, py);
                }

                for (int y = Px(-gw, FieldMetersY); y <= Px(gw, FieldMetersY); y++)
                {
                    for (int x = Px(Math.Min(gx, gx + (side * depth)), FieldMetersX); x <= Px(Math.Max(gx, gx + (side * depth)), FieldMetersX); x++)
                    {
                        var net = (x + y) % 4 == 0 || (x - y + 1000) % 4 == 0;
                        t.SetPixel(x, y, net ? new Color32(220, 220, 220, 255) : new Color32(30, 60, 40, 255));
                    }
                }

                foreach (var py in new[] { -gw, gw })
                {
                    Disc(t, Px(gx, FieldMetersX), Px(py, FieldMetersY), 2.5f, White, new Color32(30, 30, 30, 255));
                }
            }
        }

        private static void HLine(Texture2D t, float x0, float x1, float y)
        {
            for (int x = Px(x0, FieldMetersX) - 1; x <= Px(x1, FieldMetersX) + 1; x++)
            {
                for (int k = -1; k <= 0; k++) { Set(t, x, Px(y, FieldMetersY) + k, White); }
            }
        }

        private static void VLine(Texture2D t, float x, float y0, float y1)
        {
            for (int y = Px(y0, FieldMetersY) - 1; y <= Px(y1, FieldMetersY) + 1; y++)
            {
                for (int k = -1; k <= 0; k++) { Set(t, Px(x, FieldMetersX) + k, y, White); }
            }
        }

        private static void Circle(Texture2D t, float cx, float cy, float r)
        {
            for (int i = 0; i < 720; i++)
            {
                var a = i * Mathf.Deg2Rad / 2f;
                Dot(t, cx + (Mathf.Cos(a) * r), cy + (Mathf.Sin(a) * r));
            }
        }

        private static void Dot(Texture2D t, float mx, float my)
        {
            var x = Px(mx, FieldMetersX);
            var y = Px(my, FieldMetersY);
            Set(t, x, y, White);
            Set(t, x - 1, y, White);
            Set(t, x, y - 1, White);
            Set(t, x - 1, y - 1, White);
        }

        private static void Set(Texture2D t, int x, int y, Color32 c)
        {
            if (x >= 0 && y >= 0 && x < t.width && y < t.height) { t.SetPixel(x, y, c); }
        }

        // ---- 図形 ----

        private static void Disc(Texture2D t, float cx, float cy, float r, Color32 fill, Color32 outline)
        {
            for (int y = 0; y < t.height; y++)
            {
                for (int x = 0; x < t.width; x++)
                {
                    var d = Mathf.Sqrt(((x + .5f - cx) * (x + .5f - cx)) + ((y + .5f - cy) * (y + .5f - cy)));
                    if (d <= r - 1f) { t.SetPixel(x, y, fill); }
                    else if (d <= r) { t.SetPixel(x, y, outline); }
                }
            }
        }

        private static void Ring(Texture2D t, float cx, float cy, float outer, float inner, Color32 c)
        {
            for (int y = 0; y < t.height; y++)
            {
                for (int x = 0; x < t.width; x++)
                {
                    var d = Mathf.Sqrt(((x + .5f - cx) * (x + .5f - cx)) + ((y + .5f - cy) * (y + .5f - cy)));
                    if (d <= outer && d >= inner) { t.SetPixel(x, y, c); }
                }
            }
        }

        private static void Ellipse(Texture2D t, float cx, float cy, float rx, float ry, Color32 c)
        {
            for (int y = 0; y < t.height; y++)
            {
                for (int x = 0; x < t.width; x++)
                {
                    var dx = (x + .5f - cx) / rx;
                    var dy = (y + .5f - cy) / ry;
                    if ((dx * dx) + (dy * dy) <= 1f) { t.SetPixel(x, y, c); }
                }
            }
        }
    }
}
