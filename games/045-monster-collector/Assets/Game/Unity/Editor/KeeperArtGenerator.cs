#nullable enable
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>
    /// タイル・主人公（4 方向 × 2 コマ）・トレーナー 3 人・モンスター 6 種の正面と背中・捕獲玉・戦闘の背景のドット絵を PNG で作り、
    /// スプライトとして取り込む設定にする。既にある PNG は上書きしない（手で直した絵を残す）。
    /// </summary>
    public static class KeeperArtGenerator
    {
        public const string ArtDir = "Assets/Game/Unity/Art/";

        /// <summary>種族の ID の順（Species.txt の順）。絵の名前に使う。</summary>
        public static readonly string[] SpeciesIds = { "cinderpup", "wickmoth", "ripplet", "brinecrab", "mossling", "burrowroot" };

        public static readonly string[] TrainerIds = { "saki", "oboro", "touma" };

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtDir);
            var created = false;
            created |= Write("TileGround.png", 16, 16, t => Ground(t, new Color32(122, 156, 88, 255), new Color32(104, 140, 76, 255)));
            created |= Write("TileRoad.png", 16, 16, Road);
            created |= Write("TileFlower.png", 16, 16, Flower);
            created |= Write("TileGrassA.png", 16, 16, t => Grass(t, new Color32(54, 118, 62, 255), new Color32(86, 168, 84, 255)));
            created |= Write("TileGrassB.png", 16, 16, t => Grass(t, new Color32(46, 102, 74, 255), new Color32(74, 150, 112, 255)));
            created |= Write("TileGrassC.png", 16, 16, t => Grass(t, new Color32(70, 92, 48, 255), new Color32(132, 146, 70, 255)));
            created |= Write("TileWater.png", 16, 16, Water);
            created |= Write("TileWall.png", 16, 16, Tree);
            created |= Write("TileHouse.png", 16, 16, House);
            created |= Write("TileCounter.png", 16, 16, Counter);
            var dirs = new[] { "Up", "Right", "Down", "Left" };
            for (int d = 0; d < 4; d++)
            {
                for (int f = 0; f < 2; f++)
                {
                    var dir = d;
                    var frame = f;
                    created |= Write($"Hero{dirs[d]}{f}.png", 16, 16, t => Person(t, new Color32(214, 64, 52, 255), new Color32(60, 44, 36, 255), dir, frame));
                }
            }

            created |= Write("Trainer_saki.png", 16, 16, t => Person(t, new Color32(236, 150, 186, 255), new Color32(236, 200, 96, 255), 3, 0));
            created |= Write("Trainer_oboro.png", 16, 16, t => Person(t, new Color32(70, 120, 196, 255), new Color32(120, 120, 120, 255), 3, 0));
            created |= Write("Trainer_touma.png", 16, 16, t => Person(t, new Color32(48, 112, 64, 255), new Color32(30, 30, 36, 255), 3, 0));
            foreach (var id in SpeciesIds)
            {
                var sid = id;
                created |= Write($"Front_{id}.png", 40, 40, t => Monster(t, sid, back: false));
                created |= Write($"Back_{id}.png", 40, 40, t => Monster(t, sid, back: true));
            }

            created |= Write("Ball.png", 8, 8, Ball);
            created |= Write("Backdrop.png", 320, 180, Backdrop);
            if (created) { AssetDatabase.Refresh(); }
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtDir.TrimEnd('/') }))
            {
                Configure(AssetDatabase.GUIDToAssetPath(guid));
            }
        }

        private static bool Write(string file, int w, int h, Action<Texture2D> paint)
        {
            var path = ArtDir + file;
            if (File.Exists(path)) { return false; }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(new Color32[w * h]);
            paint(tex);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            return true;
        }

        private static void Configure(string path)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) { return; }
            var dirty = false;
            if (importer.textureType != TextureImporterType.Sprite) { importer.textureType = TextureImporterType.Sprite; dirty = true; }
            if (importer.spriteImportMode != SpriteImportMode.Single) { importer.spriteImportMode = SpriteImportMode.Single; dirty = true; }
            if (!Mathf.Approximately(importer.spritePixelsPerUnit, 16f)) { importer.spritePixelsPerUnit = 16f; dirty = true; }
            if (importer.filterMode != FilterMode.Point) { importer.filterMode = FilterMode.Point; dirty = true; }
            if (importer.textureCompression != TextureImporterCompression.Uncompressed) { importer.textureCompression = TextureImporterCompression.Uncompressed; dirty = true; }
            if (importer.mipmapEnabled) { importer.mipmapEnabled = false; dirty = true; }
            if (dirty) { importer.SaveAndReimport(); }
        }

        private static void Rect(Texture2D t, int x0, int y0, int w, int h, Color32 c)
        {
            for (int y = Math.Max(0, y0); y < Math.Min(t.height, y0 + h); y++)
            {
                for (int x = Math.Max(0, x0); x < Math.Min(t.width, x0 + w); x++) { t.SetPixel(x, y, c); }
            }
        }

        private static void Ellipse(Texture2D t, float cx, float cy, float rx, float ry, Color32 c)
        {
            for (int y = 0; y < t.height; y++)
            {
                for (int x = 0; x < t.width; x++)
                {
                    var dx = (x + 0.5f - cx) / rx;
                    var dy = (y + 0.5f - cy) / ry;
                    if (dx * dx + dy * dy <= 1f) { t.SetPixel(x, y, c); }
                }
            }
        }

        private static void Ground(Texture2D t, Color32 a, Color32 b)
        {
            Rect(t, 0, 0, 16, 16, a);
            t.SetPixel(3, 4, b);
            t.SetPixel(11, 9, b);
            t.SetPixel(7, 13, b);
        }

        private static void Road(Texture2D t)
        {
            Rect(t, 0, 0, 16, 16, new Color32(196, 170, 120, 255));
            t.SetPixel(4, 6, new Color32(170, 146, 100, 255));
            t.SetPixel(12, 11, new Color32(170, 146, 100, 255));
        }

        private static void Flower(Texture2D t)
        {
            Ground(t, new Color32(122, 156, 88, 255), new Color32(104, 140, 76, 255));
            Rect(t, 3, 9, 2, 2, new Color32(246, 120, 160, 255));
            Rect(t, 10, 4, 2, 2, new Color32(250, 230, 110, 255));
            Rect(t, 11, 12, 2, 2, new Color32(240, 240, 250, 255));
        }

        private static void Grass(Texture2D t, Color32 dark, Color32 light)
        {
            Rect(t, 0, 0, 16, 16, dark);
            for (int i = 0; i < 4; i++)
            {
                var x = 1 + i * 4;
                Rect(t, x, 2, 1, 5, light);
                Rect(t, x + 2, 9, 1, 5, light);
            }
        }

        private static void Water(Texture2D t)
        {
            Rect(t, 0, 0, 16, 16, new Color32(52, 104, 178, 255));
            Rect(t, 2, 5, 5, 1, new Color32(120, 176, 232, 255));
            Rect(t, 9, 11, 5, 1, new Color32(120, 176, 232, 255));
        }

        private static void Tree(Texture2D t)
        {
            Rect(t, 0, 0, 16, 16, new Color32(62, 92, 54, 255));
            Ellipse(t, 8f, 9.5f, 7f, 6f, new Color32(36, 86, 46, 255));
            Ellipse(t, 6f, 11f, 3f, 2.5f, new Color32(60, 122, 64, 255));
            Rect(t, 7, 1, 2, 3, new Color32(96, 66, 40, 255));
        }

        private static void House(Texture2D t)
        {
            Rect(t, 0, 0, 16, 16, new Color32(206, 188, 150, 255));
            Rect(t, 0, 9, 16, 7, new Color32(170, 70, 56, 255));
            Rect(t, 0, 9, 16, 1, new Color32(120, 48, 40, 255));
            Rect(t, 5, 2, 6, 4, new Color32(110, 150, 196, 255));
        }

        private static void Counter(Texture2D t)
        {
            Rect(t, 0, 0, 16, 16, new Color32(206, 188, 150, 255));
            Rect(t, 0, 2, 16, 7, new Color32(150, 98, 60, 255));
            Rect(t, 0, 8, 16, 2, new Color32(236, 220, 180, 255));
            Rect(t, 6, 10, 4, 4, new Color32(232, 80, 96, 255));
            Rect(t, 7, 11, 2, 2, new Color32(250, 250, 250, 255));
        }

        /// <summary>向き 0=上 1=右 2=下 3=左（Core の Direction と同じ）。上向きは後ろ姿、それ以外は顔。</summary>
        private static void Person(Texture2D t, Color32 shirt, Color32 hair, int dir, int frame)
        {
            var leg = new Color32(52, 48, 70, 255);
            var skin = new Color32(244, 206, 168, 255);
            Rect(t, 5, frame == 0 ? 1 : 2, 2, 3, leg);
            Rect(t, 9, frame == 0 ? 2 : 1, 2, 3, leg);
            Rect(t, 4, 4, 8, 6, shirt);
            Rect(t, 5, 10, 6, 5, skin);
            Rect(t, 4, 13, 8, 3, hair);
            var eye = new Color32(26, 26, 36, 255);
            switch (dir)
            {
                case 0:
                    Rect(t, 5, 10, 6, 4, hair);
                    break;
                case 1:
                    t.SetPixel(9, 11, eye);
                    break;
                case 2:
                    t.SetPixel(6, 11, eye);
                    t.SetPixel(9, 11, eye);
                    break;
                default:
                    t.SetPixel(6, 11, eye);
                    break;
            }
        }

        private static void Ball(Texture2D t)
        {
            Ellipse(t, 4f, 4f, 4f, 4f, new Color32(236, 236, 240, 255));
            Rect(t, 0, 4, 8, 4, new Color32(70, 150, 90, 255));
            Rect(t, 0, 3, 8, 1, new Color32(30, 30, 36, 255));
            Rect(t, 3, 3, 2, 2, new Color32(250, 250, 250, 255));
        }

        private static void Backdrop(Texture2D t)
        {
            // 地面は画面の下 2/3 ほどまで続け、遠くの木立の帯を挟んで空につなぐ（急に空の色にしない）。
            const int horizon = 124;
            for (int y = 0; y < t.height; y++)
            {
                var c = y < horizon
                    ? Color32.Lerp(new Color32(112, 160, 92, 255), new Color32(160, 196, 124, 255), y / (float)horizon)
                    : Color32.Lerp(new Color32(186, 220, 240, 255), new Color32(110, 168, 220, 255), (y - horizon) / (float)(t.height - horizon));
                for (int x = 0; x < t.width; x++) { t.SetPixel(x, y, c); }
            }

            for (int x = 0; x < t.width; x++)
            {
                var h = 4 + (int)(3f * Mathf.Sin(x * 0.21f) + 2f * Mathf.Sin(x * 0.53f + 1f));
                for (int y = horizon; y < horizon + h; y++) { t.SetPixel(x, y, new Color32(88, 134, 86, 255)); }
            }

            // 足場は戦闘の Stage の立ち位置（KeeperBattleStage.PlayerPosition・FoePosition の足もと）に合わせる。
            Ellipse(t, PlatformPx(KeeperBattleStage.PlayerPosition.x), PlatformPy(KeeperBattleStage.PlayerPosition.y - 2.6f), 56f, 12f, new Color32(96, 140, 78, 255));
            Ellipse(t, PlatformPx(KeeperBattleStage.FoePosition.x), PlatformPy(KeeperBattleStage.FoePosition.y - 2.1f), 50f, 10f, new Color32(96, 140, 78, 255));
        }

        /// <summary>背景（320×180、16 ピクセル = 1、中心が原点）のピクセル座標へ。</summary>
        private static float PlatformPx(float x) => x * 16f + 160f;

        private static float PlatformPy(float y) => y * 16f + 90f;

        private static void Monster(Texture2D t, string id, bool back)
        {
            var eye = new Color32(24, 22, 30, 255);
            var white = new Color32(250, 250, 244, 255);
            switch (id)
            {
                case "cinderpup":
                    {
                        var body = new Color32(236, 120, 52, 255);
                        Ellipse(t, 20f, 14f, 11f, 9f, body);
                        Ellipse(t, 20f, 25f, 8f, 7f, body);
                        Rect(t, 13, 30, 3, 5, body);
                        Rect(t, 24, 30, 3, 5, body);
                        Ellipse(t, 32f, 20f, 4f, 7f, new Color32(255, 200, 70, 255));
                        Ellipse(t, 33f, 23f, 2f, 4f, new Color32(255, 240, 150, 255));
                        Ellipse(t, 20f, 12f, 6f, 4f, new Color32(255, 214, 160, 255));
                        if (!back) { Eyes(t, 17, 24, eye, white); }
                        break;
                    }

                case "wickmoth":
                    {
                        Ellipse(t, 11f, 22f, 9f, 11f, new Color32(250, 210, 110, 255));
                        Ellipse(t, 29f, 22f, 9f, 11f, new Color32(250, 210, 110, 255));
                        Ellipse(t, 11f, 22f, 4f, 5f, new Color32(246, 120, 70, 255));
                        Ellipse(t, 29f, 22f, 4f, 5f, new Color32(246, 120, 70, 255));
                        var moth = new Color32(120, 84, 60, 255);
                        Ellipse(t, 20f, 17f, 4f, 11f, moth);

                        // 頭は胴の上に乗せ、目は頭の中に収める（羽に目がはみ出さない）。
                        Ellipse(t, 20f, 29f, 5f, 4f, moth);
                        Rect(t, 17, 32, 1, 6, moth);
                        Rect(t, 22, 32, 1, 6, moth);
                        if (!back)
                        {
                            Rect(t, 17, 28, 2, 3, white);
                            Rect(t, 21, 28, 2, 3, white);
                            Rect(t, 17, 28, 2, 2, eye);
                            Rect(t, 21, 28, 2, 2, eye);
                        }

                        break;
                    }

                case "ripplet":
                    {
                        var body = new Color32(78, 150, 226, 255);
                        Ellipse(t, 20f, 16f, 13f, 11f, body);
                        Ellipse(t, 20f, 13f, 8f, 6f, new Color32(200, 230, 250, 255));
                        Ellipse(t, 8f, 9f, 4f, 3f, body);
                        Ellipse(t, 32f, 9f, 4f, 3f, body);
                        Ellipse(t, 20f, 31f, 4f, 5f, new Color32(140, 200, 246, 255));
                        if (!back) { Eyes(t, 15, 21, eye, white); }
                        break;
                    }

                case "brinecrab":
                    {
                        var shell = new Color32(214, 74, 70, 255);
                        Ellipse(t, 20f, 15f, 13f, 8f, shell);
                        Ellipse(t, 6f, 24f, 5f, 5f, shell);
                        Ellipse(t, 34f, 24f, 5f, 5f, shell);
                        Rect(t, 4, 26, 3, 3, new Color32(250, 210, 200, 255));
                        Rect(t, 33, 26, 3, 3, new Color32(250, 210, 200, 255));
                        for (int i = 0; i < 3; i++)
                        {
                            Rect(t, 9 + i * 4, 4, 2, 5, shell);
                            Rect(t, 25 + i * 4, 4, 2, 5, shell);
                        }

                        if (!back)
                        {
                            Rect(t, 15, 22, 2, 5, shell);
                            Rect(t, 23, 22, 2, 5, shell);
                            Eyes(t, 14, 26, eye, white);
                        }

                        break;
                    }

                case "mossling":
                    {
                        var body = new Color32(92, 170, 80, 255);
                        Ellipse(t, 20f, 15f, 12f, 11f, body);
                        Ellipse(t, 20f, 12f, 7f, 5f, new Color32(184, 226, 140, 255));
                        Ellipse(t, 16f, 30f, 3f, 6f, new Color32(60, 140, 60, 255));
                        Ellipse(t, 25f, 31f, 4f, 7f, new Color32(80, 170, 70, 255));
                        Rect(t, 12, 3, 4, 3, body);
                        Rect(t, 24, 3, 4, 3, body);
                        if (!back) { Eyes(t, 15, 19, eye, white); }
                        break;
                    }

                default:
                    {
                        var body = new Color32(140, 96, 62, 255);
                        Ellipse(t, 20f, 15f, 14f, 11f, body);
                        Ellipse(t, 20f, 22f, 5f, 3f, new Color32(236, 160, 150, 255));
                        Rect(t, 10, 26, 2, 8, new Color32(96, 140, 60, 255));
                        Rect(t, 19, 26, 2, 10, new Color32(96, 140, 60, 255));
                        Rect(t, 28, 26, 2, 7, new Color32(96, 140, 60, 255));
                        Rect(t, 6, 4, 6, 3, new Color32(220, 210, 190, 255));
                        Rect(t, 28, 4, 6, 3, new Color32(220, 210, 190, 255));
                        if (!back) { Eyes(t, 15, 18, eye, white); }
                        break;
                    }
            }

            if (back)
            {
                // 背中は少し暗くして奥行きを出す。
                for (int y = 0; y < t.height; y++)
                {
                    for (int x = 0; x < t.width; x++)
                    {
                        var c = t.GetPixel(x, y);
                        if (c.a > 0f) { t.SetPixel(x, y, new Color(c.r * 0.82f, c.g * 0.82f, c.b * 0.82f, 1f)); }
                    }
                }
            }
        }

        private static void Eyes(Texture2D t, int x, int y, Color32 eye, Color32 white)
        {
            Rect(t, x, y, 3, 3, white);
            Rect(t, x + 8, y, 3, 3, white);
            Rect(t, x + 1, y, 2, 2, eye);
            Rect(t, x + 9, y, 2, 2, eye);
        }
    }
}
