using System;
using System.Collections.Generic;
using System.IO;
using Game.Core;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>
    /// 図形を組み合わせてドット絵の素材 PNG を描き出す。既にあるファイルとそのインポート設定は上書きしない。
    /// 1 単位（1 マス）= 16 ピクセル。夜空の台：濃い藍の盤面に星、水色の壁、桃色のバンパー、金のターゲット。
    /// フリッパー・バンパー・球は台のデータの大きさで描く（フリッパーは軸の円の中心が基点）。乱数は使わず、毎回同じ絵にする。
    /// </summary>
    public static class CometCoilArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";
        public const int PixelsPerUnit = 16;

        public const string BackgroundFile = "Background.png";
        public const string RailFile = CometCoilStage.RailSpriteName + ".png";
        public const string GateFile = CometCoilStage.GateSpriteName + ".png";
        public const string TargetFile = "Target.png";
        public const string BumperFile = "Bumper.png";
        public const string BallFile = "Ball.png";
        public const string FlipperFile = "Flipper.png";
        public const string PlungerFile = "Plunger.png";

        /// <summary>背景は台（24 × 44 マス）の周りに 2 マスの枠を付ける。</summary>
        public const int FrameUnits = 2;

        public const int BackgroundWidth = (Board.Columns + (2 * FrameUnits)) * PixelsPerUnit;
        public const int BackgroundHeight = (Board.Rows + (2 * FrameUnits)) * PixelsPerUnit;

        private static readonly Color32 _outline = new(8, 10, 24, 255);

        public static void GenerateIfMissing(Table table, PinballConfig config)
        {
            if (table == null) { throw new ArgumentNullException(nameof(table)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            Directory.CreateDirectory(ArtRoot);
            var created = new List<(string Path, Vector2 Pivot, Vector4 Border)>();
            var center = new Vector2(.5f, .5f);
            Save(created, BackgroundFile, BackgroundWidth, BackgroundHeight, center, Vector4.zero, c => Background(c, table));
            Save(created, RailFile, 8, 8, center, new Vector4(3, 3, 3, 3), c => Bar(c, new Color32(120, 200, 255, 255), new Color32(200, 240, 255, 255)));
            Save(created, GateFile, 8, 8, center, new Vector4(3, 3, 3, 3), c => Bar(c, new Color32(255, 170, 70, 255), new Color32(255, 230, 160, 255)));
            Save(created, TargetFile, 8, 8, center, new Vector4(3, 3, 3, 3), c => Bar(c, new Color32(255, 210, 80, 255), new Color32(255, 250, 200, 255)));

            var bumper = Bumper(table);
            var bumperSize = Mathf.Max(8, Mathf.RoundToInt(2f * bumper / Board.Unit * PixelsPerUnit));
            Save(created, BumperFile, bumperSize, bumperSize, center, Vector4.zero, Bumper);
            var ballSize = Mathf.Max(6, Mathf.RoundToInt(2f * config.BallRadius / Board.Unit * PixelsPerUnit));
            Save(created, BallFile, ballSize, ballSize, center, Vector4.zero, Ball);

            // フリッパー：軸の円の中心を基点に、右へ長さだけ伸びるカプセル。
            var spec = table.Left;
            var radius = (float)spec.Radius / Board.Unit * PixelsPerUnit;
            var length = (float)spec.Length / Board.Unit * PixelsPerUnit;
            var width = Mathf.RoundToInt(length + (2 * radius));
            var height = Mathf.Max(4, Mathf.RoundToInt(2 * radius));
            Save(created, FlipperFile, width, height, new Vector2(radius / width, .5f), Vector4.zero, c => Flipper(c, radius, length));
            Save(created, PlungerFile, 26, 48, new Vector2(.5f, 1f), Vector4.zero, Plunger);
            AssetDatabase.Refresh();
            foreach (var (path, pivot, border) in created) { Import(path, pivot, border); }
        }

        private static int Bumper(Table table)
        {
            foreach (var collider in table.Colliders)
            {
                if (collider.Kind == ColliderKind.Bumper) { return collider.Radius; }
            }

            throw new InvalidOperationException("バンパーがありません。");
        }

        private static void Save(List<(string, Vector2, Vector4)> created, string file, int width, int height, Vector2 pivot, Vector4 border, Action<Canvas> paint)
        {
            var path = ArtRoot + file;
            if (File.Exists(path)) { return; }
            var canvas = new Canvas(width, height);
            paint(canvas);
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(canvas.Pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            created.Add((path, pivot, border));
        }

        private static void Import(string path, Vector2 pivot, Vector4 border)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = pivot;
            settings.spriteBorder = border;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        /// <summary>背景：濃い藍の盤面（上ほど濃い）に星と薄い彗星の渦、ドレインの下は暗い赤、外は金属の枠。</summary>
        private static void Background(Canvas c, Table table)
        {
            var frame = FrameUnits * PixelsPerUnit;
            for (var y = 0; y < BackgroundHeight; y++)
            {
                for (var x = 0; x < BackgroundWidth; x++)
                {
                    var tx = (x - frame) / (float)PixelsPerUnit;
                    var ty = (y - frame) / (float)PixelsPerUnit;
                    var inside = tx >= 0 && tx < Board.Columns && ty >= 0 && ty < Board.Rows;
                    if (!inside)
                    {
                        var band = ((x / 4) + (y / 4)) % 2 == 0;
                        c.Set(x, y, band ? new Color32(46, 50, 70, 255) : new Color32(38, 42, 60, 255));
                        continue;
                    }

                    var t = Mathf.Floor(ty / Board.Rows * 10) / 10;
                    var sky = Color.Lerp(new Color(.10f, .12f, .30f), new Color(.03f, .03f, .11f), t);
                    if (ty * Board.Unit < table.DrainY + (2 * Board.Unit)) { sky = Color.Lerp(sky, new Color(.25f, .04f, .08f), .6f); }
                    if (tx * Board.Unit > table.LaneX0) { sky = Color.Lerp(sky, new Color(.12f, .14f, .22f), .7f); }
                    c.Set(x, y, sky);
                }
            }

            // 彗星の渦（バンパーの周りの薄い輪）。
            var swirl = new Color32(40, 60, 120, 255);
            foreach (var collider in table.Colliders)
            {
                if (collider.Kind != ColliderKind.Bumper) { continue; }
                var cx = frame + ((float)collider.A.X / Board.Unit * PixelsPerUnit);
                var cy = frame + ((float)collider.A.Y / Board.Unit * PixelsPerUnit);
                var r = (float)collider.Radius / Board.Unit * PixelsPerUnit;
                c.Ring(cx, cy, r + 9, swirl);
                c.Ring(cx, cy, r + 15, new Color32(30, 44, 96, 255));
            }

            // 星：位置は整数のハッシュで決める（毎回同じ絵）。
            for (var i = 0; i < 260; i++)
            {
                var h = (uint)i * 2654435761u;
                var x = frame + (int)(h % (uint)(Board.Columns * PixelsPerUnit));
                var y = frame + (int)((h >> 11) % (uint)(Board.Rows * PixelsPerUnit));
                var bright = (h >> 23) % 5 == 0;
                c.Set(x, y, bright ? new Color32(255, 250, 220, 255) : new Color32(150, 160, 210, 255));
                if (bright)
                {
                    c.Set(x + 1, y, new Color32(120, 130, 190, 255));
                    c.Set(x - 1, y, new Color32(120, 130, 190, 255));
                    c.Set(x, y + 1, new Color32(120, 130, 190, 255));
                    c.Set(x, y - 1, new Color32(120, 130, 190, 255));
                }
            }

            // レーンの上向きの矢印。
            var laneX = frame + ((float)(table.LaneX0 + table.LaneX1) / 2 / Board.Unit * PixelsPerUnit);
            for (var k = 0; k < 4; k++)
            {
                var top = frame + ((8 + (k * 6)) * PixelsPerUnit);
                c.Line(laneX - 6, top - 6, laneX, top, new Color32(90, 110, 170, 255));
                c.Line(laneX + 6, top - 6, laneX, top, new Color32(90, 110, 170, 255));
            }
        }

        /// <summary>9 分割で伸ばす棒（壁・扉・ターゲット）：縁は濃く、中は明るい。</summary>
        private static void Bar(Canvas c, Color32 body, Color32 light)
        {
            c.Rect(0, 0, 8, 8, body);
            c.Rect(1, 4, 6, 2, light);
            c.Rect(0, 0, 8, 1, _outline);
            c.Rect(0, 7, 8, 1, _outline);
            c.Rect(0, 0, 1, 8, _outline);
            c.Rect(7, 0, 1, 8, _outline);
        }

        private static void Bumper(Canvas c)
        {
            var size = c.Width;
            var mid = size / 2f;
            c.Ellipse(mid, mid, mid - 1, mid - 1, new Color32(240, 90, 170, 255));
            c.Ellipse(mid, mid, (mid - 1) * .72f, (mid - 1) * .72f, new Color32(255, 160, 210, 255));
            c.Ellipse(mid, mid, (mid - 1) * .38f, (mid - 1) * .38f, new Color32(255, 240, 250, 255));
            c.Ring(mid, mid, (mid - 1) * .86f, new Color32(150, 40, 110, 255));
            c.Outline(_outline);
        }

        private static void Ball(Canvas c)
        {
            var mid = c.Width / 2f;
            c.Ellipse(mid, mid, mid - .5f, mid - .5f, new Color32(190, 200, 220, 255));
            c.Ellipse(mid - 1, mid + 1, (mid - .5f) * .7f, (mid - .5f) * .7f, new Color32(230, 236, 250, 255));
            c.Ellipse(mid - 2, mid + 2, 1.5f, 1.5f, Color.white);
        }

        private static void Flipper(Canvas c, float radius, float length)
        {
            var body = new Color32(255, 120, 70, 255);
            for (var y = 0; y < c.Height; y++)
            {
                for (var x = 0; x < c.Width; x++)
                {
                    // 軸（radius, 中央）から先端（radius + length, 中央）への線分までの距離が半径以内。
                    var px = x + .5f - radius;
                    var py = y + .5f - (c.Height / 2f);
                    var along = Mathf.Clamp(px, 0, length);
                    var dx = px - along;
                    if ((dx * dx) + (py * py) <= radius * radius) { c.Set(x, y, body); }
                }
            }

            c.Rect(Mathf.RoundToInt(radius), (c.Height / 2) + 1, Mathf.RoundToInt(length), 1, new Color32(255, 200, 150, 255));
            c.Ellipse(radius, c.Height / 2f, 2, 2, new Color32(250, 240, 220, 255));
        }

        private static void Plunger(Canvas c)
        {
            c.Rect(10, 0, 6, 40, new Color32(170, 176, 200, 255));
            for (var y = 6; y < 38; y += 4) { c.Rect(5, y, 16, 2, new Color32(110, 120, 150, 255)); }
            c.Rect(3, 40, 20, 8, new Color32(220, 70, 80, 255));
            c.Rect(3, 46, 20, 2, new Color32(255, 150, 150, 255));
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

            /// <summary>太さ 1 ピクセルの円周。</summary>
            public void Ring(float cx, float cy, float r, Color32 color)
            {
                var steps = Mathf.Max(16, (int)(r * 8));
                for (var i = 0; i < steps; i++)
                {
                    var a = i * 2 * Mathf.PI / steps;
                    Set(Mathf.FloorToInt(cx + (r * Mathf.Cos(a))), Mathf.FloorToInt(cy + (r * Mathf.Sin(a))), color);
                }
            }

            public void Line(float x0, float y0, float x1, float y1, Color32 color)
            {
                var steps = (int)Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0));
                for (var i = 0; i <= steps; i++)
                {
                    var t = steps == 0 ? 0 : (float)i / steps;
                    Set(Mathf.RoundToInt(Mathf.Lerp(x0, x1, t)), Mathf.RoundToInt(Mathf.Lerp(y0, y1, t)), color);
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

            private bool Opaque(Color32[] source, int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && source[(y * Width) + x].a != 0;
        }
    }
}
