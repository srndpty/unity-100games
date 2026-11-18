using System;

namespace Game.Core
{
    /// <summary>1手の差分。Undo はこの記録を逆にたどる。</summary>
    public sealed class MoveRecord
    {
        internal MoveRecord(Direction direction, Cell playerFrom, Cell playerTo, Cell? boxFrom, Cell? boxTo)
        {
            Direction = direction;
            PlayerFrom = playerFrom;
            PlayerTo = playerTo;
            BoxFrom = boxFrom;
            BoxTo = boxTo;
        }

        public Direction Direction { get; }
        public Cell PlayerFrom { get; }
        public Cell PlayerTo { get; }

        /// <summary>押した箱の移動元。押していなければ null。</summary>
        public Cell? BoxFrom { get; }

        /// <summary>押した箱の移動先。押していなければ null。</summary>
        public Cell? BoxTo { get; }

        public bool IsPush => BoxFrom != null;
    }

    /// <summary>移動の判定。入力の盤面は変更しない。</summary>
    public static class MoveRules
    {
        public static bool IsDirection(Direction direction) =>
            direction == Direction.Up || direction == Direction.Down || direction == Direction.Left || direction == Direction.Right;

        /// <summary>その方向へ動けるなら1手の差分を返す。動けなければ null。</summary>
        public static MoveRecord? TryMove(LevelMap map, BoardState board, Direction direction)
        {
            if (map == null) { throw new ArgumentNullException(nameof(map)); }
            if (board == null) { throw new ArgumentNullException(nameof(board)); }
            if (!IsDirection(direction)) { return null; }

            var target = board.Player.Step(direction);
            if (!map.IsFloor(target)) { return null; }
            if (!board.HasBox(target)) { return new MoveRecord(direction, board.Player, target, null, null); }

            // 押せるのは1個だけ。箱の先が箱や壁なら動けない。
            var beyond = target.Step(direction);
            if (!map.IsFloor(beyond) || board.HasBox(beyond)) { return null; }
            return new MoveRecord(direction, board.Player, target, target, beyond);
        }

        public static BoardState Apply(BoardState board, MoveRecord record)
        {
            if (board == null) { throw new ArgumentNullException(nameof(board)); }
            if (record == null) { throw new ArgumentNullException(nameof(record)); }
            return board.Moved(record.PlayerTo, record.BoxFrom, record.BoxTo);
        }

        public static BoardState Revert(BoardState board, MoveRecord record)
        {
            if (board == null) { throw new ArgumentNullException(nameof(board)); }
            if (record == null) { throw new ArgumentNullException(nameof(record)); }
            return board.Moved(record.PlayerFrom, record.BoxTo, record.BoxFrom);
        }

        public static bool IsCleared(LevelMap map, BoardState board)
        {
            if (map == null) { throw new ArgumentNullException(nameof(map)); }
            if (board == null) { throw new ArgumentNullException(nameof(board)); }
            foreach (var box in board.Boxes)
            {
                if (!map.IsGoal(box)) { return false; }
            }

            return true;
        }
    }
}
