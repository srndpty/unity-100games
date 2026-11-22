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
    /// 1 タイル = 32 ピクセル（Core の 1 px = 2 ピクセル）。自機・敵・ボス・像は下端の中央、弾・アイテム・タイルは中央を基準点にする。
    /// 乱数は使わず、毎回同じ絵にする。
    /// </summary>
    public static class MossDepthsArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";
        public const int PixelsPerUnit = 32;

        public const string MossTopFile = "MossTop.png";
        public const string RockFile = "Rock.png";
        public const string CrackedFile = "Cracked.png";
        public const string DoorFile = "Door.png";
        public const string BackdropFile = "Backdrop.png";
        public const string StandFile = "PlayerStand.png";
        public const string Run0File = "PlayerRun0.png";
        public const string Run1File = "PlayerRun1.png";
        public const string RiseFile = "PlayerRise.png";
        public const string FallFile = "PlayerFall.png";
        public const string HurtFile = "PlayerHurt.png";
        public const string Walk0File = "Walker0.png";
        public const string Walk1File = "Walker1.png";
        public const string BossIdleFile = "BossIdle.png";
        public const string BossChargeFile = "BossCharge.png";
        public const string BossStunnedFile = "BossStunned.png";
        public const string ShotFile = "Shot.png";
        public const string BreakShotFile = "BreakShot.png";
        public const string BulletFile = "Bullet.png";
        public const string FruitFile = "Fruit.png";
        public const string JumpOrbFile = "OrbJump.png";
        public const string BreakOrbFile = "OrbBreak.png";
        public const string StatueFile = "Statue.png";

        /// <summary>背景は縦 24 単位（部屋と HUD の帯より少し大きい）で、21:9 まで覆える幅に描く。</summary>
        public const int BackdropWidth = 960;
        public const int BackdropHeight = 400;
        public const float BackdropPixelsPerUnit = BackdropHeight / 24f;

        private static readonly Color32 _outline = new(12, 18, 16, 255);

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtRoot);
            var created = new List<(string Path, float PixelsPerUnit, Vector2 Pivot)>();
            var center = new Vector2(.5f, .5f);
            var feet = new Vector2(.5f, 0);
            Save(created, MossTopFile, 32, 32, center, c => Stone(c, true));
            Save(created, RockFile, 32, 32, center, c => Stone(c, false));
            Save(created, CrackedFile, 32, 32, center, Cracked);
            Save(created, DoorFile, 32, 32, center, Door);
            Save(created, StandFile, 32, 32, feet, c => Explorer(c, Pose.Stand));
            Save(created, Run0File, 32, 32, feet, c => Explorer(c, Pose.Run0));
            Save(created, Run1File, 32, 32, feet, c => Explorer(c, Pose.Run1));
            Save(created, RiseFile, 32, 32, feet, c => Explorer(c, Pose.Rise));
            Save(created, FallFile, 32, 32, feet, c => Explorer(c, Pose.Fall));
            Save(created, HurtFile, 32, 32, feet, c => Explorer(c, Pose.Hurt));
            Save(created, Walk0File, 32, 32, feet, c => Beetle(c, 0));
            Save(created, Walk1File, 32, 32, feet, c => Beetle(c, 1));
            Save(created, BossIdleFile, 64, 64, feet, c => Warden(c, BossPose.Idle));
            Save(created, BossChargeFile, 64, 64, feet, c => Warden(c, BossPose.Charge));
            Save(created, BossStunnedFile, 64, 64, feet, c => Warden(c, BossPose.Stunned));
            Save(created, ShotFile, 16, 16, center, c => Pellet(c, new Color32(150, 255, 200, 255), new Color32(60, 200, 150, 255), false));
            Save(created, BreakShotFile, 16, 16, center, c => Pellet(c, new Color32(255, 210, 120, 255), new Color32(230, 120, 50, 255), true));
            Save(created, BulletFile, 16, 16, center, Bullet);
            Save(created, FruitFile, 32, 32, center, Fruit);
            Save(created, JumpOrbFile, 32, 32, center, c => Orb(c, new Color32(120, 190, 255, 255), true));
            Save(created, BreakOrbFile, 32, 32, center, c => Orb(c, new Color32(255, 170, 90, 255), false));
            Save(created, StatueFile, 32, 48, feet, Statue);
            SaveBackdrop(created);
            AssetDatabase.Refresh();
            foreach (var (path, pixelsPerUnit, pivot) in created) { Import(path, pixelsPerUnit, pivot); }
        }

        private enum Pose
        {
            Stand,
            Run0,
            Run1,
            Rise,
            Fall,
            Hurt,
        }

        private enum BossPose
        {
            Idle,
            Charge,
            Stunned,
        }

        private static void Save(List<(string, float, Vector2)> created, string file, int width, int height, Vector2 pivot, Action<Canvas> paint)
        {
            var path = ArtRoot + file;
            if (File.Exists(path)) { return; }
            var canvas = new Canvas(width, height);
            paint(canvas);
            Write(path, canvas);
            created.Add((path, PixelsPerUnit, pivot));
        }

        private static void Write(string path, Canvas canvas)
        {
            var texture = new Texture2D(canvas.Width, canvas.Height, TextureFormat.RGBA32, false);
            texture.SetPixels32(canvas.Pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        private static void Import(string path, float pixelsPerUnit, Vector2 pivot)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = pivot;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        // 地下の洞。暗い緑から青へのグラデーション、垂れ下がる岩、光る苔の粒。
        private static void SaveBackdrop(List<(string, float, Vector2)> created)
        {
            var path = ArtRoot + BackdropFile;
            if (File.Exists(path)) { return; }
            var canvas = new Canvas(BackdropWidth, BackdropHeight);
            for (var y = 0; y < BackdropHeight; y++)
            {
                var t = Mathf.Floor((float)y / BackdropHeight * 8) / 8;
                var color = Color.Lerp(new Color(.07f, .14f, .12f), new Color(.04f, .06f, .1f), t);
                for (var x = 0; x < BackdropWidth; x++) { canvas.Set(x, y, color); }
            }

            for (var x = 0; x < BackdropWidth; x++)
            {
                var hang = 40 + (int)(36 * Mathf.Abs(Mathf.Sin(x * .013f)) + (18 * Mathf.Sin((x * .051f) + .7f)));
                var floor = 50 + (int)(30 * Mathf.Abs(Mathf.Sin((x * .008f) + 1.9f)) + (10 * Mathf.Sin((x * .037f) + .2f)));
                for (var y = BackdropHeight - hang; y < BackdropHeight; y++) { canvas.Set(x, y, new Color(.05f, .09f, .09f)); }
                for (var y = 0; y < floor; y++) { canvas.Set(x, y, new Color(.06f, .11f, .1f)); }
            }

            for (var i = 0; i < 180; i++)
            {
                var x = (i * 433) % BackdropWidth;
                var y = 60 + ((i * 197) % (BackdropHeight - 120));
                var bright = i % 5 == 0;
                canvas.Set(x, y, bright ? new Color(.6f, 1f, .7f) : new Color(.25f, .5f, .38f));
                if (bright) { canvas.Set(x + 1, y, new Color(.35f, .7f, .5f)); }
            }

            Write(path, canvas);
            created.Add((path, BackdropPixelsPerUnit, new Vector2(.5f, .5f)));
        }

        // 石：深い青緑の石組み。top なら上面に苔をのせる。
        private static void Stone(Canvas c, bool top)
        {
            var stone = new Color32(52, 64, 70, 255);
            var dark = new Color32(36, 44, 50, 255);
            var light = new Color32(72, 88, 94, 255);
            c.Rect(0, 0, 32, 32, stone);
            c.Rect(0, 15, 32, 1, dark);
            c.Rect(0, 31, 32, 1, dark);
            c.Rect(10, 16, 1, 15, dark);
            c.Rect(24, 0, 1, 15, dark);
            foreach (var (x, y, w, h) in new[] { (2, 3, 6, 3), (14, 6, 7, 3), (13, 21, 8, 4), (26, 19, 4, 3) }) { c.Rect(x, y, w, h, light); }
            if (top)
            {
                var moss = new Color32(84, 156, 88, 255);
                var bright = new Color32(140, 210, 120, 255);
                c.Rect(0, 26, 32, 6, moss);
                for (var x = 0; x < 32; x += 4) { c.Rect(x + 1, 23 + (x / 4 % 2), 2, 3, moss); }
                c.Rect(0, 31, 32, 1, bright);
                for (var x = 3; x < 32; x += 9) { c.Set(x, 29, bright); }
            }
        }

        // ひびの壁：明るい石に大きなひびと、ほのかな光。
        private static void Cracked(Canvas c)
        {
            c.Rect(0, 0, 32, 32, new Color32(98, 90, 80, 255));
            c.Rect(1, 1, 30, 30, new Color32(118, 108, 94, 255));
            var crack = new Color32(40, 30, 26, 255);
            var glow = new Color32(255, 190, 110, 255);
            var path = new[] { (16, 31), (14, 26), (17, 21), (13, 16), (16, 11), (12, 6), (15, 0) };
            for (var i = 0; i + 1 < path.Length; i++) { Line(c, path[i], path[i + 1], crack); }
            Line(c, (14, 26), (7, 23), crack);
            Line(c, (13, 16), (24, 13), crack);
            Line(c, (16, 11), (22, 4), crack);
            c.Set(15, 21, glow);
            c.Set(14, 12, glow);
            c.Rect(0, 0, 32, 1, new Color32(60, 54, 48, 255));
        }

        // 扉：通り道に立つ淡い光の帯。
        private static void Door(Canvas c)
        {
            for (var x = 0; x < 32; x++)
            {
                for (var y = 0; y < 32; y++)
                {
                    var d = Mathf.Abs(x - 15.5f) / 16f;
                    var a = (byte)Mathf.Clamp(90 * (1 - d), 0, 255);
                    var sparkle = (x * 11 + y * 7) % 37 == 0;
                    c.Set(x, y, sparkle ? new Color32(200, 255, 220, 200) : new Color32(120, 220, 170, a));
                }
            }
        }

        // 自機：フードの探検者と腰のランタン。体は 20×28 ピクセル（Core の 10×14 px）を横 6〜26 に描く。
        private static void Explorer(Canvas c, Pose pose)
        {
            var cloak = new Color32(46, 140, 150, 255);
            var shade = new Color32(30, 96, 110, 255);
            var face = new Color32(250, 220, 190, 255);
            var boots = new Color32(70, 50, 44, 255);
            var lamp = new Color32(255, 214, 110, 255);
            var crouch = pose == Pose.Fall ? 1 : 0;
            var lean = pose == Pose.Hurt ? -2 : 0;

            switch (pose)
            {
                case Pose.Run0: c.Rect(9, 0, 4, 5, boots); c.Rect(18, 2, 4, 4, boots); break;
                case Pose.Run1: c.Rect(11, 2, 4, 4, boots); c.Rect(16, 0, 4, 5, boots); break;
                case Pose.Rise: c.Rect(10, 1, 4, 5, boots); c.Rect(18, 3, 4, 4, boots); break;
                case Pose.Hurt: c.Rect(8, 2, 4, 4, boots); c.Rect(17, 0, 4, 4, boots); break;
                default: c.Rect(10, 0, 4, 5, boots); c.Rect(18, 0, 4, 5, boots); break;
            }

            c.Rect(8 + lean, 5 - crouch, 16, 13, cloak);
            c.Rect(8 + lean, 5 - crouch, 3, 13, shade);
            c.Ellipse(17 + lean, 21 - crouch, 7f, 6f, face);
            c.Rect(20 + lean, 20 - crouch, 2, 3, pose == Pose.Hurt ? new Color32(200, 40, 40, 255) : _outline);
            c.Ellipse(15 + lean, 24 - crouch, 9f, 5.5f, cloak);
            c.Rect(7 + lean, 18 - crouch, 6, 8, cloak);
            c.Rect(22 + lean, 7 - crouch, 4, 5, lamp);
            c.Rect(23 + lean, 12 - crouch, 2, 1, boots);
            c.Outline(_outline);
        }

        // 歩く敵：苔をかぶった甲虫。体は 28×24 ピクセル（Core の 14×12 px）を横 2〜30 に描く。左向き。
        private static void Beetle(Canvas c, int frame)
        {
            var shell = new Color32(96, 70, 120, 255);
            var moss = new Color32(96, 170, 96, 255);
            var legs = new Color32(40, 30, 50, 255);
            for (var i = 0; i < 3; i++)
            {
                var x = 7 + (i * 8) + ((i + frame) % 2 == 0 ? 1 : -1);
                c.Rect(x, 0, 2, 5, legs);
            }

            c.Ellipse(16, 12, 13, 9, shell);
            c.Ellipse(17, 17, 10, 4, moss);
            c.Ellipse(5, 9, 4, 4, new Color32(70, 50, 90, 255));
            c.Set(3, 10, new Color32(255, 240, 120, 255));
            c.Rect(0, 12, 3, 1, legs);
            c.Outline(_outline);
        }

        // ボス：石の番人。体は 56×56 ピクセル（Core の 28×28 px）を横 4〜60 に描く。左向き。
        private static void Warden(Canvas c, BossPose pose)
        {
            var stone = new Color32(96, 104, 96, 255);
            var dark = new Color32(62, 68, 64, 255);
            var moss = new Color32(90, 160, 90, 255);
            var eye = pose == BossPose.Stunned ? new Color32(160, 160, 160, 255) : new Color32(255, 90, 80, 255);
            var drop = pose == BossPose.Charge ? 6 : 0;
            c.Rect(12, 0, 10, 10, dark);
            c.Rect(40, 0, 10, 10, dark);
            c.Ellipse(32, 26, 26, 20, stone);
            c.Ellipse(36, 40, 18, 8, moss);
            c.Ellipse(14, 30 - drop, 11, 10, stone);
            c.Rect(5, 30 - drop, 6, 3, eye);
            c.Rect(4, 22 - drop, 12, 2, dark);
            c.Rect(20, 14, 26, 2, dark);
            if (pose == BossPose.Charge)
            {
                for (var i = 0; i < 4; i++) { c.Rect(54 + (i % 2 * 3), 8 + (i * 7), 6, 1, new Color32(200, 220, 200, 255)); }
            }

            if (pose == BossPose.Stunned)
            {
                foreach (var (x, y) in new[] { (10, 52), (22, 56), (34, 52) })
                {
                    c.Rect(x - 1, y, 3, 1, new Color32(255, 240, 140, 255));
                    c.Rect(x, y - 1, 1, 3, new Color32(255, 240, 140, 255));
                }
            }

            c.Outline(_outline);
        }

        // 自機の弾：12×8 ピクセル（Core の 6×4 px）の光の粒。破壊ショットは先が尖る。右向き。
        private static void Pellet(Canvas c, Color32 core, Color32 rim, bool sharp)
        {
            c.Ellipse(8, 8, 6, 4, rim);
            c.Ellipse(9, 8, 3.5f, 2.5f, core);
            if (sharp)
            {
                c.Rect(13, 7, 3, 2, core);
                c.Set(15, 8, new Color32(255, 255, 255, 255));
            }
        }

        // ボスの弾：16 ピクセル（Core の 8 px）の紫の玉。
        private static void Bullet(Canvas c)
        {
            c.Ellipse(8, 8, 7.5f, 7.5f, new Color32(150, 70, 190, 255));
            c.Ellipse(7, 9, 4, 4, new Color32(220, 160, 255, 255));
            c.Outline(_outline);
        }

        // 回復の実：赤い実と葉。
        private static void Fruit(Canvas c)
        {
            c.Ellipse(16, 13, 8, 8, new Color32(220, 60, 80, 255));
            c.Ellipse(13, 16, 3, 3, new Color32(255, 150, 160, 255));
            c.Rect(16, 21, 2, 4, new Color32(90, 60, 40, 255));
            c.Ellipse(21, 24, 4, 2, new Color32(90, 180, 90, 255));
            c.Outline(_outline);
        }

        // 能力の玉：光る球に印（二段ジャンプは上向きの矢印 2 つ、破壊ショットはひび）。
        private static void Orb(Canvas c, Color32 color, bool jump)
        {
            c.Ellipse(16, 16, 11, 11, color);
            c.Ellipse(13, 19, 4, 4, new Color32(255, 255, 255, 200));
            var mark = new Color32(30, 40, 60, 255);
            if (jump)
            {
                for (var k = 0; k < 2; k++)
                {
                    var y = 10 + (k * 6);
                    for (var i = 0; i < 4; i++) { c.Rect(16 - i, y + i, 1, 1, mark); c.Rect(16 + i, y + i, 1, 1, mark); }
                }
            }
            else
            {
                Line(c, (16, 25), (14, 18), mark);
                Line(c, (14, 18), (18, 13), mark);
                Line(c, (18, 13), (15, 7), mark);
            }

            c.Outline(_outline);
        }

        // セーブ像：苔むした台座と、光る結晶。32×48 ピクセル。
        private static void Statue(Canvas c)
        {
            var stone = new Color32(110, 118, 112, 255);
            c.Rect(4, 0, 24, 6, new Color32(80, 86, 82, 255));
            c.Rect(8, 6, 16, 18, stone);
            c.Rect(6, 22, 20, 4, new Color32(84, 156, 88, 255));
            for (var y = 26; y < 44; y++)
            {
                var half = Math.Max(1, 7 - Math.Abs(y - 34) * 7 / 9);
                c.Rect(16 - half, y, half * 2, 1, new Color32(150, 230, 255, 255));
            }

            c.Rect(14, 30, 2, 8, new Color32(240, 255, 255, 255));
            c.Outline(_outline);
        }

        private static void Line(Canvas c, (int X, int Y) from, (int X, int Y) to, Color32 color)
        {
            var steps = Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y));
            for (var i = 0; i <= steps; i++)
            {
                var t = steps == 0 ? 0 : (float)i / steps;
                c.Set(Mathf.RoundToInt(Mathf.Lerp(from.X, to.X, t)), Mathf.RoundToInt(Mathf.Lerp(from.Y, to.Y, t)), color);
            }
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
