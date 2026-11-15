using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 画面の縦横比にかかわらず、フィールド全体（壁を含む）と余白が収まるように正投影カメラを合わせる。
    /// フィールドは縦長なので横長の画面では高さで決まり、細い画面ほど幅で決まる。左右の余りに HUD を置く。
    /// </summary>
    public static class FieldFraming
    {
        /// <summary>フィールドの外側に確保する余白（ワールド単位）。壁の太さを含む。</summary>
        public const float Margin = 0.6f;

        public static float OrthographicSize(BreakoutConfig config, float aspect)
        {
            var halfHeight = (config.FieldHeight / 2f) + Margin;
            var halfWidth = (config.FieldWidth / 2f) + Margin;
            return Mathf.Max(halfHeight, halfWidth / aspect);
        }

        public static void Fit(Camera camera, BreakoutConfig config)
        {
            camera.orthographic = true;
            camera.orthographicSize = OrthographicSize(config, camera.aspect);
        }
    }
}
