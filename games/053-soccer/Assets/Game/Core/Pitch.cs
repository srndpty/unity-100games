using System;
using System.Collections.ObjectModel;
using IntGeometry;

namespace Game.Core
{
    /// <summary>チーム。青（人間側）は +x のゴールへ攻め、赤は -x のゴールへ攻める。</summary>
    public enum Team
    {
        Blue,
        Red,
    }

    /// <summary>
    /// コートの寸法（1 m = 1000 単位）。中心が原点、x が縦（ゴールは x = ±HalfLength）、y が横（壁は y = ±HalfWidth）。コートは壁で囲まれている。
    /// 寸法は試合のルールそのものなので設定にしない（画面の配置とテストの場面が同じ値を使う）。
    /// </summary>
    public static class Pitch
    {
        public const int UnitsPerMeter = 1000;
        public const int TickRate = 60;

        public const int HalfLength = 16000;
        public const int HalfWidth = 10000;

        /// <summary>ゴールの幅の半分（ポストの中心まで）。</summary>
        public const int GoalHalfWidth = 1500;

        /// <summary>クロスバーの高さ（球の下端がこれより下なら、ゴールの中・ポストに当たる）。</summary>
        public const int CrossbarHeight = 2000;

        /// <summary>ゴールの奥行き（得点の後、球はこの中で止まる）。</summary>
        public const int GoalDepth = 1200;

        public const int PostRadius = 60;

        /// <summary>キックオフの間、相手が入れないセンターの円の半径。</summary>
        public const int ExclusionRadius = 3000;

        /// <summary>チームが攻めるゴールの x（青は +、赤は -）。</summary>
        public static int AttackGoalX(Team team) => team == Team.Blue ? HalfLength : -HalfLength;

        /// <summary>チームが守るゴールの x。</summary>
        public static int OwnGoalX(Team team) => -AttackGoalX(team);

        /// <summary>チームの攻める向き（+1 か -1）。</summary>
        public static int Direction(Team team) => team == Team.Blue ? 1 : -1;

        public static Team Opponent(Team team) => team == Team.Blue ? Team.Red : Team.Blue;

        /// <summary>ゴールのポスト 4 本（-x の左右、+x の左右）。</summary>
        public static readonly ReadOnlyCollection<Vec2i> Posts = Array.AsReadOnly(new[]
        {
            new Vec2i(-HalfLength, -GoalHalfWidth),
            new Vec2i(-HalfLength, GoalHalfWidth),
            new Vec2i(HalfLength, -GoalHalfWidth),
            new Vec2i(HalfLength, GoalHalfWidth),
        });
    }
}
