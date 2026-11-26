using System;
using System.Globalization;
using System.Text;

namespace Game.Core
{
    /// <summary>クリアの記録（最短クリア時間・最多の捕まえた種類数・クリア回数）。良くなった値だけを置き換える。</summary>
    public sealed class Records
    {
        public const string Header = "THORNWILD-RECORDS";
        public const int Version = 1;

        public Records(int clears, long bestTicks, int bestCaught)
        {
            if (clears < 0) { throw new ArgumentOutOfRangeException(nameof(clears)); }
            if (bestTicks < 0) { throw new ArgumentOutOfRangeException(nameof(bestTicks)); }
            if (bestCaught < 0) { throw new ArgumentOutOfRangeException(nameof(bestCaught)); }
            Clears = clears;
            BestTicks = bestTicks;
            BestCaught = bestCaught;
        }

        public static Records Empty { get; } = new(0, 0, 0);

        public int Clears { get; }

        /// <summary>最短クリアの Tick（0 は記録なし）。</summary>
        public long BestTicks { get; }

        public int BestCaught { get; }

        /// <summary>クリアを 1 回足した記録。</summary>
        public Records WithClear(long ticks, int caught)
        {
            var best = BestTicks == 0 ? ticks : Math.Min(BestTicks, ticks);
            return new Records(Clears == int.MaxValue ? Clears : Clears + 1, best, Math.Max(BestCaught, caught));
        }

        public string Encode()
        {
            var b = new StringBuilder();
            b.Append(Header).Append('\n');
            b.Append("version=").Append(Version.ToString(CultureInfo.InvariantCulture)).Append('\n');
            b.Append("clears=").Append(Clears.ToString(CultureInfo.InvariantCulture)).Append('\n');
            b.Append("best_ticks=").Append(BestTicks.ToString(CultureInfo.InvariantCulture)).Append('\n');
            b.Append("best_caught=").Append(BestCaught.ToString(CultureInfo.InvariantCulture)).Append('\n');
            var body = b.ToString();
            return body + "checksum=" + SaveCodec.Checksum(body).ToString("x8", CultureInfo.InvariantCulture) + "\n";
        }

        /// <summary>読めなければ理由を返す（壊れた記録は無視して空から数え直す）。</summary>
        public static bool TryDecode(string? text, int speciesCount, out Records records, out string reason)
        {
            records = Empty;
            var lines = (text ?? string.Empty).Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
            if (lines.Length < 7 || lines[0] != Header || lines[6].Length != 0 || lines.Length != 7)
            {
                reason = "記録の形式が違います";
                return false;
            }

            var body = string.Join("\n", lines, 0, 5) + "\n";
            if (lines[5] != "checksum=" + SaveCodec.Checksum(body).ToString("x8", CultureInfo.InvariantCulture))
            {
                reason = "記録が壊れています";
                return false;
            }

            if (!Value(lines[1], "version", out var version) || version != Version)
            {
                reason = "記録の版が違います";
                return false;
            }

            if (!Value(lines[2], "clears", out var clears) || clears < 0 || clears > int.MaxValue ||
                !Value(lines[3], "best_ticks", out var ticks) || ticks < 0 ||
                !Value(lines[4], "best_caught", out var caught) || caught < 0 || caught > speciesCount ||
                (clears == 0) != (ticks == 0))
            {
                reason = "記録の値が正しくありません";
                return false;
            }

            records = new Records((int)clears, ticks, (int)caught);
            reason = string.Empty;
            return true;
        }

        private static bool Value(string line, string key, out long value)
        {
            value = 0;
            var prefix = key + "=";
            return line.StartsWith(prefix, StringComparison.Ordinal) &&
                long.TryParse(line.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }
    }
}
