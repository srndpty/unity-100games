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
    public static class StrataDropArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";
        public const string BackdropFile = "Backdrop.png";
        public const string WellTileFile = "WellTile.png";
        public const string SolidFile = "Solid.png";
        private const int SpriteSize = 256;
        private const int BackdropSize = 512;
        private const int Samples = 3;

        private static readonly Color _clear = new(0, 0, 0, 0);

        // 並びは PieceKind と同じ。銅、青鉛、翡翠、紫水晶、黄鉄、紅石、氷晶。
        private static readonly Color[] _oreColors =
        {
            new(.77f, .44f, .23f),
            new(.44f, .53f, .65f),
            new(.25f, .64f, .49f),
            new(.54f, .36f, .72f),
            new(.82f, .65f, .23f),
            new(.79f, .38f, .50f),
            new(.35f, .72f, .77f),
        };

        private static readonly Color[] _strata =
        {
            new(.16f, .12f, .10f),
            new(.20f, .15f, .11f),
            new(.13f, .11f, .11f),
            new(.18f, .14f, .13f),
        };

        public static string OreFile(PieceKind kind) => $"Ore{kind}.png";

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtRoot);
            var created = new List<(string Path, int Size)>();
            for (var i = 0; i < PieceShapes.KindCount; i++)
            {
                var kind = (PieceKind)i;
                Save(created, OreFile(kind), SpriteSize, (x, y) => Ore(x, y, kind));
            }

            Save(created, BackdropFile, BackdropSize, Backdrop);
            Save(created, WellTileFile, SpriteSize, WellTile);
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

        // 角丸の四角形までの符号付き距離。内側が負。
        private static float RoundedBox(float x, float y, float halfSize, float radius)
        {
            var qx = Mathf.Abs(x) - halfSize + radius;
            var qy = Mathf.Abs(y) - halfSize + radius;
            var outside = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude;
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0) - radius;
        }

        // 色が見分けにくくても種類が分かるよう、鉱石ごとに結晶の模様を変える。
        private static float Facet(float x, float y, PieceKind kind)
        {
            switch (kind)
            {
                case PieceKind.I: return Mathf.Sin(y * 16f);
                case PieceKind.O: return Mathf.Repeat(Mathf.Abs(x) + Mathf.Abs(y), .56f) < .28f ? 1 : -1;
                case PieceKind.T: return y > Mathf.Abs(x) ? 1 : y < -Mathf.Abs(x) ? -1 : x > 0 ? .35f : -.35f;
                case PieceKind.S: return Mathf.Repeat(x + y, .64f) < .32f ? 1 : -1;
                case PieceKind.Z: return Mathf.Repeat(x - y, .64f) < .32f ? 1 : -1;
                case PieceKind.J:
                    var dx = Mathf.Repeat(x + 1, .5f) - .25f;
                    var dy = Mathf.Repeat(y + 1, .5f) - .25f;
                    return (dx * dx) + (dy * dy) < .017f ? 1 : -.4f;
                default:
                    return Mathf.Repeat(x + 1, .5f) < .12f || Mathf.Repeat(y + 1, .5f) < .12f ? 1 : -.4f;
            }
        }

        private static Color Ore(float x, float y, PieceKind kind)
        {
            var distance = RoundedBox(x, y, .95f, .2f);
            if (distance > 0) { return _clear; }

            var color = _oreColors[(int)kind];
            var shade = distance > -.16f
                ? (y - x > 0 ? .34f : -.36f)
                : Facet(x, y, kind) * .13f;
            return shade >= 0
                ? Color.Lerp(color, Color.white, shade)
                : Color.Lerp(color, Color.black, -shade);
        }

        // 波打つ地層の断面。
        private static Color Backdrop(float x, float y)
        {
            var wave = y + (.05f * Mathf.Sin((x * 3f) + 1f)) + (.025f * Mathf.Sin((x * 8f) + 2.4f));
            var band = Mathf.FloorToInt((wave + 1.2f) * 7f);
            var color = _strata[Mathf.Abs(band) % _strata.Length];

            var grain = Mathf.Repeat(Mathf.Sin((Mathf.Floor(x * 110f) * 12.9898f) + (Mathf.Floor(y * 110f) * 78.233f)) * 43758.547f, 1f);
            if (grain > .965f) { color = Color.Lerp(color, new Color(.55f, .47f, .36f), .35f); }

            var edge = Mathf.Clamp01(1.25f - (new Vector2(x * .8f, y).magnitude * .75f));
            return Color.Lerp(Color.black, color, .55f + (edge * .45f));
        }

        // 坑道の奥の暗がりと、マスの目安の線。
        private static Color WellTile(float x, float y)
        {
            var onLine = Mathf.Abs(x) > .95f || Mathf.Abs(y) > .95f;
            return onLine ? new Color(.17f, .16f, .17f) : new Color(.075f, .07f, .08f);
        }
    }
}
