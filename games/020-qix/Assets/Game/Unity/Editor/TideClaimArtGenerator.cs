using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>図形を組み合わせて素材の PNG を描き出す。既にあるファイルとそのインポート設定は上書きしない。</summary>
    public static class TideClaimArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";
        public const string BackdropFile = "Backdrop.png";
        public const string FrameFile = "Frame.png";
        public const string FieldFile = "Field.png";
        public const string PlayerFile = "Player.png";
        public const string EddyFile = "Eddy.png";

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
            var created = new List<(string Path, float PixelsPerUnit, bool Pixelated)>();
            Save(created, BackdropFile, BackdropWidth, BackdropHeight, BackdropPixelsPerUnit, Backdrop);
            Save(created, FrameFile, 64, 64, PixelsPerUnit, Frame);
            Save(created, PlayerFile, 64, 64, PixelsPerUnit, Player);
            Save(created, EddyFile, 64, 64, PixelsPerUnit, Eddy);
            SaveField(created);
            AssetDatabase.Refresh();
            foreach (var (path, pixelsPerUnit, pixelated) in created) { Import(path, pixelsPerUnit, pixelated); }
        }

        private static void Save(
            List<(string, float, bool)> created, string file, int width, int height, float pixelsPerUnit, Func<float, float, float, Color> shader)
        {
            var path = ArtRoot + file;
            if (File.Exists(path)) { return; }
            var texture = Paint(width, height, shader);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            created.Add((path, pixelsPerUnit, false));
        }

        // 試合の前の盤（外周の縁と海）。1 セル = 1 ピクセルで、実行時の盤と同じ配色を使う。Scene ビューでの配置確認用。
        private static void SaveField(List<(string, float, bool)> created)
        {
            var path = ArtRoot + FieldFile;
            if (File.Exists(path)) { return; }
            const int width = TideClaimSettings.BoardWidth;
            const int height = TideClaimSettings.BoardHeight;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++) { pixels[(y * width) + x] = TideClaimPalette.InitialColor(x, y, width, height); }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            created.Add((path, 1f / TideClaimFieldView.CellSize, true));
        }

        private static void Import(string path, float pixelsPerUnit, bool pixelated)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = !pixelated;
            importer.filterMode = pixelated ? FilterMode.Point : FilterMode.Bilinear;
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

        // 干潟の夕暮れ。空のグラデーション、遠い水平線、手前の濡れた砂の筋。
        private static Color Backdrop(float x, float y, float aspect)
        {
            var sky = Color.Lerp(new Color(.32f, .22f, .26f), new Color(.08f, .12f, .2f), (y + 1) * .5f);
            var horizon = .1f + (.03f * Mathf.Sin((x * 2.1f) + .4f));
            if (y < horizon)
            {
                var depth = Mathf.Clamp01((horizon - y) / 1.1f);
                sky = Color.Lerp(new Color(.16f, .2f, .24f), new Color(.1f, .13f, .15f), depth);
                var ripple = Mathf.Sin((y * 60f) + (Mathf.Sin(x * 3f) * 2f));
                if (ripple > .92f) { sky = Color.Lerp(sky, new Color(.36f, .36f, .38f), .35f); }
            }

            return sky;
        }

        // 盤の台。白で描き、色はシーン側で掛ける。
        private static Color Frame(float x, float y, float aspect)
        {
            const float radius = .1f;
            var dx = Mathf.Max(Mathf.Abs(x) - (1 - radius), 0);
            var dy = Mathf.Max(Mathf.Abs(y) - (1 - radius), 0);
            return (dx * dx) + (dy * dy) <= radius * radius ? Color.white : _clear;
        }

        // 自機。ひし形の灯。白で描き、色はビューが掛ける（描いている間は赤みを帯びる）。
        private static Color Player(float x, float y, float aspect)
        {
            var d = Mathf.Abs(x) + Mathf.Abs(y);
            if (d > .95f) { return _clear; }
            if (d > .78f) { return new Color(.2f, .2f, .22f); }
            return d < .32f ? Color.white : new Color(.92f, .92f, .9f);
        }

        // 渦。中心へ巻き込む 3 本の腕の渦巻き。円の外は透明（当たり判定の円と同じ大きさ）。
        private static Color Eddy(float x, float y, float aspect)
        {
            var r = Mathf.Sqrt((x * x) + (y * y));
            if (r >= 1) { return _clear; }
            var angle = Mathf.Atan2(y, x);
            var arm = Mathf.Sin((angle * 3) + (r * 9f));
            var body = Color.Lerp(new Color(.55f, .9f, .95f), new Color(.08f, .2f, .32f), r);
            var alpha = Mathf.Clamp01((1 - r) * 6f);
            if (arm > .35f) { return new Color(.92f, 1f, 1f, alpha); }
            return new Color(body.r, body.g, body.b, alpha * .9f);
        }
    }
}
