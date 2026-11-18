using System;

namespace Game.Core
{
    public enum Direction
    {
        Up,
        Down,
        Left,
        Right,
    }

    /// <summary>盤面のマス。x は右、y は下へ増える。</summary>
    public readonly struct Cell : IEquatable<Cell>
    {
        public Cell(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }
        public int Y { get; }

        public static bool operator ==(Cell left, Cell right) => left.Equals(right);

        public static bool operator !=(Cell left, Cell right) => !left.Equals(right);

        public Cell Step(Direction direction) => direction switch
        {
            Direction.Up => new Cell(X, Y - 1),
            Direction.Down => new Cell(X, Y + 1),
            Direction.Left => new Cell(X - 1, Y),
            Direction.Right => new Cell(X + 1, Y),
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "不正な方向です。"),
        };

        public bool Equals(Cell other) => X == other.X && Y == other.Y;

        public override bool Equals(object? obj) => obj is Cell other && Equals(other);

        public override int GetHashCode() => unchecked((X * 397) ^ Y);

        public override string ToString() => $"({X}, {Y})";
    }
}
