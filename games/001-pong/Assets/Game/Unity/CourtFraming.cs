using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 画面の縦横比にかかわらず、コート全体（左右のゴールラインと上下の壁）と余白が収まるように正投影カメラを合わせる。
    /// 横長なら高さで、縦長に近づくほど幅で決まる。
    /// </summary>
    public static class CourtFraming
    {
        /// <summary>コートの外側に確保する余白（ワールド単位）。上下の線と HUD のスコアが収まる量。</summary>
        public const float Margin = 0.5f;

        public static float OrthographicSize(PongConfig config, float aspect)
        {
            var halfHeight = (config.FieldHeight / 2f) + Margin;
            var halfWidth = (config.FieldWidth / 2f) + Margin;
            return Mathf.Max(halfHeight, halfWidth / aspect);
        }

        public static void Fit(Camera camera, PongConfig config)
        {
            camera.orthographic = true;
            camera.orthographicSize = OrthographicSize(config, camera.aspect);
        }
    }
}
