using System;
using System.Collections.Generic;
using System.Globalization;
using TileMotion;

namespace Game.Core.Save
{
    public enum SaveErrorKind
    {
        None,
        Empty,
        Truncated,
        Syntax,
        DuplicateKey,
        MissingKey,
        Version,
        Fingerprint,
        Checksum,
        Invalid,
    }

    /// <summary>保存データを読めなかった理由。</summary>
    public sealed class SaveError
    {
        public SaveError(SaveErrorKind kind, string message)
        {
            Kind = kind;
            Message = message;
        }

        public SaveErrorKind Kind { get; }
        public string Message { get; }

        public override string ToString() => Message;
    }

    /// <summary>
    /// 行指向テキスト（key=value の行 + 末尾の checksum=XXXXXXXX）の共通部分（041 の方式）。
    /// 版番号とスクリプトの指紋を照合し、移行はしない。
    /// </summary>
    public static class SaveText
    {
        private const string ChecksumPrefix = "checksum=";

        /// <summary>本文の末尾にチェックサムの行を付ける。</summary>
        public static string Sign(string body)
        {
            if (body == null) { throw new ArgumentNullException(nameof(body)); }
            var b = body.Replace("\r\n", "\n", StringComparison.Ordinal);
            if (b.Length > 0 && b[b.Length - 1] != '\n') { b += "\n"; }
            return b + ChecksumPrefix + Checksum(b).ToString("X8", CultureInfo.InvariantCulture) + "\n";
        }

        public static string Line(string key, string value) => key + "=" + value + "\n";

        public static string Hex(uint value) => value.ToString("X8", CultureInfo.InvariantCulture);

        public static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>チェックサム・行の書式・重複・版・指紋を確かめ、キーと値の表にする。</summary>
        public static bool TryOpen(string? raw, string kind, int version, uint fingerprint, out Dictionary<string, string> map, out SaveError? error)
        {
            map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(raw)) { return Fail(SaveErrorKind.Empty, "データが空です", out error); }
            var text = raw!.Replace("\r\n", "\n", StringComparison.Ordinal);
            if (text[text.Length - 1] != '\n') { return Fail(SaveErrorKind.Truncated, "データが途中で切れています", out error); }
            var withoutLf = text.Substring(0, text.Length - 1);
            var lastLf = withoutLf.LastIndexOf('\n');
            if (lastLf < 0) { return Fail(SaveErrorKind.Truncated, "データの本体がありません", out error); }
            var sumLine = withoutLf.Substring(lastLf + 1);
            if (!sumLine.StartsWith(ChecksumPrefix, StringComparison.Ordinal) || sumLine.Length != ChecksumPrefix.Length + 8)
            {
                return Fail(SaveErrorKind.Truncated, "データが途中で切れています（チェックサムの行）", out error);
            }

            var body = text.Substring(0, lastLf + 1);
            if (!TryHex(sumLine.Substring(ChecksumPrefix.Length), out var expected) || expected != Checksum(body))
            {
                return Fail(SaveErrorKind.Checksum, "データが壊れています（チェックサムが合いません）", out error);
            }

            var lines = body.Split('\n');
            for (int i = 0; i < lines.Length - 1; i++)
            {
                var eq = lines[i].IndexOf('=');
                if (eq <= 0) { return Fail(SaveErrorKind.Syntax, $"{i + 1} 行目の書式が違います", out error); }
                var key = lines[i].Substring(0, eq);
                if (map.ContainsKey(key)) { return Fail(SaveErrorKind.DuplicateKey, $"キー {key} が重複しています", out error); }
                map[key] = lines[i].Substring(eq + 1);
            }

            if (!map.TryGetValue("kind", out var k) || k != kind) { return Fail(SaveErrorKind.Invalid, $"{kind} のデータではありません", out error); }
            if (!TryInt(map, "version", out var v, out error)) { return false; }
            if (v != version) { return Fail(SaveErrorKind.Version, $"データの版 {v} には対応していません（{version}）", out error); }
            if (!map.TryGetValue("fingerprint", out var fp) || !TryHex(fp, out var f) || f != fingerprint)
            {
                return Fail(SaveErrorKind.Fingerprint, "シナリオが保存したときと違います", out error);
            }

            error = null;
            return true;
        }

        public static bool TryInt(Dictionary<string, string> map, string key, out int value, out SaveError? error)
        {
            if (map == null) { throw new ArgumentNullException(nameof(map)); }
            value = 0;
            if (!map.TryGetValue(key, out var s)) { return Fail(SaveErrorKind.MissingKey, $"キー {key} がありません", out error); }
            if (!int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value) || Num(value) != s)
            {
                return Fail(SaveErrorKind.Syntax, $"キー {key} の値 {s} は整数ではありません", out error);
            }

            error = null;
            return true;
        }

        public static bool TryString(Dictionary<string, string> map, string key, out string value, out SaveError? error)
        {
            if (map == null) { throw new ArgumentNullException(nameof(map)); }
            if (!map.TryGetValue(key, out var s))
            {
                value = "";
                return Fail(SaveErrorKind.MissingKey, $"キー {key} がありません", out error);
            }

            value = s;
            error = null;
            return true;
        }

        public static bool Fail(SaveErrorKind kind, string message, out SaveError? error)
        {
            error = new SaveError(kind, message);
            return false;
        }

        private static bool TryHex(string s, out uint value) =>
            uint.TryParse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value) && s.Length == 8;

        private static uint Checksum(string body)
        {
            var h = StateHash.Start();
            h.Mix(0x4E4F5645); // "NOVE"
            h.Mix(body.Length);
            foreach (var ch in body) { h.Mix(ch); }
            return h.Value;
        }
    }
}
