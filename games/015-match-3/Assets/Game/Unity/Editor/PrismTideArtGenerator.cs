using System;
using System.Collections.Generic;
using System.IO;
using Game.Core;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>図形を組み合わせて素材の PNG を描き出す。既にあるファイルは上書きしない。</summary>
    public static class PrismTideArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";
        public const string BackdropFile = "Backdrop.png";
        public const string SlotFile = "Slot.png";
        public const string SolidFile = "Solid.png";
        public const string SelectionFile = "Selection.png";
        public const string CursorFile = "Cursor.png";
        private const int SpriteSize = 256;
        private const int BackdropSize = 512;
        private const int Samples = 3;
        private const float Rim = .1f;

        private static readonly Color _clear = new(0, 0, 0, 0);

        public static string CrystalFile(Piece piece) => $"Crystal{piece}.png";

        public static IEnumerable<Piece> AllPieces()
        {
            for (var i = 0; i < Pieces.MaxColors; i++) { yield return Pieces.Color(i); }
        }

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtRoot);
            var created = new List<(string Path, int Size)>();
            foreach (var piece in AllPieces()) { Save(created, CrystalFile(piece), SpriteSize, (x, y) => Crystal(x, y, piece)); }
            Save(created, BackdropFile, BackdropSize, Backdrop);
            Save(created, SlotFile, SpriteSize, Slot);
            Save(created, SolidFile, SpriteSize, (_, _) => Color.white);
            Save(created, SelectionFile, SpriteSize, Selection);
            Save(created, CursorFile, SpriteSize, Cursor);
            AssetDatabase.Refresh();

            // 既存の素材のインポート設定は調整を保つため触らない。
            foreach (var (path, size) in created) { Import(path, size); }
        }

        private static void Save(List<(string Path, int Size)> created, string file, int size, Func<float, float, Color> shader)
        {
            var path = ArtRoot + file;
            if (File.Exists(path)) { return; }
            var texture = Paint(size, shader);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            created.Add((path, size));
        }

        private static void Import(string path, int size)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = size;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        // 座標は x, y とも -1〜1、y は上向き。
        private static Texture2D Paint(int size, Func<float, float, Color> shader)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (var py = 0; py < size; py++)
            {
                for (var px = 0; px < size; px++)
                {
                    var sum = Vector4.zero;
                    for (var s = 0; s < Samples * Samples; s++)
                    {
                        var x = ((px + (((s % Samples) + .5f) / Samples)) / size * 2) - 1;
                        var y = ((py + (((s / Samples) + .5f) / Samples)) / size * 2) - 1;
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

        private static Color BaseColor(Piece piece) => piece switch
        {
            Piece.Coral => new Color(.93f, .36f, .40f),
            Piece.Amber => new Color(.98f, .70f, .22f),
            Piece.Kelp => new Color(.34f, .80f, .42f),
            Piece.Lagoon => new Color(.25f, .56f, .95f),
            Piece.Pearl => new Color(.94f, .90f, .86f),
            _ => new Color(.68f, .42f, .92f),
        };

        // 色が見分けにくくても種類が分かるよう、結晶ごとに形を変える。負なら内側（およその距離）。
        private static float Shape(float x, float y, Piece piece)
        {
            var ax = Mathf.Abs(x);
            var ay = Mathf.Abs(y);
            switch (piece)
            {
                case Piece.Coral: return ((ax / .78f) + (ay / .94f) - 1f) * .6f;
                case Piece.Amber: return Mathf.Max((ax * .866f) + (ay * .5f), ay) - .8f;
                case Piece.Kelp: return Mathf.Max(-.72f - y, ax - (.9f * (.86f - y) / 1.58f));
                case Piece.Lagoon: return new Vector2(x, y).magnitude - .82f;
                case Piece.Pearl:
                    var q = new Vector2(ax - .5f, ay - .5f);
                    return new Vector2(Mathf.Max(q.x, 0), Mathf.Max(q.y, 0)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0) - .26f;
                default:
                    var angle = Mathf.Atan2(y, x) - (Mathf.PI / 2f);
                    var radius = .58f + (.32f * Mathf.Pow(Mathf.Abs(Mathf.Cos(angle * 2.5f)), 3f));
                    return new Vector2(x, y).magnitude - radius;
            }
        }

        private static Color Crystal(float x, float y, Piece piece)
        {
            var distance = Shape(x, y, piece);
            if (distance > 0) { return _clear; }

            var color = BaseColor(piece);
            var rim = distance > -Rim;

            // 中心から放射状の切子面。左上の面ほど明るい。
            var facet = Mathf.Floor((Mathf.Atan2(y, x) + Mathf.PI) / (Mathf.PI / 3f));
            var shade = rim ? -.38f : (Mathf.Sin(facet * 1.7f) * .1f) + Mathf.Clamp((y - x) * .16f, -.16f, .2f);
            var glint = new Vector2(x + .3f, y - .34f).magnitude < .14f;
            if (glint) { return Color.Lerp(color, Color.white, .8f); }
            return shade >= 0 ? Color.Lerp(color, Color.white, shade) : Color.Lerp(color, Color.black, -shade);
        }

        // 深い潮だまりの背景。上から差す光の筋と細かな泡。
        private static Color Backdrop(float x, float y)
        {
            var depth = Mathf.Clamp01((y + 1f) / 2f);
            var color = Color.Lerp(new Color(.02f, .07f, .11f), new Color(.05f, .2f, .26f), depth);
            var ray = Mathf.Pow(Mathf.Max(0, Mathf.Sin((x * 7f) + (y * 2.2f))), 12f) * depth * .25f;
            color = Color.Lerp(color, new Color(.4f, .75f, .8f), ray);
            var grain = Mathf.Repeat(Mathf.Sin((Mathf.Floor(x * 140f) * 12.9898f) + (Mathf.Floor(y * 140f) * 78.233f)) * 43758.547f, 1f);
            return grain > .985f ? Color.Lerp(color, new Color(.6f, .85f, .9f), .4f) : color;
        }

        // 盤面の1マスのくぼみ。
        private static Color Slot(float x, float y)
        {
            var edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(y));
            if (edge > .96f) { return new Color(.1f, .26f, .32f); }
            return Color.Lerp(new Color(.05f, .15f, .2f), new Color(.08f, .2f, .26f), Mathf.Clamp01((y + 1f) / 2f));
        }

        // 選んだ結晶を囲む明るい枠。
        private static Color Selection(float x, float y)
        {
            var edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(y));
            return Mathf.Abs(edge - .9f) < .07f ? new Color(1f, .95f, .6f) : _clear;
        }

        // キーボードのカーソル。四隅のかぎ括弧。
        private static Color Cursor(float x, float y)
        {
            var edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(y));
            var corner = Mathf.Abs(x) > .5f && Mathf.Abs(y) > .5f;
            return corner && Mathf.Abs(edge - .92f) < .06f ? new Color(.7f, 1f, 1f) : _clear;
        }
    }
}
