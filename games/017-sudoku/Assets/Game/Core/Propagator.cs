using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>1 マスが決まった理由。</summary>
    public enum DeductionReason
    {
        /// <summary>そのマスに入りうる数字が 1 つだけ。</summary>
        NakedSingle,

        /// <summary>ある単位（行・列・ブロック）で、その数字が入りうるマスが 1 つだけ。</summary>
        HiddenSingle,
    }

    /// <summary>制約伝播で決まった 1 マス。</summary>
    public readonly struct Deduction : IEquatable<Deduction>
    {
        public Deduction(int cell, int digit, DeductionReason reason, int unit)
        {
            Cell = cell;
            Digit = digit;
            Reason = reason;
            Unit = unit;
        }

        public int Cell { get; }
        public int Digit { get; }
        public DeductionReason Reason { get; }

        /// <summary>HiddenSingle の単位（<see cref="Board.KindOf"/>）。NakedSingle では -1。</summary>
        public int Unit { get; }

        public bool Equals(Deduction other) => Cell == other.Cell && Digit == other.Digit && Reason == other.Reason && Unit == other.Unit;

        public override bool Equals(object? obj) => obj is Deduction other && Equals(other);

        public override int GetHashCode() => unchecked((((Cell * 10 + Digit) * 2 + (int)Reason) * 31) + Unit);

        public static bool operator ==(Deduction left, Deduction right) => left.Equals(right);

        public static bool operator !=(Deduction left, Deduction right) => !left.Equals(right);

        public override string ToString() => $"{Reason}(cell {Cell}, {Digit}, unit {Unit})";
    }

    public enum PropagationStatus
    {
        /// <summary>全マスが埋まった。</summary>
        Solved,

        /// <summary>矛盾はないが、これ以上は伝播で決まらない。</summary>
        Stuck,

        /// <summary>重複、候補の尽きたマス、または数字の入る場所がない単位がある（解なし）。</summary>
        Contradiction,
    }

    /// <summary>制約伝播の結果：決めた順のマスと、伝播を終えた盤（矛盾したときは元の盤）。</summary>
    public sealed class Propagation
    {
        internal Propagation(PropagationStatus status, Grid grid, IReadOnlyList<Deduction> steps)
        {
            Status = status;
            Grid = grid;
            Steps = steps;
        }

        public PropagationStatus Status { get; }
        public Grid Grid { get; }
        public IReadOnlyList<Deduction> Steps { get; }
    }

    /// <summary>
    /// 制約伝播だけを行う（分岐はしない）。使う推論は Naked Single と Hidden Single の 2 つ。
    /// 調べる順序は固定（マス番号の順に Naked Single、次に行・列・ブロックの順、数字の小さい順に Hidden Single）で、
    /// 同じ盤からは必ず同じ手順になる。
    /// </summary>
    public static class Propagator
    {
        /// <summary>次に決まる 1 マス。矛盾している盤や、伝播で決まるマスがない盤では null。</summary>
        public static Deduction? FindNext(Grid grid)
        {
            if (grid == null) { throw new ArgumentNullException(nameof(grid)); }
            var state = new CandidateState(grid);
            return Next(state, out var deduction) == StepResult.Found ? deduction : null;
        }

        /// <summary>決まるマスがなくなるまで伝播する。</summary>
        public static Propagation Run(Grid grid)
        {
            if (grid == null) { throw new ArgumentNullException(nameof(grid)); }
            var state = new CandidateState(grid);
            var steps = new List<Deduction>();
            var status = Run(state, steps);
            return new Propagation(status, status == PropagationStatus.Contradiction ? grid : state.ToGrid(), steps.AsReadOnly());
        }

        /// <summary>state を伝播しきる。steps を渡すと決めた順に記録する。</summary>
        internal static PropagationStatus Run(CandidateState state, List<Deduction>? steps)
        {
            while (true)
            {
                switch (Next(state, out var deduction))
                {
                    case StepResult.Contradiction:
                        return PropagationStatus.Contradiction;
                    case StepResult.None:
                        return state.IsSolved ? PropagationStatus.Solved : PropagationStatus.Stuck;
                }

                state.Place(deduction.Cell, deduction.Digit);
                steps?.Add(deduction);
            }
        }

        private static StepResult Next(CandidateState state, out Deduction deduction)
        {
            deduction = default;
            if (state.IsContradiction) { return StepResult.Contradiction; }

            for (var cell = 0; cell < Board.CellCount; cell++)
            {
                if (state.Value(cell) != 0) { continue; }
                var mask = state.Mask(cell);
                if (mask == 0) { return StepResult.Contradiction; }
                if (Digits.Count(mask) == 1)
                {
                    deduction = new Deduction(cell, Digits.Lowest(mask), DeductionReason.NakedSingle, -1);
                    return StepResult.Found;
                }
            }

            var found = false;
            for (var unit = 0; unit < Board.UnitCount; unit++)
            {
                for (var digit = 1; digit <= Board.Size; digit++)
                {
                    var bit = Digits.Bit(digit);
                    var places = 0;
                    var place = -1;
                    var placed = false;
                    for (var i = 0; i < Board.Size; i++)
                    {
                        var cell = Board.UnitCell(unit, i);
                        if (state.Value(cell) == digit)
                        {
                            placed = true;
                            break;
                        }

                        if ((state.Mask(cell) & bit) != 0)
                        {
                            places++;
                            place = cell;
                        }
                    }

                    if (placed) { continue; }
                    if (places == 0) { return StepResult.Contradiction; }
                    if (places == 1 && !found)
                    {
                        deduction = new Deduction(place, digit, DeductionReason.HiddenSingle, unit);
                        found = true;
                    }
                }
            }

            return found ? StepResult.Found : StepResult.None;
        }

        private enum StepResult
        {
            Found,
            None,
            Contradiction,
        }
    }
}
