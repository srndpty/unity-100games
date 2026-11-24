using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using IntGeometry;

namespace Game.Core
{
    /// <summary>円（目標・トゲ）。</summary>
    public readonly struct Circle : IEquatable<Circle>
    {
        public Circle(Vec2i center, int radius)
        {
            if (radius <= 0) { throw new ArgumentOutOfRangeException(nameof(radius), radius, "半径は正にしてください。"); }
            Center = center;
            Radius = radius;
        }

        public Vec2i Center { get; }

        public int Radius { get; }

        public bool Equals(Circle other) => Center == other.Center && Radius == other.Radius;

        public override bool Equals(object? obj) => obj is Circle other && Equals(other);

        public override int GetHashCode() => unchecked((Center.GetHashCode() * 397) ^ Radius);

        public static bool operator ==(Circle left, Circle right) => left.Equals(right);

        public static bool operator !=(Circle left, Circle right) => !left.Equals(right);
    }

    /// <summary>ロープの定義：支点・全長・節の数。節点は節の数 - 1 個。</summary>
    public readonly struct RopeSpec : IEquatable<RopeSpec>
    {
        public RopeSpec(Vec2i anchor, int length, int links)
        {
            Anchor = anchor;
            Length = length;
            Links = links;
        }

        public Vec2i Anchor { get; }

        public int Length { get; }

        /// <summary>節の数（支点 → 節点 → … → 露玉）。</summary>
        public int Links { get; }

        public int Nodes => Links - 1;

        /// <summary>1 つの節の自然長（全長 ÷ 節の数、切り捨て）。</summary>
        public int RestLength => Length / Links;

        public bool Equals(RopeSpec other) => Anchor == other.Anchor && Length == other.Length && Links == other.Links;

        public override bool Equals(object? obj) => obj is RopeSpec other && Equals(other);

        public override int GetHashCode() => unchecked((((Anchor.GetHashCode() * 397) ^ Length) * 397) ^ Links);

        public static bool operator ==(RopeSpec left, RopeSpec right) => left.Equals(right);

        public static bool operator !=(RopeSpec left, RopeSpec right) => !left.Equals(right);
    }

    /// <summary>1 問の定義。作った後は変わらない。</summary>
    public sealed class Puzzle
    {
        public Puzzle(string name, string hint, int par, Vec2i body, IList<RopeSpec> ropes, IList<Rect> walls, IList<Circle> spikes, Circle goal)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Hint = hint ?? throw new ArgumentNullException(nameof(hint));
            Par = par;
            Body = body;
            Ropes = Array.AsReadOnly(Copy(ropes, nameof(ropes)));
            Walls = Array.AsReadOnly(Copy(walls, nameof(walls)));
            Spikes = Array.AsReadOnly(Copy(spikes, nameof(spikes)));
            Goal = goal;
        }

        public string Name { get; }

        /// <summary>問の案内文（画面に出す）。</summary>
        public string Hint { get; }

        /// <summary>目安の手数（解答の手順の手数）。</summary>
        public int Par { get; }

        /// <summary>露玉の初期位置。</summary>
        public Vec2i Body { get; }

        public ReadOnlyCollection<RopeSpec> Ropes { get; }

        public ReadOnlyCollection<Rect> Walls { get; }

        public ReadOnlyCollection<Circle> Spikes { get; }

        public Circle Goal { get; }

        /// <summary>この問の節点の総数。</summary>
        public int NodeCount
        {
            get
            {
                var count = 0;
                foreach (var rope in Ropes) { count += rope.Nodes; }
                return count;
            }
        }

        /// <summary>設定に照らして問を検証する。問題があれば例外（メッセージに問の名前を含める）。</summary>
        public void Validate(RopeConfig config)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            try { Check(config); }
            catch (FormatException error) { throw new FormatException($"問「{Name}」：{error.Message}", error); }
        }

        private void Check(RopeConfig config)
        {
            if (Name.Length == 0) { throw new FormatException("名前が空です。"); }
            if (Ropes.Count < 1 || Ropes.Count > 4) { throw new FormatException("ロープは 1〜4 本にしてください。"); }
            if (Par < 1 || Par > Ropes.Count) { throw new FormatException("目安の手数は 1 以上、ロープの本数以下にしてください。"); }
            var r = config.BodyRadius;
            if (!InsideWithMargin(Body, r)) { throw new FormatException("露玉が盤からはみ出しています。"); }
            foreach (var rope in Ropes)
            {
                if (!Board.Contains(rope.Anchor)) { throw new FormatException("支点が盤の外です。"); }
                if (rope.Links < 2 || rope.Links > config.MaxLinks) { throw new FormatException($"節の数は 2〜{config.MaxLinks} にしてください。"); }
                if (rope.RestLength < 1) { throw new FormatException("節が短すぎます。"); }
                var distance = IntMath.SqrtCeiling(Vec2i.DistanceSquared(rope.Anchor, Body));
                if ((long)rope.RestLength * rope.Links < distance) { throw new FormatException("ロープが支点から露玉まで届きません（初めから伸びています）。"); }
            }

            foreach (var wall in Walls)
            {
                if (!Board.Contains(new Vec2i(wall.MinX, wall.MinY)) || !Board.Contains(new Vec2i(wall.MaxX, wall.MaxY)))
                {
                    throw new FormatException("壁が盤の外です。");
                }

                // 中心が壁に入っても浅い側へ押し戻せるよう、1 Tick の移動量を「半径 + 壁の厚さの半分」より小さく保つ。
                if (config.MaxStep >= r + (Math.Min(wall.Width, wall.Height) / 2))
                {
                    throw new FormatException("壁が薄すぎます（1 Tick の移動量の上限ですり抜けます）。");
                }

                if (Overlaps(wall, Body, r)) { throw new FormatException("露玉が初めから壁に重なっています。"); }
            }

            foreach (var spike in Spikes)
            {
                CheckCircle(spike);
                if (Vec2i.DistanceSquared(spike.Center, Body) < Square(spike.Radius + r)) { throw new FormatException("露玉が初めからトゲに触れています。"); }
            }

            CheckCircle(Goal);
            if (Vec2i.DistanceSquared(Goal.Center, Body) < Square(Goal.Radius + r)) { throw new FormatException("露玉が初めから目標に触れています。"); }
        }

        private static void CheckCircle(Circle circle)
        {
            if (!Board.Contains(circle.Center)) { throw new FormatException("目標・トゲの中心が盤の外です。"); }
            if (circle.Radius > 2 * Board.Unit) { throw new FormatException("目標・トゲの半径は 2 マス以下にしてください。"); }
        }

        private static bool InsideWithMargin(Vec2i point, int margin) =>
            point.X - margin >= 0 && point.X + margin <= Board.Width && point.Y - margin >= 0 && point.Y + margin <= Board.Height;

        /// <summary>円が矩形に重なるか（最も近い点までの距離が半径より小さい）。</summary>
        internal static bool Overlaps(Rect wall, Vec2i center, int radius)
        {
            var closest = new Vec2i(IntMath.Clamp(center.X, wall.MinX, wall.MaxX), IntMath.Clamp(center.Y, wall.MinY, wall.MaxY));
            return Vec2i.DistanceSquared(closest, center) < Square(radius);
        }

        private static long Square(long value) => value * value;

        private static T[] Copy<T>(IList<T> items, string name)
        {
            if (items == null) { throw new ArgumentNullException(name); }
            var copy = new T[items.Count];
            items.CopyTo(copy, 0);
            return copy;
        }
    }
}
