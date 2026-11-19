using System;

namespace Game.Core
{
    /// <summary>ペアの回転規則。</summary>
    public static class PairRotation
    {
        private const int OrientationCount = 4;

        /// <summary>回転後のペアを返す。回せないときは null。</summary>
        public static ActivePair? Rotate(Board board, ActivePair pair, bool clockwise)
        {
            if (board == null) { throw new ArgumentNullException(nameof(board)); }
            var step = clockwise ? 1 : OrientationCount - 1;
            var target = (PairOrientation)(((int)pair.Orientation + step) % OrientationCount);

            // ① その場で回す。
            var turned = pair.Turned(target);
            if (board.Fits(turned)) { return turned; }

            // ② 子の行き先の反対へ軸を1マス押し出す。
            var kicked = pair.Turned(target, -ActivePair.DirectionX(target), -ActivePair.DirectionY(target));
            if (board.Fits(kicked)) { return kicked; }

            // ③ 縦向きで左右とも塞がっていれば、同じ2マスの中で軸と子を入れ替える。
            return pair.Orientation switch
            {
                PairOrientation.Up => pair.Turned(PairOrientation.Down, 0, 1),
                PairOrientation.Down => pair.Turned(PairOrientation.Up, 0, -1),
                _ => null,
            };
        }
    }
}
