using System;
using System.Collections.Generic;
using System.IO;
using Game.Core;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>図形を組み合わせて素材の PNG を描き出す。既にあるファイルは上書きしない。</summary>
    public static class SafeDigArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";
        public const string CursorFile = "Cursor.png";
        public const string FrameFile = "Frame.png";
        private const int SpriteSize = 64;
        private const int Samples = 4;

        private static readonly Color _clear = new(0, 0, 0, 0);
        private static readonly Color _ink = new(.13f, .11f, .1f);
        private static readonly Color[] _digitColors =
        {
            new(.1f, .3f, .75f), new(.1f, .45f, .2f), new(.75f, .15f, .12f), new(.4f, .15f, .6f),
            new(.55f, .25f, .05f), new(.05f, .42f, .45f), new(.15f, .13f, .12f), new(.38f, .36f, .34f),
        };

        // 5×7 のドット文字。数字は色だけでなく字形で読めるようにする。
        private static readonly string[][] _digits =
        {
            new[] { "00100", "01100", "00100", "00100", "00100", "00100", "01110" },
            new[] { "01110", "10001", "00001", "00010", "00100", "01000", "11111" },
            new[] { "11110", "00001", "00001", "01110", "00001", "00001", "11110" },
            new[] { "00010", "00110", "01010", "10010", "11111", "00010", "00010" },
            new[] { "11111", "10000", "11110", "00001", "00001", "10001", "01110" },
            new[] { "00110", "01000", "10000", "11110", "10001", "10001", "01110" },
            new[] { "11111", "00001", "00010", "00100", "01000", "01000", "01000" },
            new[] { "01110", "10001", "10001", "01110", "10001", "10001", "01110" },
        };

        public static string FaceFile(CellFace face) => "Face" + face + ".png";

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtRoot);
            var created = new List<string>();
            foreach (CellFace face in Enum.GetValues(typeof(CellFace))) { Save(created, FaceFile(face), FaceShader(face)); }
            Save(created, CursorFile, Cursor);
            Save(created, FrameFile, (x, y) => Color.white);
            AssetDatabase.Refresh();

            // 既存の素材のインポート設定は調整を保つため触らない。
            foreach (var path in created) { Import(path); }
        }

        private static void Save(List<string> created, string file, Func<float, float, Color> shader)
        {
            var path = ArtRoot + file;
            if (File.Exists(path)) { return; }
            var texture = Paint(shader);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            created.Add(path);
        }

        private static void Import(string path)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = SpriteSize;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        private static Func<float, float, Color> FaceShader(CellFace face)
        {
            switch (face)
            {
                case CellFace.Hidden: return Turf;
                case CellFace.Flagged: return Layer(Flag, Turf);
                case CellFace.Open0: return Soil;
                case CellFace.Mine: return Layer(Bomb, Soil);
                case CellFace.Exploded: return Layer(Bomb, Blast);
                case CellFace.WrongFlag: return Layer(Cross, Layer(Flag, Turf));
                default:
                    var number = face - CellFace.Open0;
                    return Layer((x, y) => Digit(x, y, number), Soil);
            }
        }

        // 座標は x, y とも -1〜1、y は上向き。
        private static Texture2D Paint(Func<float, float, Color> shader)
        {
            var texture = new Texture2D(SpriteSize, SpriteSize, TextureFormat.RGBA32, false);
            for (var py = 0; py < SpriteSize; py++)
            {
                for (var px = 0; px < SpriteSize; px++)
                {
                    var sum = Vector4.zero;
                    for (var s = 0; s < Samples * Samples; s++)
                    {
                        var x = (px + (((s % Samples) + .5f) / Samples)) / SpriteSize * 2 - 1;
                        var y = (py + (((s / Samples) + .5f) / Samples)) / SpriteSize * 2 - 1;
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

        private static Func<float, float, Color> Layer(Func<float, float, Color> top, Func<float, float, Color> bottom) => (x, y) =>
        {
            var over = top(x, y);
            return over.a > 0 ? over : bottom(x, y);
        };

        private static bool Disc(float x, float y, float cx, float cy, float radius) =>
            ((x - cx) * (x - cx)) + ((y - cy) * (y - cy)) <= radius * radius;

        private static bool Tile(float x, float y) => Mathf.Abs(x) <= .95f && Mathf.Abs(y) <= .95f;

        // 未公開のセル。上下の縁で少し盛り上がって見せる。
        private static Color Turf(float x, float y)
        {
            if (!Tile(x, y)) { return _clear; }
            if (y > .74f || x < -.74f) { return new Color(.56f, .76f, .36f); }
            if (y < -.74f || x > .74f) { return new Color(.24f, .42f, .18f); }
            var tuft = Disc(x, y, -.35f, .3f, .1f) || Disc(x, y, .38f, -.05f, .1f) || Disc(x, y, -.08f, -.42f, .1f);
            return tuft ? new Color(.33f, .54f, .24f) : new Color(.41f, .62f, .29f);
        }

        // 掘った後のセル。数字が読みやすいよう明るい無地にする。
        private static Color Soil(float x, float y)
        {
            if (!Tile(x, y)) { return _clear; }
            return Mathf.Abs(x) > .86f || Mathf.Abs(y) > .86f ? new Color(.72f, .62f, .46f) : new Color(.89f, .81f, .66f);
        }

        private static Color Blast(float x, float y)
        {
            if (!Tile(x, y)) { return _clear; }
            return Disc(x, y, 0, 0, .8f) ? new Color(.98f, .62f, .2f) : new Color(.85f, .2f, .14f);
        }

        private static Color Digit(float x, float y, int number)
        {
            const float halfWidth = .45f;
            const float halfHeight = .63f;
            if (Mathf.Abs(x) >= halfWidth || Mathf.Abs(y) >= halfHeight) { return _clear; }
            var column = Mathf.FloorToInt((x + halfWidth) / (halfWidth * 2) * 5);
            var row = Mathf.FloorToInt((halfHeight - y) / (halfHeight * 2) * 7);
            return _digits[number - 1][row][column] == '1' ? _digitColors[number - 1] : _clear;
        }

        private static Color Flag(float x, float y)
        {
            if (x >= -.3f && x <= -.2f && y >= -.55f && y <= .62f) { return _ink; }
            if (x >= -.5f && x <= .3f && y >= -.66f && y <= -.52f) { return _ink; }
            var cloth = x > -.2f && y <= .62f - ((x + .2f) * .55f) && y >= .08f + ((x + .2f) * .55f);
            return cloth ? new Color(.9f, .2f, .15f) : _clear;
        }

        private static Color Bomb(float x, float y)
        {
            if (Disc(x, y, .42f, .62f, .13f)) { return new Color(1f, .78f, .2f); }
            if (x >= .08f && x <= .4f && Mathf.Abs(y - (.3f + x)) <= .07f) { return new Color(.45f, .32f, .2f); }
            if (Disc(x, y, -.16f, .02f, .12f)) { return new Color(.62f, .62f, .66f); }
            return Disc(x, y, 0, -.12f, .5f) ? _ink : _clear;
        }

        private static Color Cross(float x, float y)
        {
            if (Mathf.Abs(x) > .72f || Mathf.Abs(y) > .72f) { return _clear; }
            return Mathf.Abs(x - y) <= .15f || Mathf.Abs(x + y) <= .15f ? new Color(.1f, .08f, .08f) : _clear;
        }

        private static Color Cursor(float x, float y) =>
            Tile(x, y) && (Mathf.Abs(x) > .8f || Mathf.Abs(y) > .8f) ? new Color(1f, .96f, .62f) : _clear;
    }
}
