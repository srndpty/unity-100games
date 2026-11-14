namespace Game.Core
{
    /// <summary>コートの左右。プレイヤーは左、CPU は右。</summary>
    public enum Side
    {
        Left,
        Right,
    }

    public static class SideExtensions
    {
        public static Side Opponent(this Side side) => side == Side.Left ? Side.Right : Side.Left;

        /// <summary>その陣地へ向かう x 方向の符号（左 = -1、右 = +1）。</summary>
        public static float Direction(this Side side) => side == Side.Left ? -1f : 1f;
    }
}
