using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>押している操作キーから Tick の操作を作る。狙いは打球キーを押した時点の左右。</summary>
    public sealed class HeldControlsTests
    {
        [Test]
        public void AimIsTheDirectionHeldWhenTheShotKeyWasPressed_EvenIfReleasedInTheSameTick()
        {
            var held = new HeldControls();
            held.Press(Control.Right);
            held.Press(Control.Stroke);
            held.Release(Control.Right);
            var command = held.TakeTickCommand();
            Assert.That(command.Stroke, Is.True);
            Assert.That(command.AimX, Is.EqualTo(1), "押した瞬間は右");
            Assert.That(command.MoveX, Is.Zero, "移動は今の状態（離した）");
        }

        [Test]
        public void DirectionPressedAfterTheShotKeyInTheSameTickDoesNotChangeTheAim()
        {
            var held = new HeldControls();
            held.Press(Control.Stroke);
            held.Press(Control.Right);
            var command = held.TakeTickCommand();
            Assert.That(command.AimX, Is.Zero, "押した瞬間は中央");
            Assert.That(command.MoveX, Is.EqualTo(1));
        }

        [Test]
        public void DirectionChangedAfterTheShotKeyInTheSameTickKeepsTheFirstAim()
        {
            var held = new HeldControls();
            held.Press(Control.Left);
            held.Press(Control.Lob);
            held.Press(Control.Right);
            var command = held.TakeTickCommand();
            Assert.That(command.Lob, Is.True);
            Assert.That(command.AimX, Is.EqualTo(-1));
            Assert.That(command.MoveX, Is.EqualTo(1));
        }

        [Test]
        public void AimIsForgottenAfterTheTick_AndDuringAHoldItFollowsTheDirection()
        {
            var held = new HeldControls();
            held.Press(Control.Right);
            held.Press(Control.Stroke);
            held.Release(Control.Right);
            held.TakeTickCommand();

            // 押し続けている間は押し始めではないので、狙いは移動の向きと同じでよい（Core は押し始めでしか使わない）。
            held.Press(Control.Left);
            Assert.That(held.TakeTickCommand().AimX, Is.EqualTo(-1));

            held.Release(Control.Stroke);
            held.Release(Control.Left);
            held.Press(Control.Stroke);
            Assert.That(held.TakeTickCommand().AimX, Is.Zero, "前の押下の狙いは残らない");
        }

        [Test]
        public void ClearForgetsTheAim()
        {
            var held = new HeldControls();
            held.Press(Control.Right);
            held.Press(Control.Stroke);
            held.Clear();
            held.Press(Control.Stroke);
            Assert.That(held.TakeTickCommand().AimX, Is.Zero);
        }
    }
}
