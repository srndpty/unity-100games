using System;

namespace Game.Core
{
    /// <summary>操作の判定。Ok 以外では盤も候補も変わらない。</summary>
    public enum MoveVerdict
    {
        Ok,

        /// <summary>完成している。</summary>
        Solved,

        /// <summary>0〜80 以外のマス。</summary>
        OutOfBoard,

        /// <summary>1〜9 以外の数字。</summary>
        InvalidDigit,

        /// <summary>与えのマス。変更も消去もできない。</summary>
        Given,

        /// <summary>同じ行・列・ブロックに同じ数字がある。重複したマスは <see cref="SudokuGame.LastConflict"/>。</summary>
        Conflict,

        /// <summary>同じ数字がすでに入っている。</summary>
        Unchanged,

        /// <summary>数字の入ったマスに候補は付けられない。</summary>
        Filled,

        /// <summary>消す数字も候補もない。</summary>
        Empty,
    }

    /// <summary>ヒントの種類。</summary>
    public enum HintKind
    {
        /// <summary>完成しているのでヒントは出さない。</summary>
        None,

        /// <summary>正解と違う数字を指した。盤は変えない。</summary>
        Mistake,

        /// <summary>制約伝播で決まるマスを埋めた。理由は <see cref="Hint.Deduction"/>。</summary>
        Deduced,

        /// <summary>伝播で決まるマスがないので、解の数字を 1 マス埋めた。</summary>
        Revealed,
    }

    /// <summary>ヒントの結果。</summary>
    public readonly struct Hint
    {
        public Hint(HintKind kind, int cell, int digit, Deduction? deduction)
        {
            Kind = kind;
            Cell = cell;
            Digit = digit;
            Deduction = deduction;
        }

        public HintKind Kind { get; }

        /// <summary>指した・埋めたマス。None では -1。</summary>
        public int Cell { get; }

        /// <summary>Mistake では誤った数字、Deduced と Revealed では埋めた数字。</summary>
        public int Digit { get; }

        public Deduction? Deduction { get; }
    }

    /// <summary>
    /// 1 試合の盤：与え・プレイヤーの数字・候補（メモ）。時間は持たない。
    /// 重複する数字は拒否するが、正解と違う数字は置ける。全マスが埋まり解と一致したときだけ完成とする。
    /// </summary>
    public sealed class SudokuGame
    {
        private readonly int[] _values;
        private readonly int[] _notes;

        public SudokuGame(Puzzle puzzle)
        {
            Puzzle = puzzle ?? throw new ArgumentNullException(nameof(puzzle));
            _values = puzzle.Givens.ToDigits();
            _notes = new int[Board.CellCount];
            IsSolved = puzzle.Givens.IsComplete;
        }

        public Puzzle Puzzle { get; }

        public bool IsSolved { get; private set; }

        /// <summary>使ったヒントの回数（誤りの指摘も数える）。</summary>
        public int HintsUsed { get; private set; }

        /// <summary>直前に Conflict で拒否したときの、重複した相手のマス。それ以外では -1。</summary>
        public int LastConflict { get; private set; } = -1;

        /// <summary>全マスが埋まっている（正解とは限らない）。</summary>
        public bool IsFull
        {
            get
            {
                foreach (var value in _values)
                {
                    if (value == 0) { return false; }
                }

                return true;
            }
        }

        public int ValueAt(int cell)
        {
            Board.RequireCell(cell, nameof(cell));
            return _values[cell];
        }

        /// <summary>候補の集合（<see cref="Digits"/>）。数字の入ったマスは 0。</summary>
        public int NotesAt(int cell)
        {
            Board.RequireCell(cell, nameof(cell));
            return _notes[cell];
        }

        public bool IsGiven(int cell)
        {
            Board.RequireCell(cell, nameof(cell));
            return Puzzle.Givens[cell] != 0;
        }

        /// <summary>今の盤（与えとプレイヤーの数字）。</summary>
        public Grid Current => Grid.FromDigits(_values);

