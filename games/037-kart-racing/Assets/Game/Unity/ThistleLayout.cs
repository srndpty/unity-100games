using IntGeometry;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>mm のコース座標を、1 m = 1 Unity 単位のシーン座標にする。描画と Prefab の照合が同じ式を使う。</summary>
    public static class ThistleLayout
    {
        public const float MillimetersPerMeter = 1000f;

        public static Vector3 At(Vec2i point) => new(point.X / MillimetersPerMeter, point.Y / MillimetersPerMeter, 0f);

        public static float Degrees(int heading) => heading * (360f / Trig.FullTurn);

        /// <summary>画面の上が車の進行方向になるカメラの回転。</summary>
        public static Quaternion CameraRotation(int heading) => Quaternion.Euler(0f, 0f, Degrees(heading) - 90f);
    }
}
