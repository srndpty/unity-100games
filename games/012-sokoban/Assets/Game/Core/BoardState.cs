using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>面の変化するレイヤー（プレイヤーと箱）。不変で、箱の並び順は移動しても保つ。</summary>
    public sealed class BoardState : IEquatable<BoardState>
    {
        private readonly Cell[] _boxes;

        public BoardState(Cell player, IEnumerable<Cell> boxes)
        {
            if (boxes == null) { throw new ArgumentNullException(nameof(boxes)); }
            _boxes = new List<Cell>(boxes).ToArray();
            Player = player;

            var seen = new HashSet<Cell>();
            foreach (var box in _boxes)
            {
                if (!seen.Add(box)) { throw new ArgumentException("同じマスに箱が重なっています: " + box, nameof(boxes)); }
                if (box == player) { throw new ArgumentException("プレイヤーと箱が重なっています: " + box, nameof(boxes)); }
            }
        }

        public Cell Player { get; }
        public IReadOnlyList<Cell> Boxes => _boxes;

        public bool HasBox(Cell cell) => Array.IndexOf(_boxes, cell) >= 0;

        /// <summary>プレイヤーを移し、押す箱があればその箱も移した盤面を返す。</summary>
        internal BoardState Moved(Cell player, Cell? boxFrom, Cell? boxTo)
        {
            if (boxFrom == null || boxTo == null) { return new BoardState(player, _boxes); }
            var boxes = (Cell[])_boxes.Clone();
            boxes[Array.IndexOf(boxes, boxFrom.Value)] = boxTo.Value;
            return new BoardState(player, boxes);
        }

        // 箱は区別しないので、並び順によらず同じ配置なら等しい。
        public bool Equals(BoardState? other)
        {
            if (other == null || Player != other.Player || _boxes.Length != other._boxes.Length) { return false; }
            foreach (var box in _boxes)
            {
                if (!other.HasBox(box)) { return false; }
            }

            return true;
        }

        public override bool Equals(object? obj) => Equals(obj as BoardState);

        public override int GetHashCode()
        {
            var hash = Player.GetHashCode();
            foreach (var box in _boxes) { hash = unchecked(hash + (box.GetHashCode() * 31)); }
            return hash;
        }

        public override string ToString() => $"人{Player} 箱[{string.Join(", ", _boxes)}]";
    }
}
