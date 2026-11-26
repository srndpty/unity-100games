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
    /// 1 マス = 16 ピクセル = 1 ワールド単位。どの絵も中央を基準点にする。向きの印は北（+y）向きに描く。乱数は使わない。
    /// </summary>
    public static class LanternArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";
        public const int PixelsPerUnit = 16;

        public const string RoomFile = "Room.png";
        public const string CorridorFile = "Corridor.png";
        public const string WallFile = "Wall.png";
        public const string StairsFile = "Stairs.png";
        public const string PlayerFile = "Player.png";
        public const string FacingFile = "Facing.png";
        public const string SleepFile = "Sleep.png";
        public const string HitFile = "Hit.png";

        /// <summary>敵の絵（<see cref="Game.Core.MonsterKind"/> の順）。</summary>
        public static readonly string[] MonsterFiles = { "Skitter.png", "Golem.png", "Archer.png", "Thief.png", "Serpent.png" };

        /// <summary>品の絵（<see cref="Game.Core.ItemKind"/> の順）。</summary>
        public static readonly string[] ItemFiles =
        {
            "HealPotion.png", "PoisonPotion.png", "SleepPotion.png", "Bread.png", "BigBread.png", "Stone.png", "MapScroll.png", "WarpScroll.png",
        };

        private static readonly Color32 _outline = new(10, 12, 10, 255);

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtRoot);
            var created = new List<string>();
            Save(created, RoomFile, 16, 16, Room);
            Save(created, CorridorFile, 16, 16, Corridor);
            Save(created, WallFile, 16, 16, Wall);
            Save(created, StairsFile, 16, 16, Stairs);
            Save(created, PlayerFile, 16, 16, Player);
            Save(created, FacingFile, 16, 16, Facing);
            Save(created, SleepFile, 7, 7, Sleep);
            Save(created, HitFile, 16, 16, Hit);
            Save(created, MonsterFiles[0], 16, 16, Skitter);
            Save(created, MonsterFiles[1], 16, 16, Golem);
            Save(created, MonsterFiles[2], 16, 16, Archer);
            Save(created, MonsterFiles[3], 16, 16, Thief);
            Save(created, MonsterFiles[4], 16, 16, Serpent);
            Save(created, ItemFiles[0], 16, 16, c => Flask(c, new Color32(220, 60, 70, 255)));
            Save(created, ItemFiles[1], 16, 16, c => Flask(c, new Color32(110, 190, 60, 255)));
            Save(created, ItemFiles[2], 16, 16, c => Flask(c, new Color32(90, 120, 230, 255)));
            Save(created, ItemFiles[3], 16, 16, c => Bread(c, 5));
            Save(created, ItemFiles[4], 16, 16, c => Bread(c, 7));
            Save(created, ItemFiles[5], 16, 16, Stone);
            Save(created, ItemFiles[6], 16, 16, c => Scroll(c, new Color32(90, 70, 40, 255)));
            Save(created, ItemFiles[7], 16, 16, c => Scroll(c, new Color32(150, 70, 200, 255)));
            AssetDatabase.Refresh();
            foreach (var path in created) { Import(path); }
        }

        private static void Save(List<string> created, string file, int width, int height, Action<Canvas> paint)
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
        }

        private static void Import(string path)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        // 部屋の床：湿った石畳。
        private static void Room(Canvas c)
        {
            c.Rect(0, 0, 16, 16, new Color32(58, 66, 62, 255));
            var seam = new Color32(42, 48, 45, 255);
            c.Rect(0, 0, 16, 1, seam);
            c.Rect(0, 8, 16, 1, seam);
            c.Rect(0, 0, 1, 8, seam);
            c.Rect(8, 8, 1, 8, seam);
            foreach (var (x, y) in new[] { (4, 4), (12, 3), (4, 12), (13, 12) }) { c.Set(x, y, new Color32(80, 90, 84, 255)); }
        }

        // 通路：踏み固めた泥。
        private static void Corridor(Canvas c)
        {
            c.Rect(0, 0, 16, 16, new Color32(52, 46, 36, 255));
            foreach (var (x, y) in new[] { (2, 3), (9, 1), (13, 6), (5, 9), (11, 12), (3, 14), (14, 14) }) { c.Set(x, y, new Color32(70, 62, 48, 255)); }
        }

        // 壁：苔むした石積み。上端に苔の緑。
        private static void Wall(Canvas c)
        {
            c.Rect(0, 0, 16, 16, new Color32(40, 44, 50, 255));
            var mortar = new Color32(24, 26, 30, 255);
            for (int row = 0; row < 4; row++)
            {
                int y = row * 4;
                c.Rect(0, y, 16, 1, mortar);
                int offset = row % 2 == 0 ? 0 : 4;
                for (int x = offset; x < 16; x += 8) { c.Rect(x, y, 1, 4, mortar); }
            }

            c.Rect(0, 14, 16, 2, new Color32(60, 96, 58, 255));
            c.Rect(3, 13, 3, 1, new Color32(60, 96, 58, 255));
            c.Rect(11, 13, 2, 1, new Color32(60, 96, 58, 255));
        }

        // 階段：下へ降りる段と、灯りの縁。
        private static void Stairs(Canvas c)
        {
            c.Rect(0, 0, 16, 16, new Color32(24, 22, 20, 255));
            for (int i = 0; i < 5; i++)
            {
                byte shade = (byte)(200 - (i * 32));
                c.Rect(1 + i, 13 - (i * 3), 14 - (2 * i), 2, new Color32(shade, (byte)(shade * .85f), (byte)(shade * .45f), 255));
            }

            c.Rect(0, 15, 16, 1, new Color32(255, 210, 110, 255));
            c.Rect(0, 0, 1, 16, new Color32(255, 210, 110, 255));
            c.Rect(15, 0, 1, 16, new Color32(255, 210, 110, 255));
        }

        // 自機：フードの旅人と、手に提げた灯り。
        private static void Player(Canvas c)
        {
            c.Ellipse(8, 6, 4, 4.5f, new Color32(60, 90, 150, 255));
            c.Ellipse(8, 11, 3.2f, 3.2f, new Color32(70, 105, 170, 255));
            c.Ellipse(8, 10.5f, 2, 2, new Color32(236, 200, 160, 255));
            c.Rect(7, 10, 1, 1, _outline);
            c.Rect(9, 10, 1, 1, _outline);
            c.Rect(12, 3, 3, 4, new Color32(255, 220, 100, 255));
            c.Rect(13, 4, 1, 2, new Color32(255, 255, 220, 255));
            c.Rect(13, 7, 1, 2, new Color32(120, 100, 60, 255));
            c.Outline(_outline);
        }

        // 向きの印：北向きの小さな三角（描画で回す）。
        private static void Facing(Canvas c)
        {
            for (int row = 0; row < 3; row++) { c.Rect(7 - row, 15 - row, 2 + (2 * row), 1, new Color32(255, 230, 140, 255)); }
        }

        // 眠りの印。
        private static void Sleep(Canvas c)
        {
            var z = new Color32(200, 220, 255, 255);
            c.Rect(0, 6, 7, 1, z);
            for (int i = 0; i < 5; i++) { c.Set(5 - i, 5 - i, z); }
            c.Rect(0, 0, 7, 1, z);
        }

        // 攻撃が当たった印：白い斜めの線。
        private static void Hit(Canvas c)
        {
            var white = new Color32(255, 255, 255, 220);
            for (int i = 2; i < 14; i++)
            {
                c.Set(i, i, white);
                c.Set(i + 1, i, white);
                c.Set(15 - i, i, white);
            }
        }

        // ちょろネズミ：茶色の小さな獣。
        private static void Skitter(Canvas c)
        {
            c.Ellipse(8, 6, 5, 3.5f, new Color32(140, 100, 70, 255));
            c.Ellipse(12, 8, 2.5f, 2.5f, new Color32(160, 118, 84, 255));
            c.Rect(13, 9, 1, 1, _outline);
            c.Rect(1, 5, 3, 1, new Color32(200, 150, 140, 255));
            c.Rect(5, 2, 1, 2, new Color32(90, 60, 40, 255));
            c.Rect(10, 2, 1, 2, new Color32(90, 60, 40, 255));
            c.Outline(_outline);
        }

        // 岩人形：灰色の四角い体。
        private static void Golem(Canvas c)
        {
            c.Rect(3, 2, 10, 9, new Color32(120, 118, 110, 255));
            c.Rect(5, 11, 6, 4, new Color32(140, 138, 128, 255));
            c.Rect(6, 12, 1, 1, new Color32(255, 160, 60, 255));
            c.Rect(9, 12, 1, 1, new Color32(255, 160, 60, 255));
            c.Rect(1, 5, 2, 5, new Color32(100, 98, 92, 255));
            c.Rect(13, 5, 2, 5, new Color32(100, 98, 92, 255));
            c.Rect(5, 5, 4, 1, new Color32(90, 88, 82, 255));
            c.Outline(_outline);
        }

        // 葦の射手：緑の外套と弓。
        private static void Archer(Canvas c)
        {
            c.Ellipse(7, 6, 3.5f, 4.5f, new Color32(70, 130, 70, 255));
            c.Ellipse(7, 12, 2.5f, 2.5f, new Color32(210, 180, 140, 255));
            c.Rect(6, 12, 1, 1, _outline);
            for (int y = 3; y <= 13; y++) { c.Set(12 + (Math.Abs(y - 8) < 3 ? 1 : 0), y, new Color32(150, 100, 50, 255)); }
            c.Rect(11, 8, 1, 1, new Color32(230, 230, 200, 255));
            c.Outline(_outline);
        }

        // くすね鳥：黒い鳥と光り物。
        private static void Thief(Canvas c)
        {
            c.Ellipse(8, 7, 5, 4, new Color32(40, 40, 56, 255));
            c.Ellipse(11, 11, 2.8f, 2.6f, new Color32(50, 50, 70, 255));
            c.Rect(14, 11, 2, 1, new Color32(240, 190, 60, 255));
            c.Rect(11, 12, 1, 1, new Color32(240, 240, 255, 255));
            c.Rect(2, 8, 4, 2, new Color32(30, 30, 42, 255));
            c.Rect(5, 2, 1, 2, new Color32(240, 190, 60, 255));
            c.Rect(9, 2, 1, 2, new Color32(240, 190, 60, 255));
            c.Outline(new Color32(150, 150, 180, 255));
        }

        // ねむり大蛇：とぐろを巻いた紫の蛇。
        private static void Serpent(Canvas c)
        {
            c.Ring(8, 6, 6, 3, new Color32(130, 70, 150, 255));
            c.Ring(8, 6, 3, 1, new Color32(150, 90, 170, 255));
            c.Ellipse(11, 12, 3, 2.5f, new Color32(150, 90, 170, 255));
            c.Rect(12, 13, 1, 1, new Color32(255, 230, 90, 255));
            c.Rect(14, 11, 2, 1, new Color32(230, 70, 70, 255));
            c.Outline(_outline);
        }

        // 薬の瓶。
        private static void Flask(Canvas c, Color32 liquid)
        {
            c.Ellipse(8, 6, 4.5f, 4.5f, new Color32(210, 230, 235, 255));
            c.Ellipse(8, 5.5f, 3.5f, 3.5f, liquid);
            c.Rect(7, 10, 3, 3, new Color32(210, 230, 235, 255));
            c.Rect(6, 13, 5, 2, new Color32(140, 100, 60, 255));
            c.Rect(6, 6, 1, 2, new Color32(255, 255, 255, 255));
            c.Outline(_outline);
        }

        // パン。
        private static void Bread(Canvas c, int half)
        {
            c.Ellipse(8, 7, half, 4, new Color32(200, 140, 70, 255));
            c.Ellipse(8, 8, half - 1, 2.5f, new Color32(230, 180, 100, 255));
            for (int x = 8 - half + 2; x < 8 + half - 1; x += 3) { c.Rect(x, 8, 1, 2, new Color32(170, 110, 50, 255)); }
            c.Outline(_outline);
        }

        private static void Stone(Canvas c)
        {
            c.Ellipse(8, 7, 5, 4, new Color32(130, 130, 136, 255));
            c.Ellipse(7, 8, 2.5f, 1.5f, new Color32(170, 170, 176, 255));
            c.Outline(_outline);
        }

        // 巻物：紙と、印の色。
        private static void Scroll(Canvas c, Color32 mark)
        {
            c.Rect(3, 4, 10, 8, new Color32(232, 218, 180, 255));
            c.Rect(2, 3, 2, 10, new Color32(190, 160, 110, 255));
            c.Rect(12, 3, 2, 10, new Color32(190, 160, 110, 255));
            c.Rect(5, 9, 6, 1, mark);
            c.Rect(5, 7, 5, 1, mark);
            c.Rect(5, 5, 6, 1, mark);
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
