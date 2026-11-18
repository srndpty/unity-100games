using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>図形を組み合わせて素材の PNG を描き出す。既にあるファイルは上書きしない。</summary>
    public static class HarborShiftArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";
        public const string FloorFile = "Floor.png";
        public const string WallFile = "Wall.png";
        public const string GoalFile = "Goal.png";
        public const string BoxFile = "Box.png";
        public const string BoxPlacedFile = "BoxPlaced.png";
        public const string PlayerFile = "Player.png";
        public const string BackdropFile = "Backdrop.png";
        private const int SpriteSize = 256;
        private const int Samples = 3;

        private static readonly Color _clear = new(0, 0, 0, 0);
        private static readonly Color _wood = new(.70f, .47f, .24f);
        private static readonly Color _teal = new(.20f, .58f, .46f);

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtRoot);
            var created = new List<string>();
            Save(created, FloorFile, Floor);
            Save(created, WallFile, Wall);
            Save(created, GoalFile, Goal);
            Save(created, BoxFile, (x, y) => Crate(x, y, _wood));
            Save(created, BoxPlacedFile, BoxPlaced);
            Save(created, PlayerFile, Player);
            Save(created, BackdropFile, Backdrop);
            AssetDatabase.Refresh();

            // 既存の素材のインポート設定は調整を保つため触らない。
            foreach (var path in created) { Import(path); }
        }

        private static void Save(List<string> created, string file, Func<float, float, Color> shader)
        {
            var path = ArtRoot + file;
            if (File.Exists(path)) { return; }
            var texture = Paint(shader);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            created.Add(path);
        }

        private static void Import(string path)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = SpriteSize;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
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

        // 角丸の四角形までの符号付き距離。内側が負。
        private static float RoundedBox(float x, float y, float halfSize, float radius)
        {
            var qx = Mathf.Abs(x) - halfSize + radius;
            var qy = Mathf.Abs(y) - halfSize + radius;
            var outside = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude;
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0) - radius;
        }

        // 線分までの距離。
        private static float Segment(float x, float y, Vector2 a, Vector2 b)
        {
            var p = new Vector2(x, y) - a;
            var d = b - a;
            return (p - (d * Mathf.Clamp01(Vector2.Dot(p, d) / d.sqrMagnitude))).magnitude;
        }

        // 継ぎ目が目立たない板張りの床。
        private static Color Floor(float x, float y)
        {
            var baseColor = new Color(.40f, .36f, .30f);
            var seam = Mathf.Abs(Mathf.Repeat(y + 1, .5f) - .25f) > .235f;
            if (seam) { return Color.Lerp(baseColor, Color.black, .28f); }
            var grain = Mathf.Sin((x * 9f) + (Mathf.Floor((y + 1) * 2) * 2.1f)) * .035f;
            return Color.Lerp(baseColor, Color.white, grain + .04f);
        }

        // 積み上げたコンテナの壁。縦の波板と上辺の明るい縁。
        private static Color Wall(float x, float y)
        {
            var steel = new Color(.17f, .30f, .40f);
            if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) > .9f) { return Color.Lerp(steel, Color.black, .45f); }
            if (y > .72f) { return Color.Lerp(steel, Color.white, .24f); }
            var rib = Mathf.Sin(x * Mathf.PI * 4.5f);
            return Color.Lerp(steel, rib > 0 ? Color.white : Color.black, Mathf.Abs(rib) * .14f);
        }

        // 黄色い輪と四隅の印。箱が載っていなくても遠目に分かるようにする。
        private static Color Goal(float x, float y)
        {
            var mark = new Color(.98f, .78f, .24f);
            var radius = new Vector2(x, y).magnitude;
            if (Mathf.Abs(radius - .5f) < .085f) { return mark; }
            if (radius < .13f) { return mark; }
            var corner = Mathf.Abs(x) > .68f && Mathf.Abs(y) > .68f && Mathf.Abs(x) < .86f && Mathf.Abs(y) < .86f;
            return corner ? mark : _clear;
        }

        private static Color Crate(float x, float y, Color wood)
        {
            var distance = RoundedBox(x, y, .86f, .1f);
            if (distance > 0) { return _clear; }
            var dark = Color.Lerp(wood, Color.black, .42f);
            if (distance > -.06f) { return dark; }
            if (distance > -.26f) { return Color.Lerp(wood, Color.white, .14f); }
            if (distance > -.31f) { return dark; }
            if (Mathf.Abs(x - y) < .13f) { return Color.Lerp(wood, Color.white, .14f); }
            if (Mathf.Abs(x - y) < .18f) { return dark; }
            return Color.Lerp(wood, Color.black, .1f);
        }

        // 色だけに頼らないよう、目標に載った箱にはチェック印を付ける。
        private static Color BoxPlaced(float x, float y)
        {
            var color = Crate(x, y, _teal);
            if (color.a == 0) { return color; }
            var check = Mathf.Min(
                Segment(x, y, new Vector2(-.42f, -.02f), new Vector2(-.12f, -.34f)),
                Segment(x, y, new Vector2(-.12f, -.34f), new Vector2(.46f, .36f)));
            return check < .11f ? new Color(.96f, 1f, .95f) : color;
        }

        // 真上から見た作業員。ヘルメットのつばが向きを示す。
        private static Color Player(float x, float y)
        {
            var helmet = new Color(.96f, .50f, .14f);
            var radius = new Vector2(x, y).magnitude;
            if (radius < .4f) { return Color.Lerp(helmet, Color.white, Mathf.Clamp01(y + .1f) * .3f); }
            if (Mathf.Abs(x) < .3f && y > 0 && y < .62f) { return Color.Lerp(helmet, Color.black, .3f); }
            var shoulders = new Vector2(x / .78f, y / .5f).magnitude;
            if (shoulders < 1f) { return shoulders > .86f ? new Color(.05f, .11f, .2f) : new Color(.13f, .27f, .5f); }
            return _clear;
        }

        // 夜の港の海面。
        private static Color Backdrop(float x, float y)
        {
            var depth = Color.Lerp(new Color(.03f, .08f, .13f), new Color(.05f, .17f, .24f), (y + 1) / 2f);
            var wave = Mathf.Sin((y * 42f) + (Mathf.Sin(x * 7f) * 1.6f));
            return wave > .93f ? Color.Lerp(depth, Color.white, .07f) : depth;
        }
    }
}
