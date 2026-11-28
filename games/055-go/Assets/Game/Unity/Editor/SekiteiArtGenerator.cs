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
    /// 盤（木目・線・星）・黒石・白石・地の印・最後の手の印・カーソルを PNG で作り、スプライトとして取り込む（1 単位 = 64 px）。
    /// 盤の線は <see cref="BoardProjection"/> と同じ間隔（1 単位）で描く。既にある PNG は上書きしない（盤だけは同じ寸法なら同じバイト列なので描き直す）。
    /// </summary>
    public static class SekiteiArtGenerator
    {
        public const string ArtDir = "Assets/Game/Unity/Art/";
        public const int PixelsPerUnit = 64;

        public const string BoardFile = "Board.png";
        public const string BlackFile = "BlackStone.png";
        public const string WhiteFile = "WhiteStone.png";
        public const string MarkFile = "Mark.png";
        public const string LastFile = "LastMark.png";
        public const string CursorFile = "Cursor.png";

        private static readonly Color32 Clear = new(0, 0, 0, 0);
        private static readonly Color32 White = new(255, 255, 255, 255);

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtDir);
            var created = false;
            var boardPx = (int)BoardProjection.BoardSize * PixelsPerUnit;
            created |= Write(BoardFile, boardPx, boardPx, PaintBoard, overwrite: true);
            created |= Write(BlackFile, 60, 60, t => PaintStone(t, new Color(0.10f, 0.10f, 0.12f), new Color(0.42f, 0.42f, 0.46f)));
            created |= Write(WhiteFile, 60, 60, t => PaintStone(t, new Color(0.86f, 0.85f, 0.80f), new Color(1f, 1f, 0.98f)));
            created |= Write(MarkFile, 16, 16, t =>
            {
                for (var y = 0; y < 16; y++)
                {
                    for (var x = 0; x < 16; x++) { t.SetPixel(x, y, White); }
                }
            });
            created |= Write(LastFile, 28, 28, t => Ring(t, 14f, 14f, 13f, 9.5f, White));
            created |= Write(CursorFile, 64, 64, PaintCursor);
            if (created) { AssetDatabase.Refresh(); }
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtDir.TrimEnd('/') }))
            {
                Configure(AssetDatabase.GUIDToAssetPath(guid));
            }
        }

        private static bool Write(string file, int w, int h, Action<Texture2D> paint, bool overwrite = false)
        {
            var path = ArtDir + file;
            if (File.Exists(path) && !overwrite) { return false; }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pixels = new Color32[w * h];
            for (var i = 0; i < pixels.Length; i++) { pixels[i] = Clear; }
            tex.SetPixels32(pixels);
            paint(tex);
            tex.Apply();
            var bytes = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) { return false; }
            File.WriteAllBytes(path, bytes);
            return true;
        }

        private static void Configure(string path)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) { return; }
            var dirty = false;
            if (importer.textureType != TextureImporterType.Sprite) { importer.textureType = TextureImporterType.Sprite; dirty = true; }
            if (importer.spriteImportMode != SpriteImportMode.Single) { importer.spriteImportMode = SpriteImportMode.Single; dirty = true; }
            if (!Mathf.Approximately(importer.spritePixelsPerUnit, PixelsPerUnit)) { importer.spritePixelsPerUnit = PixelsPerUnit; dirty = true; }
            if (importer.filterMode != FilterMode.Bilinear) { importer.filterMode = FilterMode.Bilinear; dirty = true; }
            if (importer.textureCompression != TextureImporterCompression.Uncompressed) { importer.textureCompression = TextureImporterCompression.Uncompressed; dirty = true; }
            if (importer.mipmapEnabled) { importer.mipmapEnabled = false; dirty = true; }
            if (importer.alphaIsTransparency == false) { importer.alphaIsTransparency = true; dirty = true; }
            if (dirty) { importer.SaveAndReimport(); }
        }

        /// <summary>榧の盤：木目（決まった式のゆらぎ）・縁・9 本ずつの線・星（3-3 と天元）。</summary>
        private static void PaintBoard(Texture2D t)
        {
            var w = t.width;
            var h = t.height;
            var wood = new Color(0.86f, 0.70f, 0.42f);
            var dark = new Color(0.72f, 0.55f, 0.30f);
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var grain = (Mathf.Sin((x * 0.045f) + (Mathf.Sin(y * 0.013f) * 3.1f)) * 0.5f) + 0.5f;
                    var fine = (Mathf.Sin((x * 0.31f) + (y * 0.02f)) * 0.5f) + 0.5f;
                    var c = Color.Lerp(wood, dark, (grain * 0.35f) + (fine * 0.08f));
                    var edge = Mathf.Min(Mathf.Min(x, w - 1 - x), Mathf.Min(y, h - 1 - y));
                    if (edge < 4) { c = Color.Lerp(c, new Color(0.45f, 0.30f, 0.15f), 0.6f); }
                    t.SetPixel(x, y, c);
                }
            }

            var line = new Color(0.20f, 0.13f, 0.06f);
            var first = PixelsPerUnit;
            var last = PixelsPerUnit * Points.Size;
            for (var i = 0; i < Points.Size; i++)
            {
                var p = PixelsPerUnit * (i + 1);
                for (var k = first - 1; k <= last + 1; k++)
                {
                    for (var d = -1; d <= 1; d++)
                    {
                        if (d == 1 && i != 0 && i != Points.Size - 1) { continue; } // 内側の線は 2 px、外周は 3 px
                        t.SetPixel(p + d, k, line);
                        t.SetPixel(k, p + d, line);
                    }
                }
            }

            foreach (var (c, r) in new[] { (2, 2), (6, 2), (4, 4), (2, 6), (6, 6) })
            {
                var cx = PixelsPerUnit * (c + 1);
                var cy = PixelsPerUnit * (r + 1);
                for (var y = -6; y <= 6; y++)
                {
                    for (var x = -6; x <= 6; x++)
                    {
                        if ((x * x) + (y * y) <= 30) { t.SetPixel(cx + x, cy + y, line); }
                    }
                }
            }
        }

        /// <summary>丸い石：中心から縁へ暗くなり、左上に光。縁は 1 px でなめらかに抜く。</summary>
        private static void PaintStone(Texture2D t, Color body, Color highlight)
        {
            var r = (t.width / 2f) - 0.5f;
            var c = (t.width - 1) / 2f;
            for (var y = 0; y < t.height; y++)
            {
                for (var x = 0; x < t.width; x++)
                {
                    var dx = x - c;
                    var dy = y - c;
                    var d = Mathf.Sqrt((dx * dx) + (dy * dy));
                    if (d > r + 0.5f) { continue; }
                    var alpha = Mathf.Clamp01(r + 0.5f - d);
                    var shade = Color.Lerp(body, body * 0.7f, Mathf.Clamp01(d / r) * 0.6f);
                    var hx = dx + (r * 0.35f);
                    var hy = dy - (r * 0.35f);
                    var glow = Mathf.Clamp01(1f - (Mathf.Sqrt((hx * hx) + (hy * hy)) / (r * 0.75f)));
                    var col = Color.Lerp(shade, highlight, glow * glow * 0.8f);
                    col.a = alpha;
                    t.SetPixel(x, y, col);
                }
            }
        }

        private static void PaintCursor(Texture2D t)
        {
            var gold = new Color32(255, 214, 90, 255);
            const int Len = 18;
            const int Thick = 4;
            for (var y = 0; y < t.height; y++)
            {
                for (var x = 0; x < t.width; x++)
                {
                    var nearX = x < Thick || x >= t.width - Thick;
                    var nearY = y < Thick || y >= t.height - Thick;
                    var cornerX = x < Len || x >= t.width - Len;
                    var cornerY = y < Len || y >= t.height - Len;
                    if ((nearX && cornerY) || (nearY && cornerX)) { t.SetPixel(x, y, gold); }
                }
            }
        }

        private static void Ring(Texture2D t, float cx, float cy, float outer, float inner, Color32 color)
        {
            for (var y = 0; y < t.height; y++)
            {
                for (var x = 0; x < t.width; x++)
                {
                    var d = Mathf.Sqrt(((x - cx + 0.5f) * (x - cx + 0.5f)) + ((y - cy + 0.5f) * (y - cy + 0.5f)));
                    if (d <= outer && d >= inner) { t.SetPixel(x, y, color); }
                }
            }
        }
    }
}
