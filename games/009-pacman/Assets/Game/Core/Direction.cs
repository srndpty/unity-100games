using System;

namespace Game.Core
{
    /// <summary>移動方向。数値の順が猫の分岐で同点だったときの優先順（上→左→下→右）になる。</summary>
    public enum Direction
    {
        None = -1,
        Up = 0,
        Left = 1,
        Down = 2,
        Right = 3,
    }

    public static class DirectionExtensions
    {
        public const int Count = 4;

        public static bool IsMove(this Direction direction) => direction >= Direction.Up && direction <= Direction.Right;

        public static Direction Opposite(this Direction direction)
        {
            Require(direction);
            return (Direction)(((int)direction + 2) % Count);
        }

        public static int Dx(this Direction direction)
        {
            Require(direction);
            return direction == Direction.Left ? -1 : direction == Direction.Right ? 1 : 0;
        }

        /// <summary>盤面の y は下向きに増える。</summary>
        public static int Dy(this Direction direction)
        {
            Require(direction);
            return direction == Direction.Up ? -1 : direction == Direction.Down ? 1 : 0;
        }

        private static void Require(Direction direction)
        {
            if (!direction.IsMove()) { throw new ArgumentOutOfRangeException(nameof(direction)); }
        }
    }
}
