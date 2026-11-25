using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Unity.Editor
{
    /// <summary>
    /// 登場者の姿（5 種 × 9 姿勢）、路地の背景、地面、影、エフェクトのドット絵を生成する。既にあるファイルは上書きしない。
    /// 姿は足元中央を原点にした 72 × 88 px、1 px = 1 ワールド単位。
    /// </summary>
    public static class SootArtGenerator
    {
        public const string ArtRoot = "Assets/Game/Unity/Art/";
        public const int FigureWidth = 72;
        public const int FigureHeight = 88;
        public const int BackdropWidth = 2112;
        public const int BackdropHeight = 160;
        public const int GroundWidth = 2112;
        public const int GroundHeight = 80;

        /// <summary>地面の画像の上端（奥の端）からワールドの y = 0（手前の端）までの高さ。</summary>
        public const int GroundFront = 24;

        public static readonly string[] Kinds = { "Player", "Grunt", "Brute", "Kicker", "Boss" };

        public static readonly Color32 Night = new(20, 16, 26, 255);

        public static string FigureFile(int kind, int pose) => Kinds[kind] + SootStage.PoseNames[pose] + ".png";

        public static void GenerateIfMissing()
        {
            Directory.CreateDirectory(ArtRoot);
            var created = new List<(string Path, Vector2 Pivot)>();
            var feet = new Vector2(0.5f, 0f);
            for (var kind = 0; kind < Kinds.Length; kind++)
            {
                for (var pose = 0; pose < SootStage.PoseCount; pose++)
                {
                    var k = kind;
                    var p = pose;
                    Save(created, FigureFile(kind, pose), FigureWidth, FigureHeight, feet, c => Figure(c, k, p));
                }
            }

            Save(created, "Backdrop.png", BackdropWidth, BackdropHeight, Vector2.zero, Backdrop);
            Save(created, "Ground.png", GroundWidth, GroundHeight, Vector2.zero, Ground);
            Save(created, "Shadow.png", 32, 10, new Vector2(0.5f, 0.5f), Shadow);
            Save(created, "Flare.png", 40, 40, new Vector2(0.5f, 0.5f), Flare);
            Save(created, "Whirl.png", 96, 64, new Vector2(0.5f, 0.5f), Whirl);
            Save(created, "Lamp.png", 12, 96, new Vector2(0.5f, 0f), Lamp);
            Save(created, "Food.png", 22, 14, new Vector2(0.5f, 0.2f), Food);
            AssetDatabase.Refresh();
            foreach (var (path, pivot) in created) { Import(path, pivot); }
        }

        private static void Save(List<(string, Vector2)> created, string file, int width, int height, Vector2 pivot, Action<Canvas> paint)
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
            created.Add((path, pivot));
        }

        private static void Import(string path, Vector2 pivot)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 1;
            importer.filterMode = FilterMode.Point;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 4096;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = pivot;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        // ---- 登場者 ----

        private readonly struct Look
        {
            public Look(float scale, float girth, Color32 skin, Color32 hair, Color32 shirt, Color32 pants, Color32 accent, int hairStyle)
            {
                Scale = scale;
                Girth = girth;
                Skin = skin;
                Hair = hair;
                Shirt = shirt;
                Pants = pants;
                Accent = accent;
                HairStyle = hairStyle;
            }

            public float Scale { get; }

            public float Girth { get; }

            public Color32 Skin { get; }

            public Color32 Hair { get; }

            public Color32 Shirt { get; }

            public Color32 Pants { get; }

            public Color32 Accent { get; }

            public int HairStyle { get; }
        }

        private static Look LookOf(int kind)
        {
            switch (kind)
            {
                case 0: return new Look(1f, 1f, new Color32(232, 188, 148, 255), new Color32(60, 38, 28, 255), new Color32(236, 232, 220, 255), new Color32(48, 78, 140, 255), new Color32(214, 40, 40, 255), 0);
                case 1: return new Look(0.97f, 0.95f, new Color32(214, 170, 130, 255), new Color32(150, 60, 190, 255), new Color32(158, 176, 64, 255), new Color32(88, 88, 96, 255), new Color32(40, 40, 40, 255), 1);
                case 2: return new Look(1.13f, 1.45f, new Color32(196, 150, 112, 255), new Color32(196, 150, 112, 255), new Color32(118, 70, 40, 255), new Color32(46, 42, 52, 255), new Color32(170, 170, 176, 255), 2);
                case 3: return new Look(1f, 0.9f, new Color32(226, 182, 140, 255), new Color32(30, 30, 34, 255), new Color32(56, 168, 190, 255), new Color32(28, 28, 32, 255), new Color32(236, 210, 60, 255), 3);
                default: return new Look(1.2f, 1.35f, new Color32(204, 160, 124, 255), new Color32(170, 170, 176, 255), new Color32(138, 28, 40, 255), new Color32(24, 22, 28, 255), new Color32(150, 156, 168, 255), 4);
            }
        }

        /// <summary>姿勢ごとの関節の角度（度、下向きが 0、前が正）。腕は肩から、脚は腰から。</summary>
        private readonly struct Pose
        {
            public Pose(float lean, float frontArm, float frontElbow, float backArm, float backElbow, float frontLeg, float frontKnee, float backLeg, float backKnee, float crouch)
            {
                Lean = lean;
                FrontArm = frontArm;
                FrontElbow = frontElbow;
                BackArm = backArm;
                BackElbow = backElbow;
                FrontLeg = frontLeg;
                FrontKnee = frontKnee;
                BackLeg = backLeg;
                BackKnee = backKnee;
                Crouch = crouch;
            }

            public float Lean { get; }

            public float FrontArm { get; }

            public float FrontElbow { get; }

            public float BackArm { get; }

            public float BackElbow { get; }

            public float FrontLeg { get; }

            public float FrontKnee { get; }

            public float BackLeg { get; }

            public float BackKnee { get; }

            public float Crouch { get; }
        }

        private static Pose PoseOf(int pose)
        {
            switch (pose)
            {
                case SootStage.Stand: return new Pose(0f, 30f, 70f, -20f, 60f, 8f, 0f, -8f, 0f, 0f);
                case SootStage.Walk: return new Pose(4f, -25f, 30f, 30f, 50f, 28f, -10f, -24f, -20f, 1f);
                case SootStage.Windup: return new Pose(-8f, -40f, 120f, 40f, 80f, 22f, 0f, -22f, -10f, 3f);
                case SootStage.Strike: return new Pose(10f, 92f, 0f, -30f, 70f, 26f, -6f, -26f, -6f, 2f);
                case SootStage.Hurt: return new Pose(-22f, 150f, -30f, 120f, -20f, 18f, -12f, -10f, -14f, 2f);
                case SootStage.Air: return new Pose(0f, 120f, 30f, 100f, 40f, 50f, -80f, 10f, -70f, 0f);
                case SootStage.Kick: return new Pose(-14f, -40f, 60f, 60f, 40f, 92f, 0f, -20f, -80f, 0f);
                case SootStage.Hold: return new Pose(4f, 78f, 20f, 64f, 30f, 14f, -4f, -16f, -6f, 2f);
                default: return new Pose(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
            }
        }

        private static void Figure(Canvas c, int kind, int pose)
        {
            var look = LookOf(kind);
            if (pose == SootStage.Down)
            {
                Lying(c, look);
                return;
            }

            var p = PoseOf(pose);
            var s = look.Scale;
            var cx = FigureWidth / 2f;
            var hip = new Vector2(cx, (24f - p.Crouch) * s);
            var leanRad = p.Lean * Mathf.Deg2Rad;
            var torso = 20f * s;
            var shoulder = hip + new Vector2(Mathf.Sin(leanRad) * torso, Mathf.Cos(leanRad) * torso);
            var thigh = 12f * s;
            var shin = 12f * s;
            var upper = 9f * s;
            var fore = 9f * s;
            var legWidth = 3.2f * s * Mathf.Sqrt(look.Girth);
            var armWidth = 2.6f * s * Mathf.Sqrt(look.Girth);
            var dark = Shade(look.Pants, 0.7f);
            var shirtDark = Shade(look.Shirt, 0.75f);

            // 奥の手足 → 胴 → 頭 → 手前の手足
            Limb(c, hip, p.BackLeg, p.BackKnee, thigh, shin, legWidth, dark, Shade(look.Accent, 0.6f), 2.6f * s);
            Limb(c, shoulder, p.BackArm, p.BackElbow, upper, fore, armWidth, shirtDark, Shade(look.Skin, 0.8f), 2.2f * s);
            var half = 5.5f * s * look.Girth;
            c.Quad(hip + new Vector2(-half * 0.8f, 0f), hip + new Vector2(half * 0.8f, 0f), shoulder + new Vector2(half, 0f), shoulder + new Vector2(-half, 0f), look.Shirt);
            c.Line(hip + new Vector2(-half * 0.8f, 1f), hip + new Vector2(half * 0.8f, 1f), 1.6f * s, look.Accent);
            var head = shoulder + new Vector2(Mathf.Sin(leanRad) * 6f * s, 7f * s);
            c.Disc(head, 5.2f * s, look.Skin);
            Hair(c, head, s, look);
            c.Dot((int)(head.x + 2.5f * s), (int)(head.y + 0.5f * s), new Color32(20, 16, 20, 255));
            Limb(c, hip, p.FrontLeg, p.FrontKnee, thigh, shin, legWidth, look.Pants, look.Accent, 2.6f * s);
            Limb(c, shoulder, p.FrontArm, p.FrontElbow, upper, fore, armWidth, look.Shirt, look.Skin, 2.4f * s);
            c.Outline(new Color32(16, 12, 18, 255));
        }

        private static void Limb(Canvas c, Vector2 root, float angle, float bend, float upper, float lower, float width, Color32 color, Color32 end, float endRadius)
        {
            var a = angle * Mathf.Deg2Rad;
            var joint = root + new Vector2(Mathf.Sin(a) * upper, -Mathf.Cos(a) * upper);
            var b = (angle + bend) * Mathf.Deg2Rad;
            var tip = joint + new Vector2(Mathf.Sin(b) * lower, -Mathf.Cos(b) * lower);
            c.Line(root, joint, width, color);
            c.Line(joint, tip, width * 0.9f, color);
            c.Disc(tip, endRadius, end);
        }

        private static void Hair(Canvas c, Vector2 head, float s, Look look)
        {
            switch (look.HairStyle)
            {
                case 0:
                    c.Arc(head, 5.4f * s, 20f, 200f, 2.4f * s, look.Hair);
                    c.Line(head + new Vector2(-5f * s, 1.5f * s), head + new Vector2(5f * s, 1.5f * s), 1.6f * s, look.Accent);
                    c.Line(head + new Vector2(-5f * s, 1.5f * s), head + new Vector2(-9f * s, -1f * s), 1.2f * s, look.Accent);
                    break;
                case 1:
                    c.Line(head + new Vector2(-3f * s, 5f * s), head + new Vector2(3f * s, 8f * s), 2.4f * s, look.Hair);
                    break;
                case 2:
                    c.Disc(head + new Vector2(-1f * s, 4f * s), 1.2f * s, Shade(look.Skin, 1.15f));
                    break;
                case 3:
                    c.Arc(head, 5.6f * s, 0f, 180f, 2.6f * s, look.Hair);
                    c.Line(head + new Vector2(2f * s, 3.5f * s), head + new Vector2(9f * s, 3.5f * s), 1.6f * s, look.Accent);
                    break;
                default:
                    c.Arc(head, 5.6f * s, 60f, 230f, 2.6f * s, look.Hair);
                    c.Line(head + new Vector2(-1f * s, -3.5f * s), head + new Vector2(4f * s, -4.5f * s), 1.6f * s, look.Hair);
                    break;
            }
        }

        private static void Lying(Canvas c, Look look)
        {
            var s = look.Scale;
            var y = 5f * s;
            var head = new Vector2(FigureWidth / 2f - 26f * s, y + 1f);
            var hip = new Vector2(FigureWidth / 2f + 4f * s, y);
            var half = 5f * s * look.Girth;
            c.Quad(head + new Vector2(5f * s, -half), hip + new Vector2(0f, -half), hip + new Vector2(0f, half), head + new Vector2(5f * s, half), look.Shirt);
            c.Line(hip, hip + new Vector2(22f * s, -1f), 3.2f * s * look.Girth, look.Pants);
            c.Line(hip + new Vector2(0f, 2f), hip + new Vector2(20f * s, 5f * s), 3f * s * look.Girth, Shade(look.Pants, 0.7f));
            c.Disc(hip + new Vector2(23f * s, -1f), 2.6f * s, look.Accent);
            c.Line(head + new Vector2(8f * s, 3f), head + new Vector2(14f * s, 10f * s), 2.4f * s, look.Skin);
            c.Disc(head, 5.2f * s, look.Skin);
            Hair(c, head, s, look);
            c.Outline(new Color32(16, 12, 18, 255));
        }

        private static Color32 Shade(Color32 color, float factor) =>
            new((byte)Mathf.Clamp(color.r * factor, 0f, 255f), (byte)Mathf.Clamp(color.g * factor, 0f, 255f), (byte)Mathf.Clamp(color.b * factor, 0f, 255f), color.a);

        // ---- 舞台 ----

        private static void Backdrop(Canvas c)
        {
            var random = new System.Random(40);
            for (var y = 0; y < c.Height; y++)
            {
                var t = y / (float)c.Height;
                c.Rect(0, y, c.Width, 1, new Color32((byte)(34 + (8 * t)), (byte)(26 + (6 * t)), (byte)(44 + (18 * t)), 255));
            }

            var palettes = new[]
            {
                new Color32(92, 58, 52, 255), new Color32(64, 70, 86, 255), new Color32(84, 74, 56, 255), new Color32(60, 52, 70, 255), new Color32(96, 44, 48, 255),
            };
            var x = 0;
            var index = 0;
            while (x < c.Width)
            {
                var width = 70 + random.Next(80);
                var height = 70 + random.Next(80);
                var section = Mathf.Min(palettes.Length - 1, x / 420);
                var wall = Shade(palettes[section], 0.85f + (0.3f * random.Next(3) / 2f));
                c.Rect(x, 0, width - 2, height, wall);
                c.Rect(x, height - 4, width - 2, 4, Shade(wall, 0.7f));
                for (var wy = 26; wy < height - 12; wy += 22)
                {
                    for (var wx = x + 8; wx < x + width - 16; wx += 18)
                    {
                        var lit = random.Next(4) == 0;
                        c.Rect(wx, wy, 9, 12, lit ? new Color32(236, 196, 104, 255) : new Color32(30, 30, 44, 255));
                    }
                }

                if (index % 3 == 1)
                {
                    c.Rect(x + 10, 0, 18, 30, new Color32(36, 28, 30, 255));
                    c.Rect(x + 34, 36, 32, 10, new Color32(220, 60, 110, 255));
                    c.Rect(x + 36, 38, 28, 6, new Color32(255, 170, 200, 255));
                }

                x += width;
                index++;
            }

            for (var px = 0; px < c.Width; px += 2) { c.Dot(px, 0, new Color32(18, 14, 20, 255)); }
        }

        private static void Ground(Canvas c)
        {
            for (var y = 0; y < c.Height; y++)
            {
                var back = y >= GroundFront;
                var t = y / (float)c.Height;
                var color = back ? new Color32((byte)(70 - (14 * t)), (byte)(64 - (12 * t)), (byte)(70 - (10 * t)), 255) : new Color32(52, 46, 54, 255);
                c.Rect(0, y, c.Width, 1, color);
            }

            for (var row = GroundFront; row < c.Height; row += 12) { c.Rect(0, row, c.Width, 1, new Color32(46, 40, 48, 255)); }
            for (var x = 0; x < c.Width; x += 48)
            {
                c.Line(new Vector2(x, GroundFront), new Vector2(x + 24, c.Height), 1f, new Color32(48, 42, 50, 255));
            }

            c.Rect(0, c.Height - 3, c.Width, 3, new Color32(110, 104, 112, 255));
            c.Rect(0, GroundFront - 2, c.Width, 2, new Color32(34, 28, 36, 255));
            var random = new System.Random(7);
            for (var i = 0; i < 260; i++) { c.Dot(random.Next(c.Width), GroundFront + random.Next(c.Height - GroundFront - 4), new Color32(86, 80, 88, 255)); }
        }

        private static void Shadow(Canvas c) => c.Ellipse(16, 5, 15, 4, new Color32(0, 0, 0, 110));

        private static void Flare(Canvas c)
        {
            for (var y = 0; y < c.Height; y++)
            {
                for (var x = 0; x < c.Width; x++)
                {
                    var dx = (x - 19.5f) / 20f;
                    var dy = (y - 19.5f) / 20f;
                    var radius = Mathf.Sqrt((dx * dx) + (dy * dy));
                    var angle = Mathf.Atan2(dy, dx);
                    var ray = Mathf.Pow(Mathf.Abs(Mathf.Cos(4f * angle)), 8f);
                    var alpha = Mathf.Clamp01(1f - (radius / (0.25f + (0.75f * ray))));
                    if (alpha > 0.05f) { c.Dot(x, y, new Color32(255, 250, 220, (byte)(255f * Mathf.Sqrt(alpha)))); }
                }
            }
        }

        private static void Whirl(Canvas c)
        {
            for (var y = 0; y < c.Height; y++)
            {
                for (var x = 0; x < c.Width; x++)
                {
                    var dx = (x - 47.5f) / 48f;
                    var dy = (y - 31.5f) / 32f;
                    var radius = Mathf.Sqrt((dx * dx) + (dy * dy));
                    if (radius > 1f || radius < 0.55f) { continue; }
                    var angle = Mathf.Atan2(dy, dx);
                    var swirl = Mathf.Repeat((angle / (2f * Mathf.PI)) + (radius * 1.5f), 0.25f) / 0.25f;
                    var alpha = (1f - Mathf.Abs((radius - 0.78f) / 0.23f)) * swirl;
                    if (alpha > 0.08f) { c.Dot(x, y, new Color32(140, 220, 255, (byte)(220f * alpha))); }
                }
            }
        }

        /// <summary>回復アイテム：皿にのった骨付き肉。</summary>
        private static void Food(Canvas c)
        {
            c.Ellipse(11, 3, 10, 2, new Color32(230, 230, 236, 255));
            c.Ellipse(11, 3, 8, 1, new Color32(196, 200, 210, 255));
            c.Ellipse(9, 7, 6, 4, new Color32(150, 72, 34, 255));
            c.Ellipse(8, 8, 4, 2, new Color32(196, 110, 56, 255));
            c.Rect(14, 7, 5, 2, new Color32(244, 238, 222, 255));
            c.Ellipse(19, 8, 2, 2, new Color32(244, 238, 222, 255));
            c.Outline(new Color32(16, 12, 18, 255));
        }

        private static void Lamp(Canvas c)
        {
            c.Rect(5, 0, 3, 88, new Color32(40, 40, 48, 255));
            c.Rect(2, 0, 9, 4, new Color32(30, 30, 36, 255));
            c.Rect(1, 86, 11, 6, new Color32(40, 40, 48, 255));
            c.Ellipse(6, 84, 5, 3, new Color32(255, 220, 140, 255));
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

            public void Rect(int x, int y, int width, int height, Color32 color)
            {
                for (var row = y; row < y + height; row++)
                {
                    for (var col = x; col < x + width; col++) { Dot(col, row, color); }
                }
            }

            public void Ellipse(int cx, int cy, int rx, int ry, Color32 color)
            {
                for (var y = cy - ry; y <= cy + ry; y++)
                {
                    for (var x = cx - rx; x <= cx + rx; x++)
                    {
                        var nx = (x - cx) / (float)rx;
                        var ny = (y - cy) / (float)ry;
                        if ((nx * nx) + (ny * ny) <= 1f) { Dot(x, y, color); }
                    }
                }
            }

            public void Disc(Vector2 center, float radius, Color32 color)
            {
                for (var y = Mathf.FloorToInt(center.y - radius); y <= Mathf.CeilToInt(center.y + radius); y++)
                {
                    for (var x = Mathf.FloorToInt(center.x - radius); x <= Mathf.CeilToInt(center.x + radius); x++)
                    {
                        var d = new Vector2(x + 0.5f, y + 0.5f) - center;
                        if (d.sqrMagnitude <= radius * radius) { Dot(x, y, color); }
                    }
                }
            }

            public void Line(Vector2 a, Vector2 b, float width, Color32 color)
            {
                var steps = Mathf.Max(1, Mathf.CeilToInt((b - a).magnitude * 2f));
                for (var i = 0; i <= steps; i++) { Disc(Vector2.Lerp(a, b, i / (float)steps), width * 0.5f + 0.3f, color); }
            }

            public void Arc(Vector2 center, float radius, float from, float to, float width, Color32 color)
            {
                for (var deg = from; deg <= to; deg += 6f)
                {
                    var r = deg * Mathf.Deg2Rad;
                    Disc(center + new Vector2(Mathf.Cos(r) * radius, Mathf.Sin(r) * radius), width * 0.5f + 0.3f, color);
                }
            }

            public void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 color)
            {
                var minX = Mathf.FloorToInt(Mathf.Min(Mathf.Min(a.x, b.x), Mathf.Min(c.x, d.x)));
                var maxX = Mathf.CeilToInt(Mathf.Max(Mathf.Max(a.x, b.x), Mathf.Max(c.x, d.x)));
                var minY = Mathf.FloorToInt(Mathf.Min(Mathf.Min(a.y, b.y), Mathf.Min(c.y, d.y)));
                var maxY = Mathf.CeilToInt(Mathf.Max(Mathf.Max(a.y, b.y), Mathf.Max(c.y, d.y)));
                for (var y = minY; y <= maxY; y++)
                {
                    for (var x = minX; x <= maxX; x++)
                    {
                        var p = new Vector2(x + 0.5f, y + 0.5f);
                        if (Inside(p, a, b, c) || Inside(p, a, c, d)) { Dot(x, y, color); }
                    }
                }
            }

            public void Outline(Color32 color)
            {
                var copy = (Color32[])Pixels.Clone();
                for (var y = 0; y < Height; y++)
                {
                    for (var x = 0; x < Width; x++)
                    {
                        if (copy[(y * Width) + x].a != 0) { continue; }
                        if (Solid(copy, x - 1, y) || Solid(copy, x + 1, y) || Solid(copy, x, y - 1) || Solid(copy, x, y + 1)) { Dot(x, y, color); }
                    }
                }
            }

            public void Dot(int x, int y, Color32 color)
            {
                if (x < 0 || y < 0 || x >= Width || y >= Height) { return; }
                Pixels[(y * Width) + x] = color;
            }

            private bool Solid(Color32[] pixels, int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && pixels[(y * Width) + x].a > 200;

            private static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
            {
                var d1 = Cross(p, a, b);
                var d2 = Cross(p, b, c);
                var d3 = Cross(p, c, a);
                var negative = d1 < 0 || d2 < 0 || d3 < 0;
                var positive = d1 > 0 || d2 > 0 || d3 > 0;
                return !(negative && positive);
            }

            private static float Cross(Vector2 p, Vector2 a, Vector2 b) => ((b.x - a.x) * (p.y - a.y)) - ((b.y - a.y) * (p.x - a.x));
        }
    }
}
