#nullable enable
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.Unity.Editor
{
    /// <summary>
    /// フィールド地形・主人公4方向歩行・町人・宝箱・鉄格子・戦闘背景・敵6体のドット絵 PNG を生成しインポート設定を整える。
    /// </summary>
    public static class EmberlockArtGenerator
    {
        public const string ArtDir = "Assets/Game/Unity/Art/";

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtDir);
            var createdAny = false;

            createdAny |= WriteIfMissing("TileWall.png", 16, 16, DrawWall);
            createdAny |= WriteIfMissing("TileFloor.png", 16, 16, DrawFloor);
            createdAny |= WriteIfMissing("TileGrass.png", 16, 16, DrawGrass);
            createdAny |= WriteIfMissing("TileBridge.png", 16, 16, DrawBridge);
            createdAny |= WriteIfMissing("TileStairs.png", 16, 16, DrawStairs);
            createdAny |= WriteIfMissing("TileWater.png", 16, 16, DrawWater);
            createdAny |= WriteIfMissing("ChestClosed.png", 16, 16, (t) => DrawChest(t, opened: false));
            createdAny |= WriteIfMissing("ChestOpen.png", 16, 16, (t) => DrawChest(t, opened: true));
            createdAny |= WriteIfMissing("DoorClosed.png", 16, 16, DrawIronDoor);

            createdAny |= WriteIfMissing("HeroDown0.png", 16, 16, t => DrawPerson(t, new Color32(55, 125, 235, 255), new Color32(235, 195, 90, 255), dir: 0, step: 0));
            createdAny |= WriteIfMissing("HeroDown1.png", 16, 16, t => DrawPerson(t, new Color32(55, 125, 235, 255), new Color32(235, 195, 90, 255), dir: 0, step: 1));
            createdAny |= WriteIfMissing("HeroLeft0.png", 16, 16, t => DrawPerson(t, new Color32(55, 125, 235, 255), new Color32(235, 195, 90, 255), dir: 1, step: 0));
            createdAny |= WriteIfMissing("HeroLeft1.png", 16, 16, t => DrawPerson(t, new Color32(55, 125, 235, 255), new Color32(235, 195, 90, 255), dir: 1, step: 1));
            createdAny |= WriteIfMissing("HeroRight0.png", 16, 16, t => DrawPerson(t, new Color32(55, 125, 235, 255), new Color32(235, 195, 90, 255), dir: 2, step: 0));
            createdAny |= WriteIfMissing("HeroRight1.png", 16, 16, t => DrawPerson(t, new Color32(55, 125, 235, 255), new Color32(235, 195, 90, 255), dir: 2, step: 1));
            createdAny |= WriteIfMissing("HeroUp0.png", 16, 16, t => DrawPerson(t, new Color32(55, 125, 235, 255), new Color32(235, 195, 90, 255), dir: 3, step: 0));
            createdAny |= WriteIfMissing("HeroUp1.png", 16, 16, t => DrawPerson(t, new Color32(55, 125, 235, 255), new Color32(235, 195, 90, 255), dir: 3, step: 1));

            createdAny |= WriteIfMissing("NpcInn.png", 16, 16, t => DrawPerson(t, new Color32(195, 95, 55, 255), new Color32(140, 85, 50, 255), dir: 0, step: 0));
            createdAny |= WriteIfMissing("NpcPriest.png", 16, 16, t => DrawPerson(t, new Color32(225, 225, 240, 255), new Color32(235, 200, 95, 255), dir: 0, step: 0));
            createdAny |= WriteIfMissing("NpcMerchant.png", 16, 16, t => DrawPerson(t, new Color32(60, 165, 95, 255), new Color32(180, 130, 65, 255), dir: 0, step: 0));
            createdAny |= WriteIfMissing("NpcTown.png", 16, 16, t => DrawPerson(t, new Color32(135, 110, 185, 255), new Color32(195, 175, 145, 255), dir: 0, step: 0));
            createdAny |= WriteIfMissing("BossField.png", 20, 20, DrawBossField);

            createdAny |= WriteIfMissing("EnemySlime.png", 28, 28, DrawSlime);
            createdAny |= WriteIfMissing("EnemyBat.png", 32, 28, DrawBat);
            createdAny |= WriteIfMissing("EnemyHound.png", 36, 32, DrawHound);
            createdAny |= WriteIfMissing("EnemyAcolyte.png", 32, 36, DrawAcolyte);
            createdAny |= WriteIfMissing("EnemySentinel.png", 36, 40, DrawSentinel);
            createdAny |= WriteIfMissing("EnemyBossValug.png", 64, 64, DrawBossValug);
            createdAny |= WriteIfMissing("TargetArrow.png", 16, 16, DrawTargetArrow);
            createdAny |= WriteIfMissing("BattleBackdrop.png", 320, 180, DrawBattleBackdrop);

            if (createdAny)
            {
                AssetDatabase.Refresh();
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtDir.TrimEnd('/') }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                ConfigureSpriteImporter(path);
            }
        }

        private static bool WriteIfMissing(string fileName, int width, int height, System.Action<Texture2D> painter)
        {
            var path = ArtDir + fileName;
            if (File.Exists(path))
            {
                return false;
            }

            var tex = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false);
            var clear = new Color32[width * height];
            tex.SetPixels32(clear);
            painter(tex);
            tex.Apply();
            var bytes = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            File.WriteAllBytes(path, bytes);
            return true;
        }

        private static void ConfigureSpriteImporter(string path)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
            {
                return;
            }

            var dirty = false;
            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                dirty = true;
            }

            if (importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                dirty = true;
            }

            if (!Mathf.Approximately(importer.spritePixelsPerUnit, 16f))
            {
                importer.spritePixelsPerUnit = 16f;
                dirty = true;
            }

            if (importer.filterMode != FilterMode.Point)
            {
                importer.filterMode = FilterMode.Point;
                dirty = true;
            }

            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                dirty = true;
            }

            if (dirty)
            {
                importer.SaveAndReimport();
            }
        }

        private static void FillRect(Texture2D t, int x0, int y0, int w, int h, Color32 c)
        {
            for (var y = y0; y < y0 + h; y++)
            {
                if (y < 0 || y >= t.height)
                {
                    continue;
                }

                for (var x = x0; x < x0 + w; x++)
                {
                    if (x >= 0 && x < t.width)
                    {
                        t.SetPixel(x, y, c);
                    }
                }
            }
        }

        private static void DrawWall(Texture2D t)
        {
            FillRect(t, 0, 0, 16, 16, new Color32(46, 48, 64, 255));
            FillRect(t, 1, 9, 6, 6, new Color32(68, 72, 92, 255));
            FillRect(t, 8, 9, 7, 6, new Color32(62, 66, 86, 255));
            FillRect(t, 1, 1, 10, 7, new Color32(64, 68, 88, 255));
            FillRect(t, 12, 1, 3, 7, new Color32(58, 62, 82, 255));
            FillRect(t, 0, 15, 16, 1, new Color32(92, 98, 122, 255));
        }

        private static void DrawFloor(Texture2D t)
        {
            FillRect(t, 0, 0, 16, 16, new Color32(74, 66, 60, 255));
            FillRect(t, 1, 1, 14, 14, new Color32(86, 76, 68, 255));
            t.SetPixel(4, 5, new Color32(102, 92, 82, 255));
            t.SetPixel(11, 12, new Color32(64, 56, 50, 255));
        }

        private static void DrawGrass(Texture2D t)
        {
            FillRect(t, 0, 0, 16, 16, new Color32(46, 94, 58, 255));
            for (var i = 0; i < 6; i++)
            {
                var gx = ((i * 5) + 2) % 14;
                var gy = ((i * 7) + 3) % 14;
                t.SetPixel(gx, gy, new Color32(72, 136, 82, 255));
                t.SetPixel(gx + 1, gy + 1, new Color32(84, 152, 90, 255));
            }
        }

        private static void DrawBridge(Texture2D t)
        {
            FillRect(t, 0, 0, 16, 16, new Color32(116, 84, 52, 255));
            for (var y = 0; y < 16; y += 4)
            {
                FillRect(t, 0, y, 16, 1, new Color32(84, 58, 34, 255));
                FillRect(t, 1, y + 1, 14, 2, new Color32(138, 102, 64, 255));
            }
        }

        private static void DrawStairs(Texture2D t)
        {
            FillRect(t, 0, 0, 16, 16, new Color32(28, 26, 36, 255));
            for (var step = 0; step < 4; step++)
            {
                FillRect(t, 2, 2 + (step * 3), 12 - (step * 2), 2, new Color32(168, 152, 116, 255));
            }
        }

        private static void DrawWater(Texture2D t)
        {
            FillRect(t, 0, 0, 16, 16, new Color32(34, 78, 138, 255));
            FillRect(t, 2, 5, 5, 1, new Color32(92, 158, 218, 255));
            FillRect(t, 8, 11, 6, 1, new Color32(92, 158, 218, 255));
        }

        private static void DrawChest(Texture2D t, bool opened)
        {
            FillRect(t, 2, 2, 12, 9, new Color32(142, 84, 36, 255));
            FillRect(t, 2, 9, 12, opened ? 4 : 3, opened ? new Color32(42, 28, 22, 255) : new Color32(172, 108, 46, 255));
            FillRect(t, 2, 7, 12, 1, new Color32(232, 194, 78, 255));
            FillRect(t, 7, 6, 2, 3, new Color32(246, 218, 96, 255));
        }

        private static void DrawIronDoor(Texture2D t)
        {
            FillRect(t, 1, 0, 14, 16, new Color32(42, 46, 56, 255));
            for (var x = 2; x <= 13; x += 3)
            {
                FillRect(t, x, 0, 2, 16, new Color32(142, 150, 168, 255));
            }

            FillRect(t, 1, 7, 14, 2, new Color32(214, 174, 78, 255));
        }

        private static void DrawPerson(Texture2D t, Color32 tunic, Color32 hair, int dir, int step)
        {
            var legShift = step == 0 ? 0 : 1;
            FillRect(t, 5, 1 + legShift, 2, 3, new Color32(60, 48, 42, 255));
            FillRect(t, 9, 2 - legShift, 2, 3, new Color32(60, 48, 42, 255));
            FillRect(t, 4, 4, 8, 6, tunic);
            FillRect(t, 5, 10, 6, 5, new Color32(242, 206, 172, 255));
            FillRect(t, 4, 13, 8, 3, hair);
            if (dir == 0)
            {
                t.SetPixel(6, 11, new Color32(28, 28, 38, 255));
                t.SetPixel(9, 11, new Color32(28, 28, 38, 255));
            }
            else if (dir == 1)
            {
                t.SetPixel(5, 11, new Color32(28, 28, 38, 255));
            }
            else if (dir == 2)
            {
                t.SetPixel(10, 11, new Color32(28, 28, 38, 255));
            }
            else
            {
                FillRect(t, 5, 10, 6, 4, hair);
            }
        }

        private static void DrawBossField(Texture2D t)
        {
            FillRect(t, 3, 2, 14, 12, new Color32(168, 44, 42, 255));
            FillRect(t, 5, 12, 10, 6, new Color32(92, 32, 42, 255));
            FillRect(t, 3, 15, 3, 4, new Color32(238, 176, 68, 255));
            FillRect(t, 14, 15, 3, 4, new Color32(238, 176, 68, 255));
            FillRect(t, 7, 14, 2, 2, new Color32(255, 224, 92, 255));
            FillRect(t, 11, 14, 2, 2, new Color32(255, 224, 92, 255));
        }

        private static void DrawSlime(Texture2D t)
        {
            FillRect(t, 4, 3, 20, 12, new Color32(84, 96, 124, 255));
            FillRect(t, 7, 15, 14, 6, new Color32(104, 118, 150, 255));
            FillRect(t, 12, 21, 4, 4, new Color32(126, 140, 174, 255));
            FillRect(t, 9, 9, 3, 3, new Color32(248, 244, 220, 255));
            FillRect(t, 16, 9, 3, 3, new Color32(248, 244, 220, 255));
            t.SetPixel(10, 10, new Color32(220, 60, 60, 255));
            t.SetPixel(17, 10, new Color32(220, 60, 60, 255));
        }

        private static void DrawBat(Texture2D t)
        {
            FillRect(t, 2, 10, 10, 10, new Color32(118, 72, 134, 255));
            FillRect(t, 20, 10, 10, 10, new Color32(118, 72, 134, 255));
            FillRect(t, 11, 7, 10, 13, new Color32(78, 48, 96, 255));
            FillRect(t, 13, 15, 2, 2, new Color32(255, 188, 72, 255));
            FillRect(t, 17, 15, 2, 2, new Color32(255, 188, 72, 255));
        }

        private static void DrawHound(Texture2D t)
        {
            FillRect(t, 6, 4, 22, 13, new Color32(188, 72, 44, 255));
            FillRect(t, 20, 15, 11, 10, new Color32(214, 92, 52, 255));
            FillRect(t, 8, 17, 10, 5, new Color32(248, 172, 64, 255));
            FillRect(t, 26, 20, 2, 2, new Color32(255, 240, 120, 255));
            FillRect(t, 8, 1, 4, 5, new Color32(132, 46, 32, 255));
            FillRect(t, 22, 1, 4, 5, new Color32(132, 46, 32, 255));
        }

        private static void DrawAcolyte(Texture2D t)
        {
            FillRect(t, 7, 2, 18, 22, new Color32(68, 52, 108, 255));
            FillRect(t, 10, 22, 12, 11, new Color32(94, 74, 144, 255));
            FillRect(t, 12, 25, 8, 5, new Color32(24, 20, 36, 255));
            FillRect(t, 13, 27, 2, 2, new Color32(140, 235, 255, 255));
            FillRect(t, 17, 27, 2, 2, new Color32(140, 235, 255, 255));
        }

        private static void DrawSentinel(Texture2D t)
        {
            FillRect(t, 8, 3, 20, 24, new Color32(118, 128, 146, 255));
            FillRect(t, 11, 27, 14, 10, new Color32(148, 158, 178, 255));
            FillRect(t, 13, 31, 10, 2, new Color32(236, 92, 64, 255));
            FillRect(t, 3, 8, 5, 18, new Color32(92, 102, 122, 255));
            FillRect(t, 29, 6, 4, 26, new Color32(198, 206, 220, 255));
        }

        private static void DrawBossValug(Texture2D t)
        {
            FillRect(t, 12, 6, 40, 36, new Color32(138, 34, 38, 255));
            FillRect(t, 4, 18, 12, 26, new Color32(198, 72, 42, 255));
            FillRect(t, 48, 18, 12, 26, new Color32(198, 72, 42, 255));
            FillRect(t, 18, 40, 28, 16, new Color32(88, 26, 36, 255));
            FillRect(t, 10, 50, 10, 11, new Color32(242, 184, 72, 255));
            FillRect(t, 44, 50, 10, 11, new Color32(242, 184, 72, 255));
            FillRect(t, 23, 46, 5, 4, new Color32(255, 232, 96, 255));
            FillRect(t, 36, 46, 5, 4, new Color32(255, 232, 96, 255));
            FillRect(t, 20, 14, 24, 6, new Color32(252, 142, 54, 255));
        }

        private static void DrawTargetArrow(Texture2D t)
        {
            for (var row = 0; row < 8; row++)
            {
                FillRect(t, 8 - row, 4 + row, (row * 2) + 1, 2, new Color32(255, 218, 76, 255));
            }
        }

        private static void DrawBattleBackdrop(Texture2D t)
        {
            for (var y = 0; y < t.height; y++)
            {
                var k = y / (float)t.height;
                var col = y < 62
                    ? Color.Lerp(new Color(0.14f, 0.12f, 0.18f), new Color(0.24f, 0.21f, 0.28f), y / 62f)
                    : Color.Lerp(new Color(0.08f, 0.10f, 0.20f), new Color(0.18f, 0.22f, 0.36f), (k - 0.34f) / 0.66f);
                var c32 = (Color32)col;
                for (var x = 0; x < t.width; x++)
                {
                    t.SetPixel(x, y, c32);
                }
            }

            // 石柱シルエット
            FillRect(t, 24, 62, 22, 118, new Color32(26, 28, 44, 255));
            FillRect(t, 274, 62, 22, 118, new Color32(26, 28, 44, 255));
            FillRect(t, 0, 60, t.width, 3, new Color32(96, 86, 68, 255));
        }
    }
}
