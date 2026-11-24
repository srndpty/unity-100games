using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using IntGeometry;

namespace Game.Core
{
    /// <summary>切る手順の 1 手：Tick（その Tick を進める前）とロープの番号。</summary>
    public readonly struct CutStep : IEquatable<CutStep>
    {
        public CutStep(long tick, int rope)
        {
            if (tick < 0) { throw new ArgumentOutOfRangeException(nameof(tick), tick, "Tick は 0 以上にしてください。"); }
            if (rope < 0) { throw new ArgumentOutOfRangeException(nameof(rope), rope, "ロープの番号は 0 以上にしてください。"); }
            Tick = tick;
            Rope = rope;
        }

        public long Tick { get; }

        public int Rope { get; }

        public bool Equals(CutStep other) => Tick == other.Tick && Rope == other.Rope;

        public override bool Equals(object? obj) => obj is CutStep other && Equals(other);

        public override int GetHashCode() => unchecked((Tick.GetHashCode() * 397) ^ Rope);

        public static bool operator ==(CutStep left, CutStep right) => left.Equals(right);

        public static bool operator !=(CutStep left, CutStep right) => !left.Equals(right);
    }

    /// <summary>
    /// 問ごとの切る手順（<c>Assets/Game/Data/Solutions.txt</c>）。1 行が 1 問で、<c>Tick:ロープ</c> を空白で並べる。
    /// テストと撮影だけが使い、ゲームの画面には出さない。
    /// </summary>
    public static class CutPlan
    {
        private static readonly char[] _separators = { ' ', '\t' };

        public static ReadOnlyCollection<ReadOnlyCollection<CutStep>> Parse(string text)
        {
            if (text == null) { throw new ArgumentNullException(nameof(text)); }
            var plans = new List<ReadOnlyCollection<CutStep>>();
            var lines = text.Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var comment = line.IndexOf('#');
                if (comment >= 0) { line = line.Substring(0, comment); }
                line = line.Trim();
                if (line.Length == 0) { continue; }

                var steps = new List<CutStep>();
                foreach (var token in line.Split(_separators, StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = token.Split(':');
                    if (parts.Length != 2
                        || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var tick)
                        || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var rope))
                    {
                        throw new FormatException($"{i + 1} 行目：「{token}」は Tick:ロープ の形ではありません。");
                    }

                    if (steps.Count > 0 && tick < steps[steps.Count - 1].Tick) { throw new FormatException($"{i + 1} 行目：Tick は昇順に並べてください。"); }
                    steps.Add(new CutStep(tick, rope));
                }

                plans.Add(steps.AsReadOnly());
            }

            return plans.AsReadOnly();
        }

        /// <summary>
        /// ロープ <paramref name="rope"/> の節を垂直に横切る短いなぞり（長さ 1/4 マス）で、そのロープだけを横切るもの。
        /// 真ん中の節から外へ順に探す。見つからなければ null（切ったロープも null）。
        /// </summary>
        public static (Vec2i From, Vec2i To)? SwipeAcross(PuzzleRun run, int rope)
        {
            if (run == null) { throw new ArgumentNullException(nameof(run)); }
            if (run.IsCut(rope)) { return null; }
            var links = run.LinksOf(rope);
            for (var offset = 0; offset < links; offset++)
            {
                for (var side = 0; side < 2; side++)
                {
                    var j = (links / 2) + (side == 0 ? offset : -offset);
                    if (j < 0 || j >= links || (side == 1 && offset == 0)) { continue; }
                    var a = run.RopePoint(rope, j);
                    var b = run.RopePoint(rope, j + 1);
                    var d = b - a;
                    if (d.IsZero) { continue; }
                    var middle = new Vec2i((int)(((long)a.X + b.X) / 2), (int)(((long)a.Y + b.Y) / 2));
                    var half = IntMath.Scale(new Vec2i(-d.Y, d.X), Board.Unit / 8);
                    var from = middle - half;
                    var to = middle + half;
                    if (run.Crossed(from, to) == 1 << rope) { return (from, to); }
                }
            }

            return null;
        }
    }
}
