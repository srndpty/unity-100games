using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// Core の座標（x 横・y 縦・z 高さ、単位は 0.1 mm）を、斜め上から見下ろした画面（1 m = 1 ワールド単位）へ写す。
    /// 縦は <see cref="DepthScale"/> 倍に縮め、高さは上へずらす。奥ほど少し小さく描く（遠近）。
    /// 描画・生成スクリプトのコートの絵・PlayMode テストが同じ式を使う。
    /// </summary>
    public static class CourtProjection
    {
        /// <summary>縦 1 m が画面で何単位か。</summary>
        public const float DepthScale = .46f;

        /// <summary>高さ 1 m が画面で何単位か（手前の基準の大きさで）。</summary>
        public const float HeightScale = .9f;

        /// <summary>奥のベースラインで何割小さく描くか。</summary>
        public const float Perspective = .14f;

        private const float HalfLengthMeters = (float)Court.HalfLength / Units.PerMeter;

        public static float Meters(int units) => (float)units / Units.PerMeter;

        /// <summary>縦の位置 y（m）での描く大きさの倍率。ネットで 1、手前で大きく、奥で小さい。</summary>
        public static float Scale(float yMeters) => 1f - (Perspective * yMeters / HalfLengthMeters);

        public static Vector3 ToWorld(Vec3 position) => ToWorld(Meters(position.X), Meters(position.Y), Meters(position.Z));

        public static Vector3 ToWorld(float xMeters, float yMeters, float zMeters)
        {
            var k = Scale(yMeters);
            return new Vector3(xMeters * k, (yMeters * DepthScale) + (zMeters * HeightScale * k), 0);
        }

        /// <summary>画面の点を、地面（z = 0）の上の点（m）へ戻す。コートの絵を描くときに使う。</summary>
        public static Vector2 GroundFromWorld(Vector2 world)
        {
            var y = world.y / DepthScale;
            return new Vector2(world.x / Scale(y), y);
        }

        /// <summary>手前ほど前に描く順序（0.1 m 刻み）。ネットは 0。</summary>
        public static int SortingOrder(int yUnits) => -yUnits / 1000;
    }
}
