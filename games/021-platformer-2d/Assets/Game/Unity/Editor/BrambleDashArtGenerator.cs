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
    /// 1 タイル = 32 ピクセル（Core の 1 px = 2 ピクセル）。キャラクターは下端の中央を基準点にする。
    /// </summary>
    public static class BrambleDashArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";
        public const int PixelsPerUnit = 32;

        public const string GrassFile = "Grass.png";
        public const string DirtFile = "Dirt.png";
        public const string PoleFile = "Pole.png";
        public const string FlagFile = "Flag.png";
        public const string BackdropFile = "Backdrop.png";
        public const string PlayerStandFile = "PlayerStand.png";
        public const string PlayerRun0File = "PlayerRun0.png";
        public const string PlayerRun1File = "PlayerRun1.png";
        public const string PlayerJumpFile = "PlayerJump.png";
        public const string PlayerMissFile = "PlayerMiss.png";
        public const string Walker0File = "Walker0.png";
        public const string Walker1File = "Walker1.png";
        public const string WalkerSquashedFile = "WalkerSquashed.png";
        public const string HopperFile = "Hopper.png";
        public const string HopperAirFile = "HopperAir.png";
        public const string HopperSquashedFile = "HopperSquashed.png";
        public const string Thorn0File = "Thorn0.png";
        public const string Thorn1File = "Thorn1.png";

        /// <summary>背景は縦 15 単位（画面の高さ）で、21:9 まで覆える幅に描く。</summary>
        public const int BackdropWidth = 720;
        public const int BackdropHeight = 300;
        public const float BackdropPixelsPerUnit = BackdropHeight / 15f;

        private static readonly Color32 _outline = new(36, 30, 28, 255);

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtRoot);
            var created = new List<(string Path, float PixelsPerUnit, Vector2 Pivot)>();
            var center = new Vector2(.5f, .5f);
            var feet = new Vector2(.5f, 0);
            Save(created, GrassFile, center, Grass);
            Save(created, DirtFile, center, Dirt);
            Save(created, PoleFile, center, Pole);
            Save(created, FlagFile, new Vector2(0, .5f), Flag);
            Save(created, PlayerStandFile, feet, c => Player(c, 0));
            Save(created, PlayerRun0File, feet, c => Player(c, 1));
            Save(created, PlayerRun1File, feet, c => Player(c, 2));
            Save(created, PlayerJumpFile, feet, c => Player(c, 3));
            Save(created, PlayerMissFile, feet, c => Player(c, 4));
            Save(created, Walker0File, feet, c => Walker(c, 0));
            Save(created, Walker1File, feet, c => Walker(c, 1));
            Save(created, WalkerSquashedFile, feet, WalkerSquashed);
            Save(created, HopperFile, feet, c => Hopper(c, false));
            Save(created, HopperAirFile, feet, c => Hopper(c, true));
            Save(created, HopperSquashedFile, feet, HopperSquashed);
            Save(created, Thorn0File, feet, c => Thorn(c, 0));
            Save(created, Thorn1File, feet, c => Thorn(c, 1));
            SaveBackdrop(created);
            AssetDatabase.Refresh();
            foreach (var (path, pixelsPerUnit, pivot) in created) { Import(path, pixelsPerUnit, pivot); }
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

        // 夕方の野原。空のグラデーション、雲、遠い丘と茨の茂みの影。ドット絵に合わせて色を段階に分ける。
        private static void SaveBackdrop(List<(string, float, Vector2)> created)
        {
            var path = ArtRoot + BackdropFile;
            if (File.Exists(path)) { return; }
            var canvas = new Canvas(BackdropWidth, BackdropHeight);
            for (var y = 0; y < BackdropHeight; y++)
            {
                var t = Mathf.Floor((float)y / BackdropHeight * 8) / 8;
                var sky = Color.Lerp(new Color(.98f, .84f, .62f), new Color(.46f, .66f, .86f), t);
                for (var x = 0; x < BackdropWidth; x++) { canvas.Set(x, y, sky); }
            }

            foreach (var (cx, cy, r) in new[] { (90, 230, 18), (130, 236, 24), (170, 228, 16), (420, 250, 20), (455, 256, 26), (495, 248, 18), (640, 220, 14), (668, 226, 20) })
            {
                canvas.Ellipse(cx, cy, r * 1.6f, r * .7f, new Color(1f, .97f, .92f));
            }

            for (var x = 0; x < BackdropWidth; x++)
            {
                var far = 110 + (int)(26 * Mathf.Sin(x * .011f) + (14 * Mathf.Sin((x * .027f) + 1.3f)));
                var near = 60 + (int)(18 * Mathf.Sin((x * .017f) + 2.1f) + (9 * Mathf.Sin((x * .05f) + .4f)));
                for (var y = 0; y < far; y++) { canvas.Set(x, y, new Color(.58f, .72f, .6f)); }
                for (var y = 0; y < near; y++) { canvas.Set(x, y, new Color(.4f, .56f, .42f)); }
                var bush = 24 + (int)(6 * Mathf.Abs(Mathf.Sin(x * .09f)));
                for (var y = 0; y < bush; y++) { canvas.Set(x, y, new Color(.29f, .4f, .32f)); }
            }

            Write(path, canvas);
            created.Add((path, BackdropPixelsPerUnit, new Vector2(.5f, .5f)));
        }

        // 地面の上面：草の帯と、ところどころ伸びた草の葉。下は土。
        private static void Grass(Canvas c)
        {
            Dirt(c);
            var grass = new Color32(104, 170, 72, 255);
            var light = new Color32(150, 206, 96, 255);
            for (var x = 0; x < 32; x++)
            {
                var depth = 7 + ((x * 7) % 3);
                for (var y = 32 - depth; y < 32; y++) { c.Set(x, y, grass); }
                c.Set(x, 32 - depth, _outline);
                if (x % 5 == 1) { c.Set(x, 30, light); c.Set(x, 29, light); }
            }
        }

        // 土：茶色に小石の点。タイルを並べても継ぎ目が目立たないよう、点は端に寄せない。
        private static void Dirt(Canvas c)
        {
            c.Rect(0, 0, 32, 32, new Color32(150, 98, 60, 255));
            foreach (var (x, y) in new[] { (5, 6), (19, 4), (26, 13), (11, 17), (22, 23), (7, 26), (15, 10) })
            {
                c.Rect(x, y, 3, 2, new Color32(118, 74, 44, 255));
                c.Set(x, y + 2, new Color32(182, 132, 88, 255));
            }
        }

        // 旗の柱：細い銀の柱。
        private static void Pole(Canvas c)
        {
            c.Rect(13, 0, 6, 32, _outline);
            c.Rect(14, 0, 4, 32, new Color32(200, 206, 214, 255));
            c.Rect(15, 0, 1, 32, new Color32(246, 248, 252, 255));
        }

        // 旗：左端を柱に付ける三角の旗。
        private static void Flag(Canvas c)
        {
            for (var x = 0; x < 30; x++)
            {
                var half = (int)(13 * (1 - (x / 30f)));
                for (var y = 16 - half; y <= 16 + half; y++) { c.Set(x, y, new Color32(228, 72, 64, 255)); }
                c.Set(x, 16 - half, _outline);
                c.Set(x, 16 + half, _outline);
            }

            c.Ellipse(9, 16, 4, 4, new Color(1f, .9f, .5f));
        }

        // 自機：緑の頭巾をかぶった旅人。体は 24×28 ピクセル（Core の 12×14 px）。
        // pose：0 立ち、1・2 走り、3 跳び、4 ミス。
        private static void Player(Canvas c, int pose)
        {
            var hood = new Color32(70, 146, 82, 255);
            var face = new Color32(252, 220, 180, 255);
            var boots = new Color32(110, 66, 40, 255);
            var legLeft = pose == 1 ? 2 : pose == 2 ? -2 : pose == 3 ? -3 : 0;
            var lift = pose == 3 ? 3 : 0;

            // 足と体。
            c.Rect(10 + legLeft, lift, 5, 5, boots);
            c.Rect(17 - legLeft, pose == 3 ? 1 : 0, 5, 5, boots);
            c.Ellipse(16, 12, 9.5f, 8f, hood);

            // 顔と頭巾。
            c.Ellipse(16, 20, 8.5f, 7.5f, face);
            for (var y = 22; y < 28; y++)
            {
                var half = 9 - ((y - 22) * 3 / 2);
                for (var x = 16 - half; x <= 16 + half; x++) { c.Set(x, y, hood); }
            }

            if (pose == 4)
            {
                // 目を×にする。
                foreach (var ex in new[] { 18, 23 })
                {
                    c.Set(ex - 1, 21, _outline); c.Set(ex + 1, 21, _outline); c.Set(ex, 20, _outline); c.Set(ex - 1, 19, _outline); c.Set(ex + 1, 19, _outline);
                }
            }
            else
            {
                c.Rect(18, 19, 2, 3, _outline);
                c.Rect(22, 19, 2, 3, _outline);
            }

            c.Outline(_outline);
        }

        // 歩き虫：橙の甲羅の甲虫。体は 28×24 ピクセル。frame で足を入れ替える。
        private static void Walker(Canvas c, int frame)
        {
            var shell = new Color32(214, 120, 44, 255);
            var spot = new Color32(250, 196, 96, 255);
            var legs = new Color32(70, 46, 34, 255);
            for (var i = 0; i < 3; i++)
            {
                var x = 7 + (i * 7) + (frame == 0 ? 0 : 2);
                c.Rect(x, 0, 3, 5, legs);
            }

            c.Ellipse(16, 11, 13f, 9f, shell);
            c.Ellipse(11, 14, 3, 2, spot);
            c.Ellipse(19, 16, 2.5f, 2, spot);
            c.Ellipse(4, 9, 4, 4, legs);
            c.Set(3, 10, Color.white);
            c.Outline(_outline);
        }

        private static void WalkerSquashed(Canvas c)
        {
            c.Ellipse(16, 3, 14f, 3.5f, new Color32(214, 120, 44, 255));
            c.Ellipse(11, 4, 3, 1, new Color32(250, 196, 96, 255));
            c.Outline(_outline);
        }

        // 跳ね虫：桃色のぷにぷにした跳ねる虫。体は 24×24 ピクセル。空中では縦に伸びる。
        private static void Hopper(Canvas c, bool air)
        {
            var body = new Color32(236, 120, 168, 255);
            var belly = new Color32(255, 190, 214, 255);
            var rx = air ? 10f : 11.5f;
            var ry = air ? 12.5f : 11f;
            c.Ellipse(16, ry, rx, ry, body);
            c.Ellipse(16, ry - 3, rx - 4, ry - 6, belly);
            c.Rect(9, (int)(ry * 1.3f), 3, 4, Color.white);
            c.Rect(19, (int)(ry * 1.3f), 3, 4, Color.white);
            c.Rect(9, (int)(ry * 1.3f), 2, 2, _outline);
            c.Rect(19, (int)(ry * 1.3f), 2, 2, _outline);
            c.Outline(_outline);
        }

        private static void HopperSquashed(Canvas c)
        {
            c.Ellipse(16, 3, 13f, 3.5f, new Color32(236, 120, 168, 255));
            c.Outline(_outline);
        }

        // 茨玉：紫のとげだらけの玉。体は 28×28 ピクセル。frame でとげを少し回す。
        private static void Thorn(Canvas c, int frame)
        {
            var body = new Color32(128, 76, 170, 255);
            var spike = new Color32(236, 228, 246, 255);
            const int cx = 16;
            const int cy = 13;
            for (var i = 0; i < 8; i++)
            {
                var angle = ((i * 45f) + (frame * 22.5f)) * Mathf.Deg2Rad;
                for (var r = 8f; r <= 14f; r += .5f)
                {
                    var width = (int)((14f - r) / 3f);
                    var x = cx + Mathf.RoundToInt(Mathf.Cos(angle) * r);
                    var y = cy + Mathf.RoundToInt(Mathf.Sin(angle) * r);
                    for (var w = -width; w <= width; w++) { c.Set(x + w, y, spike); }
                }
            }

            c.Ellipse(cx, cy, 9.5f, 9.5f, body);
            c.Ellipse(cx - 3, cy + 3, 3, 3, new Color32(176, 126, 214, 255));
            c.Rect(cx + 1, cy, 2, 3, Color.white);
            c.Rect(cx + 5, cy, 2, 3, Color.white);
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

            private bool Opaque(Color32[] source, int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && source[(y * Width) + x].a != 0;
        }
    }
}
