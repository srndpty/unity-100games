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
    /// 1 タイル = 32 ピクセル（Core の 1 px = 2 ピクセル）。見下ろしなので、どの絵も中央を基準点にし、体の箱の中央に置く。
    /// 乱数は使わず、毎回同じ絵にする。
    /// </summary>
    public static class EmberVaultArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";
        public const int PixelsPerUnit = 32;

        public const string FloorFile = "Floor.png";
        public const string ThresholdFile = "Threshold.png";
        public const string WallFile = "Wall.png";
        public const string WallTopFile = "WallTop.png";
        public const string SmallDoorFile = "SmallDoor.png";
        public const string BossDoorFile = "BossDoor.png";
        public const string ChestFile = "Chest.png";
        public const string BossChestFile = "ChestBoss.png";
        public const string ChestOpenFile = "ChestOpen.png";
        public const string HeroDown0File = "HeroDown0.png";
        public const string HeroDown1File = "HeroDown1.png";
        public const string HeroUp0File = "HeroUp0.png";
        public const string HeroUp1File = "HeroUp1.png";
        public const string HeroSide0File = "HeroSide0.png";
        public const string HeroSide1File = "HeroSide1.png";
        public const string HeroRollFile = "HeroRoll.png";
        public const string HeroHurtFile = "HeroHurt.png";
        public const string SlashFile = "Slash.png";
        public const string Slime0File = "Slime0.png";
        public const string Slime1File = "Slime1.png";
        public const string SpitterFile = "Spitter.png";
        public const string ChargerFile = "Charger.png";
        public const string ChargerChargeFile = "ChargerCharge.png";
        public const string BossFile = "Boss.png";
        public const string BossChargeFile = "BossCharge.png";
        public const string BossStunFile = "BossStun.png";
        public const string ShotFile = "Shot.png";
        public const string KeyFile = "Key.png";
        public const string TreasureFile = "Treasure.png";
        public const string BackdropFile = "Backdrop.png";

        /// <summary>背景は縦 16 単位で、21:9 まで覆える幅に描く（カメラの子にして画面全体を覆う）。</summary>
        public const int BackdropWidth = 640;
        public const int BackdropHeight = 256;
        public const float BackdropPixelsPerUnit = BackdropHeight / 16f;

        private static readonly Color32 _outline = new(20, 12, 10, 255);

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtRoot);
            var created = new List<(string Path, float PixelsPerUnit)>();
            Save(created, FloorFile, 32, 32, c => Floor(c, false));
            Save(created, ThresholdFile, 32, 32, c => Floor(c, true));
            Save(created, WallFile, 32, 32, c => Wall(c, false));
            Save(created, WallTopFile, 32, 32, c => Wall(c, true));
            Save(created, SmallDoorFile, 32, 32, SmallDoor);
            Save(created, BossDoorFile, 32, 32, BossDoor);
            Save(created, ChestFile, 32, 32, c => Chest(c, false, false));
            Save(created, BossChestFile, 32, 32, c => Chest(c, true, false));
            Save(created, ChestOpenFile, 32, 32, c => Chest(c, false, true));
            Save(created, HeroDown0File, 32, 32, c => Hero(c, View.Down, 0));
            Save(created, HeroDown1File, 32, 32, c => Hero(c, View.Down, 1));
            Save(created, HeroUp0File, 32, 32, c => Hero(c, View.Up, 0));
            Save(created, HeroUp1File, 32, 32, c => Hero(c, View.Up, 1));
            Save(created, HeroSide0File, 32, 32, c => Hero(c, View.Side, 0));
            Save(created, HeroSide1File, 32, 32, c => Hero(c, View.Side, 1));
            Save(created, HeroRollFile, 32, 32, Roll);
            Save(created, HeroHurtFile, 32, 32, c => Hero(c, View.Hurt, 0));
            Save(created, SlashFile, 28, 40, Slash);
            Save(created, Slime0File, 32, 32, c => Slime(c, 0));
            Save(created, Slime1File, 32, 32, c => Slime(c, 1));
            Save(created, SpitterFile, 32, 32, Spitter);
            Save(created, ChargerFile, 32, 32, c => Charger(c, false));
            Save(created, ChargerChargeFile, 32, 32, c => Charger(c, true));
            Save(created, BossFile, 64, 64, c => Boss(c, BossPose.Idle));
            Save(created, BossChargeFile, 64, 64, c => Boss(c, BossPose.Charge));
            Save(created, BossStunFile, 64, 64, c => Boss(c, BossPose.Stun));
            Save(created, ShotFile, 16, 16, Shot);
            Save(created, KeyFile, 32, 32, Key);
            Save(created, TreasureFile, 32, 32, Treasure);
            SaveBackdrop(created);
            AssetDatabase.Refresh();
            foreach (var (path, pixelsPerUnit) in created) { Import(path, pixelsPerUnit); }
        }

        private enum View
        {
            Down,
            Up,
            Side,
            Hurt,
        }

        private enum BossPose
        {
            Idle,
            Charge,
            Stun,
        }

        private static void Save(List<(string, float)> created, string file, int width, int height, Action<Canvas> paint)
        {
            var path = ArtRoot + file;
            if (File.Exists(path)) { return; }
            var canvas = new Canvas(width, height);
            paint(canvas);
            Write(path, canvas);
            created.Add((path, PixelsPerUnit));
        }

        private static void Write(string path, Canvas canvas)
        {
            var texture = new Texture2D(canvas.Width, canvas.Height, TextureFormat.RGBA32, false);
            texture.SetPixels32(canvas.Pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        private static void Import(string path, float pixelsPerUnit)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
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

        // 地下の宝物殿の外の闇。赤茶から黒へのグラデーションと、遠くの燠火の粒。
        private static void SaveBackdrop(List<(string, float)> created)
        {
            var path = ArtRoot + BackdropFile;
            if (File.Exists(path)) { return; }
            var canvas = new Canvas(BackdropWidth, BackdropHeight);
            for (var y = 0; y < BackdropHeight; y++)
            {
                var t = Mathf.Floor((float)y / BackdropHeight * 8) / 8;
                var color = Color.Lerp(new Color(.12f, .06f, .05f), new Color(.04f, .03f, .05f), t);
                for (var x = 0; x < BackdropWidth; x++) { canvas.Set(x, y, color); }
            }

            for (var i = 0; i < 140; i++)
            {
                var x = (i * 389) % BackdropWidth;
                var y = (i * 151) % BackdropHeight;
                var bright = i % 6 == 0;
                canvas.Set(x, y, bright ? new Color(1f, .62f, .3f) : new Color(.45f, .2f, .12f));
            }

            Write(path, canvas);
            created.Add((path, BackdropPixelsPerUnit));
        }

        // 床：くすんだ砂岩の敷石。threshold（出入口）は明るい石と光の筋。
        private static void Floor(Canvas c, bool threshold)
        {
            var stone = threshold ? new Color32(120, 98, 72, 255) : new Color32(78, 62, 54, 255);
            var seam = threshold ? new Color32(90, 70, 52, 255) : new Color32(58, 46, 40, 255);
            var fleck = threshold ? new Color32(150, 126, 96, 255) : new Color32(92, 76, 66, 255);
            c.Rect(0, 0, 32, 32, stone);
            c.Rect(0, 0, 32, 1, seam);
            c.Rect(0, 16, 32, 1, seam);
            c.Rect(0, 0, 1, 16, seam);
            c.Rect(16, 16, 1, 16, seam);
            foreach (var (x, y) in new[] { (5, 6), (22, 9), (9, 24), (26, 27), (12, 12), (28, 3) }) { c.Set(x, y, fleck); }
            if (threshold)
            {
                for (var y = 2; y < 30; y += 6) { c.Rect(14, y, 4, 2, new Color32(255, 200, 120, 255)); }
            }
        }

        // 壁：赤茶のれんが。top（下が床の壁）は手前の面に影を付ける。
        private static void Wall(Canvas c, bool face)
        {
            var brick = new Color32(120, 62, 46, 255);
            var mortar = new Color32(60, 34, 28, 255);
            var light = new Color32(150, 84, 60, 255);
            c.Rect(0, 0, 32, 32, mortar);
            for (var row = 0; row < 4; row++)
            {
                var offset = row % 2 == 0 ? 0 : 8;
                for (var x = -offset; x < 32; x += 16)
                {
                    c.Rect(x + 1, (row * 8) + 1, 14, 6, brick);
                    c.Rect(x + 1, (row * 8) + 6, 14, 1, light);
                }
            }

            if (face) { c.Rect(0, 0, 32, 5, new Color32(40, 22, 18, 255)); }
        }

        // 小扉：木の板と鉄の帯、中央に鍵穴。
        private static void SmallDoor(Canvas c)
        {
            var wood = new Color32(130, 86, 44, 255);
            var dark = new Color32(84, 52, 26, 255);
            var iron = new Color32(70, 72, 80, 255);
            c.Rect(0, 0, 32, 32, wood);
            for (var x = 0; x < 32; x += 8) { c.Rect(x, 0, 1, 32, dark); }
            c.Rect(0, 6, 32, 3, iron);
            c.Rect(0, 23, 32, 3, iron);
            c.Ellipse(16, 16, 4, 4, new Color32(220, 180, 70, 255));
            c.Rect(15, 12, 2, 5, new Color32(20, 12, 8, 255));
            c.Set(15, 17, new Color32(20, 12, 8, 255));
            c.Set(16, 17, new Color32(20, 12, 8, 255));
        }

        // ボス扉：黒鉄の扉に赤い紋章。
        private static void BossDoor(Canvas c)
        {
            c.Rect(0, 0, 32, 32, new Color32(46, 44, 54, 255));
            c.Rect(2, 2, 28, 28, new Color32(66, 62, 76, 255));
            for (var i = 0; i < 4; i++) { c.Rect(4 + (i * 8), 4, 2, 2, new Color32(120, 116, 130, 255)); c.Rect(4 + (i * 8), 26, 2, 2, new Color32(120, 116, 130, 255)); }
            c.Ellipse(16, 16, 7, 7, new Color32(170, 30, 40, 255));
            c.Ellipse(16, 16, 3.5f, 3.5f, new Color32(255, 120, 80, 255));
        }

        // 宝箱：木と金具。boss（ボス鍵）は金の縁。open はふたが開いて中が光る。
        private static void Chest(Canvas c, bool boss, bool open)
        {
            var wood = new Color32(150, 92, 46, 255);
            var dark = new Color32(96, 56, 28, 255);
            var trim = boss ? new Color32(255, 210, 80, 255) : new Color32(170, 170, 180, 255);
            c.Rect(3, 3, 26, 16, wood);
            c.Rect(3, 3, 26, 2, dark);
            c.Rect(3, 10, 26, 2, trim);
            if (open)
            {
                c.Rect(3, 19, 26, 3, new Color32(30, 18, 10, 255));
                c.Rect(6, 18, 20, 2, new Color32(255, 230, 150, 255));
                c.Rect(3, 22, 26, 7, dark);
                c.Rect(3, 27, 26, 2, trim);
            }
            else
            {
                c.Rect(3, 19, 26, 8, wood);
                c.Rect(3, 25, 26, 2, trim);
                c.Rect(14, 12, 4, 6, trim);
                c.Rect(15, 13, 2, 3, new Color32(30, 18, 10, 255));
            }

            c.Outline(_outline);
        }

        // 自機：緑の外套と金髪の旅人。体は 24×24 ピクセル（Core の 12×12 px）を中央に描く。
        private static void Hero(Canvas c, View view, int frame)
        {
            var cloak = view == View.Hurt ? new Color32(220, 90, 90, 255) : new Color32(60, 150, 90, 255);
            var shade = new Color32(36, 100, 60, 255);
            var hair = new Color32(240, 200, 90, 255);
            var face = new Color32(250, 214, 180, 255);
            var boots = new Color32(90, 58, 40, 255);
            var step = frame == 0 ? 0 : 2;

            // 足（歩くと左右が入れ替わる）
            c.Rect(10, 4 + step, 4, 4, boots);
            c.Rect(18, 6 - step, 4, 4, boots);
            c.Ellipse(16, 13, 8, 6, cloak);
            c.Rect(8, 11, 16, 5, cloak);
            c.Rect(8, 9, 3, 7, shade);
            switch (view)
            {
                case View.Up:
                    c.Ellipse(16, 21, 7, 6, hair);
                    c.Rect(11, 15, 10, 2, shade);
                    break;
                case View.Side:
                    c.Ellipse(16, 21, 6.5f, 6, face);
                    c.Ellipse(14, 23, 6, 4.5f, hair);
                    c.Rect(19, 20, 2, 2, _outline);
                    c.Rect(21, 11, 4, 3, new Color32(200, 200, 210, 255));
                    break;
                default:
                    c.Ellipse(16, 21, 7, 6, face);
                    c.Ellipse(16, 25, 7, 3, hair);
                    c.Rect(13, 20, 2, 2, view == View.Hurt ? new Color32(160, 20, 20, 255) : _outline);
                    c.Rect(18, 20, 2, 2, view == View.Hurt ? new Color32(160, 20, 20, 255) : _outline);
                    break;
            }

            c.Outline(_outline);
        }

        // ロール：丸まった外套の玉と、後ろへ流れる線。
        private static void Roll(Canvas c)
        {
            c.Ellipse(17, 15, 9, 9, new Color32(60, 150, 90, 255));
            c.Ellipse(17, 15, 5, 5, new Color32(36, 100, 60, 255));
            c.Ellipse(20, 19, 3, 3, new Color32(240, 200, 90, 255));
            c.Outline(_outline);
            for (var i = 0; i < 3; i++) { c.Rect(1, 9 + (i * 6), 5 - i, 1, new Color32(200, 230, 200, 160)); }
        }

        // 剣の振り：右向きの弧（剣の当たりの箱 14×20 px を 28×40 ピクセルに描く）。
        private static void Slash(Canvas c)
        {
            for (var y = 0; y < 40; y++)
            {
                for (var x = 0; x < 28; x++)
                {
                    var dx = (x + .5f) / 28f;
                    var dy = (y + .5f - 20f) / 20f;
                    var r = Mathf.Sqrt((dx * dx) + (dy * dy * .55f));
                    if (r > .62f && r < 1f)
                    {
                        var edge = r > .9f;
                        c.Set(x, y, edge ? new Color32(255, 255, 255, 230) : new Color32(190, 230, 255, (byte)(150 * (r - .62f) / .38f + 60)));
                    }
                }
            }
        }

        // スライム：半透明の緑のしずく。frame で伸び縮み。
        private static void Slime(Canvas c, int frame)
        {
            var body = new Color32(110, 200, 90, 255);
            var light = new Color32(190, 250, 160, 255);
            var squash = frame == 0 ? 0 : 1.5f;
            c.Ellipse(16, 12, 11 + squash, 9 - squash, body);
            c.Ellipse(12, 15, 3, 2.5f, light);
            c.Rect(11, 11, 3, 3, _outline);
            c.Rect(19, 11, 3, 3, _outline);
            c.Outline(_outline);
        }

        // 射手：石の筒を背負った紫のずきん。
        private static void Spitter(Canvas c)
        {
            c.Ellipse(16, 13, 10, 9, new Color32(120, 70, 150, 255));
            c.Ellipse(16, 18, 7, 6, new Color32(150, 100, 180, 255));
            c.Rect(13, 16, 6, 3, new Color32(30, 16, 40, 255));
            c.Rect(14, 17, 1, 1, new Color32(255, 210, 90, 255));
            c.Rect(17, 17, 1, 1, new Color32(255, 210, 90, 255));
            c.Ellipse(16, 6, 4, 3, new Color32(80, 80, 90, 255));
            c.Outline(_outline);
        }

        // 突進兵：角のある甲冑の獣。charge は頭を下げ、角が光る。体は 28 ピクセル。
        private static void Charger(Canvas c, bool charge)
        {
            var armor = new Color32(150, 110, 70, 255);
            var dark = new Color32(100, 70, 44, 255);
            var horn = charge ? new Color32(255, 230, 160, 255) : new Color32(220, 210, 190, 255);
            c.Ellipse(16, 14, 13, 11, armor);
            c.Rect(4, 13, 24, 2, dark);
            var head = charge ? 22 : 24;
            c.Rect(7, head, 3, 6, horn);
            c.Rect(22, head, 3, 6, horn);
            c.Rect(12, 17, 3, 3, new Color32(255, 80, 60, 255));
            c.Rect(18, 17, 3, 3, new Color32(255, 80, 60, 255));
            c.Outline(_outline);
        }

        // ボス：炎の目をもつ石の番人。体は 56×56 ピクセル（Core の 28×28 px）。
        private static void Boss(Canvas c, BossPose pose)
        {
            var stone = pose == BossPose.Charge ? new Color32(150, 80, 60, 255) : new Color32(110, 96, 90, 255);
            var dark = new Color32(70, 60, 58, 255);
            var eye = pose == BossPose.Stun ? new Color32(150, 150, 150, 255) : new Color32(255, 140, 40, 255);
            c.Ellipse(32, 30, 26, 24, stone);
            c.Ellipse(32, 22, 18, 10, dark);
            c.Rect(10, 30, 44, 3, dark);
            c.Ellipse(23, 40, 5, 4, eye);
            c.Ellipse(41, 40, 5, 4, eye);
            c.Rect(8, 48, 6, 10, new Color32(200, 190, 170, 255));
            c.Rect(50, 48, 6, 10, new Color32(200, 190, 170, 255));
            if (pose == BossPose.Stun)
            {
                foreach (var (x, y) in new[] { (20, 58), (32, 61), (44, 58) })
                {
                    c.Rect(x - 1, y, 3, 1, new Color32(255, 240, 140, 255));
                    c.Rect(x, y - 1, 1, 3, new Color32(255, 240, 140, 255));
                }
            }

            c.Outline(_outline);
        }

        // 敵の弾：12 ピクセル（Core の 6 px）の燠火の玉。
        private static void Shot(Canvas c)
        {
            c.Ellipse(8, 8, 6, 6, new Color32(240, 90, 40, 255));
            c.Ellipse(7, 9, 3, 3, new Color32(255, 220, 140, 255));
            c.Outline(_outline);
        }

        // 小鍵：金の鍵。
        private static void Key(Canvas c)
        {
            var gold = new Color32(255, 210, 70, 255);
            c.Ellipse(11, 20, 6, 6, gold);
            c.Ellipse(11, 20, 2.5f, 2.5f, new Color32(0, 0, 0, 0));
            c.Rect(16, 19, 12, 3, gold);
            c.Rect(24, 14, 2, 5, gold);
            c.Rect(20, 15, 2, 4, gold);
            c.Outline(_outline);
        }

        // 秘宝：赤く光る宝珠と台座。
        private static void Treasure(Canvas c)
        {
            c.Rect(8, 3, 16, 5, new Color32(200, 170, 90, 255));
            c.Ellipse(16, 18, 9, 9, new Color32(230, 50, 70, 255));
            c.Ellipse(13, 21, 3, 3, new Color32(255, 200, 210, 255));
            c.Outline(_outline);
        }

        /// <summary>透明で始まる小さな画素の板。描いた後に輪郭線を付けられる。</summary>
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

            public void Set(int x, int y, Color color) => Set(x, y, (Color32)color);

            public void Rect(int x, int y, int width, int height, Color32 color)
            {
                for (var py = y; py < y + height; py++)
                {
                    for (var px = x; px < x + width; px++) { Set(px, py, color); }
                }
            }

            public void Ellipse(float cx, float cy, float rx, float ry, Color32 color)
            {
                for (var py = (int)(cy - ry - 1); py <= (int)(cy + ry + 1); py++)
                {
                    for (var px = (int)(cx - rx - 1); px <= (int)(cx + rx + 1); px++)
                    {
                        var dx = (px + .5f - cx) / rx;
                        var dy = (py + .5f - cy) / ry;
                        if ((dx * dx) + (dy * dy) <= 1) { Set(px, py, color); }
                    }
                }
            }

            /// <summary>不透明な画素に隣り合う透明な画素を輪郭の色で塗る（上下左右）。</summary>
            public void Outline(Color32 color)
            {
                var source = (Color32[])Pixels.Clone();
                for (var y = 0; y < Height; y++)
                {
                    for (var x = 0; x < Width; x++)
                    {
                        if (source[(y * Width) + x].a != 0) { continue; }
                        if (Opaque(source, x - 1, y) || Opaque(source, x + 1, y) || Opaque(source, x, y - 1) || Opaque(source, x, y + 1))
                        {
                            Pixels[(y * Width) + x] = color;
                        }
                    }
                }
            }

            private bool Opaque(Color32[] source, int x, int y) =>
                x >= 0 && y >= 0 && x < Width && y < Height && source[(y * Width) + x].a != 0;
        }
    }
}
