using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>
    /// 図形を組み合わせて素材の PNG を描き出す。既にあるファイルは上書きしない。
    /// 数字はフォントを使わず、線分と円弧の太い線で描く（白で描き、色は SpriteRenderer で付ける）。
    /// </summary>
    public static class NineLatticeArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";
        public const string BackdropFile = "Backdrop.png";
        public const string CellFile = "Cell.png";
        public const string SolidFile = "Solid.png";
        public const string SelectionFile = "Selection.png";
        private const int SpriteSize = 128;
        private const int BackdropSize = 512;
        private const int Samples = 3;
        private const float StrokeWidth = .085f;
        private const int ArcSegments = 28;

        private static readonly Color _clear = new(0, 0, 0, 0);

        public static string DigitFile(int digit) => $"Digit{digit}.png";

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtRoot);
            var created = new List<(string Path, int Size)>();
            Save(created, BackdropFile, BackdropSize, Backdrop);
            Save(created, CellFile, SpriteSize, Cell);
            Save(created, SolidFile, SpriteSize, (_, _) => Color.white);
            Save(created, SelectionFile, SpriteSize, Selection);
            for (var digit = 1; digit <= 9; digit++)
            {
                var strokes = DigitStrokes(digit);
                Save(created, DigitFile(digit), SpriteSize, (x, y) => Stroke(strokes, x, y));
            }

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

        // 藍染めの布。上から下へ深くなる藍に、細い格子の織り目。
        private static Color Backdrop(float x, float y)
        {
            var height = Mathf.Clamp01((y + 1f) / 2f);
            var color = Color.Lerp(new Color(.07f, .11f, .2f), new Color(.13f, .2f, .32f), height);
            var weave = (Mathf.Abs(Mathf.Sin(x * 90f)) > .985f) || (Mathf.Abs(Mathf.Sin(y * 90f)) > .985f);
            return weave ? Color.Lerp(color, new Color(.3f, .4f, .55f), .25f) : color;
        }

        // 和紙のマス。白に近い生成り。色は SpriteRenderer で付けるので、明るさのむらだけ描く。
        private static Color Cell(float x, float y)
        {
            var grain = Mathf.Repeat(Mathf.Sin((Mathf.Floor(x * 40f) * 12.9898f) + (Mathf.Floor(y * 40f) * 78.233f)) * 43758.547f, 1f);
            var shade = .96f + (grain * .04f) - (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) > .9f ? .03f : 0f);
            return new Color(shade, shade, shade);
        }

        // 選んでいるマスの枠。角を丸めた太い縁。
        private static Color Selection(float x, float y)
        {
            var qx = Mathf.Max(Mathf.Abs(x) - .7f, 0f);
            var qy = Mathf.Max(Mathf.Abs(y) - .7f, 0f);
            var d = new Vector2(qx, qy).magnitude - .24f;
            return d < 0 && d > -.1f ? Color.white : _clear;
        }

        private static Color Stroke(List<(Vector2 A, Vector2 B)> segments, float x, float y)
        {
            var p = new Vector2(x, y);
            var best = float.MaxValue;
            foreach (var (a, b) in segments)
            {
                var ab = b - a;
                var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                best = Mathf.Min(best, (a + (ab * t) - p).magnitude);
            }

            return best < StrokeWidth ? Color.white : _clear;
        }

        // 数字の字形。幅 -0.4〜0.4、高さ -0.68〜0.68 に収める。円弧の角度は度、反時計回りが正。
        private static List<(Vector2 A, Vector2 B)> DigitStrokes(int digit)
        {
            var s = new List<(Vector2 A, Vector2 B)>();
            switch (digit)
            {
                case 1:
                    Line(s, new(.04f, .68f), new(.04f, -.68f));
                    Line(s, new(-.24f, .44f), new(.04f, .68f));
                    Line(s, new(-.24f, -.68f), new(.3f, -.68f));
                    break;
                case 2:
                    Arc(s, new(0f, .32f), .34f, 160f, -30f);
                    Line(s, Polar(new(0f, .32f), .34f, -30f), new(-.38f, -.68f));
                    Line(s, new(-.38f, -.68f), new(.4f, -.68f));
                    break;
                case 3:
                    Arc(s, new(0f, .35f), .31f, 150f, -90f);
                    Arc(s, new(0f, -.32f), .36f, 90f, -150f);
                    break;
                case 4:
                    Line(s, new(.18f, .68f), new(.18f, -.68f));
                    Line(s, new(.18f, .68f), new(-.4f, -.24f));
                    Line(s, new(-.4f, -.24f), new(.4f, -.24f));
                    break;
                case 5:
                    Line(s, new(.36f, .68f), new(-.26f, .68f));
                    Line(s, new(-.26f, .68f), new(-.32f, .06f));
                    Arc(s, new(0f, -.27f), .4f, 125f, -150f);
                    break;
                case 6:
                    Arc(s, new(0f, -.28f), .38f, 0f, 360f);
                    Line(s, new(.24f, .68f), new(-.33f, -.12f));
                    break;
                case 7:
                    Line(s, new(-.4f, .68f), new(.4f, .68f));
                    Line(s, new(.4f, .68f), new(-.08f, -.68f));
                    break;
                case 8:
                    Arc(s, new(0f, .36f), .3f, 0f, 360f);
                    Arc(s, new(0f, -.3f), .37f, 0f, 360f);
                    break;
                case 9:
                    Arc(s, new(0f, .28f), .38f, 0f, 360f);
                    Line(s, new(.33f, .12f), new(-.24f, -.68f));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(digit), digit, "1〜9 を指定してください。");
            }

            return s;
        }

        private static void Line(List<(Vector2 A, Vector2 B)> segments, Vector2 a, Vector2 b) => segments.Add((a, b));

        private static void Arc(List<(Vector2 A, Vector2 B)> segments, Vector2 center, float radius, float from, float to)
        {
            for (var i = 0; i < ArcSegments; i++)
            {
                var a = Mathf.Lerp(from, to, (float)i / ArcSegments);
                var b = Mathf.Lerp(from, to, (float)(i + 1) / ArcSegments);
                segments.Add((Polar(center, radius, a), Polar(center, radius, b)));
            }
        }

        private static Vector2 Polar(Vector2 center, float radius, float degrees)
        {
            var radians = degrees * Mathf.Deg2Rad;
            return center + (new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * radius);
        }
    }
}
