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
    /// 1 タイル = 32 ピクセル（Core の 1 px = 2 ピクセル）。自機は下端の中央を基準点にする。
    /// </summary>
    public static class GlintAscentArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";
        public const int PixelsPerUnit = 32;

        public const string RockTopFile = "RockTop.png";
        public const string RockFile = "Rock.png";
        public const string SpikeFile = "Spike.png";
        public const string ExitFile = "Exit.png";
        public const string BackdropFile = "Backdrop.png";
        public const string StandFile = "PlayerStand.png";
        public const string Run0File = "PlayerRun0.png";
        public const string Run1File = "PlayerRun1.png";
        public const string RiseFile = "PlayerRise.png";
        public const string FallFile = "PlayerFall.png";
        public const string DashFile = "PlayerDash.png";
        public const string SlideFile = "PlayerSlide.png";
        public const string DeadFile = "PlayerDead.png";

        /// <summary>背景は縦 24 単位（部屋と HUD の帯より少し大きい）で、21:9 まで覆える幅に描く。</summary>
        public const int BackdropWidth = 960;
        public const int BackdropHeight = 400;
        public const float BackdropPixelsPerUnit = BackdropHeight / 24f;

        private static readonly Color32 _outline = new(20, 18, 34, 255);

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtRoot);
            var created = new List<(string Path, float PixelsPerUnit, Vector2 Pivot)>();
            var center = new Vector2(.5f, .5f);
            var feet = new Vector2(.5f, 0);
            Save(created, RockTopFile, center, c => Rock(c, true));
            Save(created, RockFile, center, c => Rock(c, false));
            Save(created, SpikeFile, center, Spike);
            Save(created, ExitFile, center, Exit);
            Save(created, StandFile, feet, c => Climber(c, Pose.Stand));
            Save(created, Run0File, feet, c => Climber(c, Pose.Run0));
            Save(created, Run1File, feet, c => Climber(c, Pose.Run1));
            Save(created, RiseFile, feet, c => Climber(c, Pose.Rise));
            Save(created, FallFile, feet, c => Climber(c, Pose.Fall));
            Save(created, DashFile, feet, c => Climber(c, Pose.Dash));
            Save(created, SlideFile, feet, c => Climber(c, Pose.Slide));
            Save(created, DeadFile, feet, Burst);
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
            Dash,
            Slide,
        }

        private static void Save(List<(string, float, Vector2)> created, string file, Vector2 pivot, Action<Canvas> paint)
        {
            var path = ArtRoot + file;
            if (File.Exists(path)) { return; }
            var canvas = new Canvas(32, 32);
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

        // 夜の山。星空のグラデーション、遠い峰、山頂の光。ドット絵に合わせて色を段階に分ける。
        private static void SaveBackdrop(List<(string, float, Vector2)> created)
        {
            var path = ArtRoot + BackdropFile;
            if (File.Exists(path)) { return; }
            var canvas = new Canvas(BackdropWidth, BackdropHeight);
            for (var y = 0; y < BackdropHeight; y++)
            {
                var t = Mathf.Floor((float)y / BackdropHeight * 10) / 10;
                var sky = Color.Lerp(new Color(.24f, .2f, .42f), new Color(.06f, .07f, .18f), t);
                for (var x = 0; x < BackdropWidth; x++) { canvas.Set(x, y, sky); }
            }

            // 星は決まった式で散らす（乱数を使わず、毎回同じ絵にする）。
            for (var i = 0; i < 220; i++)
            {
                var x = (i * 389) % BackdropWidth;
                var y = 120 + ((i * 211) % (BackdropHeight - 120));
                var bright = i % 7 == 0;
                canvas.Set(x, y, bright ? new Color(1f, .96f, .8f) : new Color(.7f, .74f, .95f));
                if (bright) { canvas.Set(x + 1, y, new Color(.8f, .8f, .9f)); canvas.Set(x, y + 1, new Color(.8f, .8f, .9f)); }
            }

            canvas.Ellipse(780, 330, 22, 22, new Color(.96f, .94f, .82f));
            canvas.Ellipse(788, 336, 20, 20, new Color(.1f, .1f, .24f));

            for (var x = 0; x < BackdropWidth; x++)
            {
                var far = 150 + (int)(70 * Mathf.Abs(Mathf.Sin(x * .006f)) + (24 * Mathf.Sin((x * .021f) + 1.1f)));
                var near = 70 + (int)(40 * Mathf.Abs(Mathf.Sin((x * .009f) + 2.3f)) + (12 * Mathf.Sin((x * .04f) + .4f)));
                for (var y = 0; y < far; y++) { canvas.Set(x, y, y > far - 6 ? new Color(.62f, .64f, .8f) : new Color(.2f, .2f, .38f)); }
                for (var y = 0; y < near; y++) { canvas.Set(x, y, new Color(.13f, .13f, .26f)); }
            }

            Write(path, canvas);
            created.Add((path, BackdropPixelsPerUnit, new Vector2(.5f, .5f)));
        }

        // 岩：灰青の石。top なら上面に雪をのせる。
        private static void Rock(Canvas c, bool top)
        {
            var stone = new Color32(74, 78, 112, 255);
            var dark = new Color32(56, 58, 88, 255);
            var light = new Color32(98, 104, 144, 255);
            c.Rect(0, 0, 32, 32, stone);
            foreach (var (x, y, w, h) in new[] { (3, 4, 9, 6), (17, 12, 11, 7), (5, 20, 8, 5), (20, 2, 7, 5) })
            {
                c.Rect(x, y, w, h, light);
                c.Rect(x, y, w, 1, dark);
                c.Rect(x + w - 1, y, 1, h, dark);
            }

            if (top)
            {
                c.Rect(0, 26, 32, 6, new Color32(232, 238, 252, 255));
                for (var x = 0; x < 32; x += 5) { c.Rect(x, 24, 3, 2, new Color32(232, 238, 252, 255)); }
                c.Rect(0, 31, 32, 1, new Color32(255, 255, 255, 255));
            }
        }

        // トゲ：下半分（16 ピクセル）に 4 本の氷の刃。当たりはタイルの下半分。
        private static void Spike(Canvas c)
        {
            var ice = new Color32(196, 228, 252, 255);
            var shade = new Color32(120, 160, 214, 255);
            c.Rect(0, 0, 32, 2, shade);
            for (var i = 0; i < 4; i++)
            {
                var cx = 4 + (i * 8);
                for (var y = 0; y < 16; y++)
                {
                    var half = 4 - (y / 4);
                    for (var x = cx - half; x < cx + half; x++) { c.Set(x, y, x < cx ? ice : shade); }
                }
            }

            c.Outline(_outline);
        }

        // 出口：縦に流れる光の柱。
        private static void Exit(Canvas c)
        {
            for (var x = 0; x < 32; x++)
            {
                var d = Mathf.Abs(x - 15.5f) / 16f;
                var a = (byte)Mathf.Clamp(220 * (1 - d) * (1 - d), 0, 255);
                for (var y = 0; y < 32; y++)
                {
                    var sparkle = (x * 7 + y * 13) % 29 == 0;
                    c.Set(x, y, sparkle ? new Color32(255, 255, 255, 255) : new Color32(255, 226, 140, a));
                }
            }
        }

        // 自機：赤い髪とマフラーの登山者。体は 20×28 ピクセル（Core の 10×14 px）を横 6〜26 に描く。
        private static void Climber(Canvas c, Pose pose)
        {
            var hair = new Color32(232, 84, 70, 255);
            var coat = new Color32(70, 110, 190, 255);
            var face = new Color32(252, 222, 190, 255);
            var boots = new Color32(60, 44, 52, 255);
            var tilt = pose == Pose.Dash ? 3 : 0;
            var crouch = pose == Pose.Fall ? 1 : 0;

            // 足。
            switch (pose)
            {
                case Pose.Run0:
                    c.Rect(9, 0, 4, 5, boots); c.Rect(18, 2, 4, 4, boots);
                    break;
                case Pose.Run1:
                    c.Rect(11, 2, 4, 4, boots); c.Rect(16, 0, 4, 5, boots);
                    break;
                case Pose.Rise:
                    c.Rect(10, 1, 4, 5, boots); c.Rect(18, 3, 4, 4, boots);
                    break;
                case Pose.Dash:
                    c.Rect(6, 3, 5, 4, boots); c.Rect(12, 2, 5, 4, boots);
                    break;
                default:
                    c.Rect(10, 0, 4, 5, boots); c.Rect(18, 0, 4, 5, boots);
                    break;
            }

            // 体と顔。
            c.Rect(8 + tilt, 5 - crouch, 16, 11, coat);
            c.Rect(8 + tilt, 14 - crouch, 16, 2, new Color32(240, 200, 80, 255));
            c.Ellipse(16 + tilt, 21 - crouch, 7.5f, 6.5f, face);
            c.Rect(19 + tilt, 20 - crouch, 2, 3, _outline);

            // 髪：上と後ろに流す。落下中は上へなびき、ダッシュ中は後ろへ長く伸びる。
            c.Ellipse(14 + tilt, 25 - crouch, 8.5f, 4.5f, hair);
            var tailY = pose == Pose.Fall ? 27 : pose == Pose.Rise ? 19 : 22;
            var tailLength = pose == Pose.Dash ? 10 : pose == Pose.Run0 || pose == Pose.Run1 ? 6 : 4;
            for (var i = 0; i < tailLength; i++) { c.Rect(7 + tilt - i, tailY - (i / 3) - crouch, 3, 3, hair); }

            if (pose == Pose.Slide)
            {
                // 壁に手をつく。
                c.Rect(24, 12, 4, 3, face);
            }

            c.Outline(_outline);
        }

        // 死亡：光の粒が散る。
        private static void Burst(Canvas c)
        {
            var color = new Color32(255, 236, 200, 255);
            for (var i = 0; i < 8; i++)
            {
                var angle = i * 45f * Mathf.Deg2Rad;
                var x = 16 + Mathf.RoundToInt(Mathf.Cos(angle) * 10);
                var y = 14 + Mathf.RoundToInt(Mathf.Sin(angle) * 10);
                c.Ellipse(x, y, 2.5f, 2.5f, color);
            }

            c.Ellipse(16, 14, 4, 4, new Color32(232, 84, 70, 255));
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

            public void Ellipse(float cx, float cy, float rx, float ry, Color color) => Ellipse(cx, cy, rx, ry, (Color32)color);

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
