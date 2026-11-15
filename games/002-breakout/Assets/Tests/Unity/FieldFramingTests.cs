using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Unity.Tests
{
    /// <summary>
    /// 画面の縦横比が変わっても、フィールド全体（壁とミスの線）が画面内に収まり、HUD に空けた領域（左右の余白または上部の帯）と重ならない。
    /// HUD がその領域の中に置かれることは <see cref="HudLayoutTests"/> で確かめる。
    /// </summary>
    public sealed class FieldFramingTests
    {
        // 壁の太さ（FieldView）。
        private const float Wall = 0.2f;

        [TestCase(21f, 9f, HudLayout.Sides)]
        [TestCase(16f, 9f, HudLayout.Sides)]
        [TestCase(16f, 10f, HudLayout.Sides)]
        [TestCase(4f, 3f, HudLayout.Sides)]
        [TestCase(1f, 1f, HudLayout.Top)]
        [TestCase(3f, 4f, HudLayout.Top)]
        [TestCase(9f, 16f, HudLayout.Top)]
        public void Field_FitsInsideViewport_AndOutsideHudArea(float width, float height, HudLayout expected)
        {
            var config = new BreakoutConfig();
            var cameraObject = new GameObject("Test Camera");
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                camera.aspect = width / height;

                var framing = FieldFraming.Fit(camera, config);

                Assert.That(framing.Layout, Is.EqualTo(expected));
                var min = camera.WorldToViewportPoint(new Vector2((-config.FieldWidth / 2f) - Wall, -config.FieldHeight / 2f));
                var max = camera.WorldToViewportPoint(new Vector2((config.FieldWidth / 2f) + Wall, (config.FieldHeight / 2f) + Wall));
                var aspect = $"{width}:{height}";
                Assert.That(min.x, Is.GreaterThanOrEqualTo(0f), aspect);
                Assert.That(min.y, Is.GreaterThanOrEqualTo(0f), aspect);
                Assert.That(max.x, Is.LessThanOrEqualTo(1f), aspect);
                Assert.That(max.y, Is.LessThanOrEqualTo(1f), aspect);
                if (framing.Layout == HudLayout.Sides)
                {
                    Assert.That(min.x, Is.GreaterThanOrEqualTo(framing.HudFraction), $"{aspect} で左の HUD と重なる");
                    Assert.That(max.x, Is.LessThanOrEqualTo(1f - framing.HudFraction), $"{aspect} で右の HUD と重なる");
                    Assert.That(framing.HudFraction, Is.GreaterThanOrEqualTo(FieldFraming.MinSideFraction));
                }
                else
                {
                    Assert.That(max.y, Is.LessThanOrEqualTo(1f - framing.HudFraction), $"{aspect} で上の HUD と重なる");
                }
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void Widescreen_IsFramedByFieldHeightWithoutOffset()
        {
            var config = new BreakoutConfig();

            var framing = FieldFraming.Compute(config, 16f / 9f);

            Assert.That(framing.OrthographicSize, Is.EqualTo((config.FieldHeight / 2f) + FieldFraming.Margin));
            Assert.That(framing.CameraY, Is.Zero);
        }
    }
}
