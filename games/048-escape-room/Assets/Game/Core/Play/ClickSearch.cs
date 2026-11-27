using System;
using System.Collections.Generic;
using Game.Core.Geometry;
using Game.Core.Rooms;

namespace Game.Core.Play
{
    /// <summary>
    /// クリックが部屋に届く画面の範囲（画面の整数座標、縦の半分 = 1000）。横は ±<see cref="HalfWidth"/>、
    /// 縦は <see cref="MinY"/> から上端まで（下は HUD の持ち物の帯に隠れて部屋に届かない）。
    /// </summary>
    public readonly struct ScreenRegion : IEquatable<ScreenRegion>
    {
        public ScreenRegion(int halfWidth, int minY)
        {
            if (halfWidth < 1 || halfWidth > CameraPose.MaxHalfWidth) { throw new ArgumentOutOfRangeException(nameof(halfWidth), $"横の半分は 1〜{CameraPose.MaxHalfWidth} です。"); }
            if (minY < -CameraPose.HalfHeight || minY >= CameraPose.HalfHeight) { throw new ArgumentOutOfRangeException(nameof(minY)); }
            HalfWidth = halfWidth;
            MinY = minY;
        }

        public int HalfWidth { get; }
        public int MinY { get; }

        /// <summary>
        /// 対応する全ての画面で必ず部屋に届く範囲：対応する画面の比率の最も狭い 16:10（横の半分 1600）と、
        /// 1080 のうち 150 の HUD の帯（上端 -722）より少し上。Unity は画面がこれより狭くならないよう左右・上下に帯を付ける。
        /// 部屋の定義の検証と操縦役は、この範囲だけでクリックする。
        /// </summary>
        public static ScreenRegion Guaranteed => new(1600, -700);

        public bool Contains(int x, int y) => x >= -HalfWidth && x <= HalfWidth && y >= MinY && y <= CameraPose.HalfHeight;

        /// <summary>この範囲が other を含むか。</summary>
        public bool Covers(ScreenRegion other) => HalfWidth >= other.HalfWidth && MinY <= other.MinY;

        public bool Equals(ScreenRegion other) => HalfWidth == other.HalfWidth && MinY == other.MinY;

        public override bool Equals(object? obj) => obj is ScreenRegion other && Equals(other);

        public override int GetHashCode() => (HalfWidth * 397) ^ MinY;

        public static bool operator ==(ScreenRegion left, ScreenRegion right) => left.Equals(right);

        public static bool operator !=(ScreenRegion left, ScreenRegion right) => !left.Equals(right);

        public override string ToString() => $"±{HalfWidth}・y≥{MinY}";
    }

    /// <summary>
    /// 命令になる画面の点を探す（ビルド前の検証・操縦役・テストで共有する）。部品の手前の面の中心を写した点から試し、
    /// だめなら範囲の中を一定の間隔で刻んで探す。見つかった点は必ずその命令になるが、刻みの間に隠れた点は見逃しうる
    /// （完全な可視領域の判定ではない）。見つからなければ「クリックできない」として扱う（fail-closed）。
    /// </summary>
    public static class ClickSearch
    {
        public const int ScanStep = 20;

        /// <summary>今の視点で、クリックすると command になる範囲の中の点。見つからなければ null。</summary>
        public static (int X, int Y)? PointFor(World world, Command command, int selectedItem, ScreenRegion region)
        {
            if (world == null) { throw new ArgumentNullException(nameof(world)); }
            var def = world.Def;
            var pose = def.Views[world.View].Pose;
            foreach (var part in TargetParts(def, command))
            {
                if (!world.IsPresent(part.Index)) { continue; }

                // 手前の面の中心から、面の上の 5 × 5 の点（縁を少し避ける）を試す。一部だけ隠れた部品はここで見つかる。
                for (int k = 0; k < _faceSamples.Length; k++)
                {
                    var point = FacePoint(part.Box, pose.Wall, _faceSamples[k].U, _faceSamples[k].V);
                    if (!pose.TryProject(point, out var x, out var y) || !region.Contains(x, y)) { continue; }
                    if (world.CommandAt(x, y, selectedItem) == command) { return (x, y); }
                }
            }

            for (int sy = CameraPose.HalfHeight - (ScanStep / 2); sy >= region.MinY; sy -= ScanStep)
            {
                for (int sx = -region.HalfWidth + (ScanStep / 2); sx <= region.HalfWidth; sx += ScanStep)
                {
                    if (world.CommandAt(sx, sy, selectedItem) == command) { return (sx, sy); }
                }
            }

            return null;
        }

        /// <summary>面の上で試す点（面の幅と高さに対する千分率、中心が最初）。</summary>
        private static readonly (int U, int V)[] _faceSamples = BuildSamples();

        private static (int U, int V)[] BuildSamples()
        {
            var list = new List<(int U, int V)> { (500, 500) };
            foreach (var v in new[] { 500, 300, 700, 120, 880 })
            {
                foreach (var u in new[] { 500, 300, 700, 120, 880 })
                {
                    if (u != 500 || v != 500) { list.Add((u, v)); }
                }
            }

            return list.ToArray();
        }

        /// <summary>壁を向いて見たときの手前の面の上の点（u は右、v は上、千分率）。</summary>
        private static Vec3i FacePoint(Box3 box, int wall, int u, int v)
        {
            var front = box.FrontCenter(wall);
            var y = box.Min.Y + (int)((long)box.Size.Y * v / 1000);
            switch (wall & 3)
            {
                case 0:
                    return new Vec3i(box.Min.X + (int)((long)box.Size.X * u / 1000), y, front.Z);
                case 1:
                    return new Vec3i(front.X, y, box.Max.Z - (int)((long)box.Size.Z * u / 1000));
                case 2:
                    return new Vec3i(box.Max.X - (int)((long)box.Size.X * u / 1000), y, front.Z);
                default:
                    return new Vec3i(front.X, y, box.Min.Z + (int)((long)box.Size.Z * u / 1000));
            }
        }

        /// <summary>命令がクリックで作れる種類か（向き直り・戻る・回す・ヒント・リセットはキーで出す）。</summary>
        public static bool NeedsClick(Command command) => command.Kind is CommandKind.Focus or CommandKind.Operate or CommandKind.Take or CommandKind.Inspect or CommandKind.UseItem;

        /// <summary>命令を作るときに選んでいる持ち物（使うなら使う持ち物、ほかは何も選ばない）。</summary>
        public static int SelectedFor(Command command) => command.Kind == CommandKind.UseItem ? command.A : -1;

        private static IEnumerable<PartDef> TargetParts(RoomDef def, Command command)
        {
            switch (command.Kind)
            {
                case CommandKind.Focus:
                    foreach (var p in def.Parts)
                    {
                        if (p.View == command.A) { yield return p; }
                    }

                    break;
                case CommandKind.UseItem:
                    if (command.B >= 0 && command.B < def.Parts.Count) { yield return def.Parts[command.B]; }
                    break;
                case CommandKind.Operate:
                case CommandKind.Take:
                case CommandKind.Inspect:
                    if (command.A >= 0 && command.A < def.Parts.Count) { yield return def.Parts[command.A]; }
                    break;
                default:
                    break;
            }
        }
    }
}
