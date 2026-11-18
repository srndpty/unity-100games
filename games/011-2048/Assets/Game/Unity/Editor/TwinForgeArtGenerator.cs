using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>図形を組み合わせて素材の PNG を描き出す。既にあるファイルは上書きしない。</summary>
    public static class TwinForgeArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";
        public const string SlotFile = "Slot.png";
        public const string FrameFile = "Frame.png";
        public const string BackdropFile = "Backdrop.png";
        private const int SpriteSize = 256;
        private const int Samples = 3;

        private static readonly Color _clear = new(0, 0, 0, 0);

        // 数字が上がるほど、鉄→銅→金→赤熱→白熱と変わる。
        private static readonly (int Value, Color Metal, Color Ink)[] _plates =
        {
            (2, new Color(.36f, .38f, .42f), new Color(.93f, .94f, .96f)),
            (4, new Color(.55f, .59f, .65f), new Color(.08f, .09f, .11f)),
            (8, new Color(.42f, .28f, .17f), new Color(.98f, .90f, .78f)),
            (16, new Color(.62f, .35f, .18f), new Color(1f, .96f, .88f)),
            (32, new Color(.82f, .49f, .24f), new Color(.14f, .07f, .03f)),
            (64, new Color(.76f, .62f, .24f), new Color(.14f, .09f, .02f)),
            (128, new Color(.90f, .74f, .22f), new Color(.16f, .10f, .02f)),
            (256, new Color(.98f, .86f, .44f), new Color(.20f, .11f, .02f)),
            (512, new Color(.96f, .52f, .12f), new Color(.16f, .04f, .01f)),
            (1024, new Color(.80f, .15f, .09f), new Color(1f, .95f, .82f)),
            (2048, new Color(1f, .95f, .78f), new Color(.70f, .11f, .05f)),
        };

        // 5×7 のドット文字。刻印らしく角張った字形にする。
        private static readonly string[][] _digits =
        {
            new[] { "01110", "10001", "10011", "10101", "11001", "10001", "01110" },
            new[] { "00100", "01100", "00100", "00100", "00100", "00100", "01110" },
            new[] { "01110", "10001", "00001", "00010", "00100", "01000", "11111" },
            new[] { "11110", "00001", "00001", "01110", "00001", "00001", "11110" },
            new[] { "00010", "00110", "01010", "10010", "11111", "00010", "00010" },
            new[] { "11111", "10000", "11110", "00001", "00001", "10001", "01110" },
            new[] { "00110", "01000", "10000", "11110", "10001", "10001", "01110" },
            new[] { "11111", "00001", "00010", "00100", "01000", "01000", "01000" },
            new[] { "01110", "10001", "10001", "01110", "10001", "10001", "01110" },
            new[] { "01110", "10001", "10001", "01111", "00001", "00010", "01100" },
        };

        public static IEnumerable<int> PlateValues => _plates.Select(plate => plate.Value);

        public static string PlateFile(int value) => "Plate" + value + ".png";

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtRoot);
            var created = new List<string>();
            foreach (var plate in _plates) { Save(created, PlateFile(plate.Value), (x, y) => Plate(x, y, plate.Value.ToString(), plate.Metal, plate.Ink)); }
            Save(created, SlotFile, Slot);
            Save(created, FrameFile, Frame);
            Save(created, BackdropFile, Backdrop);
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

        // 角丸の四角形までの符号付き距離。内側が負。
        private static float RoundedBox(float x, float y, float halfSize, float radius)
        {
            var qx = Mathf.Abs(x) - halfSize + radius;
            var qy = Mathf.Abs(y) - halfSize + radius;
            var outside = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude;
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0) - radius;
        }

        private static Color Plate(float x, float y, string text, Color metal, Color ink)
        {
            var distance = RoundedBox(x, y, .96f, .2f);
            if (distance > 0) { return _clear; }
            if (Stamped(x, y, text)) { return ink; }
            if (distance > -.09f) { return Color.Lerp(metal, y - x > 0 ? Color.white : Color.black, .32f); }

            var rivet = Mathf.Abs(Mathf.Abs(x) - .7f) < .07f && Mathf.Abs(Mathf.Abs(y) - .7f) < .07f;
            if (rivet) { return Color.Lerp(metal, Color.black, .4f); }
            return Color.Lerp(metal, y > 0 ? Color.white : Color.black, Mathf.Abs(y) * .1f);
        }

        // 桁数に応じて字の大きさを変え、板の内側に中央揃えで収める。
        private static bool Stamped(float x, float y, string text)
        {
            const float maxWidth = 1.36f;
            const float maxHeight = 1.05f;
            var columns = (text.Length * 6) - 1;
            var unit = Mathf.Min(maxWidth / columns, maxHeight / 7);
            var column = Mathf.FloorToInt((x / unit) + (columns / 2f));
            var row = Mathf.FloorToInt(3.5f - (y / unit));
            if (column < 0 || column >= columns || row < 0 || row >= 7 || column % 6 == 5) { return false; }
            return _digits[text[column / 6] - '0'][row][column % 6] == '1';
        }

        private static Color Slot(float x, float y)
        {
            var distance = RoundedBox(x, y, .96f, .2f);
            if (distance > 0) { return _clear; }
            return distance > -.05f ? new Color(.2f, .15f, .13f) : new Color(.13f, .1f, .09f);
        }

        private static Color Frame(float x, float y)
        {
            var distance = RoundedBox(x, y, .99f, .06f);
            if (distance > 0) { return _clear; }
            return distance > -.015f ? new Color(.42f, .25f, .14f) : new Color(.2f, .15f, .13f);
        }

        // 炉の照り返し。下から暖色の光が差す暗い壁。
        private static Color Backdrop(float x, float y)
        {
            var glow = Mathf.Clamp01(1f - new Vector2(x * .7f, (y + 1.1f) * .7f).magnitude);
            return Color.Lerp(new Color(.07f, .055f, .05f), new Color(.36f, .15f, .06f), glow * glow);
        }
    }
}
