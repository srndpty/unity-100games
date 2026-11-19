namespace Game.Core
{
    /// <summary>
    /// 解く途中の盤：各マスの数字と、空きマスに入りうる数字（候補）の集合。
    /// 数字を置くと、ピアの候補からその数字を消す。重複や候補の尽きたマスが出たら矛盾とする。
    /// 制約伝播（<see cref="Propagator"/>）とバックトラック（<see cref="Solver"/>）が共有する内部の表現。
    /// </summary>
    internal sealed class CandidateState
    {
        private readonly int[] _values;
        private readonly int[] _masks;

        public CandidateState(Grid grid)
        {
            _values = new int[Board.CellCount];
            _masks = new int[Board.CellCount];
            for (var i = 0; i < Board.CellCount; i++) { _masks[i] = Digits.All; }
            for (var i = 0; i < Board.CellCount; i++)
            {
                var digit = grid[i];
                if (digit == 0) { continue; }
                if (!Digits.Has(_masks[i], digit))
                {
                    // ピアに同じ数字が置かれている。
                    IsContradiction = true;
                    return;
                }

                Place(i, digit);
            }
        }

        private CandidateState(CandidateState source)
        {
            _values = (int[])source._values.Clone();
            _masks = (int[])source._masks.Clone();
            EmptyCount = source.EmptyCount;
            IsContradiction = source.IsContradiction;
        }

        public bool IsContradiction { get; private set; }

        public int EmptyCount { get; private set; } = Board.CellCount;

        public bool IsSolved => !IsContradiction && EmptyCount == 0;

        public int Value(int cell) => _values[cell];

        /// <summary>空きマスの候補。数字の入ったマスは 0。</summary>
        public int Mask(int cell) => _masks[cell];

        public CandidateState Copy() => new(this);

        /// <summary>数字を置き、ピアの候補から消す。候補にない数字を置いた・候補が尽きたマスができたら矛盾になる。</summary>
        public void Place(int cell, int digit)
        {
            if (!Digits.Has(_masks[cell], digit))
            {
                IsContradiction = true;
                return;
            }

            _values[cell] = digit;
            _masks[cell] = 0;
            EmptyCount--;
            var bit = Digits.Bit(digit);
            for (var i = 0; i < Board.PeerCount; i++)
            {
                var peer = Board.Peer(cell, i);
                if (_values[peer] != 0 || (_masks[peer] & bit) == 0) { continue; }
                _masks[peer] &= ~bit;
                if (_masks[peer] == 0) { IsContradiction = true; }
            }
        }

        public Grid ToGrid() => Grid.FromDigits(_values);
    }
}
