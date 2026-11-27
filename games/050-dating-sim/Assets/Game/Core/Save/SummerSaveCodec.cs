using System;
using System.Globalization;
using System.Text;
using Game.Core.Data;
using Game.Core.Play;

namespace Game.Core.Save
{
    /// <summary>
    /// 日の始め（朝）の自動セーブの直列化。行指向テキスト（key=value）・版・台本の指紋・末尾のチェックサム（041・046 の方式）。
    /// 能力値・フラグ・済み・記録は ID で書き、台本が変われば指紋で拒否する（移行はしない）。
    /// </summary>
    public static class SummerSaveCodec
    {
        public const int Version = 1;
        private const string Kind = "summer";

        public static string Encode(SummerBook book, SummerSnapshot s)
        {
            if (book == null) { throw new ArgumentNullException(nameof(book)); }
            if (s == null) { throw new ArgumentNullException(nameof(s)); }
            var sb = new StringBuilder();
            sb.Append(SaveText.Line("kind", Kind));
            sb.Append(SaveText.Line("version", SaveText.Num(Version)));
            sb.Append(SaveText.Line("fingerprint", SaveText.Hex(book.Fingerprint)));
            sb.Append(SaveText.Line("seed", SaveText.Num(s.Seed)));
            sb.Append(SaveText.Line("day", SaveText.Num(s.Day)));
            foreach (var st in book.Stats) { sb.Append(SaveText.Line("stat." + st.Id, SaveText.Num(s.Stats[st.Index]))); }
            for (int i = 0; i < book.Flags.Count; i++) { sb.Append(SaveText.Line("flag." + book.Flags[i], s.Flags[i] ? "1" : "0")); }
            foreach (var e in book.Events) { sb.Append(SaveText.Line("done." + e.Id, s.Done[e.Index] ? "1" : "0")); }
            sb.Append(SaveText.Line("log.count", SaveText.Num(s.Log.Count)));
            for (int i = 0; i < s.Log.Count; i++)
            {
                var e = s.Log[i];
                var id = e.Kind == LogKind.Action ? book.Actions[e.Index].Id : book.Events[e.Index].Id;
                sb.Append(SaveText.Line("log." + SaveText.Num(i), $"{SaveText.Num(e.Day)}:{KindName(e.Kind)}:{id}:{SaveText.Num(e.Value)}"));
            }

            return SaveText.Sign(sb.ToString());
        }

        /// <summary>保存の文から朝の状態を作り直す。書式・版・指紋・キーの数・中身（範囲・記録）を全て確かめる。</summary>
        public static bool TryDecode(SummerBook book, string? text, out Summer? summer, out SaveError? error)
        {
            if (book == null) { throw new ArgumentNullException(nameof(book)); }
            summer = null;
            if (!SaveText.TryOpen(text, Kind, Version, book.Fingerprint, out var map, out error)) { return false; }
            if (!SaveText.TryInt(map, "seed", out var seed, out error)) { return false; }
            if (!SaveText.TryInt(map, "day", out var day, out error)) { return false; }
            if (!SaveText.TryInt(map, "log.count", out var count, out error)) { return false; }
            if (count < 0 || count > book.Days * 5) { return SaveText.Fail(SaveErrorKind.Invalid, $"記録の数 {count} は範囲外です", out error); }
            var expected = 6 + book.Stats.Count + book.Flags.Count + book.Events.Count + count;
            if (map.Count != expected) { return SaveText.Fail(SaveErrorKind.Invalid, $"キーの数が違います（{map.Count}）", out error); }

            var stats = new int[book.Stats.Count];
            foreach (var st in book.Stats)
            {
                if (!SaveText.TryInt(map, "stat." + st.Id, out stats[st.Index], out error)) { return false; }
            }

            var flags = new bool[book.Flags.Count];
            for (int i = 0; i < flags.Length; i++)
            {
                if (!TryBit(map, "flag." + book.Flags[i], out flags[i], out error)) { return false; }
            }

            var done = new bool[book.Events.Count];
            foreach (var e in book.Events)
            {
                if (!TryBit(map, "done." + e.Id, out done[e.Index], out error)) { return false; }
            }

            var log = new LogEntry[count];
            for (int i = 0; i < count; i++)
            {
                if (!SaveText.TryString(map, "log." + SaveText.Num(i), out var v, out error)) { return false; }
                if (!TryEntry(book, v, out log[i])) { return SaveText.Fail(SaveErrorKind.Invalid, $"記録 {v} は不正です", out error); }
            }

            summer = Summer.Restore(book, new SummerSnapshot(seed, day, stats, flags, done, log), out var reason);
            if (summer == null) { return SaveText.Fail(SaveErrorKind.Invalid, reason!, out error); }
            error = null;
            return true;
        }

        private static string KindName(LogKind kind) => kind switch
        {
            LogKind.Action => "action",
            LogKind.Event => "event",
            _ => "choice",
        };

        private static bool TryBit(System.Collections.Generic.Dictionary<string, string> map, string key, out bool value, out SaveError? error)
        {
            value = false;
            if (!SaveText.TryString(map, key, out var v, out error)) { return false; }
            if (v is not ("0" or "1")) { return SaveText.Fail(SaveErrorKind.Invalid, $"キー {key} の値 {v} は 0 か 1 です", out error); }
            value = v == "1";
            return true;
        }

        private static bool TryEntry(SummerBook book, string v, out LogEntry entry)
        {
            entry = default;
            var p = v.Split(':');
            if (p.Length != 4) { return false; }
            if (!TryNum(p[0], out var day) || !TryNum(p[3], out var value)) { return false; }
            LogKind kind;
            int index;
            switch (p[1])
            {
                case "action":
                    kind = LogKind.Action;
                    index = book.FindAction(p[2]);
                    break;
                case "event":
                    kind = LogKind.Event;
                    index = book.FindEvent(p[2]);
                    break;
                case "choice":
                    kind = LogKind.Choice;
                    index = book.FindEvent(p[2]);
                    break;
                default:
                    return false;
            }

            if (index < 0) { return false; }
            entry = new LogEntry(day, kind, index, value);
            return true;
        }

        private static bool TryNum(string s, out int value) =>
            int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value) && SaveText.Num(value) == s;
    }
}
