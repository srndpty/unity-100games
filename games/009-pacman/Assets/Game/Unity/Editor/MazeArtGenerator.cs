using System;
using System.IO;
using Game.Core;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>図形を組み合わせて素材の PNG を描き出す。既にあるファイルは上書きしない。</summary>
    public static class MazeArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";
        private const int SpriteSize = 128;
        private const int BoardCellPixels = 40;
        private const int Samples = 4;

        private static readonly Color _clear = new(0, 0, 0, 0);
        private static readonly Color _ink = new(.13f, .09f, .08f);

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtRoot);
            Save("Board.png", DrawBoard, BoardCellPixels);
            Save("Mouse.png", () => Paint(Mouse), SpriteSize);
            Save("CatTora.png", () => Paint((x, y) => Cat(x, y, new Color(.93f, .58f, .2f), Tora)), SpriteSize);
            Save("CatMike.png", () => Paint((x, y) => Cat(x, y, new Color(.97f, .93f, .84f), Mike)), SpriteSize);
            Save("CatKuro.png", () => Paint((x, y) => Cat(x, y, new Color(.27f, .25f, .33f), null, new Color(.98f, .85f, .3f))), SpriteSize);
            Save("CatShiro.png", () => Paint((x, y) => Cat(x, y, new Color(.8f, .9f, .98f), null, new Color(.25f, .5f, .9f))), SpriteSize);
            Save("CatDazed.png", () => Paint(Dazed), SpriteSize);
            Save("CatReturning.png", () => Paint(Returning), SpriteSize);
            Save("Dot.png", () => Paint((x, y) => Disc(x, y, 0, 0, .9f) ? new Color(.96f, .82f, .55f) : _clear), SpriteSize);
            Save("Power.png", () => Paint(Berry), SpriteSize);
            Save("Marker.png", () => Paint(Marker), SpriteSize);
            Save("Mask.png", () => Paint((x, y) => Color.white), SpriteSize);
            AssetDatabase.Refresh();
            foreach (var file in Directory.GetFiles(ArtRoot, "*.png"))
            {
                var name = Path.GetFileName(file);
                Import(ArtRoot + name, name == "Board.png" ? BoardCellPixels : SpriteSize);
            }
        }

        private static void Save(string file, Func<Texture2D> draw, int unused)
        {
            var path = ArtRoot + file;
            if (File.Exists(path)) { return; }
            var texture = draw();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        private static void Import(string path, int pixelsPerUnit)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        // 座標は x, y とも -1〜1、y は上向き。
        private static Texture2D Paint(Func<float, float, Color> shader)
        {
            var texture = new Texture2D(SpriteSize, SpriteSize, TextureFormat.RGBA32, false);
            for (var py = 0; py < SpriteSize; py++)
            {
                for (var px = 0; px < SpriteSize; px++)
                {
                    var sum = Vector4.zero;
                    for (var s = 0; s < Samples * Samples; s++)
                    {
                        var x = (px + (((s % Samples) + .5f) / Samples)) / SpriteSize * 2 - 1;
                        var y = (py + (((s / Samples) + .5f) / Samples)) / SpriteSize * 2 - 1;
                        var color = shader(x, y);
                        sum += new Vector4(color.r * color.a, color.g * color.a, color.b * color.a, color.a);
                    }

                    sum /= Samples * Samples;
                    texture.SetPixel(px, py, sum.w > 0 ? new Color(sum.x / sum.w, sum.y / sum.w, sum.z / sum.w, sum.w) : _clear);
                }
            }

            texture.Apply();
            return texture;
        }

        private static bool Disc(float x, float y, float cx, float cy, float radius) =>
            ((x - cx) * (x - cx)) + ((y - cy) * (y - cy)) <= radius * radius;

        private static bool Oval(float x, float y, float cx, float cy, float rx, float ry) =>
            (((x - cx) / rx) * ((x - cx) / rx)) + (((y - cy) / ry) * ((y - cy) / ry)) <= 1;

        private static bool Triangle(float x, float y, Vector2 a, Vector2 b, Vector2 c)
        {
            float Side(Vector2 p, Vector2 q) => ((x - q.x) * (p.y - q.y)) - ((p.x - q.x) * (y - q.y));
            var d1 = Side(a, b);
            var d2 = Side(b, c);
            var d3 = Side(c, a);
            return !(((d1 < 0) || (d2 < 0) || (d3 < 0)) && ((d1 > 0) || (d2 > 0) || (d3 > 0)));
        }

        // 右を向いたネズミを真上から見た形。
        private static Color Mouse(float x, float y)
        {
            var body = new Color(.74f, .72f, .74f);
            var pink = new Color(.96f, .62f, .68f);
            if (Disc(x, y, .62f, .2f, .07f) || Disc(x, y, .62f, -.2f, .07f)) { return _ink; }
            if (Disc(x, y, .9f, 0, .1f)) { return pink; }
            if (Disc(x, y, .3f, .52f, .2f) || Disc(x, y, .3f, -.52f, .2f)) { return pink; }
            if (Disc(x, y, .3f, .52f, .3f) || Disc(x, y, .3f, -.52f, .3f)) { return body; }
            if (Oval(x, y, .05f, 0, .85f, .55f)) { return body; }
            var tailY = .12f * Mathf.Sin(x * 9);
            return x < -.7f && x > -1f && Mathf.Abs(y - tailY) < .05f ? pink : _clear;
        }

        private static Color Cat(float x, float y, Color fur, Func<float, float, Color?> pattern, Color? eyes = null)
        {
            var face = CatHead(x, y);
            if (!face) { return _clear; }
            var eye = eyes ?? new Color(.35f, .72f, .35f);
            if (Oval(x, y, -.34f, .08f, .17f, .2f) || Oval(x, y, .34f, .08f, .17f, .2f))
            {
                return Oval(x, y, -.34f, .08f, .06f, .17f) || Oval(x, y, .34f, .08f, .06f, .17f) ? _ink : eye;
            }

            if (Triangle(x, y, new Vector2(-.09f, -.2f), new Vector2(.09f, -.2f), new Vector2(0, -.32f))) { return new Color(.92f, .5f, .55f); }
            return pattern?.Invoke(x, y) ?? fur;
        }

        private static bool CatHead(float x, float y) =>
            Oval(x, y, 0, -.1f, .82f, .72f)
            || Triangle(x, y, new Vector2(-.8f, .1f), new Vector2(-.72f, .92f), new Vector2(-.15f, .55f))
            || Triangle(x, y, new Vector2(.8f, .1f), new Vector2(.72f, .92f), new Vector2(.15f, .55f));

        private static Color? Tora(float x, float y)
        {
            var stripe = new Color(.62f, .32f, .1f);
            var onForehead = y > .3f && Mathf.Abs(x) < .34f && Mathf.Repeat(x + .06f, .24f) < .1f;
            var onCheek = Mathf.Abs(x) > .52f && Mathf.Repeat(y + .06f, .26f) < .09f;
            return onForehead || onCheek ? stripe : null;
        }

        private static Color? Mike(float x, float y)
        {
            if (Disc(x, y, -.55f, .5f, .42f)) { return new Color(.9f, .52f, .17f); }
            return Disc(x, y, .58f, .45f, .4f) ? new Color(.2f, .17f, .17f) : null;
        }

        // マタタビで酔った猫。渦巻きの目と赤い頬。
        private static Color Dazed(float x, float y)
        {
            if (!CatHead(x, y)) { return _clear; }
            foreach (var side in new[] { -.34f, .34f })
            {
                var radius = Mathf.Sqrt(((x - side) * (x - side)) + ((y - .08f) * (y - .08f)));
                if (radius < .2f) { return Mathf.Repeat((radius * 22) + (Mathf.Atan2(y - .08f, x - side) / Mathf.PI), 2) < 1 ? _ink : Color.white; }
            }

            if (Oval(x, y, -.52f, -.26f, .17f, .1f) || Oval(x, y, .52f, -.26f, .17f, .1f)) { return new Color(.95f, .5f, .6f); }
            return new Color(.74f, .62f, .92f);
        }

        // 寝床へ帰る猫。半透明で目を閉じている。
        private static Color Returning(float x, float y)
        {
            if (!CatHead(x, y)) { return _clear; }
            var closed = Mathf.Abs(y - .08f) < .035f && (Mathf.Abs(x + .34f) < .17f || Mathf.Abs(x - .34f) < .17f);
            return closed ? new Color(.2f, .24f, .36f, .9f) : new Color(.72f, .82f, .95f, .45f);
        }

        private static Color Berry(float x, float y)
        {
            if (Triangle(x, y, new Vector2(-.1f, .55f), new Vector2(.62f, .95f), new Vector2(.28f, .42f))) { return new Color(.36f, .68f, .3f); }
            if (Disc(x, y, -.24f, .22f, .16f)) { return new Color(.86f, .95f, .62f); }
            return Disc(x, y, 0, -.08f, .72f) ? new Color(.55f, .8f, .28f) : _clear;
        }

        private static Color Marker(float x, float y)
        {
            var radius = Mathf.Sqrt((x * x) + (y * y));
            var ring = radius > .68f && radius < .9f;
            var cross = (Mathf.Abs(x) < .09f || Mathf.Abs(y) < .09f) && radius < .55f;
            return ring || cross ? Color.white : _clear;
        }

        // 迷路の定義から盤面の画像を作る。Core の迷路と食い違わない。
        private static Texture2D DrawBoard()
        {
            var layout = MazeLayout.CreateDefault();
            var floor = new Color(.12f, .09f, .08f);
            var house = new Color(.2f, .14f, .13f);
            var shelf = new Color(.42f, .27f, .16f);
            var edge = new Color(.72f, .5f, .3f);
            var door = new Color(.93f, .62f, .68f);
            const int Border = 5;
            var texture = new Texture2D(layout.Width * BoardCellPixels, layout.Height * BoardCellPixels, TextureFormat.RGBA32, false);
            for (var py = 0; py < texture.height; py++)
            {
                for (var px = 0; px < texture.width; px++)
                {
                    var cx = px / BoardCellPixels;
                    var cy = layout.Height - 1 - (py / BoardCellPixels);
                    var ix = px % BoardCellPixels;
                    var iy = BoardCellPixels - 1 - (py % BoardCellPixels);
                    var kind = layout.Kind(cx, cy);
                    Color color;
                    if (kind == CellKind.Wall)
                    {
                        var nearOpen = (ix < Border && layout.Kind(cx - 1, cy) != CellKind.Wall && cx > 0)
                            || (ix >= BoardCellPixels - Border && layout.Kind(cx + 1, cy) != CellKind.Wall && cx < layout.Width - 1)
                            || (iy < Border && layout.Kind(cx, cy - 1) != CellKind.Wall)
                            || (iy >= BoardCellPixels - Border && layout.Kind(cx, cy + 1) != CellKind.Wall);
                        color = nearOpen ? edge : shelf;
                    }
                    else if (kind == CellKind.Door)
                    {
                        color = iy > 14 && iy < 26 ? door : house;
                    }
                    else
                    {
                        color = kind == CellKind.House ? house : floor;
                    }

                    texture.SetPixel(px, py, color);
                }
            }

            texture.Apply();
            return texture;
        }
    }
}
