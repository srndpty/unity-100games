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
    /// ランクの文字はフォントを使わず線分と円弧で、スートの印は距離関数の塗りで描く（どちらも白で描き、色は SpriteRenderer で付ける）。
    /// </summary>
    public static class SevenLanternsArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";
        public const string BackdropFile = "Backdrop.png";
        public const string FaceFile = "CardFace.png";
        public const string BackFile = "CardBack.png";
        public const string SlotFile = "Slot.png";
        public const string HighlightFile = "Highlight.png";

        /// <summary>札の画像の 1 単位（札の幅）あたりの画素数。</summary>
        public const int CardPixelsPerUnit = 200;

        private const int CardWidthPixels = CardPixelsPerUnit;
        private const int CardHeightPixels = CardPixelsPerUnit * 7 / 5;
        private const int GlyphSize = 128;
        private const int BackdropSize = 512;
        private const int Samples = 3;
        private const float StrokeWidth = .1f;
        private const int ArcSegments = 28;
        private const float CardHalfWidth = .5f;
        private const float CardHalfHeight = .7f;
        private const float CornerRadius = .08f;

        private static readonly Color _clear = new(0, 0, 0, 0);

        public static string RankFile(int rank) => $"Rank{rank}.png";

        public static string SuitFile(Core.Suit suit) => $"Suit{suit}.png";

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtRoot);
            var created = new List<(string Path, int PixelsPerUnit)>();
            Save(created, BackdropFile, BackdropSize, BackdropSize, BackdropSize, Backdrop);
            Save(created, FaceFile, CardWidthPixels, CardHeightPixels, CardPixelsPerUnit, Face);
            Save(created, BackFile, CardWidthPixels, CardHeightPixels, CardPixelsPerUnit, Back);
            Save(created, SlotFile, CardWidthPixels, CardHeightPixels, CardPixelsPerUnit, Slot);
            Save(created, HighlightFile, CardWidthPixels, CardHeightPixels, CardPixelsPerUnit, Highlight);
            for (var rank = Core.Card.MinRank; rank <= Core.Card.MaxRank; rank++)
            {
                var strokes = RankStrokes(rank);
                Save(created, RankFile(rank), GlyphSize, GlyphSize, GlyphSize, (x, y) => Stroke(strokes, x, y));
            }

            foreach (Core.Suit suit in Enum.GetValues(typeof(Core.Suit)))
            {
                var shape = SuitShape(suit);
                Save(created, SuitFile(suit), GlyphSize, GlyphSize, GlyphSize, (x, y) => shape(x, y) ? Color.white : _clear);
            }

            AssetDatabase.Refresh();

            // 既存の素材のインポート設定は調整を保つため触らない。
            foreach (var (path, pixelsPerUnit) in created) { Import(path, pixelsPerUnit); }
        }

        private static void Save(List<(string Path, int PixelsPerUnit)> created, string file, int width, int height, int pixelsPerUnit, Func<float, float, Color> shader)
        {
            var path = ArtRoot + file;
            if (File.Exists(path)) { return; }
            var texture = Paint(width, height, pixelsPerUnit, shader);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            created.Add((path, pixelsPerUnit));
        }

        private static void Import(string path, int pixelsPerUnit)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
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

        // 座標は画像の中心が原点、1 が pixelsPerUnit 画素、y は上向き。
        private static Texture2D Paint(int width, int height, int pixelsPerUnit, Func<float, float, Color> shader)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            for (var py = 0; py < height; py++)
            {
                for (var px = 0; px < width; px++)
                {
                    var sum = Vector4.zero;
                    for (var s = 0; s < Samples * Samples; s++)
                    {
                        var x = (px + (((s % Samples) + .5f) / Samples) - (width / 2f)) / pixelsPerUnit;
                        var y = (py + (((s / Samples) + .5f) / Samples) - (height / 2f)) / pixelsPerUnit;
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

        // 夜の水辺。上は深い藍、下は緑がかった闇。ところどころに灯籠の明かりがにじむ。
        private static Color Backdrop(float x, float y)
        {
            var height = Mathf.Clamp01(y + .5f);
            var color = Color.Lerp(new Color(.03f, .1f, .1f), new Color(.06f, .08f, .19f), height);
            var glow = 0f;
            glow += Glow(x, y, -.3f, .3f, .22f);
            glow += Glow(x, y, .28f, .12f, .18f);
            glow += Glow(x, y, .05f, -.32f, .25f);
            return Color.Lerp(color, new Color(.36f, .24f, .1f), Mathf.Clamp01(glow) * .35f);
        }

        private static float Glow(float x, float y, float cx, float cy, float radius)
        {
            var d = new Vector2(x - cx, y - cy).magnitude / radius;
            return Mathf.Exp(-d * d * 2f);
        }

        private static float RoundedBox(float x, float y, float halfWidth, float halfHeight, float radius)
        {
            var qx = Mathf.Max(Mathf.Abs(x) - halfWidth + radius, 0f);
            var qy = Mathf.Max(Mathf.Abs(y) - halfHeight + radius, 0f);
            var outside = new Vector2(qx, qy).magnitude;
            var inside = Mathf.Min(Mathf.Max(Mathf.Abs(x) - halfWidth + radius, Mathf.Abs(y) - halfHeight + radius), 0f);
            return outside + inside - radius;
        }

        private static float CardShape(float x, float y) => RoundedBox(x, y, CardHalfWidth - .01f, CardHalfHeight - .01f, CornerRadius);

        // 札の表。生成りの紙に細い縁取り。
        private static Color Face(float x, float y)
        {
            var d = CardShape(x, y);
            if (d > 0) { return _clear; }
            if (d > -.012f) { return new Color(.55f, .5f, .42f); }
            var paper = Color.Lerp(new Color(.99f, .97f, .9f), new Color(.95f, .91f, .82f), Mathf.Clamp01(-y + .3f) * .5f);
            return d > -.045f && d < -.035f ? Color.Lerp(paper, new Color(.78f, .62f, .38f), .6f) : paper;
        }

        // 札の裏。藍の地に金の縁と菱の格子、中央に灯籠の窓。
        private static Color Back(float x, float y)
        {
            var d = CardShape(x, y);
            if (d > 0) { return _clear; }
            var gold = new Color(.86f, .66f, .3f);
            if (d > -.03f) { return gold; }
            var indigo = new Color(.1f, .14f, .3f);
            if (d > -.06f) { return indigo; }
            if (d > -.075f) { return gold; }
            var u = (x + y) * 9f;
            var v = (x - y) * 9f;
            var lattice = Mathf.Abs(u - Mathf.Round(u)) < .06f || Mathf.Abs(v - Mathf.Round(v)) < .06f;
            var lantern = RoundedBox(x, y, .16f, .24f, .1f);
            if (lantern < 0) { return Color.Lerp(new Color(1f, .78f, .38f), new Color(.95f, .5f, .2f), Mathf.Clamp01(-y * 2f + .5f)); }
            if (lantern < .03f) { return gold; }
            return lattice ? Color.Lerp(indigo, gold, .35f) : indigo;
        }

        // 空の山の枠。半透明の地と縁。
        private static Color Slot(float x, float y)
        {
            var d = CardShape(x, y);
            if (d > 0) { return _clear; }
            return d > -.025f ? new Color(1f, 1f, 1f, .45f) : new Color(1f, 1f, 1f, .08f);
        }

        // 選んだ札の縁取り（色は SpriteRenderer で付ける）。
        private static Color Highlight(float x, float y)
        {
            var d = CardShape(x, y);
            return d < 0 && d > -.05f ? Color.white : _clear;
        }

        private static Color Stroke(List<(Vector2 A, Vector2 B)> segments, float x, float y)
        {
            // 字形は -1〜1 の正方形で定義する。画像は 1 単位なので 2 倍して合わせる。
            var p = new Vector2(x * 2, y * 2);
            var best = float.MaxValue;
            foreach (var (a, b) in segments)
            {
                var ab = b - a;
                var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                best = Mathf.Min(best, (a + (ab * t) - p).magnitude);
            }

            return best < StrokeWidth ? Color.white : _clear;
        }

        // スートの印。-1〜1 の正方形で定義する。
        private static Func<float, float, bool> SuitShape(Core.Suit suit) => suit switch
        {
            Core.Suit.Sun => (x, y) => SunShape(x * 2, y * 2),
            Core.Suit.Flame => (x, y) => FlameShape(x * 2, y * 2),
            Core.Suit.Moon => (x, y) => MoonShape(x * 2, y * 2),
            _ => (x, y) => WaveShape(x * 2, y * 2),
        };

        // 太陽：円と 8 本の光。
        private static bool SunShape(float x, float y)
        {
            var r = new Vector2(x, y).magnitude;
            if (r < .44f) { return true; }
            if (r < .58f || r > .9f) { return false; }
            var angle = Mathf.Atan2(y, x) / (Mathf.PI / 4f);
            return Mathf.Abs(angle - Mathf.Round(angle)) * (Mathf.PI / 4f) * r < .08f;
        }

        // 炎：下が丸く、上がとがって少し右へなびく滴。
        private static bool FlameShape(float x, float y)
        {
            if (new Vector2(x, y + .35f).magnitude < .48f) { return true; }
            if (y < -.35f || y > .9f) { return false; }
            var t = (y + .35f) / 1.25f;
            var center = Mathf.Sin(t * Mathf.PI) * .12f;
            var halfWidth = .48f * Mathf.Pow(1f - t, .9f);
            return Mathf.Abs(x - center) < halfWidth;
        }

        // 月：三日月。
        private static bool MoonShape(float x, float y) =>
            new Vector2(x, y).magnitude < .78f && new Vector2(x - .36f, y - .2f).magnitude > .62f;

        // 波：うねる 2 本の帯。
        private static bool WaveShape(float x, float y)
        {
            if (Mathf.Abs(x) > .85f) { return false; }
            var curve = Mathf.Sin(x * Mathf.PI * 1.3f) * .2f;
            return Mathf.Abs(y - .28f - curve) < .14f || Mathf.Abs(y + .28f - curve) < .14f;
        }

        // ランクの字形。幅 -0.4〜0.4、高さ -0.68〜0.68 に収める。円弧の角度は度、反時計回りが正。
        private static List<(Vector2 A, Vector2 B)> RankStrokes(int rank)
        {
            var s = new List<(Vector2 A, Vector2 B)>();
            switch (rank)
            {
                case 1:
                    Line(s, new(-.4f, -.68f), new(0f, .68f));
                    Line(s, new(0f, .68f), new(.4f, -.68f));
                    Line(s, new(-.24f, -.16f), new(.24f, -.16f));
                    break;
                case 10:
                    Line(s, new(-.36f, .68f), new(-.36f, -.68f));
                    Line(s, new(-.52f, .48f), new(-.36f, .68f));
                    Ellipse(s, new(.2f, 0f), .26f, .68f);
                    break;
                case 11:
                    Line(s, new(-.1f, .68f), new(.36f, .68f));
                    Line(s, new(.2f, .68f), new(.2f, -.3f));
                    Arc(s, new(-.06f, -.3f), .26f, 0f, -180f);
                    break;
                case 12:
                    Ellipse(s, new(0f, .04f), .4f, .64f);
                    Line(s, new(.08f, -.3f), new(.42f, -.72f));
                    break;
                case 13:
                    Line(s, new(-.32f, .68f), new(-.32f, -.68f));
                    Line(s, new(.38f, .68f), new(-.32f, -.08f));
                    Line(s, new(-.08f, .18f), new(.4f, -.68f));
                    break;
                default:
                    DigitStrokes(s, rank);
                    break;
            }

            return s;
        }

        private static void DigitStrokes(List<(Vector2 A, Vector2 B)> s, int digit)
        {
            switch (digit)
            {
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
                    throw new ArgumentOutOfRangeException(nameof(digit), digit, "2〜9 を指定してください。");
            }
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

        private static void Ellipse(List<(Vector2 A, Vector2 B)> segments, Vector2 center, float radiusX, float radiusY)
        {
            for (var i = 0; i < ArcSegments; i++)
            {
                var a = 2f * Mathf.PI * i / ArcSegments;
                var b = 2f * Mathf.PI * (i + 1) / ArcSegments;
                segments.Add((
                    center + new Vector2(Mathf.Cos(a) * radiusX, Mathf.Sin(a) * radiusY),
                    center + new Vector2(Mathf.Cos(b) * radiusX, Mathf.Sin(b) * radiusY)));
            }
        }

        private static Vector2 Polar(Vector2 center, float radius, float degrees)
        {
            var radians = degrees * Mathf.Deg2Rad;
            return center + (new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * radius);
        }
    }
}
