using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Unity.Tests
{
    /// <summary>画面の縦横比が変わっても、フィールド全体（壁と、端まで動いた拡張パドル）が画面内に収まる。</summary>
    public sealed class FieldFramingTests
    {
        [TestCase(16f, 9f)]
        [TestCase(16f, 10f)]
        [TestCase(4f, 3f)]
        [TestCase(1f, 1f)]
        [TestCase(9f, 16f)]
        public void FieldAndWalls_FitInsideViewport(float width, float height)
        {
            var config = new BreakoutConfig();
            var cameraObject = new GameObject("Test Camera");
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.aspect = width / height;

                FieldFraming.Fit(camera, config);

                // 壁の外側（太さ 0.2）とミスの線。
                var halfWidth = (config.FieldWidth / 2f) + 0.2f;
                var top = (config.FieldHeight / 2f) + 0.2f;
                var bottom = -config.FieldHeight / 2f;
                var points = new[]
                {
                    new Vector2(-halfWidth, bottom), new Vector2(halfWidth, top),
                    new Vector2(-halfWidth, top), new Vector2(halfWidth, bottom),
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
        public void Widescreen_IsFramedByFieldHeight()
        {
            var config = new BreakoutConfig();

            var size = FieldFraming.OrthographicSize(config, 16f / 9f);

            Assert.That(size, Is.EqualTo((config.FieldHeight / 2f) + FieldFraming.Margin));
        }
    }
}
