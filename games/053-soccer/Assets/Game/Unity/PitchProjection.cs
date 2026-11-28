using Game.Core;
using IntGeometry;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// Core の座標（x 縦・y 横・高さ z、1 m = 1000 単位）を、真上から見た画面（1 m = 1 ワールド単位、x は右・y は上）へ写す。
    /// 球の高さは上へずらし、少し大きく描く。影は地面の位置に描く。描画・生成スクリプト・PlayMode テストが同じ式を使う。
    /// </summary>
    public static class PitchProjection
    {
        /// <summary>高さ 1 m を画面の上へ何単位ずらすか。</summary>
        public const float HeightLift = .6f;

        /// <summary>高さ 1 m あたり、球を何割大きく描くか。</summary>
        public const float HeightGrow = .25f;

        public static float Meters(int units) => (float)units / Pitch.UnitsPerMeter;

        public static Vector3 Ground(Vec2i p) => new(Meters(p.X), Meters(p.Y), 0f);

        /// <summary>高さのある球の描く位置。</summary>
        public static Vector3 Lifted(Vec2i p, int height) => new(Meters(p.X), Meters(p.Y) + (Meters(height) * HeightLift), 0f);

        /// <summary>球と影を実寸より大きく描く倍率（実寸の 0.22 m では見えにくい）。</summary>
        public const float BallBase = 2f;

        /// <summary>選手を描く倍率（絵は 0.8 m）。</summary>
        public const float PlayerScale = 1.5f;

        public static float BallScale(int height) => BallBase * (1f + (Meters(height) * HeightGrow));
    }
}
