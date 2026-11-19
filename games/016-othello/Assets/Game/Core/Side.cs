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
        public static Side Opponent(this Side side) => side == Side.Black ? Side.White : Side.Black;

        public static Disc ToDisc(this Side side) => side == Side.Black ? Disc.Black : Disc.White;
    }
}
