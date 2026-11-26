using System;
using System.Collections.Generic;

namespace Game.Core.Runtime
{
    /// <summary>命令の種類。実行器はこの列だけを読み、DSL の書き方を知らない。</summary>
    public enum InstructionKind
    {
        /// <summary>文（地の文・台詞）。読み手の送りを待つ。</summary>
        Text,
        Background,
        Show,
        Hide,
        Clear,
        Assign,
        JumpIf,
        Jump,
        /// <summary>選択肢。読み手の選択を待つ。</summary>
        Choice,
        /// <summary>結末。ここで終わる。</summary>
        Ending,
    }

    /// <summary>立ち絵の置き場所。</summary>
    public enum StageSlot
    {
        Left,
        Center,
        Right,
    }

    public enum AssignOp
    {
        Set,
        Add,
        Subtract,
    }

    public enum CompareOp
    {
        Equal,
        NotEqual,
        Less,
        LessOrEqual,
        Greater,
        GreaterOrEqual,
    }

    /// <summary>変数への代入。bool は 0/1 の Set だけ。</summary>
    public sealed class Assignment
    {
        public Assignment(int variable, AssignOp op, int value)
        {
            Variable = variable;
            Op = op;
            Value = value;
        }

        public int Variable { get; }
        public AssignOp Op { get; }
        public int Value { get; }
    }

    /// <summary>条件。bool の `flag` は (Equal, 1)、`!flag` は (Equal, 0) になる。</summary>
    public sealed class Condition
    {
        public Condition(int variable, CompareOp op, int value)
        {
            Variable = variable;
            Op = op;
            Value = value;
        }

        public int Variable { get; }
        public CompareOp Op { get; }
        public int Value { get; }

        public bool Holds(int actual) => Op switch
        {
            CompareOp.Equal => actual == Value,
            CompareOp.NotEqual => actual != Value,
            CompareOp.Less => actual < Value,
            CompareOp.LessOrEqual => actual <= Value,
            CompareOp.Greater => actual > Value,
            CompareOp.GreaterOrEqual => actual >= Value,
            _ => throw new InvalidOperationException($"未知の比較 {Op}"),
        };
    }

    /// <summary>選択肢の 1 項目。選んだら代入を順に適用してから飛ぶ。</summary>
    public sealed class ChoiceOption
    {
        public ChoiceOption(string text, int target, IReadOnlyList<Assignment> assignments)
        {
            Text = text;
            Target = target;
            Assignments = assignments;
        }

        public string Text { get; }
        public int Target { get; }
        public IReadOnlyList<Assignment> Assignments { get; }
    }

    /// <summary>1 命令。種類ごとに使う項目だけが意味を持つ（使わない項目は既定値）。</summary>
    public sealed class Instruction
    {
        private static readonly IReadOnlyList<ChoiceOption> _noOptions = Array.AsReadOnly(Array.Empty<ChoiceOption>());

        private Instruction(InstructionKind kind, int line)
        {
            Kind = kind;
            Line = line;
        }

        public InstructionKind Kind { get; }

        /// <summary>DSL の行番号（1 から）。エラーと README の照合に使う。</summary>
        public int Line { get; }

        /// <summary>Text：本文。Ending：結末の名前。</summary>
        public string Text { get; private set; } = "";

        /// <summary>Text：話者の番号（地の文は -1）。</summary>
        public int Speaker { get; private set; } = -1;

        /// <summary>Text：既読の記録に使う安定した ID（ラベル名#ラベル内の連番）。</summary>
        public string TextId { get; private set; } = "";

        /// <summary>Background：背景の ID。</summary>
        public string Background { get; private set; } = "";

        /// <summary>Show・Hide：人物の番号。</summary>
        public int Character { get; private set; } = -1;

        /// <summary>Show：表情の ID。</summary>
        public string Expression { get; private set; } = "";

        public StageSlot Slot { get; private set; }

        public Assignment? Assignment { get; private set; }

        public Condition? Condition { get; private set; }

        /// <summary>Jump・JumpIf：飛び先の番地。</summary>
        public int Target { get; private set; } = -1;

        public IReadOnlyList<ChoiceOption> Options { get; private set; } = _noOptions;

        /// <summary>Ending：結末の番号。</summary>
        public int Ending { get; private set; } = -1;

        /// <summary>読み手の操作を待つ命令か。</summary>
        public bool Waits => Kind is InstructionKind.Text or InstructionKind.Choice or InstructionKind.Ending;

        public static Instruction NewText(int line, int speaker, string text, string textId) =>
            new(InstructionKind.Text, line) { Speaker = speaker, Text = text, TextId = textId };

        public static Instruction NewBackground(int line, string background) =>
            new(InstructionKind.Background, line) { Background = background };

        public static Instruction NewShow(int line, int character, string expression, StageSlot slot) =>
            new(InstructionKind.Show, line) { Character = character, Expression = expression, Slot = slot };

        public static Instruction NewHide(int line, int character) =>
            new(InstructionKind.Hide, line) { Character = character };

        public static Instruction NewClear(int line) => new(InstructionKind.Clear, line);

        public static Instruction NewAssign(int line, Assignment assignment) =>
            new(InstructionKind.Assign, line) { Assignment = assignment };

        public static Instruction NewJumpIf(int line, Condition condition, int target) =>
            new(InstructionKind.JumpIf, line) { Condition = condition, Target = target };

        public static Instruction NewJump(int line, int target) =>
            new(InstructionKind.Jump, line) { Target = target };

        public static Instruction NewChoice(int line, IReadOnlyList<ChoiceOption> options) =>
            new(InstructionKind.Choice, line) { Options = options };

        public static Instruction NewEnding(int line, int ending, string name) =>
            new(InstructionKind.Ending, line) { Ending = ending, Text = name };
    }
}
