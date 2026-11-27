#nullable enable
using Game.Core.Geometry;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>Core の整数（mm・画面の整数座標）と Unity（m・ピクセル）の換算。カメラも Core の視点の宣言から作る。</summary>
    public static class StageMath
    {
        public const float MetersPerUnit = 0.001f;

        public static Vector3 ToWorld(Vec3i p) => new(p.X * MetersPerUnit, p.Y * MetersPerUnit, p.Z * MetersPerUnit);

        public static Vector3 Center(Box3 box) =>
            new((box.Min.X + box.Max.X) * 0.5f * MetersPerUnit, (box.Min.Y + box.Max.Y) * 0.5f * MetersPerUnit, (box.Min.Z + box.Max.Z) * 0.5f * MetersPerUnit);

        public static Vector3 Size(Box3 box) => new(box.Size.X * MetersPerUnit, box.Size.Y * MetersPerUnit, box.Size.Z * MetersPerUnit);

        public static Quaternion Rotation(CameraPose pose) => Quaternion.Euler((float)PitchTable.Degrees(pose.Pitch), pose.YawDegrees, 0f);

        /// <summary>カメラを視点の宣言どおりに置く（位置・回転・縦の画角）。</summary>
        public static void Apply(Camera camera, CameraPose pose)
        {
            camera.transform.SetPositionAndRotation(ToWorld(pose.Eye), Rotation(pose));
            camera.fieldOfView = (float)pose.VerticalFovDegrees;
        }

        /// <summary>2 つの視点の間（カメラの演出）。進み具合は滑らかにする。</summary>
        public static void Blend(Camera camera, CameraPose from, CameraPose to, float t)
        {
            var s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            camera.transform.SetPositionAndRotation(
                Vector3.Lerp(ToWorld(from.Eye), ToWorld(to.Eye), s),
                Quaternion.Slerp(Rotation(from), Rotation(to), s));
            camera.fieldOfView = Mathf.Lerp((float)from.VerticalFovDegrees, (float)to.VerticalFovDegrees, s);
        }

        /// <summary>画面のピクセル（左下が原点）を Core の画面の整数座標（中央が 0、縦の半分が 1000）にする。</summary>
        public static Vector2Int ToCore(Vector2 pixel, float width, float height)
        {
            var scale = 2f * CameraPose.HalfHeight / height;
            return new Vector2Int(Mathf.RoundToInt((pixel.x - (width * 0.5f)) * scale), Mathf.RoundToInt((pixel.y - (height * 0.5f)) * scale));
        }

        /// <summary>Core の画面の整数座標をピクセル（左下が原点）にする。</summary>
        public static Vector2 ToPixel(Vector2Int core, float width, float height)
        {
            var scale = height / (2f * CameraPose.HalfHeight);
            return new Vector2((core.x * scale) + (width * 0.5f), (core.y * scale) + (height * 0.5f));
        }
    }
}
