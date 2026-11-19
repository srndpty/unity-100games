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
    public static class SporeBloomArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";
        public const string BackdropFile = "Backdrop.png";
        public const string BedTileFile = "BedTile.png";
        public const string SolidFile = "Solid.png";
        private const int SpriteSize = 256;
        private const int BackdropSize = 512;
        private const int Samples = 3;
        private const float SporeRadius = .9f;

        private static readonly Color _clear = new(0, 0, 0, 0);

        public static readonly Cell[] SporeCells = { Cell.Ember, Cell.Moss, Cell.Tide, Cell.Pollen, Cell.Ash };

        public static string SporeFile(Cell cell) => $"Spore{cell}.png";

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtRoot);
            var created = new List<(string Path, int Size)>();
            foreach (var cell in SporeCells) { Save(created, SporeFile(cell), SpriteSize, (x, y) => Spore(x, y, cell)); }
            Save(created, BackdropFile, BackdropSize, Backdrop);
            Save(created, BedTileFile, SpriteSize, BedTile);
            Save(created, SolidFile, SpriteSize, (_, _) => Color.white);
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

            // 並べて敷く素材があるので、メッシュは矩形にする。
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

        private static Color BaseColor(Cell cell) => cell switch
        {
            Cell.Ember => new Color(.86f, .36f, .24f),
            Cell.Moss => new Color(.38f, .70f, .32f),
            Cell.Tide => new Color(.26f, .55f, .85f),
            Cell.Pollen => new Color(.92f, .76f, .26f),
            _ => new Color(.47f, .46f, .44f),
        };

        // 色が見分けにくくても種類が分かるよう、胞子ごとに模様を変える。
        private static float Pattern(float x, float y, Cell cell)
        {
            var radius = new Vector2(x, y).magnitude;
            var angle = Mathf.Atan2(y, x);
            switch (cell)
            {
                case Cell.Ember: return Mathf.Sin(angle * 6f) > 0 && radius > .25f ? 1 : -.3f;
                case Cell.Moss:
                    var dx = Mathf.Repeat(x + 1, .44f) - .22f;
                    var dy = Mathf.Repeat(y + 1, .44f) - .22f;
                    return (dx * dx) + (dy * dy) < .013f ? 1 : -.3f;
                case Cell.Tide: return Mathf.Sin((y * 13f) + (Mathf.Sin(x * 5f) * 1.4f)) > .2f ? 1 : -.3f;
                case Cell.Pollen: return radius < .24f || Mathf.Abs(Mathf.Sin(angle * 2.5f)) * radius > .34f ? 1 : -.3f;
                default: return Mathf.Abs(Mathf.Sin((x * 9f) + (y * 4f))) < .12f || Mathf.Abs(Mathf.Sin((x * 3f) - (y * 8f))) < .1f ? -1 : .1f;
            }
        }

        private static Color Spore(float x, float y, Cell cell)
        {
            var radius = new Vector2(x, y).magnitude;
            if (radius > SporeRadius) { return _clear; }

            var color = BaseColor(cell);
            var rim = radius > SporeRadius - .12f;
            var shade = rim ? -.42f : Pattern(x, y, cell) * .2f;

            // 左上から光が当たる丸み。
            shade += Mathf.Clamp((y - x) * .12f, -.12f, .12f);
            return shade >= 0 ? Color.Lerp(color, Color.white, shade) : Color.Lerp(color, Color.black, -shade);
        }

        // 湿った苗床の暗い背景。
        private static Color Backdrop(float x, float y)
        {
            var edge = Mathf.Clamp01(1.3f - (new Vector2(x * .8f, y).magnitude * .8f));
            var grain = Mathf.Repeat(Mathf.Sin((Mathf.Floor(x * 120f) * 12.9898f) + (Mathf.Floor(y * 120f) * 78.233f)) * 43758.547f, 1f);
            var color = Color.Lerp(new Color(.03f, .06f, .05f), new Color(.08f, .15f, .11f), edge);
            return grain > .97f ? Color.Lerp(color, new Color(.35f, .5f, .38f), .35f) : color;
        }

        // 盤面のマスの目安の線。
        private static Color BedTile(float x, float y)
        {
            var onLine = Mathf.Abs(x) > .96f || Mathf.Abs(y) > .96f;
            return onLine ? new Color(.13f, .2f, .16f) : new Color(.06f, .1f, .08f);
        }
    }
}
