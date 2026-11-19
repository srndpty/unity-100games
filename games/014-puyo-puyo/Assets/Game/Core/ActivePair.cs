using System;

namespace Game.Core
{
    /// <summary>軸から見た子の向き。時計回りの順に並ぶ。</summary>
    public enum PairOrientation
    {
        Up,
        Right,
        Down,
        Left,
    }

    /// <summary>2個組の色。</summary>
    public readonly struct Pair : IEquatable<Pair>
    {
        public Pair(Cell pivot, Cell child)
        {
            if (!pivot.IsSpore()) { throw new ArgumentOutOfRangeException(nameof(pivot), pivot, "色付きの胞子を指定してください。"); }
            if (!child.IsSpore()) { throw new ArgumentOutOfRangeException(nameof(child), child, "色付きの胞子を指定してください。"); }
            Pivot = pivot;
            Child = child;
        }

        public Cell Pivot { get; }
        public Cell Child { get; }

        public static bool operator ==(Pair left, Pair right) => left.Equals(right);
        public static bool operator !=(Pair left, Pair right) => !left.Equals(right);

        public bool Equals(Pair other) => Pivot == other.Pivot && Child == other.Child;
        public override bool Equals(object? obj) => obj is Pair other && Equals(other);
        public override int GetHashCode() => ((int)Pivot * 8) + (int)Child;
    }

    /// <summary>操作中のペア。盤面を知らない不変の値。</summary>
    public readonly struct ActivePair : IEquatable<ActivePair>
    {
        public ActivePair(Pair pair, int x, int y, PairOrientation orientation)
        {
            Pair = pair;
            X = x;
            Y = y;
            Orientation = orientation;
        }

        public Pair Pair { get; }

        /// <summary>軸の列。</summary>
        public int X { get; }

        /// <summary>軸の段。</summary>
        public int Y { get; }

        public PairOrientation Orientation { get; }

        public int ChildX => X + DirectionX(Orientation);
        public int ChildY => Y + DirectionY(Orientation);

        /// <summary>軸と子のうち低い方の段。</summary>
        public int LowestY => Math.Min(Y, ChildY);

        public static bool operator ==(ActivePair left, ActivePair right) => left.Equals(right);
        public static bool operator !=(ActivePair left, ActivePair right) => !left.Equals(right);

        public static ActivePair Spawn(Pair pair) => new(pair, Board.SpawnX, Board.SpawnY, PairOrientation.Up);

        public static int DirectionX(PairOrientation orientation) => orientation switch
        {
            PairOrientation.Right => 1,
            PairOrientation.Left => -1,
            _ => 0,
        };

        public static int DirectionY(PairOrientation orientation) => orientation switch
        {
            PairOrientation.Up => 1,
            PairOrientation.Down => -1,
            _ => 0,
        };

        public ActivePair Moved(int dx, int dy) => new(Pair, X + dx, Y + dy, Orientation);

        public ActivePair Turned(PairOrientation orientation, int dx = 0, int dy = 0) => new(Pair, X + dx, Y + dy, orientation);

        public bool Equals(ActivePair other) => Pair.Equals(other.Pair) && X == other.X && Y == other.Y && Orientation == other.Orientation;
        public override bool Equals(object? obj) => obj is ActivePair other && Equals(other);
        public override int GetHashCode() => (Pair.GetHashCode() * 397) ^ (X * 31) ^ (Y * 7) ^ (int)Orientation;
    }
}