        /// <summary>数字を置く。プレイヤーの数字は上書きできる。置いたマスの候補と、ピアの同じ数字の候補を消す。</summary>
        public MoveVerdict Place(int cell, int digit)
        {
            LastConflict = -1;
            var verdict = CheckCell(cell, digit);
            if (verdict != MoveVerdict.Ok) { return verdict; }
            if (_values[cell] == digit) { return MoveVerdict.Unchanged; }
            for (var i = 0; i < Board.PeerCount; i++)
            {
                var peer = Board.Peer(cell, i);
                if (_values[peer] == digit)
                {
                    LastConflict = peer;
                    return MoveVerdict.Conflict;
                }
            }

            SetValue(cell, digit);
            return MoveVerdict.Ok;
        }

        /// <summary>空きマスの候補を付け外しする。</summary>
        public MoveVerdict ToggleNote(int cell, int digit)
        {
            LastConflict = -1;
            var verdict = CheckCell(cell, digit);
            if (verdict != MoveVerdict.Ok) { return verdict; }
            if (_values[cell] != 0) { return MoveVerdict.Filled; }
            _notes[cell] ^= Digits.Bit(digit);
            return MoveVerdict.Ok;
        }

        /// <summary>プレイヤーの数字を消す。数字がなければ候補をすべて消す。</summary>
        public MoveVerdict Erase(int cell)
        {
            LastConflict = -1;
            var verdict = CheckCell(cell, 1);
            if (verdict != MoveVerdict.Ok) { return verdict; }
            if (_values[cell] != 0)
            {
                _values[cell] = 0;
                return MoveVerdict.Ok;
            }

            if (_notes[cell] == 0) { return MoveVerdict.Empty; }
            _notes[cell] = 0;
            return MoveVerdict.Ok;
        }

        /// <summary>
        /// ① 正解と違う数字があれば、マス番号の最も小さいものを指す（盤は変えない）。
        /// ② なければ制約伝播で次に決まるマスを埋める。③ 決まらなければ空きマスのうち番号の最も小さいものに解の数字を埋める。
        /// </summary>
        public Hint RequestHint()
        {
            LastConflict = -1;
            if (IsSolved) { return new Hint(HintKind.None, -1, 0, null); }
            HintsUsed++;

            var solution = Puzzle.Solution;
            for (var cell = 0; cell < Board.CellCount; cell++)
            {
                if (_values[cell] != 0 && _values[cell] != solution[cell]) { return new Hint(HintKind.Mistake, cell, _values[cell], null); }
            }

            // 誤りがないので、今の盤の解は問題の解と同じ 1 つだけ。伝播で決まる数字は必ず解の数字になる。
            var deduction = Propagator.FindNext(Current);
            if (deduction is { } found)
            {
                SetValue(found.Cell, found.Digit);
                return new Hint(HintKind.Deduced, found.Cell, found.Digit, found);
            }

            for (var cell = 0; cell < Board.CellCount; cell++)
            {
                if (_values[cell] != 0) { continue; }
                SetValue(cell, solution[cell]);
                return new Hint(HintKind.Revealed, cell, solution[cell], null);
            }

            throw new InvalidOperationException("誤りがなく全マスが埋まっているのに完成していません。");
        }

        private MoveVerdict CheckCell(int cell, int digit)
        {
            if (IsSolved) { return MoveVerdict.Solved; }
            if (!Board.IsCell(cell)) { return MoveVerdict.OutOfBoard; }
            if (!Board.IsDigit(digit)) { return MoveVerdict.InvalidDigit; }
            return IsGiven(cell) ? MoveVerdict.Given : MoveVerdict.Ok;
        }

        private void SetValue(int cell, int digit)
        {
            _values[cell] = digit;
            _notes[cell] = 0;
            var bit = Digits.Bit(digit);
            for (var i = 0; i < Board.PeerCount; i++) { _notes[Board.Peer(cell, i)] &= ~bit; }
            IsSolved = Current.Equals(Puzzle.Solution);
        }
    }
}
