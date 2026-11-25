using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>
    /// 図形を組み合わせてドット絵の素材 PNG を描き出す。既にあるファイルとそのインポート設定は上書きしない。
    /// 1 タイル = 16 ピクセル（Core の 16 単位 = 1 ピクセル）。どの絵も中央を基準点にし、向きのある絵は右（+x）向きに描く。
    /// 体の絵の直径は既定の当たりの円の直径にそろえる（<see cref="AshenStage.ArtRadius"/>）。乱数は使わない。
    /// </summary>
    public static class AshenArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";
        public const int PixelsPerUnit = 16;

        public const string FloorFile = "Floor.png";
        public const string WallFile = "Wall.png";
        public const string StairsFile = "Stairs.png";
        public const string EntranceFile = "Entrance.png";
        public const string HeroFile = "Hero.png";
        public const string HeroDownFile = "HeroDown.png";
        public const string GruntFile = "Grunt.png";
        public const string ArcherFile = "Archer.png";
        public const string ChargerFile = "Charger.png";
        public const string BossFile = "Boss.png";
        public const string ArrowFile = "Arrow.png";
        public const string BoltFile = "Bolt.png";
        public const string PotionFile = "Potion.png";
        public const string CrosshairFile = "Crosshair.png";
        public const string SwingFile = "Swing.png";
        public const string SpinFile = "Spin.png";
        public const string LaneFile = "Lane.png";

        public static readonly string[] SlotFiles = { "IconWeapon.png", "IconArmor.png", "IconHelm.png", "IconRing.png", "IconAmulet.png" };

        private static readonly Color32 _outline = new(12, 8, 6, 255);
        private static readonly Color32 _icon = new(255, 255, 255, 255);
        private static readonly Color32 _iconShade = new(170, 170, 170, 255);

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtRoot);
            var created = new List<(string Path, int Ppu)>();
            Save(created, FloorFile, 16, 16, Floor);
            Save(created, WallFile, 16, 16, Wall);
            Save(created, StairsFile, 16, 16, Stairs);
            Save(created, EntranceFile, 16, 16, Entrance);
            Save(created, HeroFile, 16, 16, Hero);
            Save(created, HeroDownFile, 16, 16, HeroDown);
            Save(created, GruntFile, 14, 14, Grunt);
            Save(created, ArcherFile, 12, 12, Archer);
            Save(created, ChargerFile, 16, 16, Charger);
            Save(created, BossFile, 28, 28, Boss);
            Save(created, ArrowFile, 9, 3, Arrow);
            Save(created, BoltFile, 8, 8, Bolt);
            Save(created, PotionFile, 8, 10, Potion);
            Save(created, CrosshairFile, 11, 11, Crosshair);
            Save(created, SwingFile, 64, 64, Swing, 64);
            Save(created, SpinFile, 64, 64, Spin, 64);
            Save(created, LaneFile, 16, 16, Lane);
            Save(created, SlotFiles[0], 10, 10, IconWeapon);
            Save(created, SlotFiles[1], 10, 10, IconArmor);
            Save(created, SlotFiles[2], 10, 10, IconHelm);
            Save(created, SlotFiles[3], 10, 10, IconRing);
            Save(created, SlotFiles[4], 10, 10, IconAmulet);
            AssetDatabase.Refresh();
            foreach (var (path, ppu) in created) { Import(path, ppu); }
        }

        private static void Save(List<(string, int)> created, string file, int width, int height, Action<Canvas> paint, int ppu = PixelsPerUnit)
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
            created.Add((path, ppu));
        }

        private static void Import(string path, int ppu)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = ppu;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = ppu == PixelsPerUnit ? FilterMode.Point : FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        // 床：灰の積もった石畳。
        private static void Floor(Canvas c)
        {
            c.Rect(0, 0, 16, 16, new Color32(46, 40, 38, 255));
            c.Rect(0, 0, 16, 1, new Color32(34, 29, 28, 255));
            c.Rect(0, 8, 16, 1, new Color32(34, 29, 28, 255));
            c.Rect(0, 0, 1, 8, new Color32(34, 29, 28, 255));
            c.Rect(8, 8, 1, 8, new Color32(34, 29, 28, 255));
            foreach (var (x, y) in new[] { (3, 3), (12, 5), (5, 12), (13, 13), (10, 2) }) { c.Set(x, y, new Color32(78, 72, 68, 255)); }
        }

        // 壁：煤けた煉瓦の積み。上端に明るい縁。
        private static void Wall(Canvas c)
        {
            c.Rect(0, 0, 16, 16, new Color32(70, 48, 40, 255));
            for (int row = 0; row < 4; row++)
            {
                int y = row * 4;
                c.Rect(0, y, 16, 1, new Color32(40, 26, 22, 255));
                int offset = row % 2 == 0 ? 0 : 4;
                for (int x = offset; x < 16; x += 8) { c.Rect(x, y, 1, 4, new Color32(40, 26, 22, 255)); }
            }

            c.Rect(0, 15, 16, 1, new Color32(120, 86, 64, 255));
        }

        // 階段：下へ降りる段と、縁の灯。
        private static void Stairs(Canvas c)
        {
            c.Rect(0, 0, 16, 16, new Color32(30, 24, 22, 255));
            for (int i = 0; i < 5; i++)
            {
                byte shade = (byte)(110 - (i * 18));
                c.Rect(2 + i, 13 - (i * 3), 12 - (2 * i), 2, new Color32(shade, (byte)(shade * .8f), (byte)(shade * .6f), 255));
            }

            c.Rect(0, 0, 16, 1, new Color32(240, 170, 80, 255));
            c.Rect(0, 15, 16, 1, new Color32(240, 170, 80, 255));
        }

        // 入口：床に刻んだ灯の紋。
        private static void Entrance(Canvas c)
        {
            c.Ring(8, 8, 6.5f, 5.5f, new Color32(200, 140, 70, 160));
            c.Rect(7, 4, 2, 8, new Color32(200, 140, 70, 110));
            c.Rect(4, 7, 8, 2, new Color32(200, 140, 70, 110));
        }

        // 自機：外套をまとった剣士（右向き）。体の直径 12 ピクセル（半径 96 単位）。
        private static void Hero(Canvas c)
        {
            c.Ellipse(8, 7, 5, 5.5f, new Color32(150, 54, 40, 255));
            c.Ellipse(8, 7, 3.5f, 4, new Color32(200, 80, 56, 255));
            c.Ellipse(8.5f, 11, 2.6f, 2.4f, new Color32(240, 200, 160, 255));
            c.Rect(9, 11, 1, 1, new Color32(30, 20, 20, 255));
            c.Rect(12, 5, 4, 2, new Color32(220, 226, 236, 255));
            c.Rect(11, 4, 1, 4, new Color32(200, 170, 80, 255));
            c.Outline(_outline);
        }

        // 倒れた自機：横に倒れた外套。
        private static void HeroDown(Canvas c)
        {
            c.Ellipse(8, 5, 6.5f, 3.2f, new Color32(110, 44, 36, 255));
            c.Ellipse(13, 6, 2.2f, 2.2f, new Color32(200, 170, 140, 255));
            c.Outline(_outline);
        }

        // 灰鬼：灰色の小鬼と角。直径 12 ピクセル（半径 96 単位）。
        private static void Grunt(Canvas c)
        {
            c.Ellipse(7, 6.5f, 6, 5.5f, new Color32(120, 118, 112, 255));
            c.Ellipse(7, 7, 4, 3.5f, new Color32(160, 156, 148, 255));
            c.Rect(3, 11, 2, 3, new Color32(220, 210, 190, 255));
            c.Rect(9, 11, 2, 3, new Color32(220, 210, 190, 255));
            c.Rect(8, 7, 2, 2, new Color32(255, 80, 40, 255));
            c.Rect(4, 7, 2, 2, new Color32(255, 80, 40, 255));
            c.Outline(_outline);
        }

        // 骨弓：骨の弓兵。直径 10 ピクセル（半径 80 単位）。
        private static void Archer(Canvas c)
        {
            c.Ellipse(6, 6, 5, 5, new Color32(210, 204, 186, 255));
            c.Ellipse(6, 7, 2.5f, 2, new Color32(240, 236, 220, 255));
            c.Rect(4, 7, 1, 1, new Color32(40, 60, 120, 255));
            c.Rect(7, 7, 1, 1, new Color32(40, 60, 120, 255));
            c.Rect(10, 2, 1, 8, new Color32(120, 80, 40, 255));
            c.Outline(_outline);
        }

        // 角獣：角を前に突き出した獣。直径 14 ピクセル（半径 112 単位）。
        private static void Charger(Canvas c)
        {
            c.Ellipse(7.5f, 8, 7, 6, new Color32(110, 70, 46, 255));
            c.Ellipse(7, 8, 4.5f, 3.5f, new Color32(150, 100, 64, 255));
            c.Rect(13, 10, 3, 2, new Color32(236, 226, 200, 255));
            c.Rect(13, 5, 3, 2, new Color32(236, 226, 200, 255));
            c.Rect(11, 8, 2, 2, new Color32(255, 220, 60, 255));
            c.Outline(_outline);
        }

        // 灰の番人：燃える冠を戴いた巨躯。直径 24 ピクセル（半径 192 単位）。
        private static void Boss(Canvas c)
        {
            c.Ellipse(14, 13, 12, 11.5f, new Color32(60, 44, 40, 255));
            c.Ellipse(14, 13, 9, 8.5f, new Color32(96, 66, 56, 255));
            c.Ellipse(14, 15, 5, 3, new Color32(30, 18, 16, 255));
            c.Rect(10, 15, 2, 2, new Color32(255, 140, 40, 255));
            c.Rect(16, 15, 2, 2, new Color32(255, 140, 40, 255));
            for (int i = 0; i < 5; i++) { c.Rect(7 + (i * 3), 23, 2, 3 + (i % 2), new Color32(255, 180, 60, 255)); }
            c.Outline(_outline);
        }

        // 骨弓の矢（冷気）：右向き。
        private static void Arrow(Canvas c)
        {
            c.Rect(0, 1, 7, 1, new Color32(200, 230, 255, 255));
            c.Rect(7, 0, 2, 3, new Color32(140, 210, 255, 255));
            c.Rect(0, 0, 1, 3, new Color32(120, 160, 220, 255));
        }

        // 番人の火の弾。
        private static void Bolt(Canvas c)
        {
            c.Ellipse(4, 4, 4, 4, new Color32(255, 110, 30, 255));
            c.Ellipse(4.5f, 4, 2.2f, 2.2f, new Color32(255, 230, 140, 255));
        }

        // 回復薬：赤い小瓶。
        private static void Potion(Canvas c)
        {
            c.Ellipse(4, 3.5f, 3.5f, 3.5f, new Color32(220, 40, 50, 255));
            c.Rect(3, 6, 2, 3, new Color32(200, 190, 180, 255));
            c.Rect(3, 9, 2, 1, new Color32(120, 80, 40, 255));
            c.Set(3, 4, new Color32(255, 170, 170, 255));
            c.Outline(_outline);
        }

        private static void Crosshair(Canvas c)
        {
            c.Ring(5.5f, 5.5f, 5.5f, 4.5f, new Color32(255, 240, 220, 220));
            c.Rect(5, 5, 1, 1, new Color32(255, 255, 255, 255));
        }

        // 基本攻撃の弧：右へ開く三日月（1 単位の円に描き、当たりの円の直径に伸ばす）。
        private static void Swing(Canvas c)
        {
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    float dx = x + .5f - 32;
                    float dy = y + .5f - 32;
                    float r = Mathf.Sqrt((dx * dx) + (dy * dy));
                    float inner = Mathf.Sqrt(((dx + 14) * (dx + 14)) + (dy * dy));
                    if (r <= 31 && inner > 30) { c.Set(x, y, new Color32(255, 250, 230, (byte)Mathf.Clamp(80 + (dx * 5), 0, 230))); }
                }
            }
        }

        // 薙ぎ払いの輪：渦を巻く輪。
        private static void Spin(Canvas c)
        {
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    float dx = x + .5f - 32;
                    float dy = y + .5f - 32;
                    float r = Mathf.Sqrt((dx * dx) + (dy * dy));
                    float a = Mathf.Atan2(dy, dx);
                    if (r <= 31 && r >= 26) { c.Set(x, y, new Color32(200, 230, 255, (byte)(120 + (100 * Mathf.Abs(Mathf.Sin(a * 2)))))); }
                }
            }
        }

        // 突進の筋：1 タイルの半透明の帯（長さと幅を伸ばす）。
        private static void Lane(Canvas c)
        {
            c.Rect(0, 0, 16, 16, new Color32(255, 170, 70, 60));
            c.Rect(0, 6, 16, 4, new Color32(255, 210, 120, 90));
        }

        // 枠の印（白で描き、希少度の色で塗る）。
        private static void IconWeapon(Canvas c)
        {
            for (int i = 0; i < 7; i++) { c.Set(2 + i, 2 + i, _icon); c.Set(3 + i, 2 + i, _icon); }
            c.Rect(1, 4, 4, 1, _iconShade);
            c.Rect(3, 1, 1, 4, _iconShade);
            c.Outline(_outline);
        }

        private static void IconArmor(Canvas c)
        {
            c.Rect(2, 1, 6, 7, _icon);
            c.Rect(0, 6, 2, 3, _icon);
            c.Rect(8, 6, 2, 3, _icon);
            c.Rect(4, 2, 2, 5, _iconShade);
            c.Outline(_outline);
        }

        private static void IconHelm(Canvas c)
        {
            c.Ellipse(5, 4, 4, 4, _icon);
            c.Rect(1, 1, 8, 3, _icon);
            c.Rect(3, 4, 4, 1, _iconShade);
            c.Outline(_outline);
        }

        private static void IconRing(Canvas c)
        {
            c.Ring(5, 4, 3.5f, 2.2f, _icon);
            c.Rect(4, 7, 2, 2, _iconShade);
            c.Outline(_outline);
        }

        private static void IconAmulet(Canvas c)
        {
            c.Ring(5, 6, 4, 3.2f, _iconShade);
            c.Ellipse(5, 2.5f, 2.2f, 2.2f, _icon);
            c.Outline(_outline);
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
                        for (int dy = -1; dy <= 1 && !edge; dy++)
                        {
                            for (int dx = -1; dx <= 1 && !edge; dx++)
                            {
                                int nx = x + dx;
                                int ny = y + dy;
                                if (nx < 0 || ny < 0 || nx >= Width || ny >= Height) { continue; }
                                edge = copy[(ny * Width) + nx].a > 200 && (dx == 0 || dy == 0);
                            }
                        }

                        if (edge) { Pixels[(y * Width) + x] = color; }
                    }
                }
            }
        }
    }
}
