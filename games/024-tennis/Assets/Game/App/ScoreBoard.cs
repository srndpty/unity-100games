using System;
using Game.Core;

namespace Game.App
{
    /// <summary>HUD に出すスコアと判定の文字。</summary>
    public static class ScoreBoard
    {
        /// <summary>今のゲームの得点の呼び方。通常のゲームは 0・15・30・40・AD、タイブレークは数字。</summary>
        public static string PointText(MatchScore score, Side side)
        {
            if (score == null) { throw new ArgumentNullException(nameof(score)); }
            var mine = score.Points(side);
            if (score.InTiebreak) { return mine.ToString(System.Globalization.CultureInfo.InvariantCulture); }

            var theirs = score.Points(Court.Other(side));
            if (mine >= 3 && theirs >= 3)
            {
                if (mine == theirs) { return "40"; }
                return mine > theirs ? "AD" : "40";
            }

            return mine switch
            {
                0 => "0",
                1 => "15",
                2 => "30",
                _ => "40",
            };
        }

        /// <summary>デュース（通常のゲームで 40-40 以上の同点）か。</summary>
        public static bool IsDeuce(MatchScore score)
        {
            if (score == null) { throw new ArgumentNullException(nameof(score)); }
            var near = score.Points(Side.Near);
            return !score.InTiebreak && near >= 3 && near == score.Points(Side.Far);
        }

        /// <summary>判定の表示（審判のコール）。判定がなければ空。</summary>
        public static string VerdictText(Verdict verdict) => verdict switch
        {
            Verdict.None => string.Empty,
            Verdict.Fault => "FAULT",
            Verdict.Net => "NET",
            Verdict.Out => "OUT",
            Verdict.DoubleBounce => "2 BOUNCES",
            Verdict.Unreturned => "NOT UP",
            Verdict.DoubleFault => "DOUBLE FAULT",
            _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "未定義の判定です。"),
        };
    }
}
