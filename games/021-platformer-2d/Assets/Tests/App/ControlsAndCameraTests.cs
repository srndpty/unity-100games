using System;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class ControlsAndCameraTests
    {
        [Test]
        public void Held_LaterDirectionWinsAndReleaseFallsBack()
        {
            var held = new HeldControls();
            held.Press(Control.Left);
            held.Press(Control.Right);
            Assert.That(held.Horizontal, Is.EqualTo(1));
            held.Release(Control.Right);
            Assert.That(held.Horizontal, Is.EqualTo(-1));
            held.Release(Control.Left);
            Assert.That(held.Horizontal, Is.Zero);
        }

        [Test]
        public void Held_JumpPressAndReleaseBeforeTickCountsForOneTick()
        {
            var held = new HeldControls();
            held.Press(Control.Jump);
            held.Release(Control.Jump);
            Assert.That(held.TakeTickInput().Jump, Is.True);
            Assert.That(held.TakeTickInput().Jump, Is.False);
        }

        [Test]
        public void Held_JumpHeldStaysUntilReleased()
        {
            var held = new HeldControls();
            held.Press(Control.Jump);
            held.Press(Control.Run);
            for (var i = 0; i < 3; i++)
            {
                var input = held.TakeTickInput();
                Assert.That(input.Jump && input.Run, Is.True);
            }

            held.Release(Control.Jump);
            Assert.That(held.TakeTickInput().Jump, Is.False);
            held.Clear();
            Assert.That(held.TakeTickInput(), Is.EqualTo(PlayerInput.None));
        }

        [Test]
        public void Camera_StaysInsideDeadZone()
        {
            Assert.That(CameraFollow.Follow(5000, 5100, 200, 2000, 20000), Is.EqualTo(5000));
            Assert.That(CameraFollow.Follow(5000, 4800, 200, 2000, 20000), Is.EqualTo(5000));
        }

        [Test]
        public void Camera_FollowsBeyondDeadZoneBothWays()
        {
            Assert.That(CameraFollow.Follow(5000, 5300, 200, 2000, 20000), Is.EqualTo(5100));
            Assert.That(CameraFollow.Follow(5000, 4500, 200, 2000, 20000), Is.EqualTo(4700));
        }

        [Test]
        public void Camera_ClampsAtLevelEdges()
        {
            Assert.That(CameraFollow.Follow(1000, 0, 200, 2000, 20000), Is.EqualTo(1000));
            Assert.That(CameraFollow.Follow(19000, 20000, 200, 2000, 20000), Is.EqualTo(19000));
            Assert.That(CameraFollow.Follow(0, 0, 0, 2001, 20000), Is.EqualTo(1000), "奇数の幅でも左端が 0 を越えない");
            Assert.That(CameraFollow.Follow(0, 20000, 0, 2001, 20000), Is.EqualTo(20000 - 1001), "右端が面の幅を越えない");
        }

        [Test]
        public void Camera_CentersNarrowLevel()
        {
            Assert.That(CameraFollow.Follow(0, 300, 0, 2000, 1000), Is.EqualTo(500));
        }

        [Test]
        public void Camera_RejectsInvalidArguments()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CameraFollow.Follow(0, 0, -1, 10, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => CameraFollow.Follow(0, 0, 0, 0, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => CameraFollow.Follow(0, 0, 0, 10, 0));
        }

        /// <summary>カメラは Core の状態を読むだけで、同じ入力なら追従させてもさせなくても結果は同じ。</summary>
        [Test]
        public void Camera_DoesNotAffectSimulation()
        {
            var plain = new LevelRun(new PlatformerConfig(), Levels.Hills);
            var followed = new LevelRun(new PlatformerConfig(), Levels.Hills);
            var pilot = new Autopilot();
            var camera = 0;
            while (!plain.IsOver)
            {
                var input = pilot.Next(plain);
                plain.Tick(input);
                followed.Tick(input);
                camera = CameraFollow.Follow(camera, followed.Player.Body.CenterX, 2 * Units.Tile, 27 * Units.Tile, followed.Map.PixelWidth);
            }

            Assert.That(followed.Hash(), Is.EqualTo(plain.Hash()));
            Assert.That(camera, Is.GreaterThan(0));
        }
    }
}
