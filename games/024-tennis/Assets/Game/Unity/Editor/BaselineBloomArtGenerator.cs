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
    /// 1 m = 32 ピクセル（ワールドの 1 単位 = 1 m）。コートは画素ごとに <see cref="CourtProjection.GroundFromWorld"/> で地面へ戻して塗るので、
    /// 描画の投影と線の位置が一致する。選手・球は足元（下端の中央）、影と跡は中央を基準点にする。乱数は使わず、毎回同じ絵にする。
    /// </summary>
    public static class BaselineBloomArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";
        public const int PixelsPerUnit = 32;

        public const string CourtFile = "Court.png";
        public const string NetFile = "Net.png";
        public const string BallFile = "Ball.png";
        public const string ShadowFile = "Shadow.png";
        public const string BounceMarkFile = "BounceMark.png";

        /// <summary>壁の線の外に描く余白（m）。</summary>
        private const float CourtMargin = 1.2f;

        private const float DoublesHalfWidth = 5.485f;

        private static readonly Color32 _outline = new(16, 20, 30, 255);

        /// <summary>柵の外の植え込みの色。カメラの背景と同じにして、絵の端を見せない。</summary>
        public static readonly Color32 Hedge = new(34, 82, 58, 255);

        public static string NearPoseFile(int pose) => $"Near{pose}.png";

        public static string FarPoseFile(int pose) => $"Far{pose}.png";

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtRoot);
            var created = new List<(string Path, Vector2 Pivot)>();
            var center = new Vector2(.5f, .5f);
            var feet = new Vector2(.5f, 0);
            SaveCourt(created);
            Save(created, NetFile, 416, 34, feet, Net);
            Save(created, BallFile, 8, 8, feet, Ball);
            Save(created, ShadowFile, 12, 6, center, c => c.Ellipse(6, 3, 6, 3, new Color32(10, 20, 30, 110)));
            Save(created, BounceMarkFile, 16, 8, center, BounceMark);
            for (var pose = 0; pose < BaselineBloomStage.PoseCount; pose++)
            {
                var p = pose;
                Save(created, NearPoseFile(pose), 48, 64, feet, c => Player(c, p, back: true));
                Save(created, FarPoseFile(pose), 48, 64, feet, c => Player(c, p, back: false));
            }

            AssetDatabase.Refresh();
            foreach (var (path, pivot) in created) { Import(path, pivot); }
        }

        private static void Save(List<(string, Vector2)> created, string file, int width, int height, Vector2 pivot, Action<Canvas> paint)
        {
            var path = ArtRoot + file;
            if (File.Exists(path)) { return; }
            var canvas = new Canvas(width, height);
            paint(canvas);
            Write(path, canvas);
            created.Add((path, pivot));
        }

        private static void Write(string path, Canvas canvas)
        {
            var texture = new Texture2D(canvas.Width, canvas.Height, TextureFormat.RGBA32, false);
            texture.SetPixels32(canvas.Pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        private static void Import(string path, Vector2 pivot)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PixelsPerUnit;
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

        // コート：画素の中心を地面へ戻し、どこに当たるかで塗る。原点（ネットの中央）が基準点。
        private static void SaveCourt(List<(string, Vector2)> created)
        {
            var path = ArtRoot + CourtFile;
            if (File.Exists(path)) { return; }
            var fenceX = CourtProjection.Meters(Court.FenceX) + CourtMargin;
            var fenceY = CourtProjection.Meters(Court.FenceY) + CourtMargin;
            var minX = -CourtProjection.ToWorld(fenceX, -fenceY, 0).x;
            var minY = CourtProjection.ToWorld(0, -fenceY, 0).y;
            var maxY = CourtProjection.ToWorld(0, fenceY, 0).y;
            var width = Mathf.CeilToInt(-minX * 2 * PixelsPerUnit);
            var height = Mathf.CeilToInt((maxY - minY) * PixelsPerUnit);
            var canvas = new Canvas(width, height);
            for (var py = 0; py < height; py++)
            {
                for (var px = 0; px < width; px++)
                {
                    var world = new Vector2(minX + ((px + .5f) / PixelsPerUnit), minY + ((py + .5f) / PixelsPerUnit));
                    var ground = CourtProjection.GroundFromWorld(world);
                    var color = CourtColor(ground.x, ground.y, px, py);
                    if (color.a > 0) { canvas.Set(px, py, color); }
                }
            }

            Write(path, canvas);
            created.Add((path, new Vector2(-minX / ((float)width / PixelsPerUnit), -minY / ((float)height / PixelsPerUnit))));
        }

        private static Color32 CourtColor(float x, float y, int px, int py)
        {
            var fenceX = CourtProjection.Meters(Court.FenceX);
            var fenceY = CourtProjection.Meters(Court.FenceY);
            var halfWidth = CourtProjection.Meters(Court.HalfWidth);
            var halfLength = CourtProjection.Meters(Court.HalfLength);
            var service = CourtProjection.Meters(Court.ServiceLine);
            var ax = Mathf.Abs(x);
            var ay = Mathf.Abs(y);

            // 壁の線の外は濃い緑の植え込み（カメラの背景と同じ色）、壁の線は低い柵。
            if (ax > fenceX + .25f || ay > fenceY + .25f) { return Hedge; }

            if (ax > fenceX || ay > fenceY) { return new Color32(210, 200, 170, 255); }

            // 線：横の線（ベースライン・サービスライン）は奥行きが縮むので太めに判定する。
            const float side = .06f;
            const float across = .1f;
            var line =
                (ay <= halfLength + across && (Mathf.Abs(ax - halfWidth) <= side || Mathf.Abs(ax - DoublesHalfWidth) <= side)) ||
                (ax <= DoublesHalfWidth + side && Mathf.Abs(ay - halfLength) <= across) ||
                (ax <= halfWidth + side && Mathf.Abs(ay - service) <= across) ||
                (ay <= service && ax <= side) ||
                (Mathf.Abs(ay - halfLength) <= .35f && ax <= side);
            if (line) { return new Color32(246, 246, 238, 255); }

            var inner = ax <= DoublesHalfWidth && ay <= halfLength;
            var grain = ((px * 5) + (py * 3)) % 9 == 0;
            if (inner) { return grain ? new Color32(54, 104, 168, 255) : new Color32(58, 112, 178, 255); }
            return grain ? new Color32(70, 140, 104, 255) : new Color32(76, 150, 110, 255);
        }

        // ネット：幅 13 m（両端に柱）、白い帯の上端がネットの高さ。網は細かい格子。
        private static void Net(Canvas c)
        {
            var top = Mathf.RoundToInt(CourtProjection.Meters(Court.NetHeight) * CourtProjection.HeightScale * PixelsPerUnit);
            var mesh = new Color32(20, 26, 34, 200);
            var cord = new Color32(40, 48, 60, 230);
            for (var y = 0; y < top - 2; y++)
            {
                for (var x = 6; x < c.Width - 6; x++)
                {
                    if (x % 3 == 0 || y % 3 == 0) { c.Set(x, y, cord); }
                    else { c.Set(x, y, mesh); }
                }
            }

            c.Rect(6, top - 3, c.Width - 12, 3, new Color32(250, 250, 244, 255));
            c.Rect(0, 0, 4, c.Height, new Color32(60, 64, 74, 255));
            c.Rect(c.Width - 4, 0, 4, c.Height, new Color32(60, 64, 74, 255));
            c.Rect(0, c.Height - 3, 4, 3, new Color32(200, 200, 190, 255));
            c.Rect(c.Width - 4, c.Height - 3, 4, 3, new Color32(200, 200, 190, 255));
        }

        private static void Ball(Canvas c)
        {
            c.Ellipse(4, 4, 3.2f, 3.2f, new Color32(214, 240, 70, 255));
            c.Set(3, 5, new Color32(250, 255, 190, 255));
            c.Set(5, 3, new Color32(170, 200, 50, 255));
            c.Outline(_outline);
        }

        private static void BounceMark(Canvas c)
        {
            c.Ellipse(8, 4, 7.5f, 3.6f, new Color32(250, 240, 200, 150));
            c.Ellipse(8, 4, 5.5f, 2.2f, new Color32(0, 0, 0, 0));
        }

        /// <summary>
        /// 選手。手前の選手は後ろ姿（髪）、奥の選手は正面（顔）。右利きで、ラケットは画面の右（奥の選手は左右を反転して描く）。
        /// 姿：0・1 = 構え（足を開く・閉じる）、2 = 振りかぶり（ラケットを右後ろ）、3 = 振り抜き（左上へ）、4 = トス（左手を上げ、ラケットを頭の後ろ）。
        /// </summary>
        private static void Player(Canvas c, int pose, bool back)
        {
            var skin = new Color32(238, 196, 160, 255);
            var hair = back ? new Color32(60, 44, 36, 255) : new Color32(240, 210, 120, 255);
            var shirt = back ? new Color32(70, 200, 170, 255) : new Color32(250, 130, 110, 255);
            var shirtShade = back ? new Color32(46, 150, 130, 255) : new Color32(200, 90, 80, 255);
            var shorts = new Color32(240, 240, 236, 255);
            var shoe = new Color32(60, 60, 70, 255);
            var frame = new Color32(60, 70, 90, 255);
            var strings = new Color32(220, 230, 240, 160);
            const int cx = 24;

            // 脚と靴（構え 1 は足を閉じる）。
            var spread = pose == 1 ? 2 : 4;
            c.Rect(cx - spread - 3, 2, 4, 18, skin);
            c.Rect(cx + spread - 1, 2, 4, 18, skin);
            c.Rect(cx - spread - 4, 0, 6, 3, shoe);
            c.Rect(cx + spread - 2, 0, 6, 3, shoe);

            // 胴（短パン・シャツ）と頭。
            c.Rect(cx - 7, 18, 14, 8, shorts);
            c.Rect(cx - 7, 26, 14, 16, shirt);
            c.Rect(cx - 7, 26, 3, 16, shirtShade);
            c.Ellipse(cx, 48, 6, 6.5f, skin);
            if (back)
            {
                c.Ellipse(cx, 49, 6.2f, 6.2f, hair);
            }
            else
            {
                c.Ellipse(cx, 52, 6.2f, 3.6f, hair);
                c.Set(cx - 2, 47, new Color32(30, 30, 40, 255));
                c.Set(cx + 2, 47, new Color32(30, 30, 40, 255));
            }

            c.Rect(cx - 6, 50, 12, 2, back ? new Color32(250, 250, 250, 255) : new Color32(70, 200, 170, 255));

            // 腕とラケット。右手（画面の右）にラケット。
            switch (pose)
            {
                case 2:
                    Arm(c, cx + 7, 38, cx + 14, 34, skin);
                    Arm(c, cx - 7, 38, cx - 10, 30, skin);
                    Racket(c, cx + 14, 34, cx + 20, 42, frame, strings);
                    break;
                case 3:
                    Arm(c, cx + 7, 38, cx - 2, 42, skin);
                    Arm(c, cx - 7, 38, cx - 12, 34, skin);
                    Racket(c, cx - 2, 42, cx - 14, 50, frame, strings);
                    break;
                case 4:
                    Arm(c, cx - 7, 40, cx - 10, 58, skin);
                    Arm(c, cx + 7, 40, cx + 12, 46, skin);
                    Racket(c, cx + 12, 46, cx + 8, 56, frame, strings);
                    break;
                default:
                    Arm(c, cx + 7, 38, cx + 10, 28, skin);
                    Arm(c, cx - 7, 38, cx - 6, 28, skin);
                    Racket(c, cx + 10, 28, cx + 16, 36, frame, strings);
                    break;
            }

            c.Outline(_outline);
            if (!back) { c.FlipX(); }
        }

        private static void Arm(Canvas c, int x0, int y0, int x1, int y1, Color32 color) => c.Line(x0, y0, x1, y1, 3, color);

        // ラケット：握り（手）から面の中心まで柄を引き、面は楕円の枠と網。
        private static void Racket(Canvas c, int hx, int hy, int fx, int fy, Color32 frame, Color32 strings)
        {
            c.Line(hx, hy, (hx + fx) / 2, (hy + fy) / 2, 2, frame);
            c.Ellipse(fx, fy, 4.5f, 5.5f, frame);
            c.Ellipse(fx, fy, 3.2f, 4.2f, strings);
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

            public Color32[] Pixels { get; private set; }

            public void Set(int x, int y, Color32 color)
            {
                if (x < 0 || y < 0 || x >= Width || y >= Height) { return; }
                Pixels[(y * Width) + x] = color;
            }

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

            /// <summary>太さ <paramref name="thickness"/> の線（端点を含む）。</summary>
            public void Line(int x0, int y0, int x1, int y1, int thickness, Color32 color)
            {
                var steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
                for (var i = 0; i <= steps; i++)
                {
                    var t = steps == 0 ? 0f : (float)i / steps;
                    var x = Mathf.RoundToInt(Mathf.Lerp(x0, x1, t));
                    var y = Mathf.RoundToInt(Mathf.Lerp(y0, y1, t));
                    Rect(x - (thickness / 2), y - (thickness / 2), thickness, thickness, color);
                }
            }

            /// <summary>左右を反転する。</summary>
            public void FlipX()
            {
                var flipped = new Color32[Pixels.Length];
                for (var y = 0; y < Height; y++)
                {
                    for (var x = 0; x < Width; x++) { flipped[(y * Width) + (Width - 1 - x)] = Pixels[(y * Width) + x]; }
                }

                Pixels = flipped;
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
