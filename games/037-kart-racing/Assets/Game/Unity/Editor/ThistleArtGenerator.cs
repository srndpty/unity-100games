using System;
using System.IO;
using Game.Core;
using UnityEditor;
using UnityEngine;

namespace Game.Unity.Editor
{
    /// <summary>
    /// 車（真上から見たレーシングカート）と煙の粒のスプライトを描く。形は符号付き距離で書き、画素の大きさで縁をぼかす。
    /// 車の座標は「前が +x、左が +y」のメートルで、絵は 1.5 m × 1.0 m（当たりの円は直径 1.2 m）。
    /// 同じコードからは同じ画像になるので、毎回書き直す。
    /// </summary>
    public static class ThistleArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";

        public const string SmokePath = ArtRoot + "smoke.png";

        private const int KartWidth = 192;
        private const int KartHeight = 128;
        private const float KartPixelsPerMeter = 128f;
        private const int SmokeSize = 64;

        public static string KartPath(int index) => ArtRoot + "kart-" + index + ".png";

        public static void Generate()
        {
            Directory.CreateDirectory(ArtRoot);
            for (var i = 0; i < KartConfig.KartCount; i++) { Save(KartPath(i), DrawKart(ThistlePalette.Kart(i), i)); }
            Save(SmokePath, DrawPuff());
            AssetDatabase.Refresh();
            for (var i = 0; i < KartConfig.KartCount; i++) { Import(KartPath(i), KartPixelsPerMeter); }
            Import(SmokePath, SmokeSize);
        }

