using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>図形を組み合わせて素材の PNG を描き出す。既にあるファイルは上書きしない。</summary>
    public static class DuskDawnArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";
        public const string BackdropFile = "Backdrop.png";
        public const string CellFile = "Cell.png";
        public const string SolidFile = "Solid.png";
        public const string DuskFile = "DiscDusk.png";
        public const string DawnFile = "DiscDawn.png";
        public const string HintFile = "Hint.png";
        public const string LastMoveFile = "LastMove.png";
        public const string CursorFile = "Cursor.png";
        private const int SpriteSize = 256;
        private const int BackdropSize = 512;
        private const int Samples = 3;
        private const float DiscRadius = .94f;

        private static readonly Color _clear = new(0, 0, 0, 0);

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtRoot);
            var created = new List<(string Path, int Size)>();
            Save(created, BackdropFile, BackdropSize, Backdrop);
            Save(created, CellFile, SpriteSize, Cell);
            Save(created, SolidFile, SpriteSize, (_, _) => Color.white);
            Save(created, DuskFile, SpriteSize, Dusk);
            Save(created, DawnFile, SpriteSize, Dawn);
            Save(created, HintFile, SpriteSize, Hint);
            Save(created, LastMoveFile, SpriteSize, LastMove);
            Save(created, CursorFile, SpriteSize, Cursor);
            AssetDatabase.Refresh();

            // 既存の素材のインポート設定は調整を保つため触らない。
            foreach (var (path, size) in created) { Import(path, size); }
        }

        private static void Save(List<(string Path, int Size)> created, string file, int size, Func<float, float, Color> shader)
        {
            var path = ArtRoot + file;
            if (File.Exists(path)) { return; }
            var texture = Paint(size, shader);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            created.Add((path, size));
        }

        private static void Import(string path, int size)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = size;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        // 座標は x, y とも -1〜1、y は上向き。
        private static Texture2D Paint(int size, Func<float, float, Color> shader)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (var py = 0; py < size; py++)
            {
                for (var px = 0; px < size; px++)
                {
                    var sum = Vector4.zero;
                    for (var s = 0; s < Samples * Samples; s++)
                    {
                        var x = ((px + (((s % Samples) + .5f) / Samples)) / size * 2) - 1;
                        var y = ((py + (((s / Samples) + .5f) / Samples)) / size * 2) - 1;
                        var color = shader(x, y);
                        sum += new Vector4(color.r * color.a, color.g * color.a, color.b * color.a, color.a);
                    }

                    sum /= Samples * Samples;
                    texture.SetPixel(px, py, sum.w > 0 ? new Color(sum.x / sum.w, sum.y / sum.w, sum.z / sum.w, sum.w) : _clear);
                }
            }

            texture.Apply();
            return texture;
        }

        // 夕暮れから夜へ移る空。上は藍、地平線は茜色、上のほうに小さな星。
        private static Color Backdrop(float x, float y)
        {
            var height = Mathf.Clamp01((y + 1f) / 2f);
            var night = new Color(.06f, .05f, .16f);
            var dusk = new Color(.24f, .12f, .26f);
            var horizon = new Color(.58f, .27f, .24f);
            var color = height > .35f ? Color.Lerp(dusk, night, (height - .35f) / .65f) : Color.Lerp(horizon, dusk, height / .35f);
            var star = Mathf.Repeat(Mathf.Sin((Mathf.Floor(x * 160f) * 12.9898f) + (Mathf.Floor(y * 160f) * 78.233f)) * 43758.547f, 1f);
            return star > .993f && height > .5f ? Color.Lerp(color, new Color(1f, .95f, .85f), (height - .5f) * 1.6f) : color;
        }

        // 盤の1マス。暗い石板に金の目地。
        private static Color Cell(float x, float y)
        {
            var edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(y));
            if (edge > .955f) { return new Color(.62f, .5f, .3f); }
            var shade = Mathf.Clamp01((y + 1f) / 2f);
            return Color.Lerp(new Color(.11f, .2f, .22f), new Color(.15f, .26f, .27f), shade);
        }

        // 宵（黒）の石。藍の円盤に淡い三日月。
        private static Color Dusk(float x, float y)
        {
            var r = new Vector2(x, y).magnitude;
            if (r > DiscRadius) { return _clear; }
            var color = DiscShade(new Color(.17f, .15f, .36f), x, y, r);
            var inMoon = new Vector2(x + .04f, y + .02f).magnitude < .4f;
            var bite = new Vector2(x - .14f, y - .1f).magnitude < .33f;
            return inMoon && !bite ? Color.Lerp(color, new Color(.78f, .76f, 1f), .7f) : color;
        }

        // 暁（白）の石。淡い金の円盤に朱の日輪。
        private static Color Dawn(float x, float y)
        {
            var r = new Vector2(x, y).magnitude;
            if (r > DiscRadius) { return _clear; }
            var color = DiscShade(new Color(.97f, .87f, .64f), x, y, r);
            if (r < .24f) { return Color.Lerp(color, new Color(.92f, .45f, .2f), .85f); }
            var ray = Mathf.Abs(Mathf.Sin(Mathf.Atan2(y, x) * 4f)) > .9f && r < .44f && r > .3f;
            return ray ? Color.Lerp(color, new Color(.92f, .55f, .25f), .6f) : color;
        }

        // 縁を暗く、左上を明るくして厚みを出す。
        private static Color DiscShade(Color baseColor, float x, float y, float r)
        {
            if (r > DiscRadius - .1f) { return Color.Lerp(baseColor, Color.black, .4f); }
            var light = Mathf.Clamp((y - x) * .14f, -.14f, .18f);
            return light >= 0 ? Color.Lerp(baseColor, Color.white, light) : Color.Lerp(baseColor, Color.black, -light);
        }

        // 打てるマスの印。小さな淡い点。
        private static Color Hint(float x, float y)
        {
            var r = new Vector2(x, y).magnitude;
            return r < .2f ? new Color(1f, .86f, .52f, .6f) : _clear;
        }

        // 直前に打った石の印。石の中央に重ねる小さな菱形。
        private static Color LastMove(float x, float y)
        {
            var d = Mathf.Abs(x) + Mathf.Abs(y);
            if (d > .3f) { return _clear; }
            return d > .22f ? new Color(.3f, .08f, .08f) : new Color(1f, .42f, .32f);
        }

        // キーボードのカーソル。四隅のかぎ括弧。
        private static Color Cursor(float x, float y)
        {
            var edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(y));
            var corner = Mathf.Abs(x) > .5f && Mathf.Abs(y) > .5f;
            return corner && Mathf.Abs(edge - .9f) < .06f ? new Color(1f, .9f, .6f) : _clear;
        }
    }
}
