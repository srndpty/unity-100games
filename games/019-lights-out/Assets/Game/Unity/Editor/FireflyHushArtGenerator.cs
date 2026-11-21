using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>図形を組み合わせて素材の PNG を描き出す。既にあるファイルとそのインポート設定は上書きしない。</summary>
    public static class FireflyHushArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";
        public const string BackdropFile = "Backdrop.png";
        public const string TileFile = "Tile.png";
        public const string GlowFile = "Glow.png";
        public const string MarkFile = "Mark.png";
        public const string CursorFile = "Cursor.png";
        public const string FrameFile = "Frame.png";

        /// <summary>1 単位あたりのピクセル数。正方形の素材は 64 ピクセルで 1 単位。</summary>
        public const int PixelsPerUnit = 64;

        /// <summary>背景は 16:9 の画面（縦 12 単位）と同じ大きさで描く。</summary>
        public const int BackdropWidth = 512;
        public const int BackdropHeight = 288;
        public const float BackdropPixelsPerUnit = BackdropHeight / 12f;

        private const int Samples = 4;
        private static readonly Color _clear = new(0, 0, 0, 0);

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtRoot);
            var created = new List<(string Path, float PixelsPerUnit)>();
            Save(created, BackdropFile, BackdropWidth, BackdropHeight, BackdropPixelsPerUnit, Backdrop);
            Save(created, TileFile, 64, 64, PixelsPerUnit, Tile);
            Save(created, GlowFile, 64, 64, PixelsPerUnit, Glow);
            Save(created, MarkFile, 64, 64, PixelsPerUnit, Mark);
            Save(created, CursorFile, 64, 64, PixelsPerUnit, Cursor);
            Save(created, FrameFile, 64, 64, PixelsPerUnit, Frame);
            AssetDatabase.Refresh();
            foreach (var (path, pixelsPerUnit) in created) { Import(path, pixelsPerUnit); }
        }

        private static void Save(
            List<(string, float)> created, string file, int width, int height, float pixelsPerUnit, Func<float, float, float, Color> shader)
        {
            var path = ArtRoot + file;
            if (File.Exists(path)) { return; }
            var texture = Paint(width, height, shader);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            created.Add((path, pixelsPerUnit));
        }

        private static void Import(string path, float pixelsPerUnit)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        // 座標は y が -1〜1（上向き）、x は縦横比に合わせて -aspect〜aspect。
        private static Texture2D Paint(int width, int height, Func<float, float, float, Color> shader)
        {
            var aspect = (float)width / height;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            for (var py = 0; py < height; py++)
            {
                for (var px = 0; px < width; px++)
                {
                    var sum = Vector4.zero;
                    for (var s = 0; s < Samples * Samples; s++)
                    {
                        var x = ((px + (((s % Samples) + .5f) / Samples)) / width * 2 - 1) * aspect;
                        var y = (py + (((s / Samples) + .5f) / Samples)) / height * 2 - 1;
                        var color = shader(x, y, aspect);
                        sum += new Vector4(color.r * color.a, color.g * color.a, color.b * color.a, color.a);
                    }

                    sum /= Samples * Samples;
                    texture.SetPixel(px, py, sum.w > 0 ? new Color(sum.x / sum.w, sum.y / sum.w, sum.z / sum.w, sum.w) : _clear);
                }
            }

            texture.Apply();
            return texture;
        }

        private static float Hash(float x, float y)
        {
            var value = Mathf.Sin((x * 127.1f) + (y * 311.7f)) * 43758.547f;
            return value - Mathf.Floor(value);
        }

        // 夜空のグラデーション、星、遠くの丘、手前の草の影。
        private static Color Backdrop(float x, float y, float aspect)
        {
            var sky = Color.Lerp(new Color(.05f, .07f, .14f), new Color(.1f, .14f, .22f), (1 - y) * .5f);
            var cellX = Mathf.Floor((x + aspect) * 40);
            var cellY = Mathf.Floor((y + 1) * 40);
            if (y > -.2f && Hash(cellX, cellY) > .985f)
            {
                var fx = ((x + aspect) * 40) - cellX - .5f;
                var fy = ((y + 1) * 40) - cellY - .5f;
                if ((fx * fx) + (fy * fy) < .05f) { sky = Color.Lerp(sky, new Color(.85f, .88f, .95f), .8f); }
            }

            var hill = -.45f + (.08f * Mathf.Sin((x * 1.3f) + .6f)) + (.05f * Mathf.Sin(x * 3.1f));
            if (y < hill) { sky = new Color(.07f, .12f, .11f); }
            var blades = -.78f + (.1f * Mathf.Abs(Mathf.Sin(x * 23f))) + (.06f * Mathf.Abs(Mathf.Sin((x * 57f) + 1.3f)));
            if (y < blades) { sky = new Color(.03f, .06f, .05f); }
            return sky;
        }

        // マスの石。角を丸めた正方形で、上の縁を少し明るくする。色はビューが点灯に合わせて掛ける。
        private static Color Tile(float x, float y, float aspect)
        {
            const float half = .92f;
            const float radius = .22f;
            var dx = Mathf.Max(Mathf.Abs(x) - (half - radius), 0);
            var dy = Mathf.Max(Mathf.Abs(y) - (half - radius), 0);
            if ((dx * dx) + (dy * dy) > radius * radius) { return _clear; }
            if (y > .74f) { return new Color(.92f, .94f, .96f); }
            if (y < -.74f) { return new Color(.55f, .58f, .62f); }
            var speck = Hash(Mathf.Floor((x + 1) * 9), Mathf.Floor((y + 1) * 9)) > .8f;
            return speck ? new Color(.68f, .72f, .74f) : new Color(.78f, .8f, .82f);
        }

        // 蛍の灯。中心が明るく、外へ柔らかく消える。色はビューが掛ける。
        private static Color Glow(float x, float y, float aspect)
        {
            var distance = Mathf.Sqrt((x * x) + (y * y));
            if (distance >= 1) { return _clear; }
            var core = Mathf.Clamp01(1 - (distance / .22f));
            var halo = Mathf.Pow(1 - distance, 2.2f) * .75f;
            return new Color(1, 1, 1, Mathf.Clamp01(core + halo));
        }

        // 解答の印。マスの中央の輪。
        private static Color Mark(float x, float y, float aspect)
        {
            var distance = Mathf.Sqrt((x * x) + (y * y));
            if (distance > .38f && distance < .52f) { return new Color(.45f, .9f, 1f); }
            return distance <= .14f ? new Color(.45f, .9f, 1f) : _clear;
        }

        // カーソル。四隅の鉤。
        private static Color Cursor(float x, float y, float aspect)
        {
            var ax = Mathf.Abs(x);
            var ay = Mathf.Abs(y);
            if (ax > .98f || ay > .98f) { return _clear; }
            var corner = (ax > .86f && ay > .45f) || (ay > .86f && ax > .45f);
            return corner ? new Color(1f, .9f, .55f) : _clear;
        }

        // 盤の台。白で描き、色はシーン側で掛ける。
        private static Color Frame(float x, float y, float aspect)
        {
            const float radius = .1f;
            var dx = Mathf.Max(Mathf.Abs(x) - (1 - radius), 0);
            var dy = Mathf.Max(Mathf.Abs(y) - (1 - radius), 0);
            return (dx * dx) + (dy * dy) <= radius * radius ? Color.white : _clear;
        }
    }
}
