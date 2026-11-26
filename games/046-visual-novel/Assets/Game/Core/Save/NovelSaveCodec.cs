using System;
using System.Collections.Generic;
using System.Text;
using Game.Core.Runtime;

namespace Game.Core.Save
{
    /// <summary>
    /// クイックセーブ（実行器の写し）と既読の記録の直列化。どちらもスクリプトの指紋と版を照合し、合わなければ理由付きで拒否する。
    /// </summary>
    public static class NovelSaveCodec
    {
        public const int QuickVersion = 1;
        public const int ReadVersion = 1;
        private const string QuickKind = "quick";
        private const string ReadKind = "read";
        private static readonly string[] _slotKeys = { "slot.left", "slot.center", "slot.right" };

        // ---- クイックセーブ ----

        /// <summary>実行器の写しを保存の文にする。文か選択肢を待っているときだけ保存できる。</summary>
        public static string EncodeQuick(ScriptRunner runner)
        {
            if (runner == null) { throw new ArgumentNullException(nameof(runner)); }
            if (runner.Phase == RunnerPhase.Ended) { throw new InvalidOperationException("結末の後は保存できません"); }
            var script = runner.Script;
            var s = runner.Snapshot();
            var sb = new StringBuilder();
            sb.Append(SaveText.Line("kind", QuickKind));
            sb.Append(SaveText.Line("version", SaveText.Num(QuickVersion)));
            sb.Append(SaveText.Line("fingerprint", SaveText.Hex(script.Fingerprint)));
            sb.Append(SaveText.Line("pc", SaveText.Num(s.Pc)));
            for (int i = 0; i < script.Variables.Count; i++)
            {
                sb.Append(SaveText.Line("var." + script.Variables[i].Name, SaveText.Num(s.Variables[i])));
            }

            sb.Append(SaveText.Line("bg", s.Background));
            for (int i = 0; i < ScriptRunner.SlotCount; i++)
            {
                var p = s.Slots[i];
                sb.Append(SaveText.Line(_slotKeys[i], p.IsEmpty ? "-" : script.Characters[p.Character].Id + ":" + p.Expression));
            }

            return SaveText.Sign(sb.ToString());
        }

        /// <summary>保存の文から実行器を作り直す。書式・版・指紋・中身（番地・変数・表示）を全て確かめる。</summary>
        public static bool TryDecodeQuick(CompiledScript script, string? text, out ScriptRunner? runner, out SaveError? error)
        {
            if (script == null) { throw new ArgumentNullException(nameof(script)); }
            runner = null;
            if (!SaveText.TryOpen(text, QuickKind, QuickVersion, script.Fingerprint, out var map, out error)) { return false; }
            var expected = 4 + script.Variables.Count + 1 + ScriptRunner.SlotCount;
            if (map.Count != expected) { return SaveText.Fail(SaveErrorKind.Invalid, $"キーの数が違います（{map.Count}）", out error); }
            if (!SaveText.TryInt(map, "pc", out var pc, out error)) { return false; }
            var vars = new int[script.Variables.Count];
            for (int i = 0; i < vars.Length; i++)
            {
                if (!SaveText.TryInt(map, "var." + script.Variables[i].Name, out vars[i], out error)) { return false; }
            }

            if (!SaveText.TryString(map, "bg", out var bg, out error)) { return false; }
            var slots = new Portrait[ScriptRunner.SlotCount];
            for (int i = 0; i < slots.Length; i++)
            {
                if (!SaveText.TryString(map, _slotKeys[i], out var v, out error)) { return false; }
                if (!TryPortrait(script, v, out slots[i])) { return SaveText.Fail(SaveErrorKind.Invalid, $"立ち絵 {v} は不正です", out error); }
            }

            if (!ScriptRunner.TryRestore(script, new RunnerSnapshot(pc, vars, bg, slots), out runner, out var reason))
            {
                return SaveText.Fail(SaveErrorKind.Invalid, reason, out error);
            }

            error = null;
            return true;
        }

        // ---- 既読の記録 ----

        public static string EncodeRead(ReadLog log)
        {
            if (log == null) { throw new ArgumentNullException(nameof(log)); }
            var script = log.Script;
            var sb = new StringBuilder();
            sb.Append(SaveText.Line("kind", ReadKind));
            sb.Append(SaveText.Line("version", SaveText.Num(ReadVersion)));
            sb.Append(SaveText.Line("fingerprint", SaveText.Hex(script.Fingerprint)));
            var ids = log.ReadIdsInOrder();
            sb.Append(SaveText.Line("read.count", SaveText.Num(ids.Count)));
            for (int i = 0; i < ids.Count; i++) { sb.Append(SaveText.Line("read." + SaveText.Num(i), ids[i])); }
            for (int i = 0; i < script.Endings.Count; i++)
            {
                sb.Append(SaveText.Line("ending." + script.Endings[i].Id, log.Reached(i) ? "1" : "0"));
            }

            return SaveText.Sign(sb.ToString());
        }

        public static bool TryDecodeRead(CompiledScript script, string? text, out ReadLog? log, out SaveError? error)
        {
            if (script == null) { throw new ArgumentNullException(nameof(script)); }
            log = null;
            if (!SaveText.TryOpen(text, ReadKind, ReadVersion, script.Fingerprint, out var map, out error)) { return false; }
            if (!SaveText.TryInt(map, "read.count", out var count, out error)) { return false; }
            if (count < 0 || count > script.TextIds.Count) { return SaveText.Fail(SaveErrorKind.Invalid, $"既読の数 {count} は範囲外です", out error); }
            if (map.Count != 4 + count + script.Endings.Count) { return SaveText.Fail(SaveErrorKind.Invalid, $"キーの数が違います（{map.Count}）", out error); }
            var result = new ReadLog(script);
            var known = new HashSet<string>(script.TextIds, StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                if (!SaveText.TryString(map, "read." + SaveText.Num(i), out var id, out error)) { return false; }
                if (!known.Contains(id)) { return SaveText.Fail(SaveErrorKind.Invalid, $"文 {id} はシナリオにありません", out error); }
                if (!result.MarkRead(id)) { return SaveText.Fail(SaveErrorKind.Invalid, $"文 {id} が重複しています", out error); }
            }

            for (int i = 0; i < script.Endings.Count; i++)
            {
                if (!SaveText.TryString(map, "ending." + script.Endings[i].Id, out var v, out error)) { return false; }
                if (v is not ("0" or "1")) { return SaveText.Fail(SaveErrorKind.Invalid, $"結末 {script.Endings[i].Id} の値 {v} は 0 か 1 です", out error); }
                if (v == "1") { result.MarkEnding(i); }
            }

            log = result;
            error = null;
            return true;
        }

        private static bool TryPortrait(CompiledScript script, string v, out Portrait portrait)
        {
            portrait = Portrait.Empty;
            if (v == "-") { return true; }
            var colon = v.IndexOf(':');
            if (colon <= 0) { return false; }
            var id = v.Substring(0, colon);
            for (int c = 0; c < script.Characters.Count; c++)
            {
                if (string.Equals(script.Characters[c].Id, id, StringComparison.Ordinal))
                {
                    portrait = new Portrait(c, v.Substring(colon + 1));
                    return true;
                }
            }

            return false;
        }
    }
}
