using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Unity.Tests
{
    /// <summary>画面の縦横比が変わっても、コート全体とパドルが画面内に収まる。</summary>
    public sealed class CourtFramingTests
    {
        [TestCase(16f, 9f)]
        [TestCase(16f, 10f)]
        [TestCase(4f, 3f)]
        [TestCase(1f, 1f)]
        public void CourtAndPaddles_FitInsideViewport(float width, float height)
        {
            var config = new PongConfig();
            var cameraObject = new GameObject("Test Camera");
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.aspect = width / height;

                CourtFraming.Fit(camera, config);

                var halfWidth = config.FieldWidth / 2f;
                var halfHeight = config.FieldHeight / 2f;
                var paddleOuterX = config.PaddleX(Side.Right) + (config.PaddleWidth / 2f);
                var points = new[]
                {
                    new Vector2(-halfWidth, -halfHeight), new Vector2(halfWidth, halfHeight), // コートの角（ゴールラインと壁）
                    new Vector2(-paddleOuterX, halfHeight), new Vector2(paddleOuterX, -halfHeight), // 上下端まで動いたパドルの外側の角
                };
                foreach (var point in points)
                {
                    var viewport = camera.WorldToViewportPoint(point);
                    Assert.That(viewport.x, Is.InRange(0f, 1f), $"{width}:{height} で x={point.x} が画面外");
                    Assert.That(viewport.y, Is.InRange(0f, 1f), $"{width}:{height} で y={point.y} が画面外");
                }
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void Widescreen_IsFramedByCourtHeight()
        {
            var config = new PongConfig();

            var size = CourtFraming.OrthographicSize(config, 16f / 9f);

            Assert.That(size, Is.EqualTo((config.FieldHeight / 2f) + CourtFraming.Margin));
        }
    }
}
