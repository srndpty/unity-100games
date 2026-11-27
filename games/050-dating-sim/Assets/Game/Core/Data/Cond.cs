using System;

namespace Game.Core.Data
{
    /// <summary>条件式が読む状態（試合と区間の検証が実装する）。</summary>
    public interface IConditionState
    {
        int Stat(int index);
        bool Flag(int index);
        int Day { get; }
        Weekday Weekday { get; }

        /// <summary>今日選んだ行動（まだなら -1）。</summary>
        int Action { get; }
    }

    public enum OperandKind
    {
        Const,
        Stat,
        Day,
        Weekday,
        Action,
    }

    /// <summary>比較の片側。</summary>
    public readonly struct Operand
    {
        internal Operand(OperandKind kind, int value)
        {
            Kind = kind;
            Value = value;
        }

        public OperandKind Kind { get; }

        /// <summary>Const の値・Stat の番号（ほかは使わない）。曜日・行動の定数は番号。</summary>
        public int Value { get; }

        public int Read(IConditionState s) => Kind switch
        {
            OperandKind.Stat => s.Stat(Value),
            OperandKind.Day => s.Day,
            OperandKind.Weekday => (int)s.Weekday,
            OperandKind.Action => s.Action,
            _ => Value,
        };
    }

    public enum CompareOp
    {
        Eq,
        Ne,
        Lt,
        Le,
        Gt,
        Ge,
    }

    public enum CondKind
    {
        Compare,
        Flag,
        Not,
        And,
        Or,
    }

    /// <summary>区間の検証の 3 値（必ず偽・不明・必ず真）。</summary>
    public enum Tri
    {
        False,
        Maybe,
        True,
    }

    /// <summary>
    /// コンパイルした条件式（式の木）。比較・フラグ・not・and・or。型の検証はコンパイラが済ませている。
    /// 同じ木を、試合の状態での評価（<see cref="Eval"/>）と、取りうる範囲での 3 値の評価（<see cref="EvalRange"/>）に使う。
    /// </summary>
    public sealed class Cond
    {
        private Cond(CondKind kind, Operand left, CompareOp op, Operand right, int flag, Cond? a, Cond? b)
        {
            Kind = kind;
            Left = left;
            Op = op;
            Right = right;
            FlagIndex = flag;
            A = a;
            B = b;
        }

        public CondKind Kind { get; }
        public Operand Left { get; }
        public CompareOp Op { get; }
        public Operand Right { get; }
        public int FlagIndex { get; }
        public Cond? A { get; }
        public Cond? B { get; }

        internal static Cond Compare(Operand left, CompareOp op, Operand right) => new(CondKind.Compare, left, op, right, -1, null, null);

        internal static Cond Flag(int index) => new(CondKind.Flag, default, default, default, index, null, null);

        internal static Cond Not(Cond a) => new(CondKind.Not, default, default, default, -1, a, null);

        internal static Cond And(Cond a, Cond b) => new(CondKind.And, default, default, default, -1, a, b);

        internal static Cond Or(Cond a, Cond b) => new(CondKind.Or, default, default, default, -1, a, b);

        public bool Eval(IConditionState s)
        {
            if (s == null) { throw new ArgumentNullException(nameof(s)); }
            return Kind switch
            {
                CondKind.Flag => s.Flag(FlagIndex),
                CondKind.Not => !A!.Eval(s),
                CondKind.And => A!.Eval(s) && B!.Eval(s),
                CondKind.Or => A!.Eval(s) || B!.Eval(s),
                _ => Test(Left.Read(s), Op, Right.Read(s)),
            };
        }

        /// <summary>
        /// 取りうる範囲での 3 値の評価。range は被演算子の範囲（最小・最大）を返し、flagPossible はフラグが立ちうるかを返す。
        /// 範囲は大きめに見積もってよく、そのとき結果は「不明」に寄る（「必ず偽」は言い切れるときだけ）。
        /// </summary>
        public Tri EvalRange(Func<Operand, (int Min, int Max)> range, Func<int, bool> flagPossible)
        {
            if (range == null) { throw new ArgumentNullException(nameof(range)); }
            if (flagPossible == null) { throw new ArgumentNullException(nameof(flagPossible)); }
            switch (Kind)
            {
                case CondKind.Flag:
                    return flagPossible(FlagIndex) ? Tri.Maybe : Tri.False;
                case CondKind.Not:
                    return A!.EvalRange(range, flagPossible) switch
                    {
                        Tri.True => Tri.False,
                        Tri.False => Tri.True,
                        _ => Tri.Maybe,
                    };
                case CondKind.And:
                    {
                        var a = A!.EvalRange(range, flagPossible);
                        var b = B!.EvalRange(range, flagPossible);
                        if (a == Tri.False || b == Tri.False) { return Tri.False; }
                        return a == Tri.True && b == Tri.True ? Tri.True : Tri.Maybe;
                    }

                case CondKind.Or:
                    {
                        var a = A!.EvalRange(range, flagPossible);
                        var b = B!.EvalRange(range, flagPossible);
                        if (a == Tri.True || b == Tri.True) { return Tri.True; }
                        return a == Tri.False && b == Tri.False ? Tri.False : Tri.Maybe;
                    }

                default:
                    return CompareRange(range(Left), Op, range(Right));
            }
        }

        private static bool Test(int a, CompareOp op, int b) => op switch
        {
            CompareOp.Eq => a == b,
            CompareOp.Ne => a != b,
            CompareOp.Lt => a < b,
            CompareOp.Le => a <= b,
            CompareOp.Gt => a > b,
            _ => a >= b,
        };

        private static Tri CompareRange((int Min, int Max) a, CompareOp op, (int Min, int Max) b)
        {
            bool always;
            bool never;
            switch (op)
            {
                case CompareOp.Eq:
                    always = a.Min == a.Max && b.Min == b.Max && a.Min == b.Min;
                    never = a.Max < b.Min || b.Max < a.Min;
                    break;
                case CompareOp.Ne:
                    always = a.Max < b.Min || b.Max < a.Min;
                    never = a.Min == a.Max && b.Min == b.Max && a.Min == b.Min;
                    break;
                case CompareOp.Lt:
                    always = a.Max < b.Min;
                    never = a.Min >= b.Max;
                    break;
                case CompareOp.Le:
                    always = a.Max <= b.Min;
                    never = a.Min > b.Max;
                    break;
                case CompareOp.Gt:
                    always = a.Min > b.Max;
                    never = a.Max <= b.Min;
                    break;
                default:
                    always = a.Min >= b.Max;
                    never = a.Max < b.Min;
                    break;
            }

            return always ? Tri.True : never ? Tri.False : Tri.Maybe;
        }
    }
}
