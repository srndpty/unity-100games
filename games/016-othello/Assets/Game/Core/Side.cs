using System;

namespace Game.Core
{
    /// <summary>石の色。黒が先手。</summary>
    public enum Side
    {
        Black,
        White,
    }

    /// <summary>マスの中身。</summary>
    public enum Disc
    {
        Empty,
        Black,
        White,
    }

    public static class SideExtensions
    {
        public static Side Opponent(this Side side) => side.Validate() == Side.Black ? Side.White : Side.Black;

        public static Disc ToDisc(this Side side) => side.Validate() == Side.Black ? Disc.Black : Disc.White;

        /// <summary>定義された色ならそのまま返し、未定義の値は例外にする。黒以外を白とみなさない。</summary>
        public static Side Validate(this Side side)
        {
            if (side != Side.Black && side != Side.White) { throw new ArgumentOutOfRangeException(nameof(side), side, "未定義の色です。"); }
            return side;
        }
    }
}
