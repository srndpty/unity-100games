using System;

namespace Game.Core
{
    /// <summary>交点の状態。手番・勝者の表しにも使う（勝者の <see cref="Empty"/> は引き分け）。</summary>
    public enum Stone : byte
    {
        Empty = 0,
        Black = 1,
        White = 2,
    }

    public static class StoneExtensions
    {
        /// <summary>相手の色。空点には相手がない。</summary>
        public static Stone Opponent(this Stone stone) => stone switch
        {
            Stone.Black => Stone.White,
            Stone.White => Stone.Black,
            _ => throw new ArgumentOutOfRangeException(nameof(stone), stone, "空点に相手の色はありません。"),
        };

        /// <summary>黒か白か（空点でない）。</summary>
        public static bool IsColor(this Stone stone) => stone == Stone.Black || stone == Stone.White;

        public static string Label(this Stone stone) => stone switch
        {
            Stone.Black => "黒",
            Stone.White => "白",
            _ => "なし",
        };
    }
}
