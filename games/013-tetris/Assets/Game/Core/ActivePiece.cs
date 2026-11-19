using System;

namespace Game.Core
{
    /// <summary>操作中のピース。盤面とは独立した不変の値で、位置は枠の左下のマス。</summary>
    public readonly struct ActivePiece : IEquatable<ActivePiece>
    {
        public ActivePiece(PieceKind kind, Rotation rotation, int x, int y)
        {
            Kind = kind;
            Rotation = rotation;
            X = x;
            Y = y;
        }

        public PieceKind Kind { get; }
        public Rotation Rotation { get; }
        public int X { get; }
        public int Y { get; }

        /// <summary>一番低いマスの段。</summary>
        public int LowestRow
        {
            get
            {
                var lowest = int.MaxValue;
                for (var i = 0; i < PieceShapes.CellCount; i++) { lowest = Math.Min(lowest, CellAt(i).Y); }
                return lowest;
            }
        }

        /// <summary>全体が非表示の段にあるか。</summary>
        public bool IsAboveVisibleArea => LowestRow >= Board.VisibleHeight;

        public static bool operator ==(ActivePiece left, ActivePiece right) => left.Equals(right);
        public static bool operator !=(ActivePiece left, ActivePiece right) => !left.Equals(right);

        /// <summary>出現位置のピース。種類によらず、一番低いマスが非表示の最下段に来る。</summary>
        public static ActivePiece Spawn(PieceKind kind)
        {
            var lowest = int.MaxValue;
            var cells = PieceShapes.Cells(kind, Rotation.North);
            for (var i = 0; i < cells.Count; i++) { lowest = Math.Min(lowest, cells[i].Y); }
            return new ActivePiece(kind, Rotation.North, (Board.Width - PieceShapes.BoxSize(kind)) / 2, Board.VisibleHeight - lowest);
        }

        /// <summary>盤面上のマス。index は 0 から CellCount 未満。</summary>
        public Cell CellAt(int index)
        {
            var cell = PieceShapes.Cells(Kind, Rotation)[index];
            return new Cell(X + cell.X, Y + cell.Y);
        }

        public ActivePiece Moved(int dx, int dy) => new(Kind, Rotation, X + dx, Y + dy);

        public ActivePiece Turned(Rotation rotation, Cell offset) => new(Kind, rotation, X + offset.X, Y + offset.Y);

        public bool Equals(ActivePiece other) => Kind == other.Kind && Rotation == other.Rotation && X == other.X && Y == other.Y;
        public override bool Equals(object? obj) => obj is ActivePiece other && Equals(other);
        public override int GetHashCode() => unchecked((((((int)Kind * 397) ^ (int)Rotation) * 397) ^ X) * 397) ^ Y;
        public override string ToString() => $"{Kind} {Rotation} ({X}, {Y})";
    }
}
