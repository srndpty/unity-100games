using System;
using System.Collections.Generic;

namespace Game.Core.Runtime
{
    /// <summary>
    /// 実行器の保存用の写し（番地・変数・表示状態）。待っているもの（文か選択肢か）は番地の命令から決まる。
    /// 選択肢を待っている間の写しには、まだ選択の代入が入っていない（選んだ時点で初めて適用される）。
    /// </summary>
    public sealed class RunnerSnapshot
    {
        public RunnerSnapshot(int pc, IReadOnlyList<int> variables, string background, IReadOnlyList<Portrait> slots)
        {
            if (variables == null) { throw new ArgumentNullException(nameof(variables)); }
            if (slots == null) { throw new ArgumentNullException(nameof(slots)); }
            Pc = pc;
            var v = new int[variables.Count];
            for (int i = 0; i < v.Length; i++) { v[i] = variables[i]; }
            Variables = Array.AsReadOnly(v);
            Background = background ?? throw new ArgumentNullException(nameof(background));
            var s = new Portrait[slots.Count];
            for (int i = 0; i < s.Length; i++) { s[i] = slots[i]; }
            Slots = Array.AsReadOnly(s);
        }

        public int Pc { get; }
        public IReadOnlyList<int> Variables { get; }
        public string Background { get; }
        public IReadOnlyList<Portrait> Slots { get; }
    }
}