        private static void Import(string path, float pixelsPerUnit)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = true;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        private static void Save(string path, Texture2D texture)
        {
            try
            {
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static Texture2D DrawPuff()
        {
            var canvas = new Canvas(SmokeSize, SmokeSize, SmokeSize);
            canvas.Each((p, pixel) =>
            {
                var r = p.magnitude / 0.5f;
                var a = Mathf.Clamp01(1f - r);
                a = a * a * (3f - (2f * a));
                var swirl = 0.85f + (0.15f * Mathf.Sin((Mathf.Atan2(p.y, p.x) * 5f) + (r * 6f)));
                return new Color(1f, 1f, 1f, a * swirl);
            });
            return canvas.ToTexture();
        }

        private static Texture2D DrawKart(Color body, int index)
        {
            var canvas = new Canvas(KartWidth, KartHeight, KartPixelsPerMeter);
            var dark = Shade(body, 0.55f);
            var suit = index == 0 ? new Color(0.12f, 0.16f, 0.30f) : Shade(body, 0.45f);
            var tire = new Color(0.10f, 0.10f, 0.11f);
            var metal = new Color(0.62f, 0.64f, 0.68f);
            var floor = new Color(0.20f, 0.21f, 0.23f);

            // 影（少しずらして柔らかく）。
            canvas.Soft(p => RoundBox(p, new Vector2(0.03f, -0.035f), new Vector2(0.70f, 0.46f), 0.16f), new Color(0f, 0f, 0f, 0.42f), 0.07f);

            // フレームと車軸。
            canvas.Fill(p => Capsule(p, new Vector2(-0.47f, -0.36f), new Vector2(-0.47f, 0.36f), 0.022f), metal);
            canvas.Fill(p => Capsule(p, new Vector2(0.45f, -0.30f), new Vector2(0.45f, 0.30f), 0.016f), metal);
            canvas.Fill(p => Capsule(p, new Vector2(-0.50f, 0.17f), new Vector2(0.48f, 0.15f), 0.018f), metal);
            canvas.Fill(p => Capsule(p, new Vector2(-0.50f, -0.17f), new Vector2(0.48f, -0.15f), 0.018f), metal);

            // タイヤ（後ろが太い）。トレッドの溝と、外側の明るい縁。
            foreach (var side in new[] { 1f, -1f })
            {
                Tire(canvas, new Vector2(-0.47f, 0.37f * side), new Vector2(0.135f, 0.095f), tire);
                Tire(canvas, new Vector2(0.45f, 0.35f * side), new Vector2(0.11f, 0.07f), tire);
            }

            // 床板、後ろのバンパー。
            canvas.Fill(p => RoundBox(p, new Vector2(0.02f, 0f), new Vector2(0.38f, 0.19f), 0.06f), floor);
            canvas.Fill(p => RoundBox(p, new Vector2(-0.665f, 0f), new Vector2(0.04f, 0.40f), 0.035f), Shade(dark, 0.8f));

            // エンジン（右後ろ）と排気管。
            canvas.Fill(p => RoundBox(p, new Vector2(-0.30f, -0.21f), new Vector2(0.10f, 0.075f), 0.02f), new Color(0.34f, 0.35f, 0.38f));
            for (var fin = 0; fin < 5; fin++)
            {
                var x = -0.37f + (fin * 0.035f);
                canvas.Fill(p => Capsule(p, new Vector2(x, -0.27f), new Vector2(x, -0.15f), 0.006f), metal);
            }

            canvas.Fill(p => Capsule(p, new Vector2(-0.38f, -0.29f), new Vector2(-0.60f, -0.29f), 0.018f), new Color(0.48f, 0.42f, 0.38f));

            // サイドポッドと前のカウル（車体色、左上から光）。
            foreach (var side in new[] { 1f, -1f })
            {
                canvas.Shaded(p => RoundBox(p, new Vector2(-0.07f, 0.285f * side), new Vector2(0.24f, 0.055f), 0.05f), body, 0.22f);
            }

            canvas.Shaded(p => Nose(p), body, 0.25f);
            canvas.Fill(p => RoundBox(p, new Vector2(0.705f, 0f), new Vector2(0.025f, 0.24f), 0.02f), dark);
            canvas.Soft(p => RoundBox(p, new Vector2(0.52f, 0.09f), new Vector2(0.12f, 0.035f), 0.035f), new Color(1f, 1f, 1f, 0.35f), 0.03f);

            // ゼッケン。
            canvas.Fill(p => Circle(p, new Vector2(0.53f, 0f), 0.072f), new Color(0.97f, 0.97f, 0.95f));
            canvas.Fill(p => Ring(p, new Vector2(0.53f, 0f), 0.072f, 0.012f), dark);
            Digit(canvas, index + 1, new Vector2(0.53f, 0f), 0.045f, new Color(0.1f, 0.1f, 0.12f));

            // シート、ハンドル、ドライバー。
            canvas.Fill(p => RoundBox(p, new Vector2(-0.12f, 0f), new Vector2(0.13f, 0.15f), 0.06f), new Color(0.12f, 0.12f, 0.13f));
            canvas.Fill(p => Ring(p, new Vector2(0.20f, 0f), 0.075f, 0.016f), new Color(0.14f, 0.14f, 0.15f));
            canvas.Fill(p => Capsule(p, new Vector2(0.10f, 0f), new Vector2(0.20f, 0f), 0.014f), metal);
            canvas.Shaded(p => RoundBox(p, new Vector2(-0.10f, 0f), new Vector2(0.10f, 0.165f), 0.07f), suit, 0.2f);
            foreach (var side in new[] { 1f, -1f })
            {
                canvas.Shaded(p => Capsule(p, new Vector2(-0.04f, 0.15f * side), new Vector2(0.15f, 0.075f * side), 0.034f), suit, 0.2f);
                canvas.Fill(p => Circle(p, new Vector2(0.175f, 0.07f * side), 0.03f), new Color(0.15f, 0.15f, 0.16f));
            }

            var helmet = new Vector2(-0.08f, 0f);
            canvas.Shaded(p => Circle(p, helmet, 0.105f), new Color(0.95f, 0.95f, 0.94f), 0.3f);
            canvas.Fill(p => Mathf.Max(Circle(p, helmet, 0.105f), Mathf.Abs(p.y - helmet.y) - 0.03f), body);
            canvas.Fill(p => Mathf.Max(Circle(p, helmet, 0.092f), -(p.x - (helmet.x + 0.035f))), new Color(0.08f, 0.12f, 0.22f));
            canvas.Soft(p => Circle(p, helmet + new Vector2(-0.025f, 0.035f), 0.03f), new Color(1f, 1f, 1f, 0.55f), 0.02f);
            return canvas.ToTexture();
        }

        private static void Tire(Canvas canvas, Vector2 center, Vector2 half, Color tire)
        {
            canvas.Fill(p => RoundBox(p, center, half, 0.035f), tire);
            for (var k = -4; k <= 4; k++)
            {
                var x = center.x + (k * half.x * 0.22f);
                canvas.Fill(p => Mathf.Max(RoundBox(p, center, half - new Vector2(0.012f, 0.012f), 0.03f), Mathf.Abs(p.x - x) - 0.006f), new Color(0.05f, 0.05f, 0.05f));
            }

            canvas.Soft(p => RoundBox(p, center + new Vector2(0f, half.y * 0.35f), new Vector2(half.x * 0.8f, half.y * 0.25f), 0.02f), new Color(1f, 1f, 1f, 0.12f), 0.02f);
        }

        /// <summary>前の車輪の間のカウル。後ろが広く、先が少し細い。</summary>
        private static float Nose(Vector2 p)
        {
            var t = Mathf.Clamp01((p.x - 0.30f) / 0.40f);
            var half = Mathf.Lerp(0.25f, 0.20f, t);
            var box = RoundBox(p, new Vector2(0.50f, 0f), new Vector2(0.20f, 0.25f), 0.07f);
            return Mathf.Max(box, Mathf.Abs(p.y) - half);
        }

        /// <summary>7 セグメントの数字。</summary>
        private static void Digit(Canvas canvas, int digit, Vector2 center, float height, Color color)
        {
            // 車は +x が前なので、数字の上は +x。セグメントは a（上）から g（中）。
            var masks = new[] { 0b0111111, 0b0000110, 0b1011011, 0b1001111, 0b1100110 };
            var mask = masks[Mathf.Clamp(digit, 0, masks.Length - 1)];
            var h = height * 0.5f;
            var w = height * 0.32f;
            Vector2 At(float up, float right) => center + new Vector2(up, -right);
            var segments = new[]
            {
                (At(h, -w), At(h, w)), (At(h, w), At(0f, w)), (At(0f, w), At(-h, w)), (At(-h, -w), At(-h, w)),
                (At(-h, -w), At(0f, -w)), (At(0f, -w), At(h, -w)), (At(0f, -w), At(0f, w)),
            };
            for (var s = 0; s < segments.Length; s++)
            {
                if ((mask & (1 << s)) == 0) { continue; }
                var (a, b) = segments[s];
                canvas.Fill(p => Capsule(p, a, b, height * 0.09f), color);
            }
        }

        private static Color Shade(Color color, float factor) => new(color.r * factor, color.g * factor, color.b * factor, color.a);

        private static float Circle(Vector2 p, Vector2 center, float radius) => (p - center).magnitude - radius;

        private static float Ring(Vector2 p, Vector2 center, float radius, float width) => Mathf.Abs((p - center).magnitude - radius) - width;

        private static float RoundBox(Vector2 p, Vector2 center, Vector2 half, float radius)
        {
            var q = new Vector2(Mathf.Abs(p.x - center.x), Mathf.Abs(p.y - center.y)) - half + new Vector2(radius, radius);
            return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
        }

        private static float Capsule(Vector2 p, Vector2 a, Vector2 b, float radius)
        {
            var pa = p - a;
            var ba = b - a;
            var h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Mathf.Max(1e-6f, Vector2.Dot(ba, ba)));
            return (pa - (ba * h)).magnitude - radius;
        }

        /// <summary>メートルの座標で形を重ね塗りする画素の配列。中心が原点、+x が右、+y が上。</summary>
        private sealed class Canvas
        {
            private readonly int _width;
            private readonly int _height;
            private readonly float _pixelsPerMeter;
            private readonly Color[] _pixels;

            public Canvas(int width, int height, float pixelsPerMeter)
            {
                _width = width;
                _height = height;
                _pixelsPerMeter = pixelsPerMeter;
                _pixels = new Color[width * height];
            }

            public void Each(Func<Vector2, float, Color> shade)
            {
                for (var y = 0; y < _height; y++)
                {
                    for (var x = 0; x < _width; x++) { _pixels[(y * _width) + x] = shade(Point(x, y), 1f / _pixelsPerMeter); }
                }
            }

            /// <summary>縁を 1 画素でぼかして塗る。</summary>
            public void Fill(Func<Vector2, float> distance, Color color) => Paint(distance, (_, _) => color, 1f / _pixelsPerMeter);

            /// <summary>縁を <paramref name="softness"/> m でぼかして塗る（影・光）。</summary>
            public void Soft(Func<Vector2, float> distance, Color color, float softness) => Paint(distance, (_, _) => color, softness);

            /// <summary>左上から光が当たったように、内側ほど明るく、右下ほど暗く塗る。</summary>
            public void Shaded(Func<Vector2, float> distance, Color color, float strength)
            {
                Paint(distance, (p, d) =>
                {
                    var depth = Mathf.Clamp01(-d / 0.05f);
                    var light = 1f + (strength * ((p.y * 0.6f) + (p.x * 0.25f))) + (0.12f * depth) - 0.12f;
                    return new Color(Mathf.Clamp01(color.r * light), Mathf.Clamp01(color.g * light), Mathf.Clamp01(color.b * light), color.a);
                }, 1f / _pixelsPerMeter);
            }

            public Texture2D ToTexture()
            {
                var texture = new Texture2D(_width, _height, TextureFormat.RGBA32, false);
                texture.SetPixels(_pixels);
                texture.Apply();
                return texture;
            }

            private void Paint(Func<Vector2, float> distance, Func<Vector2, float, Color> color, float softness)
            {
                for (var y = 0; y < _height; y++)
                {
                    for (var x = 0; x < _width; x++)
                    {
                        var p = Point(x, y);
                        var d = distance(p);
                        var coverage = Mathf.Clamp01(0.5f - (d / softness));
                        if (coverage <= 0f) { continue; }
                        var c = color(p, d);
                        var alpha = c.a * coverage;
                        var index = (y * _width) + x;
                        var under = _pixels[index];
                        var outA = alpha + (under.a * (1f - alpha));
                        if (outA <= 0f) { continue; }
                        var r = ((c.r * alpha) + (under.r * under.a * (1f - alpha))) / outA;
                        var g = ((c.g * alpha) + (under.g * under.a * (1f - alpha))) / outA;
                        var b = ((c.b * alpha) + (under.b * under.a * (1f - alpha))) / outA;
                        _pixels[index] = new Color(r, g, b, outA);
                    }
                }
            }

            private Vector2 Point(int x, int y) => new(
                ((x + 0.5f) - (_width * 0.5f)) / _pixelsPerMeter,
                ((y + 0.5f) - (_height * 0.5f)) / _pixelsPerMeter);
        }
    }
}
