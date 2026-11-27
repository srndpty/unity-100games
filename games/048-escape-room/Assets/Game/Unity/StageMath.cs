#nullable enable
using Game.Core.Geometry;
using Game.Core.Play;
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

        /// <summary>対応する画面の比率（横 / 縦）。これより狭い・広い画面は上下・左右に黒い帯を付けてこの範囲に収める。</summary>
        public const float MinAspect = 1.6f;
        public const float MaxAspect = 21f / 9f;

        /// <summary>HUD の持ち物の帯の高さ（1080 の 150。パネルは縦に合わせて拡大するので、画面の高さに対する割合は一定）。</summary>
        public const float HudBarFraction = 150f / 1080f;

        /// <summary>画面の大きさから、対応する比率に収めたカメラの正規化の矩形（Camera.rect）。</summary>
        public static Rect Viewport(float screenWidth, float screenHeight)
        {
            var aspect = screenWidth / screenHeight;
            if (aspect < MinAspect)
            {
                var h = aspect / MinAspect;
                return new Rect(0f, (1f - h) * 0.5f, 1f, h);
            }

            if (aspect > MaxAspect)
            {
                var w = MaxAspect / aspect;
                return new Rect((1f - w) * 0.5f, 0f, w, 1f);
            }

            return new Rect(0f, 0f, 1f, 1f);
        }

        /// <summary>
        /// 部屋のクリックが届く画面の範囲（Core の画面の整数座標）。横は描画の矩形の比率、下は HUD の帯の上端（画面の下から高さの 150/1080）より上。
        /// </summary>
        public static ScreenRegion Region(Rect pixelRect, float screenHeight)
        {
            var halfWidth = Mathf.RoundToInt(CameraPose.HalfHeight * pixelRect.width / pixelRect.height);
            var barTop = screenHeight * HudBarFraction;
            var minY = Mathf.CeilToInt(((barTop - pixelRect.y) / pixelRect.height * 2f * CameraPose.HalfHeight) - CameraPose.HalfHeight) + 1;
            return new ScreenRegion(Mathf.Clamp(halfWidth, 1, CameraPose.MaxHalfWidth), Mathf.Clamp(minY, -CameraPose.HalfHeight, CameraPose.HalfHeight - 1));
        }

        /// <summary>画面のピクセル（左下が原点）を、描画の矩形（Camera.pixelRect）の中の Core の画面の整数座標（中央が 0、縦の半分が 1000）にする。</summary>
        public static Vector2Int ToCore(Vector2 pixel, Rect pixelRect)
        {
            var scale = 2f * CameraPose.HalfHeight / pixelRect.height;
            return new Vector2Int(Mathf.RoundToInt((pixel.x - pixelRect.center.x) * scale), Mathf.RoundToInt((pixel.y - pixelRect.center.y) * scale));
        }

        /// <summary>Core の画面の整数座標をピクセル（左下が原点）にする。</summary>
        public static Vector2 ToPixel(Vector2Int core, Rect pixelRect)
        {
            var scale = pixelRect.height / (2f * CameraPose.HalfHeight);
            return new Vector2((core.x * scale) + pixelRect.center.x, (core.y * scale) + pixelRect.center.y);
        }
    }
}
