#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Game.Core.Rules;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>
    /// ドット絵の PNG を作り、スプライトとして取り込む：部屋の背景（384×216）、ルール表の物・人物・出口ごとの絵
    /// （矩形の 1/5 の大きさ）、主人公 3 枚（立ち・歩き 2）、灯ったランプ、持ち物の絵（24×24）。
    /// 名前はルール表の ID から決める。既にある PNG は上書きしない。1 ドット = 部屋の 5 px（PPU 20、1 単位 = 100 px）。
    /// </summary>
    public static class AdventureArtGenerator
    {
        public const string ArtDir = "Assets/Game/Unity/Art/";
        public const int PixelsPerUnit = 20;
        public const int Dot = 5;

        private static readonly Color32 Clear = new(0, 0, 0, 0);
        private static readonly Color32 Ink = new(22, 18, 26, 255);
        private static readonly Color32 Wood = new(112, 72, 44, 255);
        private static readonly Color32 WoodDark = new(78, 48, 30, 255);
        private static readonly Color32 WoodLight = new(150, 104, 64, 255);
        private static readonly Color32 Iron = new(70, 74, 82, 255);
        private static readonly Color32 IronLight = new(120, 126, 136, 255);
        private static readonly Color32 Night = new(16, 24, 46, 255);
        private static readonly Color32 NightLight = new(34, 50, 86, 255);
        private static readonly Color32 Sea = new(24, 50, 74, 255);
        private static readonly Color32 Foam = new(196, 214, 226, 255);
        private static readonly Color32 RainColor = new(120, 140, 170, 255);
        private static readonly Color32 Stone = new(92, 92, 98, 255);
        private static readonly Color32 StoneDark = new(62, 62, 70, 255);
        private static readonly Color32 Warm = new(255, 196, 96, 255);
        private static readonly Color32 Glow = new(255, 236, 160, 255);
        private static readonly Color32 Skin = new(236, 196, 160, 255);
        private static readonly Color32 Yellow = new(232, 186, 48, 255);
        private static readonly Color32 White = new(236, 234, 226, 255);
        private static readonly Color32 Glass = new(150, 196, 190, 255);
        private static readonly Color32 Green = new(64, 120, 80, 255);
        private static readonly Color32 Red = new(176, 64, 52, 255);
        private static readonly Color32 Navy = new(40, 52, 92, 255);

        public static string BackgroundFile(string roomId) => $"BG_{roomId}.png";

        public static string HotspotFile(string hotspotId) => $"HS_{hotspotId.Replace('.', '_')}.png";

        public static string PlayerFile(int frame) => $"Player_{frame}.png";

        public const string LampLitFile = "LampLit.png";

        public static string IconFile(string itemId) => $"Icon_{itemId}.png";

        public static void GenerateIfMissing(RuleBook book)
        {
            if (book == null) { throw new ArgumentNullException(nameof(book)); }
            Directory.CreateDirectory(ArtDir);
            var created = false;
            var rooms = new Dictionary<string, Action<Texture2D>>(StringComparer.Ordinal)
            {
                ["cottage"] = CottageRoom,
                ["dock"] = DockRoom,
                ["lighthouse"] = LampRoom,
            };
            foreach (var r in book.Rooms)
            {
                if (!rooms.TryGetValue(r.Id, out var paint)) { throw new InvalidOperationException($"部屋 {r.Id} の描き方がありません"); }
                created |= Write(BackgroundFile(r.Id), 384, 216, paint);
            }

            var things = Painters();
            foreach (var h in book.Hotspots)
            {
                if (!things.TryGetValue(h.Id, out var paint)) { throw new InvalidOperationException($"物 {h.Id} の描き方がありません"); }
                created |= Write(HotspotFile(h.Id), Math.Max(1, (h.Area.Width + Dot / 2) / Dot), Math.Max(1, (h.Area.Height + Dot / 2) / Dot), paint);
            }

            var lamp = book.Hotspots[book.HotspotIndex("lighthouse.lamp")];
            created |= Write(LampLitFile, (lamp.Area.Width + Dot / 2) / Dot, (lamp.Area.Height + Dot / 2) / Dot, t => Lamp(t, true));
            for (int f = 0; f < 3; f++)
            {
                var frame = f;
                created |= Write(PlayerFile(f), 32, 64, t => Player(t, frame));
            }

            var icons = Icons();
            foreach (var i in book.Items)
            {
                if (!icons.TryGetValue(i.Id, out var paint)) { throw new InvalidOperationException($"持ち物 {i.Id} の描き方がありません"); }
                created |= Write(IconFile(i.Id), 24, 24, paint);
            }

            if (created) { AssetDatabase.Refresh(); }
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtDir.TrimEnd('/') }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                Configure(path, Path.GetFileName(path).StartsWith("Player_", StringComparison.Ordinal));
            }
        }

        private static bool Write(string file, int w, int h, Action<Texture2D> paint)
        {
            var path = ArtDir + file;
            if (File.Exists(path)) { return false; }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pixels = new Color32[w * h];
            for (int i = 0; i < pixels.Length; i++) { pixels[i] = Clear; }
            tex.SetPixels32(pixels);
            paint(tex);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            return true;
        }

        private static void Configure(string path, bool feetPivot)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer) { return; }
            var dirty = false;
            if (importer.textureType != TextureImporterType.Sprite) { importer.textureType = TextureImporterType.Sprite; dirty = true; }
            if (importer.spriteImportMode != SpriteImportMode.Single) { importer.spriteImportMode = SpriteImportMode.Single; dirty = true; }
            if (!Mathf.Approximately(importer.spritePixelsPerUnit, PixelsPerUnit)) { importer.spritePixelsPerUnit = PixelsPerUnit; dirty = true; }
            if (importer.filterMode != FilterMode.Point) { importer.filterMode = FilterMode.Point; dirty = true; }
            if (importer.textureCompression != TextureImporterCompression.Uncompressed) { importer.textureCompression = TextureImporterCompression.Uncompressed; dirty = true; }
            if (importer.mipmapEnabled) { importer.mipmapEnabled = false; dirty = true; }
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            var alignment = feetPivot ? (int)SpriteAlignment.BottomCenter : (int)SpriteAlignment.Center;
            if (settings.spriteAlignment != alignment)
            {
                settings.spriteAlignment = alignment;
                importer.SetTextureSettings(settings);
                dirty = true;
            }

            if (dirty) { importer.SaveAndReimport(); }
        }

        // ---- 部屋 ----

        private static void CottageRoom(Texture2D t)
        {
            Rect(t, 0, 0, t.width, t.height, WoodDark);
            for (int x = 0; x < t.width; x += 12)
            {
                Rect(t, x, 46, 11, t.height - 46, x / 12 % 2 == 0 ? Wood : new Color32(104, 66, 40, 255));
                Rect(t, x + 11, 46, 1, t.height - 46, WoodDark);
            }

            Rect(t, 0, 186, t.width, 10, WoodDark);
            Rect(t, 0, 196, t.width, 20, new Color32(60, 38, 26, 255));
            Rect(t, 0, 0, t.width, 46, new Color32(84, 58, 40, 255));
            for (int x = 0; x < t.width; x += 24) { Rect(t, x, 0, 1, 46, new Color32(66, 44, 30, 255)); }
            Rect(t, 0, 45, t.width, 2, WoodDark);

            // 天井から下がるランタン（小屋の明かり）
            Rect(t, 190, 170, 1, 26, Iron);
            Rect(t, 186, 160, 9, 10, Iron);
            Rect(t, 187, 161, 7, 8, Warm);
        }

        private static void DockRoom(Texture2D t)
        {
            for (int y = 0; y < t.height; y++)
            {
                var k = y / (float)t.height;
                Rect(t, 0, y, t.width, 1, Lerp(NightLight, Night, k));
            }

            // 遠くの町の灯と消えた灯台
            for (int x = 230; x < 300; x += 9) { Px(t, x, 100, Warm); }
            Rect(t, 40, 96, 8, 40, StoneDark);
            Rect(t, 38, 136, 12, 6, Ink);

            Rect(t, 0, 84, t.width, 18, Sea);
            for (int x = 0; x < t.width; x += 7) { Px(t, x, 100 - x % 3, Foam); }
            Rain(t, 300);
            Rect(t, 0, 0, t.width, 46, WoodDark);
            for (int x = 0; x < t.width; x += 16)
            {
                Rect(t, x, 4, 15, 38, Wood);
                Rect(t, x + 2, 8, 1, 30, WoodLight);
            }

            Rect(t, 0, 44, t.width, 2, Ink);
        }

        private static void LampRoom(Texture2D t)
        {
            Rect(t, 0, 0, t.width, t.height, StoneDark);
            for (int y = 46; y < t.height; y += 10)
            {
                for (int x = (y / 10 % 2) * 10; x < t.width; x += 20)
                {
                    Rect(t, x, y, 19, 9, Stone);
                }
            }

            Rect(t, 0, 0, t.width, 46, Iron);
            for (int x = 0; x < t.width; x += 8) { Rect(t, x, 0, 1, 46, IronLight); }
            Rect(t, 0, 46, t.width, 3, Ink);

            // 手すり
            Rect(t, 0, 70, t.width, 2, IronLight);
            for (int x = 4; x < t.width; x += 18) { Rect(t, x, 46, 2, 24, IronLight); }
        }

        // ---- 物・人物・出口 ----

        private static Dictionary<string, Action<Texture2D>> Painters() => new(StringComparer.Ordinal)
        {
            ["cottage.door"] = t => Door(t, Wood, false),
            ["cottage.tower_door"] = t => Door(t, new Color32(90, 70, 60, 255), true),
            ["cottage.window"] = t => Window(t, false),
            ["cottage.table"] = Table,
            ["cottage.knife"] = t =>
            {
                Rect(t, 0, 3, 9, 4, WoodDark);
                Poly(t, IronLight, new Vector2(9, 2), new Vector2(t.width, 5), new Vector2(9, 8));
            },
            ["cottage.cup"] = t => Cup(t, false),
            ["cottage.shelf"] = Shelf,
            ["cottage.tea_tin"] = t => Tin(t),
            ["cottage.bottle"] = t => Bottle(t, false),
            ["cottage.stove"] = Stove,
            ["dock.path"] = Trail,
            ["dock.net"] = Net,
            ["dock.barrel"] = Barrel,
            ["dock.captain"] = Captain,
            ["dock.boat"] = Boat,
            ["dock.sea"] = SeaRocks,
            ["lighthouse.stairs"] = Stairs,
            ["lighthouse.window"] = t => Window(t, true),
            ["lighthouse.lamp"] = t => Lamp(t, false),
            ["lighthouse.logbook"] = Logbook,
        };

        private static void Door(Texture2D t, Color32 wood, bool banded)
        {
            Rect(t, 0, 0, t.width, t.height, Ink);
            Rect(t, 2, 0, t.width - 4, t.height - 2, wood);
            for (int x = 6; x < t.width - 2; x += 6) { Rect(t, x, 0, 1, t.height - 2, WoodDark); }
            if (banded)
            {
                Rect(t, 2, t.height / 4, t.width - 4, 3, Iron);
                Rect(t, 2, t.height * 3 / 4, t.width - 4, 3, Iron);
                Rect(t, t.width - 10, t.height / 2 - 4, 5, 8, Iron);
                Rect(t, t.width - 8, t.height / 2 - 2, 1, 3, Ink);
            }
            else
            {
                Rect(t, t.width - 7, t.height / 2, 3, 3, Yellow);
            }
        }

        private static void Window(Texture2D t, bool large)
        {
            Rect(t, 0, 0, t.width, t.height, WoodDark);
            Rect(t, 3, 3, t.width - 6, t.height - 6, Night);
            if (large)
            {
                Rect(t, 3, 3, t.width - 6, t.height / 3, Sea);
                for (int x = 4; x < t.width - 4; x += 5) { Px(t, x, t.height / 3 + (x % 2), Foam); }
                Poly(t, StoneDark, new Vector2(10, 3), new Vector2(18, 20), new Vector2(26, 3));
            }

            Rain(t, t.width * t.height / 12);
            Rect(t, t.width / 2 - 1, 3, 2, t.height - 6, WoodDark);
            Rect(t, 3, t.height / 2 - 1, t.width - 6, 2, WoodDark);
        }

        private static void Table(Texture2D t)
        {
            Rect(t, 0, t.height - 6, t.width, 6, WoodLight);
            Rect(t, 0, t.height - 7, t.width, 1, WoodDark);
            Rect(t, 4, 0, 5, t.height - 7, Wood);
            Rect(t, t.width - 9, 0, 5, t.height - 7, Wood);
            Rect(t, 4, 14, t.width - 8, 3, WoodDark);
        }

        private static void Cup(Texture2D t, bool full)
        {
            Rect(t, 2, 0, t.width - 6, t.height - 3, White);
            Rect(t, t.width - 4, 4, 3, 6, White);
            Rect(t, t.width - 3, 6, 1, 2, Clear);
            Rect(t, 3, t.height - 4, t.width - 8, 1, full ? new Color32(150, 92, 40, 255) : new Color32(180, 178, 170, 255));
        }

        private static void Shelf(Texture2D t)
        {
            Rect(t, 0, 0, t.width, 3, WoodDark);
            Rect(t, 0, 24, t.width, 3, WoodDark);
            Rect(t, 0, 0, 2, t.height, WoodDark);
            Rect(t, t.width - 2, 0, 2, t.height, WoodDark);
            Rect(t, 4, 27, 8, 14, Red);
            Rect(t, 30, 27, 10, 10, IronLight);
            Rect(t, 44, 27, 6, 16, Glass);
            Rect(t, 6, 3, 12, 8, Yellow);
            Rect(t, 36, 3, 14, 12, White);
        }

        private static void Tin(Texture2D t)
        {
            Rect(t, 1, 0, t.width - 2, t.height - 2, Green);
            Rect(t, 0, t.height - 3, t.width, 3, IronLight);
            Rect(t, 3, 6, t.width - 6, 6, White);
            Rect(t, 5, 8, t.width - 10, 2, Green);
        }

        private static void Bottle(Texture2D t, bool oil)
        {
            var body = oil ? new Color32(180, 140, 40, 255) : Glass;
            Rect(t, 1, 0, t.width - 2, t.height * 2 / 3, body);
            Rect(t, t.width / 2 - 2, t.height * 2 / 3, 4, t.height / 3 - 2, body);
            Rect(t, t.width / 2 - 2, t.height - 2, 4, 2, WoodDark);
            Rect(t, 3, 4, 1, t.height / 2, White);
        }

        private static void Stove(Texture2D t)
        {
            Rect(t, 2, 0, t.width - 4, 46, Iron);
            Rect(t, 0, 44, t.width, 4, IronLight);
            Rect(t, 8, 10, t.width - 16, 14, Ink);
            Rect(t, 10, 12, t.width - 20, 10, Warm);
            Rect(t, 4, 0, 4, 4, Ink);
            Rect(t, t.width - 8, 0, 4, 4, Ink);

            // やかんと湯気
            Ellipse(t, t.width / 2f, 54, 10, 7, IronLight);
            Rect(t, t.width / 2 + 8, 54, 6, 2, IronLight);
            Rect(t, t.width / 2 - 4, 60, 8, 2, Iron);
            for (int i = 0; i < 4; i++) { Px(t, t.width / 2 + 12 + i % 2, 58 + i * 3, White); }
        }

        private static void Trail(Texture2D t)
        {
            Poly(t, StoneDark, new Vector2(0, 0), new Vector2(12, 0), new Vector2(8, 60), new Vector2(0, 80));
            Poly(t, Stone, new Vector2(24, 0), new Vector2(t.width, 0), new Vector2(t.width, 70), new Vector2(28, 40));
            Rect(t, 14, 0, 8, 40, new Color32(96, 82, 60, 255));
            Rect(t, 16, 40, 2, 30, Wood);
            Rect(t, 10, 62, 16, 8, WoodLight);
        }

        private static void Net(Texture2D t)
        {
            Poly(t, new Color32(90, 84, 60, 255), new Vector2(0, 0), new Vector2(t.width, 0), new Vector2(t.width - 8, 26), new Vector2(10, 34));
            for (int x = 0; x < t.width; x += 4) { Rect(t, x, 0, 1, 34, new Color32(60, 54, 40, 255)); }
            for (int y = 0; y < 34; y += 4) { Rect(t, 0, y, t.width, 1, new Color32(60, 54, 40, 255)); }
            Rect(t, 30, 14, 3, 2, Yellow);
            Px(t, 33, 15, Glow);
            Rect(t, 2, 34, 2, 6, Wood);
        }

        private static void Barrel(Texture2D t)
        {
            Ellipse(t, t.width / 2f, t.height / 2f, t.width / 2f, t.height / 2f + 2, Wood);
            Rect(t, 2, 0, t.width - 4, t.height, Wood);
            for (int x = 5; x < t.width - 4; x += 5) { Rect(t, x, 0, 1, t.height, WoodDark); }
            Rect(t, 1, 8, t.width - 2, 3, Iron);
            Rect(t, 1, t.height - 11, t.width - 2, 3, Iron);
            Rect(t, t.width / 2 - 2, 18, 4, 4, IronLight);
            Rect(t, t.width / 2 - 1, 14, 2, 4, Iron);
        }

        private static void Captain(Texture2D t)
        {
            // 雨合羽の船頭（寒そうに腕を組む）
            Rect(t, 8, 0, 6, 6, Ink);
            Rect(t, 18, 0, 6, 6, Ink);
            Poly(t, Yellow, new Vector2(5, 6), new Vector2(27, 6), new Vector2(24, 52), new Vector2(8, 52));
            Rect(t, 8, 30, 16, 6, new Color32(200, 156, 30, 255));
            Ellipse(t, 16, 58, 7, 8, Skin);
            Rect(t, 10, 50, 12, 6, White);
            Rect(t, 12, 59, 2, 2, Ink);
            Rect(t, 18, 59, 2, 2, Ink);
            Poly(t, Yellow, new Vector2(6, 63), new Vector2(26, 63), new Vector2(22, 73), new Vector2(10, 73));
            Rect(t, 4, 62, 24, 2, new Color32(200, 156, 30, 255));
        }

        private static void Boat(Texture2D t)
        {
            Poly(t, WoodDark, new Vector2(0, 22), new Vector2(t.width, 22), new Vector2(t.width - 14, 4), new Vector2(12, 4));
            Rect(t, 6, 18, t.width - 12, 3, WoodLight);
            Rect(t, 10, 22, t.width - 20, 2, Red);
            Rect(t, 48, 22, 2, 16, Wood);
            Rect(t, 0, 0, t.width, 5, Sea);
            for (int x = 0; x < t.width; x += 6) { Px(t, x, 4, Foam); }
        }

        private static void SeaRocks(Texture2D t)
        {
            // 背景の海の帯の上に、岩場と砕ける波だけを描く。
            Poly(t, StoneDark, new Vector2(20, 0), new Vector2(46, 0), new Vector2(38, 18), new Vector2(28, 22));
            Poly(t, StoneDark, new Vector2(80, 0), new Vector2(110, 0), new Vector2(98, 14));
            for (int x = 14; x < 52; x += 3) { Px(t, x, 2 + x % 4, Foam); }
            for (int x = 76; x < 116; x += 3) { Px(t, x, 2 + x % 3, Foam); }
        }

        private static void Stairs(Texture2D t)
        {
            Rect(t, 0, 0, t.width, t.height, Ink);
            for (int i = 0; i < 7; i++) { Rect(t, 2 + i * 2, i * 6, t.width - 4 - i * 4, 4, StoneDark); }
            Rect(t, 0, 0, 2, t.height, IronLight);
            Rect(t, t.width - 2, 0, 2, t.height, IronLight);
            Rect(t, 0, t.height - 3, t.width, 3, IronLight);
        }

        private static void Lamp(Texture2D t, bool lit)
        {
            if (lit)
            {
                Ellipse(t, t.width / 2f, t.height * 0.55f, t.width / 2f, t.height * 0.45f, new Color32(255, 220, 120, 110));
            }

            Rect(t, t.width / 2 - 16, 0, 32, 10, Iron);
            Rect(t, t.width / 2 - 20, 10, 40, 4, IronLight);
            Poly(t, lit ? Glow : new Color32(90, 110, 120, 255),
                new Vector2(t.width / 2f - 18, 14), new Vector2(t.width / 2f + 18, 14), new Vector2(t.width / 2f + 22, 70), new Vector2(t.width / 2f - 22, 70));
            for (int x = t.width / 2 - 16; x <= t.width / 2 + 16; x += 8) { Rect(t, x, 14, 1, 56, lit ? Warm : Iron); }
            Rect(t, t.width / 2 - 22, 70, 44, 5, Iron);
            Poly(t, Iron, new Vector2(t.width / 2f - 18, 75), new Vector2(t.width / 2f + 18, 75), new Vector2(t.width / 2f, 90));
            Rect(t, t.width / 2 - 1, 90, 2, 8, Iron);
            if (lit) { Ellipse(t, t.width / 2f, 40, 8, 12, White); }
        }

        private static void Logbook(Texture2D t)
        {
            Rect(t, 0, 22, t.width, 4, WoodLight);
            Rect(t, 3, 0, 4, 22, Wood);
            Rect(t, t.width - 7, 0, 4, 22, Wood);
            Rect(t, 8, 26, 24, 9, White);
            Rect(t, 19, 26, 2, 9, new Color32(170, 160, 140, 255));
            for (int y = 28; y < 34; y += 2)
            {
                Rect(t, 10, y, 7, 1, Navy);
                Rect(t, 22, y, 8, 1, Navy);
            }
        }

        private static void Player(Texture2D t, int frame)
        {
            // 灯台守の見習い：紺の外套と帽子。frame 0 は立ち、1・2 は歩き（脚を開く・閉じる）。
            var spread = frame == 1 ? 4 : frame == 2 ? -2 : 0;
            Rect(t, 11 - spread, 0, 5, 3, Ink);
            Rect(t, 17 + spread, 0, 5, 3, Ink);
            Rect(t, 12 - spread / 2, 3, 4, 14, new Color32(60, 50, 40, 255));
            Rect(t, 17 + spread / 2, 3, 4, 14, new Color32(60, 50, 40, 255));
            Poly(t, Navy, new Vector2(8, 16), new Vector2(24, 16), new Vector2(23, 44), new Vector2(9, 44));
            Rect(t, 15, 18, 2, 24, new Color32(28, 36, 70, 255));
            Rect(t, 6, 24, 3, 18, Navy);
            Rect(t, 23, 24, 3, 18, Navy);
            Rect(t, 6, 22, 3, 3, Skin);
            Rect(t, 23, 22, 3, 3, Skin);
            Ellipse(t, 16, 50, 6, 7, Skin);
            Rect(t, 18, 50, 2, 2, Ink);
            Rect(t, 9, 55, 14, 3, new Color32(30, 40, 72, 255));
            Rect(t, 11, 58, 10, 5, new Color32(30, 40, 72, 255));
            Rect(t, 20, 55, 6, 2, new Color32(30, 40, 72, 255));
        }

        // ---- 持ち物の絵 ----

        private static Dictionary<string, Action<Texture2D>> Icons() => new(StringComparer.Ordinal)
        {
            ["knife"] = t =>
            {
                Rect(t, 2, 10, 8, 4, WoodDark);
                Poly(t, IronLight, new Vector2(10, 9), new Vector2(23, 12), new Vector2(10, 15));
            },
            ["rusty_key"] = t =>
            {
                Ellipse(t, 6, 12, 4, 4, new Color32(150, 90, 50, 255));
                Ellipse(t, 6, 12, 2, 2, Clear);
                Rect(t, 10, 11, 12, 3, new Color32(150, 90, 50, 255));
                Rect(t, 17, 7, 2, 4, new Color32(150, 90, 50, 255));
                Rect(t, 20, 7, 2, 4, new Color32(150, 90, 50, 255));
            },
            ["bottle"] = t => Bottle(t, false),
            ["oil"] = t => Bottle(t, true),
            ["cup"] = t => Cup(t, false),
            ["hot_water"] = t =>
            {
                Cup(t, false);
                Rect(t, 3, t.height - 4, t.width - 8, 1, new Color32(170, 200, 220, 255));
                Px(t, 8, t.height - 1, White);
                Px(t, 12, t.height - 2, White);
            },
            ["tea_leaves"] = t =>
            {
                Ellipse(t, 12, 10, 9, 6, Green);
                Ellipse(t, 8, 12, 3, 2, new Color32(90, 150, 100, 255));
                Ellipse(t, 15, 9, 3, 2, new Color32(40, 90, 56, 255));
            },
            ["tea"] = t =>
            {
                Cup(t, true);
                Px(t, 8, t.height - 1, White);
                Px(t, 12, t.height - 2, White);
            },
            ["matches"] = t =>
            {
                Rect(t, 3, 6, 18, 10, Red);
                Rect(t, 5, 8, 14, 6, Yellow);
                Rect(t, 8, 16, 2, 6, WoodLight);
                Rect(t, 8, 21, 2, 2, Red);
                Rect(t, 13, 16, 2, 5, WoodLight);
                Rect(t, 13, 20, 2, 2, Red);
            },
        };

        // ---- 描く道具（y は下から） ----

        private static void Rain(Texture2D t, int drops)
        {
            uint s = 2463534242u;
            for (int i = 0; i < drops; i++)
            {
                s ^= s << 13;
                s ^= s >> 17;
                s ^= s << 5;
                var x = (int)(s % (uint)t.width);
                var y = (int)((s >> 12) % (uint)t.height);
                Px(t, x, y, RainColor);
                Px(t, x + 1, y + 2, RainColor);
            }
        }

        private static Color32 Lerp(Color32 a, Color32 b, float k) => Color32.Lerp(a, b, k);

        private static void Px(Texture2D t, int x, int y, Color32 c)
        {
            if (x >= 0 && y >= 0 && x < t.width && y < t.height) { t.SetPixel(x, y, c); }
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
            for (int y = Math.Max(0, (int)(cy - ry - 1)); y < Math.Min(t.height, (int)(cy + ry + 2)); y++)
            {
                for (int x = Math.Max(0, (int)(cx - rx - 1)); x < Math.Min(t.width, (int)(cx + rx + 2)); x++)
                {
                    var dx = (x + 0.5f - cx) / rx;
                    var dy = (y + 0.5f - cy) / ry;
                    if (dx * dx + dy * dy <= 1f) { t.SetPixel(x, y, c); }
                }
            }
        }

        /// <summary>多角形の内側を塗る（頂点は順に並べる）。</summary>
        private static void Poly(Texture2D t, Color32 c, params Vector2[] pts)
        {
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var p in pts)
            {
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }

            for (int y = Math.Max(0, (int)minY); y <= Math.Min(t.height - 1, (int)maxY); y++)
            {
                var sy = y + 0.5f;
                var xs = new List<float>();
                for (int i = 0; i < pts.Length; i++)
                {
                    var a = pts[i];
                    var b = pts[(i + 1) % pts.Length];
                    if ((a.y <= sy && b.y > sy) || (b.y <= sy && a.y > sy)) { xs.Add(a.x + (sy - a.y) / (b.y - a.y) * (b.x - a.x)); }
                }

                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    for (int x = Math.Max(0, Mathf.CeilToInt(xs[k] - 0.5f)); x <= Math.Min(t.width - 1, Mathf.FloorToInt(xs[k + 1] - 0.5f)); x++) { t.SetPixel(x, y, c); }
                }
            }
        }
    }
}
