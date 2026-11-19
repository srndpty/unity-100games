using System;

namespace Game.Core
{
    /// <summary>盤のマス。X は左から a〜h（0〜7）、Y は上から 1〜8（0〜7）。盤外の値も表せ、<see cref="IsInside"/> で確かめる。</summary>
    public readonly struct Square : IEquatable<Square>
    {
        public const int Size = 8;
        public const int Count = Size * Size;

        public Square(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }
        public int Y { get; }

        public bool IsInside => X >= 0 && X < Size && Y >= 0 && Y < Size;

        /// <summary>0〜63 の通し番号（Y * 8 + X）。盤外では使えない。</summary>
        public int Index
        {
            get
            {
                if (!IsInside) { throw new InvalidOperationException($"盤外のマスです: ({X}, {Y})"); }
                return (Y * Size) + X;
            }
        }

        /// <summary>このマスだけが立ったビット。</summary>
        public ulong Bit => 1UL << Index;

        /// <summary>棋譜の表記（例：d3）。</summary>
        public string Name => IsInside ? $"{(char)('a' + X)}{Y + 1}" : $"({X}, {Y})";

        public static Square FromIndex(int index)
        {
            if (index < 0 || index >= Count) { throw new ArgumentOutOfRangeException(nameof(index), index, "0〜63 で指定してください。"); }
            return new Square(index % Size, index / Size);
        }

        /// <summary>棋譜の表記（a1〜h8）からマスを作る。</summary>
        public static Square Parse(string name)
        {
            if (name == null) { throw new ArgumentNullException(nameof(name)); }
            if (name.Length != 2 || name[0] < 'a' || name[0] > 'h' || name[1] < '1' || name[1] > '8')
            {
                throw new FormatException($"a1〜h8 で指定してください: {name}");
            }

            return new Square(name[0] - 'a', name[1] - '1');
        }

        public bool Equals(Square other) => X == other.X && Y == other.Y;

        public override bool Equals(object? obj) => obj is Square other && Equals(other);

        public override int GetHashCode() => unchecked((X * 397) ^ Y);

        public override string ToString() => Name;

        public static bool operator ==(Square left, Square right) => left.Equals(right);

        public static bool operator !=(Square left, Square right) => !left.Equals(right);
    }
}
