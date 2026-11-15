using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>HUD をどこに置くか。</summary>
    public enum HudLayout
    {
        /// <summary>フィールドの左右の余白に置く（横長の画面）。</summary>
        Sides,

        /// <summary>画面上部の帯に置き、フィールドはその下に収める（正方形・縦長の画面）。</summary>
        Top,
    }

    /// <summary>画面比率から決めたカメラの合わせ方と、HUD に使う画面の領域（画面に対する割合）。</summary>
    public readonly struct Framing
    {
        public Framing(HudLayout layout, float orthographicSize, float cameraY, float hudFraction)
        {
            Layout = layout;
            OrthographicSize = orthographicSize;
            CameraY = cameraY;
            HudFraction = hudFraction;
        }

        public HudLayout Layout { get; }

        public float OrthographicSize { get; }

        /// <summary>カメラの y 位置（フィールド中央が原点）。上部の帯を空けるときは上へずらす。</summary>
        public float CameraY { get; }

        /// <summary><see cref="HudLayout.Sides"/> なら左右それぞれの HUD の幅、<see cref="HudLayout.Top"/> なら上部の帯の高さ（画面に対する割合）。</summary>
        public float HudFraction { get; }
    }

    /// <summary>
    /// 画面の縦横比にかかわらず、フィールド全体（壁を含む）と余白が収まり、HUD と重ならないように正投影カメラを合わせる。
    /// 高さで合わせたときに左右の余白が画面幅の <see cref="MinSideFraction"/> 以上あれば、HUD を左右に置く。
    /// 足りなければ画面上部の <see cref="TopBandFraction"/> を HUD の帯として空け、残りにフィールドを収める。
    /// </summary>
    public static class FieldFraming
    {
        /// <summary>フィールドの外側に確保する余白（ワールド単位）。壁の太さを含む。</summary>
        public const float Margin = 0.6f;

        /// <summary>HUD を左右に置くのに必要な、片側の余白の画面幅に対する割合。</summary>
        public const float MinSideFraction = 0.2f;

        /// <summary>HUD を上部に置くときの帯の高さ（画面の高さに対する割合）。</summary>
        public const float TopBandFraction = 0.14f;

        public static Framing Compute(BreakoutConfig config, float aspect)
        {
            var halfHeight = (config.FieldHeight / 2f) + Margin;
            var halfWidth = (config.FieldWidth / 2f) + Margin;

            // 高さで合わせ、左右に HUD の幅が残るか。
            var visibleHalfWidth = halfHeight * aspect;
            var sideFraction = (visibleHalfWidth - halfWidth) / (2f * visibleHalfWidth);
            if (sideFraction >= MinSideFraction)
            {
                return new Framing(HudLayout.Sides, halfHeight, 0f, sideFraction);
            }

            // 上部の帯を除いた領域にフィールドを収め、その領域の中央にフィールドの中央が来るようカメラを上へずらす。
            var size = Mathf.Max(halfHeight / (1f - TopBandFraction), halfWidth / aspect);
            return new Framing(HudLayout.Top, size, TopBandFraction * size, TopBandFraction);
        }

        public static Framing Fit(Camera camera, BreakoutConfig config)
        {
            var framing = Compute(config, camera.aspect);
            camera.orthographic = true;
            camera.orthographicSize = framing.OrthographicSize;
            var position = camera.transform.position;
            camera.transform.position = new Vector3(position.x, framing.CameraY, position.z);
            return framing;
        }
    }
}
