using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>
    /// 図形を組み合わせてドット絵の素材 PNG を描き出す。既にあるファイルとそのインポート設定は上書きしない。乱数は使わない。
    /// 人物・敵は 16 ピクセル = 1 ワールド単位、背景は 20 ピクセル = 1 ワールド単位。どの絵も足元中央（背景は中央）を基準点にする。
    /// </summary>
    public static class LedgerArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";
        public const int PixelsPerUnit = 16;
        public const int BackgroundPixelsPerUnit = 20;

        public const string BackgroundFile = "Background.png";
        public const string PlayerFile = "Player.png";
        public const string MarkerFile = "Marker.png";

        /// <summary>敵の絵（Enemies.txt の ID の順。ファイル名 = ID）。</summary>
        public static readonly string[] EnemyIds = { "imp", "warden", "moth", "spitter", "sprout", "kiln" };

        /// <summary>意図の印（<see cref="LedgerHud.IntentIcon"/> の順）。</summary>
        public static readonly string[] IntentFiles = { "IntentAttack.png", "IntentBlock.png", "IntentBuff.png", "IntentDebuff.png", "IntentSummon.png" };

        /// <summary>カードの絵（Cards.txt の ID の順。ファイル名 = Card_ID）。</summary>
        public static readonly string[] CardIds =
        {
            "strike", "guard", "crush", "twin", "sweep", "sparks", "quickcut", "shieldbash", "venom", "finisher",
            "brace", "glance", "sap", "kindle", "purge", "smog", "fortress", "temper", "bulwark", "fumes",
        };

        private static readonly Color32 _outline = new(16, 10, 8, 255);

        public static string EnemyFile(string id) => id + ".png";

        public static string CardFile(string id) => "Card_" + id + ".png";

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtRoot);
            var created = new List<string>();
            Save(created, BackgroundFile, 512, 216, Background, BackgroundPixelsPerUnit, SpriteAlignment.Center);
            Save(created, PlayerFile, 32, 32, Player);
            Save(created, MarkerFile, 32, 8, Marker);
            Save(created, EnemyFile("imp"), 32, 32, Imp);
            Save(created, EnemyFile("warden"), 32, 32, Warden);
            Save(created, EnemyFile("moth"), 32, 32, Moth);
            Save(created, EnemyFile("spitter"), 32, 32, Spitter);
            Save(created, EnemyFile("sprout"), 32, 32, Sprout);
            Save(created, EnemyFile("kiln"), 48, 48, Kiln);
            Save(created, IntentFiles[0], 16, 16, c => Blade(c, new Color32(235, 90, 70, 255)));
            Save(created, IntentFiles[1], 16, 16, c => Shield(c, new Color32(110, 170, 235, 255)));
            Save(created, IntentFiles[2], 16, 16, c => ArrowUp(c, new Color32(240, 200, 80, 255)));
            Save(created, IntentFiles[3], 16, 16, c => Drop(c, new Color32(170, 110, 220, 255)));
            Save(created, IntentFiles[4], 16, 16, Summon);
            foreach (var id in CardIds) { Save(created, CardFile(id), 16, 16, c => CardIcon(c, id)); }
            AssetDatabase.Refresh();
            foreach (var (path, ppu, alignment) in _imports) { Import(path, ppu, alignment); }
            _imports.Clear();
        }

        private static readonly List<(string, int, SpriteAlignment)> _imports = new();

        private static void Save(List<string> created, string file, int width, int height, Action<Canvas> paint, int ppu = PixelsPerUnit, SpriteAlignment alignment = SpriteAlignment.BottomCenter)
        {
            var path = ArtRoot + file;
            if (File.Exists(path)) { return; }
            var canvas = new Canvas(width, height);
            paint(canvas);
            var texture = new Texture2D(canvas.Width, canvas.Height, TextureFormat.RGBA32, false);
            texture.SetPixels32(canvas.Pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            created.Add(path);
            _imports.Add((path, ppu, alignment));
        }

        private static void Import(string path, int ppu, SpriteAlignment alignment)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = ppu;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)alignment;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        // 背景：燠火の洞窟。上は暗い岩、地平に橙の照り返し、下は床。
        private static void Background(Canvas c)
        {
            for (int y = 0; y < c.Height; y++)
            {
                var t = (float)y / c.Height;
                var glow = Mathf.Clamp01(1f - (Mathf.Abs(t - .42f) * 3.2f));
                var r = (byte)Mathf.Lerp(18, 92, glow);
                var g = (byte)Mathf.Lerp(14, 44, glow);
                var b = (byte)Mathf.Lerp(20, 30, glow);
                c.Rect(0, y, c.Width, 1, new Color32(r, g, b, 255));
            }

            // 床（下 1/3）と石の継ぎ目。
            c.Rect(0, 0, c.Width, 70, new Color32(34, 24, 22, 255));
            c.Rect(0, 70, c.Width, 3, new Color32(110, 60, 34, 255));
            for (int x = 0; x < c.Width; x += 37) { c.Rect(x, 8 + (x * 7 % 40), 18, 1, new Color32(52, 38, 32, 255)); }

            // 奥の岩の柱。
            foreach (var (x, w, h) in new[] { (30, 26, 120), (140, 18, 90), (300, 30, 110), (430, 22, 130), (480, 16, 70) })
            {
                c.Rect(x, 73, w, h, new Color32(26, 20, 24, 255));
                c.Rect(x + 3, 73, 3, h, new Color32(40, 30, 34, 255));
            }

            // 火の粉。
            for (int i = 0; i < 60; i++)
            {
                var x = (i * 97) % c.Width;
                var y = 80 + ((i * 53) % 130);
                c.Set(x, y, new Color32(255, (byte)(140 + (i * 11 % 90)), 60, 255));
            }
        }

        // 自機：灯を提げた頭巾の書記。
        private static void Player(Canvas c)
        {
            var cloak = new Color32(60, 70, 110, 255);
            var dark = new Color32(36, 42, 70, 255);
            c.Rect(10, 0, 4, 3, dark);
            c.Rect(17, 0, 4, 3, dark);
            c.Ellipse(15.5f, 10, 7, 10, cloak);
            c.Rect(9, 3, 14, 6, dark);
            c.Ellipse(15.5f, 21, 5.5f, 5.5f, cloak);
            c.Ellipse(15.5f, 20, 3.5f, 3.5f, new Color32(230, 190, 150, 255));
            c.Rect(13, 20, 1, 2, _outline);
            c.Rect(17, 20, 1, 2, _outline);
            c.Ellipse(15.5f, 25, 6, 3, cloak);

            // 灯（右手）と帳面（左手）。
            c.Rect(24, 6, 1, 8, new Color32(80, 70, 60, 255));
            c.Ellipse(24.5f, 5, 3, 3.5f, new Color32(255, 200, 80, 255));
            c.Ellipse(24.5f, 5, 1.5f, 2, new Color32(255, 250, 200, 255));
            c.Rect(4, 9, 6, 7, new Color32(150, 60, 40, 255));
            c.Rect(5, 10, 4, 5, new Color32(230, 220, 190, 255));
            c.Outline(_outline);
        }

        // 対象の印：足元の楕円。
        private static void Marker(Canvas c)
        {
            c.Ellipse(16, 4, 15, 3.5f, new Color32(255, 220, 90, 255));
            c.Ellipse(16, 4, 12, 2.2f, new Color32(0, 0, 0, 0));
        }

        private static void Imp(Canvas c)
        {
            var skin = new Color32(220, 90, 50, 255);
            c.Rect(11, 0, 3, 4, skin);
            c.Rect(18, 0, 3, 4, skin);
            c.Ellipse(16, 9, 7, 7, skin);
            c.Ellipse(16, 18, 6.5f, 6, skin);
            c.Rect(10, 22, 2, 4, new Color32(240, 220, 160, 255));
            c.Rect(20, 22, 2, 4, new Color32(240, 220, 160, 255));
            c.Rect(13, 18, 2, 2, new Color32(255, 240, 120, 255));
            c.Rect(18, 18, 2, 2, new Color32(255, 240, 120, 255));
            c.Rect(14, 14, 4, 1, _outline);
            c.Rect(6, 8, 4, 2, skin);
            c.Rect(22, 8, 4, 2, skin);
            c.Rect(3, 9, 3, 1, new Color32(240, 220, 160, 255));
            c.Outline(_outline);
        }

        private static void Warden(Canvas c)
        {
            var iron = new Color32(120, 128, 140, 255);
            var dark = new Color32(70, 76, 88, 255);
            c.Rect(10, 0, 5, 6, dark);
            c.Rect(17, 0, 5, 6, dark);
            c.Rect(9, 6, 14, 14, iron);
            c.Rect(10, 20, 12, 9, iron);
            c.Rect(11, 24, 10, 2, _outline);
            c.Rect(14, 24, 1, 2, new Color32(255, 120, 60, 255));
            c.Rect(18, 24, 1, 2, new Color32(255, 120, 60, 255));
            c.Rect(15, 29, 2, 3, new Color32(200, 60, 40, 255));

            // 大盾（左）。
            c.Rect(2, 5, 9, 16, new Color32(90, 70, 50, 255));
            c.Rect(3, 6, 7, 14, new Color32(150, 120, 70, 255));
            c.Rect(6, 6, 1, 14, new Color32(90, 70, 50, 255));
            c.Rect(23, 8, 3, 12, dark);
            c.Outline(_outline);
        }

        private static void Moth(Canvas c)
        {
            var wing = new Color32(140, 90, 180, 255);
            c.Ellipse(9, 18, 8, 9, wing);
            c.Ellipse(23, 18, 8, 9, wing);
            c.Ellipse(9, 8, 6, 6, new Color32(110, 70, 150, 255));
            c.Ellipse(23, 8, 6, 6, new Color32(110, 70, 150, 255));
            c.Ellipse(9, 19, 3, 3, new Color32(240, 210, 90, 255));
            c.Ellipse(23, 19, 3, 3, new Color32(240, 210, 90, 255));
            c.Ellipse(9, 19, 1.4f, 1.4f, _outline);
            c.Ellipse(23, 19, 1.4f, 1.4f, _outline);
            c.Rect(15, 4, 3, 22, new Color32(70, 50, 60, 255));
            c.Rect(13, 26, 1, 4, new Color32(70, 50, 60, 255));
            c.Rect(19, 26, 1, 4, new Color32(70, 50, 60, 255));
            c.Outline(_outline);
        }

        private static void Spitter(Canvas c)
        {
            var skin = new Color32(90, 140, 70, 255);
            c.Ellipse(16, 9, 13, 9, skin);
            c.Ellipse(9, 18, 4, 4, skin);
            c.Ellipse(23, 18, 4, 4, skin);
            c.Ellipse(9, 18, 2, 2, new Color32(240, 230, 120, 255));
            c.Ellipse(23, 18, 2, 2, new Color32(240, 230, 120, 255));
            c.Rect(8, 7, 16, 2, new Color32(50, 80, 40, 255));
            c.Ellipse(16, 4, 9, 3, new Color32(150, 190, 110, 255));
            foreach (var (x, y) in new[] { (6, 12), (12, 14), (21, 13), (25, 10) }) { c.Set(x, y, new Color32(60, 100, 50, 255)); }
            c.Outline(_outline);
        }

        private static void Sprout(Canvas c)
        {
            c.Rect(14, 0, 4, 16, new Color32(80, 70, 66, 255));
            c.Ellipse(8, 14, 7, 3, new Color32(130, 130, 124, 255));
            c.Ellipse(24, 18, 7, 3, new Color32(130, 130, 124, 255));
            c.Ellipse(10, 6, 6, 2.5f, new Color32(110, 110, 104, 255));
            c.Ellipse(16, 22, 6, 7, new Color32(160, 70, 50, 255));
            c.Ellipse(16, 23, 3, 4, new Color32(255, 170, 70, 255));
            c.Ellipse(16, 24, 1.5f, 2, new Color32(255, 240, 170, 255));
            c.Outline(_outline);
        }

        private static void Kiln(Canvas c)
        {
            var brick = new Color32(120, 60, 44, 255);
            var mortar = new Color32(70, 34, 26, 255);
            c.Rect(8, 0, 8, 6, mortar);
            c.Rect(32, 0, 8, 6, mortar);
            c.Ellipse(24, 22, 18, 18, brick);
            for (int y = 6; y < 40; y += 5) { c.Rect(7, y, 34, 1, mortar); }
            c.Ellipse(24, 18, 9, 7, new Color32(30, 10, 8, 255));
            c.Ellipse(24, 17, 7, 5, new Color32(255, 120, 40, 255));
            c.Ellipse(24, 16, 4, 3, new Color32(255, 230, 140, 255));
            c.Rect(14, 30, 6, 3, new Color32(255, 200, 90, 255));
            c.Rect(28, 30, 6, 3, new Color32(255, 200, 90, 255));
            c.Rect(20, 40, 8, 6, mortar);
            c.Rect(22, 44, 4, 4, new Color32(255, 150, 60, 255));
            c.Rect(1, 16, 6, 12, brick);
            c.Rect(41, 16, 6, 12, brick);
            c.Outline(_outline);
        }

        private static void Blade(Canvas c, Color32 color)
        {
            for (int i = 0; i < 10; i++) { c.Rect(3 + i, 3 + i, 2, 2, color); }
            c.Rect(2, 6, 5, 1, new Color32(200, 170, 110, 255));
            c.Rect(5, 3, 1, 5, new Color32(200, 170, 110, 255));
            c.Outline(_outline);
        }

        private static void Shield(Canvas c, Color32 color)
        {
            c.Rect(3, 6, 10, 8, color);
            c.Ellipse(8, 6, 5, 5, color);
            c.Rect(7, 3, 2, 10, new Color32(240, 240, 250, 255));
            c.Outline(_outline);
        }

        private static void ArrowUp(Canvas c, Color32 color)
        {
            c.Rect(6, 1, 4, 8, color);
            for (int i = 0; i < 5; i++) { c.Rect(3 + i, 8 + i, 10 - (2 * i), 1, color); }
            c.Outline(_outline);
        }

        private static void Drop(Canvas c, Color32 color)
        {
            c.Ellipse(8, 6, 5, 5, color);
            for (int i = 0; i < 5; i++) { c.Rect(4 + i, 10 + i, 8 - (2 * i), 1, color); }
            c.Rect(6, 5, 2, 2, new Color32(250, 240, 255, 255));
            c.Outline(_outline);
        }

        private static void Summon(Canvas c)
        {
            var color = new Color32(255, 170, 60, 255);
            c.Rect(7, 2, 2, 12, color);
            c.Rect(2, 7, 12, 2, color);
            c.Rect(4, 4, 2, 2, color);
            c.Rect(10, 10, 2, 2, color);
            c.Rect(10, 4, 2, 2, color);
            c.Rect(4, 10, 2, 2, color);
            c.Outline(_outline);
        }

        private static void CardIcon(Canvas c, string id)
        {
            var red = new Color32(235, 110, 80, 255);
            var blue = new Color32(110, 170, 235, 255);
            var green = new Color32(120, 200, 90, 255);
            var gold = new Color32(240, 200, 80, 255);
            var violet = new Color32(170, 110, 220, 255);
            switch (id)
            {
                case "strike": Blade(c, new Color32(220, 220, 230, 255)); break;
                case "guard": Shield(c, blue); break;
                case "crush":
                    c.Rect(3, 3, 4, 10, new Color32(160, 120, 80, 255));
                    c.Rect(1, 9, 9, 5, new Color32(150, 150, 160, 255));
                    c.Rect(10, 2, 4, 4, red);
                    c.Outline(_outline);
                    break;
                case "twin":
                    for (int i = 0; i < 9; i++)
                    {
                        c.Set(2 + i, 4 + i, new Color32(230, 230, 240, 255));
                        c.Set(13 - i, 4 + i, new Color32(230, 230, 240, 255));
                    }

                    c.Outline(_outline);
                    break;
                case "sweep":
                    c.Ring(8, 3, 10, 8, new Color32(230, 230, 240, 255));
                    c.Outline(_outline);
                    break;
                case "sparks":
                    foreach (var (x, y) in new[] { (3, 11), (8, 6), (12, 12), (5, 3), (12, 3) }) { c.Rect(x, y, 2, 2, gold); }
                    c.Outline(_outline);
                    break;
                case "quickcut":
                    Blade(c, new Color32(200, 240, 255, 255));
                    c.Rect(1, 12, 5, 1, gold);
                    break;
                case "shieldbash":
                    Shield(c, blue);
                    c.Rect(12, 2, 3, 3, red);
                    break;
                case "venom": Blade(c, green); break;
                case "finisher":
                    c.Ellipse(8, 6, 5, 6, red);
                    c.Ellipse(8, 5, 3, 3.5f, gold);
                    c.Rect(7, 11, 2, 4, red);
                    c.Outline(_outline);
                    break;
                case "brace":
                    Shield(c, new Color32(150, 200, 240, 255));
                    c.Rect(1, 1, 4, 3, new Color32(230, 220, 190, 255));
                    break;
                case "glance":
                    c.Ellipse(8, 8, 7, 4, new Color32(230, 230, 220, 255));
                    c.Ellipse(8, 8, 3, 3, blue);
                    c.Ellipse(8, 8, 1.3f, 1.3f, _outline);
                    c.Outline(_outline);
                    break;
                case "sap":
                    c.Ellipse(8, 8, 7, 4, violet);
                    c.Ellipse(8, 8, 2, 3, _outline);
                    c.Outline(_outline);
                    break;
                case "kindle":
                    c.Ellipse(8, 5, 5, 5, new Color32(255, 140, 50, 255));
                    c.Ellipse(8, 9, 3, 5, gold);
                    c.Outline(_outline);
                    break;
                case "purge":
                    c.Rect(4, 2, 8, 10, new Color32(140, 120, 100, 255));
                    c.Rect(3, 12, 10, 2, new Color32(170, 150, 130, 255));
                    c.Rect(6, 4, 1, 6, _outline);
                    c.Rect(9, 4, 1, 6, _outline);
                    c.Outline(_outline);
                    break;
                case "smog":
                    c.Ellipse(5, 8, 4, 3, green);
                    c.Ellipse(10, 9, 5, 4, green);
                    c.Ellipse(8, 5, 6, 3, new Color32(90, 150, 70, 255));
                    c.Outline(_outline);
                    break;
                case "fortress":
                    c.Rect(2, 2, 12, 9, new Color32(140, 140, 150, 255));
                    c.Rect(2, 11, 3, 3, new Color32(140, 140, 150, 255));
                    c.Rect(7, 11, 2, 3, new Color32(140, 140, 150, 255));
                    c.Rect(11, 11, 3, 3, new Color32(140, 140, 150, 255));
                    c.Rect(6, 2, 4, 5, _outline);
                    c.Outline(_outline);
                    break;
                case "temper": ArrowUp(c, red); break;
                case "bulwark":
                    Shield(c, gold);
                    c.Ellipse(8, 8, 2, 2, new Color32(255, 120, 50, 255));
                    break;
                case "fumes":
                    c.Ellipse(6, 5, 4, 3, new Color32(90, 90, 90, 255));
                    c.Ellipse(10, 10, 5, 3, new Color32(120, 120, 120, 255));
                    c.Rect(7, 1, 2, 3, new Color32(255, 140, 50, 255));
                    c.Outline(_outline);
                    break;
                default:
                    throw new InvalidOperationException("カードの絵がありません: " + id);
            }
        }

        private sealed class Canvas
        {
            public Canvas(int width, int height)
            {
                Width = width;
                Height = height;
                Pixels = new Color32[width * height];
            }

            public int Width { get; }

            public int Height { get; }

            public Color32[] Pixels { get; }

            public void Set(int x, int y, Color32 color)
            {
                if (x < 0 || y < 0 || x >= Width || y >= Height) { return; }
                Pixels[(y * Width) + x] = color;
            }

            public void Rect(int x, int y, int width, int height, Color32 color)
            {
                for (int py = y; py < y + height; py++)
                {
                    for (int px = x; px < x + width; px++) { Set(px, py, color); }
                }
            }

            public void Ellipse(float cx, float cy, float rx, float ry, Color32 color)
            {
                for (int py = (int)(cy - ry - 1); py <= (int)(cy + ry + 1); py++)
                {
                    for (int px = (int)(cx - rx - 1); px <= (int)(cx + rx + 1); px++)
                    {
                        float dx = (px + .5f - cx) / rx;
                        float dy = (py + .5f - cy) / ry;
                        if ((dx * dx) + (dy * dy) <= 1) { Set(px, py, color); }
                    }
                }
            }

            public void Ring(float cx, float cy, float outer, float inner, Color32 color)
            {
                for (int py = 0; py < Height; py++)
                {
                    for (int px = 0; px < Width; px++)
                    {
                        float dx = px + .5f - cx;
                        float dy = py + .5f - cy;
                        float r = Mathf.Sqrt((dx * dx) + (dy * dy));
                        if (r <= outer && r >= inner) { Set(px, py, color); }
                    }
                }
            }

            /// <summary>不透明な画素の外側 1 ピクセルを縁取る。</summary>
            public void Outline(Color32 color)
            {
                var copy = (Color32[])Pixels.Clone();
                for (int y = 0; y < Height; y++)
                {
                    for (int x = 0; x < Width; x++)
                    {
                        if (copy[(y * Width) + x].a != 0) { continue; }
                        bool edge = false;
                        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        {
                            int nx = x + dx;
                            int ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= Width || ny >= Height) { continue; }
                            if (copy[(ny * Width) + nx].a > 200) { edge = true; }
                        }

                        if (edge) { Pixels[(y * Width) + x] = color; }
                    }
                }
            }
        }
    }
}
