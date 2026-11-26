using System;
using System.Collections.Generic;
using TileMotion;

namespace Game.Core.Runtime
{
    /// <summary>実行器が待っているもの。</summary>
    public enum RunnerPhase
    {
        /// <summary>文を出して送りを待つ。</summary>
        Text,

        /// <summary>選択肢を出して選択を待つ。</summary>
        Choice,

        /// <summary>結末に着いた。以後の操作は全て拒否する。</summary>
        Ended,
    }

    /// <summary>立ち絵 1 枠の中身（空なら Character が -1）。</summary>
    public readonly struct Portrait : IEquatable<Portrait>
    {
        public static readonly Portrait Empty = new(-1, "");

        public Portrait(int character, string expression)
        {
            Character = character;
            Expression = expression;
        }

        public int Character { get; }
        public string Expression { get; }
        public bool IsEmpty => Character < 0;

        public bool Equals(Portrait other) => Character == other.Character && string.Equals(Expression, other.Expression, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is Portrait p && Equals(p);

        public override int GetHashCode() => (Character * 397) ^ StringComparer.Ordinal.GetHashCode(Expression ?? "");

        public static bool operator ==(Portrait left, Portrait right) => left.Equals(right);

        public static bool operator !=(Portrait left, Portrait right) => !left.Equals(right);
    }

    /// <summary>操作の結果。拒否なら状態は何も変わっていない。</summary>
    public readonly struct RunResult
    {
        private RunResult(bool accepted, string reason)
        {
            Accepted = accepted;
            Reason = reason;
        }

        public bool Accepted { get; }
        public string Reason { get; }

        public static RunResult Ok => new(true, "");

        public static RunResult Reject(string reason) => new(false, reason);
    }

    /// <summary>1 回の送りで待ちのない命令が上限を超えた（終わらない流れ）。</summary>
    public sealed class FlowLimitExceededException : InvalidOperationException
    {
        public FlowLimitExceededException()
        {
        }

        public FlowLimitExceededException(string message)
            : base(message)
        {
        }

        public FlowLimitExceededException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public FlowLimitExceededException(string message, int line)
            : base(message)
        {
            Line = line;
        }

        /// <summary>上限を超えた命令の行番号（分からなければ 0）。</summary>
        public int Line { get; }
    }

    /// <summary>
    /// 命令列の実行器。番地・変数・表示状態（背景と立ち絵 3 枠）だけを持ち、DSL の書き方は知らない。
    /// 送り（<see cref="Advance"/>）と選択（<see cref="Choose"/>）で次の待ちまで命令を進める。
    /// 表示状態を持つので、読み込み（<see cref="TryRestore"/>）は命令を再生せずに画面を戻せる。
    /// </summary>
    public sealed class ScriptRunner
    {
        public const int SlotCount = 3;

        private readonly int[] _vars;
        private readonly Portrait[] _slots = new Portrait[SlotCount];

        public ScriptRunner(CompiledScript script)
        {
            Script = script ?? throw new ArgumentNullException(nameof(script));
            _vars = new int[script.Variables.Count];
            for (int i = 0; i < _vars.Length; i++) { _vars[i] = script.Variables[i].Initial; }
            for (int i = 0; i < SlotCount; i++) { _slots[i] = Portrait.Empty; }
            Background = "";
            Pc = 0;
            Flow();
        }

        private ScriptRunner(ScriptRunner other)
        {
            Script = other.Script;
            _vars = (int[])other._vars.Clone();
            Array.Copy(other._slots, _slots, SlotCount);
            Background = other.Background;
            Pc = other.Pc;
            Phase = other.Phase;
        }

        public CompiledScript Script { get; }

        /// <summary>今待っている命令の番地。</summary>
        public int Pc { get; private set; }

        public RunnerPhase Phase { get; private set; }

        /// <summary>今待っている命令（文・選択肢・結末）。</summary>
        public Instruction Current => Script.Instructions[Pc];

        /// <summary>今の背景の ID（まだなければ空）。</summary>
        public string Background { get; private set; }

        /// <summary>結末の番号（終わっていなければ -1）。</summary>
        public int EndingIndex => Phase == RunnerPhase.Ended ? Current.Ending : -1;

        public Portrait Slot(StageSlot slot) => _slots[(int)slot];

        public int Value(int variable) => _vars[variable];

        public int Value(string name)
        {
            var i = Script.VariableIndex(name);
            if (i < 0) { throw new ArgumentException($"変数 {name} はありません", nameof(name)); }
            return _vars[i];
        }

        public ScriptRunner Clone() => new(this);

        /// <summary>文を送る。文を待っていなければ拒否。</summary>
        public RunResult Advance()
        {
            if (Phase != RunnerPhase.Text) { return RunResult.Reject(Phase == RunnerPhase.Ended ? "終わっています" : "選択肢を選んでください"); }
            Pc++;
            Flow();
            return RunResult.Ok;
        }

        /// <summary>選択肢を選ぶ。代入を順に適用してから飛ぶ。選択肢を待っていない・範囲外なら拒否。</summary>
        public RunResult Choose(int option)
        {
            if (Phase != RunnerPhase.Choice) { return RunResult.Reject(Phase == RunnerPhase.Ended ? "終わっています" : "選択肢がありません"); }
            var options = Current.Options;
            if (option < 0 || option >= options.Count) { return RunResult.Reject($"選択肢は 0〜{options.Count - 1}（{option}）"); }
            var chosen = options[option];
            foreach (var a in chosen.Assignments) { Apply(a); }
            Pc = chosen.Target;
            Flow();
            return RunResult.Ok;
        }

        /// <summary>今の状態の保存用の写し。</summary>
        public RunnerSnapshot Snapshot() => new(Pc, _vars, Background, _slots);

        /// <summary>写しから実行器を作り直す。スクリプトと合わない写し（番地・変数・表示）は理由付きで拒否する。</summary>
        public static bool TryRestore(CompiledScript script, RunnerSnapshot snapshot, out ScriptRunner? runner, out string error)
        {
            if (script == null) { throw new ArgumentNullException(nameof(script)); }
            if (snapshot == null) { throw new ArgumentNullException(nameof(snapshot)); }
            runner = null;
            error = Check(script, snapshot);
            if (error.Length > 0) { return false; }
            var r = new ScriptRunner(script, snapshot);
            runner = r;
            return true;
        }

        /// <summary>以後の動きと表示を決める状態のハッシュ（スクリプトの指紋を含む）。</summary>
        public uint Hash()
        {
            var h = StateHash.Start();
            h.Mix(Script.Fingerprint);
            h.Mix(Pc);
            h.Mix((int)Phase);
            foreach (var v in _vars) { h.Mix(v); }
            MixText(ref h, Background);
            foreach (var s in _slots)
            {
                h.Mix(s.Character);
                MixText(ref h, s.Expression);
            }

            return h.Value;
        }

        private ScriptRunner(CompiledScript script, RunnerSnapshot s)
        {
            Script = script;
            _vars = new int[script.Variables.Count];
            for (int i = 0; i < _vars.Length; i++) { _vars[i] = s.Variables[i]; }
            for (int i = 0; i < SlotCount; i++) { _slots[i] = s.Slots[i]; }
            Background = s.Background;
            Pc = s.Pc;
            Phase = PhaseOf(script.Instructions[Pc]);
        }

        private static string Check(CompiledScript script, RunnerSnapshot s)
        {
            var ins = script.Instructions;
            if (s.Pc < 0 || s.Pc >= ins.Count) { return $"番地 {s.Pc} はスクリプトの外です"; }
            var at = ins[s.Pc];
            if (at.Kind is not (InstructionKind.Text or InstructionKind.Choice)) { return $"番地 {s.Pc} は文でも選択肢でもありません"; }
            if (s.Variables.Count != script.Variables.Count) { return $"変数の数が違います（{s.Variables.Count}）"; }
            for (int i = 0; i < s.Variables.Count; i++)
            {
                var def = script.Variables[i];
                var v = s.Variables[i];
                var ok = def.Type == VariableType.Flag ? v is 0 or 1 : v >= -script.ValueLimit && v <= script.ValueLimit;
                if (!ok) { return $"変数 {def.Name} の値 {v} は範囲外です"; }
            }

            if (s.Background.Length > 0 && !Uses(ins, x => x.Kind == InstructionKind.Background && x.Background == s.Background))
            {
                return $"背景 {s.Background} はスクリプトにありません";
            }

            if (s.Slots.Count != SlotCount) { return $"立ち絵の枠の数が違います（{s.Slots.Count}）"; }
            var seen = new HashSet<int>();
            foreach (var p in s.Slots)
            {
                if (p.IsEmpty) { continue; }
                if (p.Character >= script.Characters.Count) { return $"人物 {p.Character} はありません"; }
                if (!seen.Add(p.Character)) { return $"人物 {script.Characters[p.Character].Id} が 2 枠にいます"; }
                if (!Uses(ins, x => x.Kind == InstructionKind.Show && x.Character == p.Character && x.Expression == p.Expression))
                {
                    return $"立ち絵 {script.Characters[p.Character].Id} {p.Expression} はスクリプトにありません";
                }
            }

            return "";
        }

        private static bool Uses(IReadOnlyList<Instruction> ins, Func<Instruction, bool> match)
        {
            foreach (var x in ins)
            {
                if (match(x)) { return true; }
            }

            return false;
        }

        private static RunnerPhase PhaseOf(Instruction ins) => ins.Kind switch
        {
            InstructionKind.Text => RunnerPhase.Text,
            InstructionKind.Choice => RunnerPhase.Choice,
            _ => RunnerPhase.Ended,
        };

        /// <summary>次の待ちまで待ちのない命令を実行する。</summary>
        private void Flow()
        {
            var ins = Script.Instructions;
            int steps = 0;
            while (true)
            {
                if (Pc < 0 || Pc >= ins.Count) { throw new InvalidOperationException($"番地 {Pc} はスクリプトの外です"); }
                var x = ins[Pc];
                if (x.Waits)
                {
                    Phase = PhaseOf(x);
                    return;
                }

                if (++steps > Script.FlowLimit)
                {
                    throw new FlowLimitExceededException($"行 {x.Line}：待ちのない命令が {Script.FlowLimit} 回を超えました（終わらない流れ）", x.Line);
                }

                switch (x.Kind)
                {
                    case InstructionKind.Background:
                        Background = x.Background;
                        Pc++;
                        break;
                    case InstructionKind.Show:
                        RemoveCharacter(x.Character);
                        _slots[(int)x.Slot] = new Portrait(x.Character, x.Expression);
                        Pc++;
                        break;
                    case InstructionKind.Hide:
                        RemoveCharacter(x.Character);
                        Pc++;
                        break;
                    case InstructionKind.Clear:
                        for (int i = 0; i < SlotCount; i++) { _slots[i] = Portrait.Empty; }
                        Pc++;
                        break;
                    case InstructionKind.Assign:
                        Apply(x.Assignment!);
                        Pc++;
                        break;
                    case InstructionKind.JumpIf:
                        Pc = x.Condition!.Holds(_vars[x.Condition.Variable]) ? x.Target : Pc + 1;
                        break;
                    case InstructionKind.Jump:
                        Pc = x.Target;
                        break;
                    default:
                        throw new InvalidOperationException($"未知の命令 {x.Kind}");
                }
            }
        }

        private void RemoveCharacter(int character)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (_slots[i].Character == character) { _slots[i] = Portrait.Empty; }
            }
        }

        private void Apply(Assignment a)
        {
            long v = _vars[a.Variable];
            v = a.Op switch
            {
                AssignOp.Set => a.Value,
                AssignOp.Add => v + a.Value,
                AssignOp.Subtract => v - a.Value,
                _ => throw new InvalidOperationException($"未知の代入 {a.Op}"),
            };
            var limit = Script.ValueLimit;
            if (Script.Variables[a.Variable].Type == VariableType.Number) { v = Math.Max(-limit, Math.Min(limit, v)); }
            _vars[a.Variable] = (int)v;
        }

        private static void MixText(ref StateHash h, string text)
        {
            h.Mix(text.Length);
            foreach (var ch in text) { h.Mix(ch); }
        }
    }
}
