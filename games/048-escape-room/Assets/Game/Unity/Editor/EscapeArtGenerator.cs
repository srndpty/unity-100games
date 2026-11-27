#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>
    /// 書斎の素材を生成する：座標から決まる模様のテクスチャ（木目・壁紙・床板・文字盤・数字と星座の表・持ち物の絵）と URP のマテリアル。
    /// 既にあるものは上書きしない（調整を残す）。
    /// </summary>
    public static class EscapeArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";

        public const string Wood = "Wood";
        public const string DarkWood = "DarkWood";
        public const string Wall = "Wall";
        public const string Floor = "Floor";
        public const string Ceiling = "Ceiling";
        public const string Brass = "Brass";
        public const string Iron = "Iron";
        public const string Silver = "Silver";
        public const string Black = "Black";
        public const string Night = "Night";
        public const string Navy = "Navy";
        public const string Leather = "Leather";
        public const string Paper = "Paper";
        public const string ClockFace = "ClockFace";
        public const string Lamp = "Lamp";
        public const string Rug = "Rug";
        public const string Glass = "Glass";
        public const string StarDim = "StarDim";

        public static readonly string[] BookColors = { "BookRed", "BookGreen", "BookBlue", "BookBrown", "BookPlum", "BookOchre" };

        public static readonly Color[] GemColors =
        {
            new(0.85f, 0.12f, 0.16f), new(0.16f, 0.32f, 0.9f), new(0.12f, 0.7f, 0.3f), new(0.95f, 0.78f, 0.15f),
        };

        public static readonly string[] ItemIcons = { "Icon_brass_key", "Icon_star_disc", "Icon_gear", "Icon_handle", "Icon_exit_key" };

        /// <summary>数字 0〜9 の字形（5 × 7、上の行から、1 が点）。</summary>
        private static readonly string[] _digits =
        {
            "01110100011000110001100011000101110", "00100011000010000100001000010001110", "01110100010000100010001000100011111",
            "11111000100010000010000011000101110", "00010001100101010010111110001000010", "11111100001111000001000011000101110",
            "00110010001000011110100011000101110", "11111000010001000100010000100001000", "01110100011000101110100011000101110",
            "01110100011000101111000010001001100",
        };

        /// <summary>星座の字形：64 × 64 の中の星の位置（線でつなぐ順）。番号は Core の RingLock.Constellations の順。</summary>
        private static readonly int[][] _constellations =
        {
            new[] { 12, 44, 22, 34, 34, 30, 44, 36, 52, 24, 40, 18 },
            new[] { 32, 54, 32, 34, 32, 14, 14, 36, 50, 32 },
            new[] { 10, 46, 20, 40, 30, 34, 36, 24, 44, 16, 54, 18 },
            new[] { 24, 50, 40, 50, 46, 30, 30, 22, 20, 34 },
            new[] { 32, 52, 32, 30, 12, 26, 52, 26, 32, 12 },
            new[] { 12, 30, 20, 20, 32, 16, 44, 20, 52, 30 },
            new[] { 10, 14, 22, 22, 30, 36, 42, 40, 50, 30, 54, 48 },
            new[] { 12, 32, 24, 22, 40, 26, 52, 36, 40, 44, 24, 42 },
        };

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtRoot);
            var created = new List<string>();
            Save(created, Wood, 64, 128, (x, y) => Grain(x, y, new Color32(122, 80, 48, 255)));
            Save(created, DarkWood, 64, 128, (x, y) => Grain(x, y, new Color32(74, 46, 30, 255)));
            Save(created, Wall, 64, 64, WallPattern);
            Save(created, Floor, 128, 64, FloorPattern);
            Save(created, ClockFace, 128, 128, ClockFacePattern);
            Save(created, "Digits", 10 * 32, 48, DigitsPattern);
            Save(created, "Constellations", 8 * 64, 64, ConstellationPattern);
            Save(created, Rug, 64, 64, RugPattern);
            for (int i = 0; i < ItemIcons.Length; i++)
            {
                var index = i;
                Save(created, ItemIcons[i], 64, 64, (x, y) => IconPattern(index, x, y));
            }

            AssetDatabase.Refresh();
            foreach (var path in created) { Import(path, path.Contains("Icon_", StringComparison.Ordinal)); }

            MaterialOf(Wood, Color.white, Color.black, .25f);
            MaterialOf(DarkWood, Color.white, Color.black, .3f);
            MaterialOf(Wall, Color.white, Color.black, .05f);
            MaterialOf(Floor, Color.white, Color.black, .2f);
            MaterialOf(Ceiling, new Color(.34f, .3f, .26f), Color.black, .05f);
            MaterialOf(Brass, new Color(.78f, .6f, .25f), Color.black, .7f, metallic: .8f);
            MaterialOf(Iron, new Color(.22f, .24f, .26f), Color.black, .45f, metallic: .6f);
            MaterialOf(Silver, new Color(.82f, .84f, .86f), Color.black, .75f, metallic: .9f);
            MaterialOf(Black, new Color(.03f, .03f, .03f), Color.black, .1f);
            MaterialOf(Night, new Color(.03f, .05f, .12f), new Color(.02f, .04f, .1f), .15f);
            MaterialOf(Navy, new Color(.08f, .12f, .3f), Color.black, .4f);
            MaterialOf(Leather, new Color(.5f, .1f, .1f), Color.black, .3f);
            MaterialOf(Paper, new Color(.9f, .86f, .74f), Color.black, .05f);
            MaterialOf(ClockFace, Color.white, Color.black, .2f);
            MaterialOf(Lamp, new Color(1f, .85f, .55f), new Color(2.2f, 1.6f, .7f), .2f);
            MaterialOf(Rug, Color.white, Color.black, 0f);
            MaterialOf(Glass, new Color(.1f, .14f, .2f), Color.black, .95f);
            MaterialOf(StarDim, new Color(.5f, .52f, .6f), new Color(.35f, .36f, .42f), 0f);
            var books = new[] { new Color(.55f, .14f, .12f), new Color(.16f, .36f, .2f), new Color(.14f, .2f, .42f), new Color(.42f, .28f, .16f), new Color(.36f, .16f, .34f), new Color(.66f, .5f, .16f) };
            for (int i = 0; i < BookColors.Length; i++) { MaterialOf(BookColors[i], books[i], Color.black, .25f); }
            for (int g = 0; g < GemColors.Length; g++)
            {
                MaterialOf(GemName(g, false), GemColors[g] * .55f, Color.black, .9f);
                MaterialOf(GemName(g, true), GemColors[g], GemColors[g] * 3f, .9f);
                MaterialOf(StarName(g), GemColors[g], (GemColors[g] * 5f) + new Color(.15f, .15f, .15f), 0f);
            }

            for (int d = 0; d < 10; d++) { AtlasMaterial(DigitName(d), "Digits", 10, d); }
            for (int c = 0; c < _constellations.Length; c++) { AtlasMaterial(SymbolName(c), "Constellations", _constellations.Length, c); }
            AssetDatabase.SaveAssets();
        }

        public static string GemName(int gem, bool lit) => (lit ? "GemLit" : "Gem") + gem;

        public static string StarName(int gem) => "Star" + gem;

        public static string DigitName(int digit) => "Digit" + digit;

        public static string SymbolName(int constellation) => "Symbol" + constellation;

        public static Material Material(string name) =>
            AssetDatabase.LoadAssetAtPath<Material>(ArtRoot + name + ".mat") ?? throw new InvalidOperationException("マテリアルがありません: " + name);

        public static Sprite Icon(int item) =>
            AssetDatabase.LoadAssetAtPath<Sprite>(ArtRoot + ItemIcons[item] + ".png") ?? throw new InvalidOperationException("持ち物の絵がありません: " + ItemIcons[item]);

        private static void MaterialOf(string name, Color color, Color emission, float smoothness, float metallic = 0f)
        {
            var path = ArtRoot + name + ".mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) { return; }
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtRoot + name + ".png");
            if (texture != null) { material.SetTexture("_BaseMap", texture); }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            if (emission != Color.black)
            {
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                material.SetColor("_EmissionColor", emission);
            }

            AssetDatabase.CreateAsset(material, path);
        }

        /// <summary>表の 1 こまを読み取りやすい Unlit で出すマテリアル。</summary>
        private static void AtlasMaterial(string name, string atlas, int cells, int cell)
        {
            var path = ArtRoot + name + ".mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) { return; }
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ArtRoot + atlas + ".png"));
            material.SetTextureScale("_BaseMap", new Vector2(1f / cells, 1f));
            material.SetTextureOffset("_BaseMap", new Vector2((float)cell / cells, 0f));
            material.SetColor("_BaseColor", Color.white);
            AssetDatabase.CreateAsset(material, path);
        }

        private static void Save(List<string> created, string name, int width, int height, Func<int, int, Color32> paint)
        {
            var path = ArtRoot + name + ".png";
            if (File.Exists(path)) { return; }
            var pixels = new Color32[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++) { pixels[(y * width) + x] = paint(x, y); }
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            created.Add(path);
        }

        private static void Import(string path, bool sprite)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = sprite ? TextureImporterType.Sprite : TextureImporterType.Default;
            importer.mipmapEnabled = !sprite;
            importer.filterMode = sprite || path.Contains("Digits", StringComparison.Ordinal) || path.Contains("Constellations", StringComparison.Ordinal) ? FilterMode.Point : FilterMode.Bilinear;
            importer.wrapMode = sprite ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = sprite;
            importer.SaveAndReimport();
        }

        private static int Noise(int x, int y) => (((x * 73) + (y * 151) + (x * y * 7)) % 9) - 4;

        private static Color32 Shade(Color32 c, int amount) =>
            new((byte)Math.Clamp(c.r + amount, 0, 255), (byte)Math.Clamp(c.g + amount, 0, 255), (byte)Math.Clamp(c.b + amount, 0, 255), c.a);

        /// <summary>縦の木目：ゆるく波打つ縞。</summary>
        private static Color32 Grain(int x, int y, Color32 baseColor)
        {
            var wave = Mathf.Sin((x * 0.35f) + (Mathf.Sin(y * 0.08f) * 2f)) * 10f;
            return Shade(baseColor, (int)wave + Noise(x, y));
        }

        /// <summary>深緑の壁紙に金の小さな菱形。</summary>
        private static Color32 WallPattern(int x, int y)
        {
            var c = new Color32(46, 66, 56, 255);
            var dx = Math.Abs((x % 32) - 16);
            var dy = Math.Abs((y % 32) - 16);
            if (dx + dy == 6) { c = new Color32(150, 124, 70, 255); }
            return Shade(c, Noise(x, y) / 2);
        }

        /// <summary>床板：横長の板と継ぎ目。</summary>
        private static Color32 FloorPattern(int x, int y)
        {
            var row = y / 16;
            var c = Grain(x + (row * 37), y, new Color32(96, 62, 38, 255));
            if (y % 16 == 0 || (x + (row * 40)) % 128 == 0) { c = new Color32(48, 30, 20, 255); }
            return c;
        }

        private static Color32 RugPattern(int x, int y)
        {
            var border = x < 4 || y < 4 || x > 59 || y > 59;
            var inner = Math.Abs(x - 32) + Math.Abs(y - 32) < 18;
            Color32 c = border ? new Color32(170, 130, 60, 255) : inner ? new Color32(40, 50, 90, 255) : new Color32(110, 28, 34, 255);
            return Shade(c, Noise(x, y));
        }

        /// <summary>象牙色の文字盤：外周の輪と 12 の目盛り（3・6・9・12 は太い）。</summary>
        private static Color32 ClockFacePattern(int x, int y)
        {
            var dx = x - 63.5f;
            var dy = y - 63.5f;
            var r = Mathf.Sqrt((dx * dx) + (dy * dy));
            if (r > 63f) { return new Color32(60, 40, 24, 255); }
            if (r > 59f) { return new Color32(150, 116, 52, 255); }
            var angle = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;
            var tick = Mathf.Abs(Mathf.DeltaAngle(angle, Mathf.Round(angle / 30f) * 30f));
            var major = Mathf.RoundToInt(angle / 30f) % 3 == 0;
            // 12 時の目盛りは赤い三角（上下を逆さに読まないように）。
            if (Mathf.Abs(angle) < 7f && r > 40f && r < 57f && Mathf.Abs(angle) < (57f - r) * 0.8f) { return new Color32(176, 32, 28, 255); }
            if (r > 46f && r < 56f && tick < (major ? 3.5f : 1.8f)) { return new Color32(30, 24, 20, 255); }
            return new Color32(232, 222, 196, 255);
        }

        /// <summary>数字の表：32 × 48 のこまに 5 × 7 の字形を 5 倍で、金の地に黒。</summary>
        private static Color32 DigitsPattern(int x, int y)
        {
            var digit = x / 32;
            var cx = (x % 32) - 3;
            var cy = 47 - y - 6;
            if (cx >= 0 && cx < 25 && cy >= 0 && cy < 35)
            {
                var bit = _digits[digit][((cy / 5) * 5) + (cx / 5)];
                if (bit == '1') { return new Color32(24, 18, 12, 255); }
            }

            return new Color32(226, 196, 120, 255);
        }

        /// <summary>星座の表：紺の地に、星（金の点）と星をつなぐ細い線。</summary>
        private static Color32 ConstellationPattern(int x, int y)
        {
            var c = x / 64;
            var px = x % 64;
            var py = 63 - y;
            var stars = _constellations[c];
            for (int i = 0; i + 1 < stars.Length; i += 2)
            {
                var dx = px - stars[i];
                var dy = py - stars[i + 1];
                if ((dx * dx) + (dy * dy) <= 9) { return new Color32(255, 222, 120, 255); }
            }

            for (int i = 0; i + 3 < stars.Length; i += 2)
            {
                if (NearSegment(px, py, stars[i], stars[i + 1], stars[i + 2], stars[i + 3])) { return new Color32(150, 160, 200, 255); }
            }

            var edge = px < 2 || py < 2 || px > 61 || py > 61;
            return edge ? new Color32(180, 150, 80, 255) : new Color32(18, 24, 56, 255);
        }

        private static bool NearSegment(int px, int py, int ax, int ay, int bx, int by)
        {
            float vx = bx - ax;
            float vy = by - ay;
            var t = Mathf.Clamp01((((px - ax) * vx) + ((py - ay) * vy)) / ((vx * vx) + (vy * vy)));
            var dx = px - (ax + (t * vx));
            var dy = py - (ay + (t * vy));
            return (dx * dx) + (dy * dy) <= 0.8f;
        }

        /// <summary>持ち物の絵（64 × 64、透過）：0 真鍮の鍵・1 星図の円盤・2 歯車・3 ハンドル・4 出口の鍵。</summary>
        private static Color32 IconPattern(int item, int x, int y)
        {
            var clear = new Color32(0, 0, 0, 0);
            var brass = new Color32(214, 170, 72, 255);
            var silver = new Color32(214, 220, 228, 255);
            var dark = new Color32(40, 32, 24, 255);
            float dx;
            float dy;
            switch (item)
            {
                case 0:
                case 4:
                    {
                        var metal = item == 0 ? brass : silver;
                        dx = x - 18;
                        dy = y - 32;
                        var r = Mathf.Sqrt((dx * dx) + (dy * dy));
                        if (r < 6) { return clear; }
                        if (r < 13) { return metal; }
                        if (x >= 28 && x <= 58 && y >= 29 && y <= 35) { return metal; }
                        if (x >= 46 && x <= 50 && y >= 20 && y < 29) { return metal; }
                        if (x >= 53 && x <= 57 && y >= 22 && y < 29) { return metal; }
                        return clear;
                    }

                case 1:
                    {
                        dx = x - 31.5f;
                        dy = y - 31.5f;
                        var r = Mathf.Sqrt((dx * dx) + (dy * dy));
                        if (r > 28) { return clear; }
                        if (r > 25) { return brass; }
                        var star = ((x * 7) + (y * 13)) % 23 == 0;
                        return star ? new Color32(255, 230, 150, 255) : new Color32(24, 34, 84, 255);
                    }

                case 2:
                    {
                        dx = x - 31.5f;
                        dy = y - 31.5f;
                        var r = Mathf.Sqrt((dx * dx) + (dy * dy));
                        var angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                        var tooth = Mathf.Abs(Mathf.DeltaAngle(angle, Mathf.Round(angle / 36f) * 36f)) < 9f;
                        if (r < 7) { return clear; }
                        if (r < 22 || (tooth && r < 29)) { return r < 10 ? dark : brass; }
                        return clear;
                    }

                default:
                    {
                        var iron = new Color32(110, 116, 124, 255);
                        if (x >= 10 && x <= 54 && y >= 40 && y <= 48) { return iron; }
                        if (x >= 28 && x <= 36 && y >= 10 && y < 40) { return iron; }
                        if (x >= 24 && x <= 40 && y >= 6 && y <= 12) { return dark; }
                        return clear;
                    }
            }
        }
    }
}
