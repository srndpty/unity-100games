using System;

namespace Game.Core
{
    public enum GamePhase
    {
        /// <summary>初手の前。爆弾はまだ配置されていない。</summary>
        Ready,
        Playing,
        Won,
        Lost,
    }

    public enum MoveResult
    {
        Rejected,
        Changed,
        Exploded,
        Won,
    }

    /// <summary>1試合の進行。答え（<see cref="MineField"/>）とプレイヤーが見ている状態を分けて持つ。</summary>
    public sealed class MinesweeperGame
    {
        private const int NoCell = -1;

        private readonly BoardConfig _config;
        private readonly CellMark[] _marks;
        private readonly int[] _queue;
        private readonly int[] _neighbors = new int[BoardGrid.MaxNeighbors];
        private int _safeCellsLeft;
        private int _explodedCell = NoCell;

        public MinesweeperGame(BoardConfig config, int seed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            _config = config.Copy();
            Seed = seed;
            Grid = new BoardGrid(config.Width, config.Height);
            _marks = new CellMark[Grid.CellCount];
            _queue = new int[Grid.CellCount];
            _safeCellsLeft = config.SafeCellCount;
        }

        private MinesweeperGame(MineField field)
            : this(new BoardConfig { Width = field.Grid.Width, Height = field.Grid.Height, MineCount = field.MineCount }, 0)
        {
            Field = field;
            Phase = GamePhase.Playing;
        }

        private enum CellMark : byte
        {
            Hidden,
            Flagged,
            Open,
        }

        public int Seed { get; }
        public BoardGrid Grid { get; }
        public GamePhase Phase { get; private set; }
        public int FlagCount { get; private set; }

        /// <summary>直前の <see cref="Reveal"/> で開いたセルの数。</summary>
        public int LastOpenedCount { get; private set; }

        /// <summary>答え。初手までは null。描画は <see cref="Face"/> だけを読み、これを直接見ない。</summary>
        public MineField? Field { get; private set; }

        public int MineCount => _config.MineCount;
        public bool IsOver => Phase == GamePhase.Won || Phase == GamePhase.Lost;
        public int SafeCellsLeft => _safeCellsLeft;

        /// <summary>爆弾の数から旗の数を引いた値。旗を立てすぎると負になる。勝利時は残りの爆弾に旗が立つので 0。</summary>
        public int RemainingMines => Phase == GamePhase.Won ? 0 : MineCount - FlagCount;

        /// <summary>爆弾の配置が決まった盤面から始める。初手の保護は働かない。</summary>
        public static MinesweeperGame FromField(MineField field)
        {
            if (field == null) { throw new ArgumentNullException(nameof(field)); }
            return new MinesweeperGame(field);
        }

        public MoveResult Reveal(int x, int y)
        {
            if (IsOver || !Grid.Contains(x, y)) { return MoveResult.Rejected; }
            var cell = Grid.Index(x, y);
            if (_marks[cell] != CellMark.Hidden) { return MoveResult.Rejected; }

            if (Field == null)
            {
                Field = MineField.Generate(_config, Seed, x, y);
                Phase = GamePhase.Playing;
            }

            if (Field.IsMine(cell))
            {
                LastOpenedCount = 0;
                _explodedCell = cell;
                Phase = GamePhase.Lost;
                return MoveResult.Exploded;
            }

            LastOpenedCount = OpenFrom(cell, Field);
            _safeCellsLeft -= LastOpenedCount;
            if (_safeCellsLeft > 0) { return MoveResult.Changed; }
            Phase = GamePhase.Won;
            return MoveResult.Won;
        }

        public MoveResult ToggleFlag(int x, int y)
        {
            if (IsOver || !Grid.Contains(x, y)) { return MoveResult.Rejected; }
            var cell = Grid.Index(x, y);
            switch (_marks[cell])
            {
                case CellMark.Hidden:
                    _marks[cell] = CellMark.Flagged;
                    FlagCount++;
                    return MoveResult.Changed;
                case CellMark.Flagged:
                    _marks[cell] = CellMark.Hidden;
                    FlagCount--;
                    return MoveResult.Changed;
                default:
                    return MoveResult.Rejected;
            }
        }

        public CellFace Face(int x, int y)
        {
            if (!Grid.Contains(x, y)) { throw new ArgumentOutOfRangeException(nameof(x), "盤面の外です。"); }
            var cell = Grid.Index(x, y);
            var mark = _marks[cell];
            if (mark == CellMark.Open) { return CellFaces.Open(Field!.Adjacent(cell)); }
            var flagged = mark == CellMark.Flagged;
            if (Phase == GamePhase.Won) { return CellFace.Flagged; }
            if (Phase != GamePhase.Lost) { return flagged ? CellFace.Flagged : CellFace.Hidden; }

            if (cell == _explodedCell) { return CellFace.Exploded; }
            var mine = Field!.IsMine(cell);
            if (flagged) { return mine ? CellFace.Flagged : CellFace.WrongFlag; }
            return mine ? CellFace.Mine : CellFace.Hidden;
        }

        // 再帰は盤面の大きさに比例してスタックを使うのでキューで展開する。
        // 投入時に開くので、各セルは最大1回しか投入されない。旗付きセルは Hidden でないため開かず、その先へも進まない。
        private int OpenFrom(int start, MineField field)
        {
            var head = 0;
            var tail = 0;
            _marks[start] = CellMark.Open;
            _queue[tail++] = start;
            while (head < tail)
            {
                var cell = _queue[head++];
                if (field.Adjacent(cell) != 0) { continue; }
                var count = Grid.Neighbors(cell, _neighbors);
                for (var i = 0; i < count; i++)
                {
                    var neighbor = _neighbors[i];
                    if (_marks[neighbor] != CellMark.Hidden) { continue; }
                    _marks[neighbor] = CellMark.Open;
                    _queue[tail++] = neighbor;
                }
            }

            return tail;
        }
    }
}
