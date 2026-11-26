using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using TileMotion;

namespace Game.Core.Runtime
{
    public enum VariableType
    {
        /// <summary>整数。</summary>
        Number,

        /// <summary>真偽（0/1 で持つ）。</summary>
        Flag,
    }

    public sealed class VariableDef
    {
        public VariableDef(string name, VariableType type, int initial)
        {
            Name = name;
            Type = type;
            Initial = initial;
        }

        public string Name { get; }
        public VariableType Type { get; }

        /// <summary>初期値（bool は 0/1）。</summary>
        public int Initial { get; }
    }

    public sealed class CharacterDef
    {
        public CharacterDef(string id, string name, uint color)
        {
            Id = id;
            Name = name;
            Color = color;
        }

        public string Id { get; }

        /// <summary>名前欄に出す表示名。</summary>
        public string Name { get; }

        /// <summary>名前の色（0xRRGGBB）。</summary>
        public uint Color { get; }
    }

    public sealed class EndingDef
    {
        public EndingDef(string id, string name)
        {
            Id = id;
            Name = name;
        }

        public string Id { get; }
        public string Name { get; }
    }

    /// <summary>
    /// 検証を通ったスクリプト（命令列と表）。不変で、実行器の全ての状態はこの外（<see cref="ScriptRunner"/>）にある。
    /// 作れるのはコンパイラだけ。
    /// </summary>
    public sealed class CompiledScript
    {
        private readonly Dictionary<string, int> _variableIndex = new(StringComparer.Ordinal);

        internal CompiledScript(
            IList<Instruction> instructions,
            IList<VariableDef> variables,
            IList<CharacterDef> characters,
            IList<EndingDef> endings,
            IDictionary<string, int> labels,
            int choiceCount,
            int flowLimit,
            int valueLimit)
        {
            Instructions = new ReadOnlyCollection<Instruction>(instructions);
            Variables = new ReadOnlyCollection<VariableDef>(variables);
            Characters = new ReadOnlyCollection<CharacterDef>(characters);
            Endings = new ReadOnlyCollection<EndingDef>(endings);
            Labels = new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(labels, StringComparer.Ordinal));
            ChoiceCount = choiceCount;
            FlowLimit = flowLimit;
            ValueLimit = valueLimit;
            for (int i = 0; i < variables.Count; i++) { _variableIndex[variables[i].Name] = i; }

            var texts = new List<string>();
            foreach (var ins in instructions)
            {
                if (ins.Kind == InstructionKind.Text) { texts.Add(ins.TextId); }
            }

            TextIds = texts.AsReadOnly();
            Fingerprint = ComputeFingerprint();
        }

        public IReadOnlyList<Instruction> Instructions { get; }
        public IReadOnlyList<VariableDef> Variables { get; }
        public IReadOnlyList<CharacterDef> Characters { get; }
        public IReadOnlyList<EndingDef> Endings { get; }

        /// <summary>ラベル名 → 番地。</summary>
        public IReadOnlyDictionary<string, int> Labels { get; }

        /// <summary>選択肢の箇所の数。</summary>
        public int ChoiceCount { get; }

        /// <summary>待ちのない命令を 1 回の送りで続けて実行できる上限。超えたら例外（検証は網羅探索でこれを超えないことを確かめる）。</summary>
        public int FlowLimit { get; }

        /// <summary>int 変数の値の範囲（±）。代入はこの範囲で飽和する。</summary>
        public int ValueLimit { get; }

        /// <summary>全ての文の ID（出てくる順）。</summary>
        public IReadOnlyList<string> TextIds { get; }

        /// <summary>命令列と表の指紋。セーブ・既読と照合し、スクリプトが変わったら拒否する。</summary>
        public uint Fingerprint { get; }

        /// <summary>変数の番号（ない名前は -1）。</summary>
        public int VariableIndex(string name) => _variableIndex.TryGetValue(name, out var i) ? i : -1;

        public int EndingIndex(string id)
        {
            for (int i = 0; i < Endings.Count; i++)
            {
                if (string.Equals(Endings[i].Id, id, StringComparison.Ordinal)) { return i; }
            }

            return -1;
        }

        private uint ComputeFingerprint()
        {
            var h = StateHash.Start();
            h.Mix(FlowLimit);
            h.Mix(ValueLimit);
            foreach (var v in Variables)
            {
                Mix(ref h, v.Name);
                h.Mix((int)v.Type);
                h.Mix(v.Initial);
            }

            foreach (var c in Characters)
            {
                Mix(ref h, c.Id);
                Mix(ref h, c.Name);
                h.Mix(c.Color);
            }

            foreach (var e in Endings)
            {
                Mix(ref h, e.Id);
                Mix(ref h, e.Name);
            }

            foreach (var ins in Instructions)
            {
                h.Mix((int)ins.Kind);
                Mix(ref h, ins.Text);
                h.Mix(ins.Speaker);
                Mix(ref h, ins.TextId);
                Mix(ref h, ins.Background);
                h.Mix(ins.Character);
                Mix(ref h, ins.Expression);
                h.Mix((int)ins.Slot);
                h.Mix(ins.Target);
                h.Mix(ins.Ending);
                if (ins.Assignment != null) { Mix(ref h, ins.Assignment); }
                if (ins.Condition != null)
                {
                    h.Mix(ins.Condition.Variable);
                    h.Mix((int)ins.Condition.Op);
                    h.Mix(ins.Condition.Value);
                }

                h.Mix(ins.Options.Count);
                foreach (var o in ins.Options)
                {
                    Mix(ref h, o.Text);
                    h.Mix(o.Target);
                    h.Mix(o.Assignments.Count);
                    foreach (var a in o.Assignments) { Mix(ref h, a); }
                }
            }

            return h.Value;
        }

        private static void Mix(ref StateHash h, Assignment a)
        {
            h.Mix(a.Variable);
            h.Mix((int)a.Op);
            h.Mix(a.Value);
        }

        private static void Mix(ref StateHash h, string text)
        {
            h.Mix(text.Length);
            foreach (var ch in text) { h.Mix(ch); }
        }
    }
}
