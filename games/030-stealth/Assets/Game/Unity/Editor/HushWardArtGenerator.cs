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
    /// 1 タイル = 32 ピクセル。見下ろしなので、どの絵も中央を基準点にし、体の箱の中央に置く（体は 176 単位 = 22 ピクセル）。
    /// 夜の研究棟：青灰色の床、暗い壁、木箱、紺の制服の警備員、黒い服の侵入者。乱数は使わず、毎回同じ絵にする。
    /// </summary>
    public static class HushWardArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";
        public const int PixelsPerUnit = 32;

        public const string FloorFile = "Floor.png";
        public const string ExitFile = "Exit.png";
        public const string WallFile = "Wall.png";
        public const string WallFaceFile = "WallFace.png";
        public const string CrateFile = "Crate.png";
        public const string NoiseFile = "Noise.png";
        public const string ObjectiveFile = "Objective.png";
        public const string CarriedFile = "Carried.png";
        public const string IntruderDown0File = "IntruderDown0.png";
        public const string IntruderDown1File = "IntruderDown1.png";
        public const string IntruderUp0File = "IntruderUp0.png";
        public const string IntruderUp1File = "IntruderUp1.png";
        public const string IntruderSide0File = "IntruderSide0.png";
        public const string IntruderSide1File = "IntruderSide1.png";
        public const string IntruderSneakFile = "IntruderSneak.png";
        public const string GuardDown0File = "GuardDown0.png";
        public const string GuardDown1File = "GuardDown1.png";
        public const string GuardUp0File = "GuardUp0.png";
        public const string GuardUp1File = "GuardUp1.png";
        public const string GuardSide0File = "GuardSide0.png";
        public const string GuardSide1File = "GuardSide1.png";
        public const string QuestionFile = "MarkQuestion.png";
        public const string ExclaimFile = "MarkExclaim.png";
        public const string SearchingFile = "MarkSearching.png";
        public const string GaugeBackFile = "GaugeBack.png";
        public const string GaugeFillFile = "GaugeFill.png";
        public const string GoalFile = "DebugGoal.png";
        public const string NoticedFile = "DebugNoticed.png";
        public const string BackdropFile = "Backdrop.png";

        /// <summary>背景は縦 18 単位で、21:9 まで覆える幅に描く（カメラの子にして画面全体を覆う）。</summary>
        public const int BackdropWidth = 672;
        public const int BackdropHeight = 288;
        public const float BackdropPixelsPerUnit = BackdropHeight / 18f;

        private static readonly Color32 _outline = new(8, 10, 16, 255);

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtRoot);
            var created = new List<(string Path, float PixelsPerUnit)>();
            Save(created, FloorFile, 32, 32, c => Floor(c, false));
            Save(created, ExitFile, 32, 32, c => Floor(c, true));
            Save(created, WallFile, 32, 32, c => Wall(c, false));
            Save(created, WallFaceFile, 32, 32, c => Wall(c, true));
            Save(created, CrateFile, 32, 32, Crate);
            Save(created, NoiseFile, 32, 32, Noise);
            Save(created, ObjectiveFile, 32, 32, c => Folder(c, 16, 15, 1f));
            Save(created, CarriedFile, 16, 16, c => Folder(c, 8, 8, .5f));
            Save(created, IntruderDown0File, 32, 32, c => Intruder(c, View.Down, 0));
            Save(created, IntruderDown1File, 32, 32, c => Intruder(c, View.Down, 1));
            Save(created, IntruderUp0File, 32, 32, c => Intruder(c, View.Up, 0));
            Save(created, IntruderUp1File, 32, 32, c => Intruder(c, View.Up, 1));
            Save(created, IntruderSide0File, 32, 32, c => Intruder(c, View.Side, 0));
            Save(created, IntruderSide1File, 32, 32, c => Intruder(c, View.Side, 1));
            Save(created, IntruderSneakFile, 32, 32, c => Intruder(c, View.Crouch, 0));
            Save(created, GuardDown0File, 32, 32, c => Guard(c, View.Down, 0));
            Save(created, GuardDown1File, 32, 32, c => Guard(c, View.Down, 1));
            Save(created, GuardUp0File, 32, 32, c => Guard(c, View.Up, 0));
            Save(created, GuardUp1File, 32, 32, c => Guard(c, View.Up, 1));
            Save(created, GuardSide0File, 32, 32, c => Guard(c, View.Side, 0));
            Save(created, GuardSide1File, 32, 32, c => Guard(c, View.Side, 1));
            Save(created, QuestionFile, 16, 20, c => Mark(c, '?'));
            Save(created, ExclaimFile, 16, 20, c => Mark(c, '!'));
            Save(created, SearchingFile, 16, 20, c => Mark(c, '.'));
            Save(created, GaugeBackFile, 26, 5, c => c.Rect(0, 0, 26, 5, new Color32(14, 18, 26, 230)));
            Save(created, GaugeFillFile, 26, 5, c => c.Rect(0, 1, 26, 3, new Color32(255, 255, 255, 255)));
            Save(created, GoalFile, 16, 16, Goal);
            Save(created, NoticedFile, 16, 16, Noticed);
            SaveBackdrop(created);
            AssetDatabase.Refresh();
            foreach (var (path, pixelsPerUnit) in created) { Import(path, pixelsPerUnit); }
        }

        private enum View
        {
            Down,
            Up,
            Side,
            Crouch,
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

        // 夜空：紺から黒へのグラデーションと、遠い街の灯り。
        private static void SaveBackdrop(List<(string, float)> created)
        {
            var path = ArtRoot + BackdropFile;
            if (File.Exists(path)) { return; }
            var canvas = new Canvas(BackdropWidth, BackdropHeight);
            for (var y = 0; y < BackdropHeight; y++)
            {
                var t = Mathf.Floor((float)y / BackdropHeight * 8) / 8;
                var color = Color.Lerp(new Color(.05f, .07f, .11f), new Color(.02f, .03f, .05f), t);
                for (var x = 0; x < BackdropWidth; x++) { canvas.Set(x, y, color); }
            }

            for (var i = 0; i < 120; i++)
            {
                var x = (i * 389) % BackdropWidth;
                var y = (i * 151) % BackdropHeight;
                canvas.Set(x, y, i % 5 == 0 ? new Color(.9f, .8f, .5f) : new Color(.25f, .3f, .4f));
            }

            Write(path, canvas);
            created.Add((path, BackdropPixelsPerUnit));
        }

        // 床：青灰色のリノリウムの四角い板。exit（入口）は緑の誘導灯の矢印。
        private static void Floor(Canvas c, bool exit)
        {
            var tile = exit ? new Color32(44, 74, 62, 255) : new Color32(54, 62, 74, 255);
            var seam = exit ? new Color32(32, 56, 46, 255) : new Color32(42, 48, 58, 255);
            var fleck = new Color32(66, 76, 90, 255);
            c.Rect(0, 0, 32, 32, tile);
            c.Rect(0, 0, 32, 1, seam);
            c.Rect(0, 0, 1, 32, seam);
            foreach (var (x, y) in new[] { (6, 7), (23, 11), (10, 25), (27, 26), (15, 18) }) { c.Set(x, y, fleck); }
            if (!exit) { return; }
            var light = new Color32(110, 230, 150, 255);
            c.Rect(8, 14, 12, 4, light);
            for (var i = 0; i < 6; i++) { c.Rect(20 + i, 10 + i, 1, 12 - (2 * i), light); }
        }

        // 壁：暗い鋼板とリベット。face（下が床の壁）は手前の面を明るくして厚みを出す。
        private static void Wall(Canvas c, bool face)
        {
            c.Rect(0, 0, 32, 32, new Color32(26, 30, 40, 255));
            c.Rect(1, 1, 30, 30, new Color32(34, 40, 52, 255));
            foreach (var (x, y) in new[] { (3, 3), (28, 3), (3, 28), (28, 28) }) { c.Rect(x, y, 2, 2, new Color32(70, 78, 94, 255)); }
            if (face)
            {
                c.Rect(0, 0, 32, 8, new Color32(58, 66, 82, 255));
                c.Rect(0, 8, 32, 1, new Color32(90, 100, 120, 255));
            }
        }

        // 木箱：板と斜めの補強材。
        private static void Crate(Canvas c)
        {
            var wood = new Color32(132, 96, 58, 255);
            var dark = new Color32(88, 60, 34, 255);
            var light = new Color32(170, 128, 80, 255);
            c.Rect(2, 2, 28, 28, wood);
            c.Rect(2, 2, 28, 3, dark);
            c.Rect(2, 27, 28, 3, light);
            c.Rect(2, 2, 3, 28, dark);
            c.Rect(27, 2, 3, 28, dark);
            for (var i = 0; i < 22; i++) { c.Rect(5 + i, 5 + i, 3, 1, dark); }
            c.Outline(_outline);
        }

        // 足音の届いたマス：白い角丸の板（色と濃さは描画のときに Tilemap の色で付ける）。
        private static void Noise(Canvas c)
        {
            c.Rect(2, 1, 28, 30, new Color32(255, 255, 255, 255));
            c.Rect(1, 2, 30, 28, new Color32(255, 255, 255, 255));
        }

        // 機密ファイル：赤い「極秘」の帯がある黄色いフォルダ。scale で大きさを変える（運んでいる印は半分）。
        private static void Folder(Canvas c, int cx, int cy, float scale)
        {
            int S(float v) => Mathf.RoundToInt(v * scale);
            var paper = new Color32(236, 196, 90, 255);
            var shade = new Color32(190, 146, 56, 255);
            c.Rect(cx - S(11), cy - S(8), S(22), S(16), paper);
            c.Rect(cx - S(11), cy + S(8), S(9), S(3), shade);
            c.Rect(cx - S(11), cy - S(8), S(22), S(2), shade);
            c.Rect(cx - S(8), cy - S(2), S(16), Math.Max(1, S(4)), new Color32(210, 50, 50, 255));
            c.Outline(_outline);
        }

        // 侵入者：黒い服と緑のゴーグル。体は 22 ピクセルの円を中央に。crouch（忍び足）は低く丸まる。
        private static void Intruder(Canvas c, View view, int frame)
        {
            var suit = new Color32(36, 38, 46, 255);
            var shade = new Color32(22, 24, 30, 255);
            var skin = new Color32(214, 176, 146, 255);
            var goggle = new Color32(110, 240, 170, 255);
            var step = frame == 0 ? 0 : 2;
            if (view == View.Crouch)
            {
                c.Ellipse(16, 13, 10, 7, suit);
                c.Ellipse(16, 18, 6, 5, shade);
                c.Rect(12, 18, 8, 2, goggle);
                c.Outline(new Color32(120, 200, 170, 255));
                return;
            }

            c.Rect(10, 5 + step, 4, 4, shade);
            c.Rect(18, 7 - step, 4, 4, shade);
            c.Ellipse(16, 14, 8, 6, suit);
            c.Rect(8, 12, 16, 5, suit);
            switch (view)
            {
                case View.Up:
                    c.Ellipse(16, 21, 6.5f, 6, shade);
                    c.Rect(11, 22, 10, 2, new Color32(60, 64, 76, 255));
                    break;
                case View.Side:
                    c.Ellipse(16, 21, 6, 6, skin);
                    c.Ellipse(14, 23, 6, 4.5f, shade);
                    c.Rect(18, 20, 4, 2, goggle);
                    break;
                default:
                    c.Ellipse(16, 21, 6.5f, 6, skin);
                    c.Ellipse(16, 25, 6.5f, 3, shade);
                    c.Rect(11, 20, 10, 2, goggle);
                    break;
            }

            c.Outline(new Color32(120, 200, 170, 255));
        }

        // 警備員：紺の制服と帽子、腰の懐中電灯。
        private static void Guard(Canvas c, View view, int frame)
        {
            var uniform = new Color32(52, 74, 130, 255);
            var shade = new Color32(34, 48, 90, 255);
            var cap = new Color32(28, 36, 64, 255);
            var skin = new Color32(232, 190, 156, 255);
            var badge = new Color32(240, 210, 90, 255);
            var step = frame == 0 ? 0 : 2;
            c.Rect(10, 4 + step, 4, 5, new Color32(24, 24, 30, 255));
            c.Rect(18, 6 - step, 4, 5, new Color32(24, 24, 30, 255));
            c.Ellipse(16, 13, 9, 6.5f, uniform);
            c.Rect(7, 11, 18, 5, uniform);
            c.Rect(7, 9, 3, 7, shade);
            switch (view)
            {
                case View.Up:
                    c.Ellipse(16, 21, 7, 6, cap);
                    break;
                case View.Side:
                    c.Ellipse(16, 21, 6.5f, 6, skin);
                    c.Ellipse(15, 23, 7, 4, cap);
                    c.Rect(20, 24, 4, 2, cap);
                    c.Rect(19, 20, 2, 2, _outline);
                    c.Rect(22, 12, 5, 3, new Color32(200, 200, 210, 255));
                    c.Rect(26, 12, 2, 3, new Color32(255, 240, 160, 255));
                    break;
                default:
                    c.Ellipse(16, 21, 7, 6, skin);
                    c.Ellipse(16, 25, 7.5f, 3, cap);
                    c.Rect(13, 20, 2, 2, _outline);
                    c.Rect(18, 20, 2, 2, _outline);
                    c.Rect(19, 13, 3, 2, badge);
                    break;
            }

            c.Outline(_outline);
        }

        // 状態の記号：吹き出しに「?」（調査）・「!」（追跡）・「…」（捜索）。
        private static void Mark(Canvas c, char symbol)
        {
            var bubble = symbol == '!' ? new Color32(255, 80, 70, 255) : symbol == '?' ? new Color32(255, 190, 60, 255) : new Color32(180, 140, 255, 255);
            c.Ellipse(8, 11, 7.5f, 8.5f, bubble);
            c.Rect(6, 0, 3, 4, bubble);
            var ink = new Color32(20, 16, 24, 255);
            switch (symbol)
            {
                case '!':
                    c.Rect(7, 9, 2, 9, ink);
                    c.Rect(7, 5, 2, 2, ink);
                    break;
                case '?':
                    c.Rect(5, 15, 6, 2, ink);
                    c.Rect(10, 12, 2, 4, ink);
                    c.Rect(7, 10, 4, 2, ink);
                    c.Rect(7, 9, 2, 2, ink);
                    c.Rect(7, 5, 2, 2, ink);
                    break;
                default:
                    for (var i = 0; i < 3; i++) { c.Rect(3 + (i * 4), 10, 2, 2, ink); }
                    break;
            }

            c.Outline(_outline);
        }

        // 詳しい表示：向かう先は十字、最後に見た位置は赤い輪。
        private static void Goal(Canvas c)
        {
            var color = new Color32(120, 220, 255, 255);
            c.Rect(7, 1, 2, 14, color);
            c.Rect(1, 7, 14, 2, color);
            c.Outline(_outline);
        }

        private static void Noticed(Canvas c)
        {
            c.Ellipse(8, 8, 7, 7, new Color32(255, 80, 80, 255));
            c.Ellipse(8, 8, 4.5f, 4.5f, new Color32(0, 0, 0, 0));
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
