using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class HeldControlsTests
    {
        [Test]
        public void OppositeKeys_LaterPressWinsAndReleaseFallsBack()
        {
            var held = new HeldControls();
            held.Press(Control.Left);
            held.Press(Control.Right);
            Assert.That(held.Horizontal, Is.EqualTo(1));
            held.Release(Control.Right);
            Assert.That(held.Horizontal, Is.EqualTo(-1));
            held.Press(Control.Down);
            held.Press(Control.Up);
            held.Release(Control.Down);
            Assert.That(held.Vertical, Is.EqualTo(1));
        }

        [Test]
        public void Bomb_IsTakenOnceEvenIfHeldOrPressedTwice()
        {
            var held = new HeldControls();
            held.Press(Control.Bomb);
            held.Press(Control.Bomb);
            Assert.That(held.TakeBomb(), Is.True);
            Assert.That(held.TakeBomb(), Is.False);
            held.Release(Control.Bomb);
            Assert.That(held.TakeBomb(), Is.False);
        }

        [Test]
        public void Shot_HeldKeepsFiringAndTapFiresOnce()
        {
            var held = new HeldControls();
            held.Press(Control.Shot);
            Assert.That(held.TakeShot(), Is.True);
            Assert.That(held.TakeShot(), Is.True);
            held.Release(Control.Shot);
            Assert.That(held.TakeShot(), Is.False);

            held.Press(Control.Shot);
            held.Release(Control.Shot);
            Assert.That(held.TakeShot(), Is.True);
            Assert.That(held.TakeShot(), Is.False);
        }

        [Test]
        public void Clear_DropsEverything()
        {
            var held = new HeldControls();
            held.Press(Control.Left);
            held.Press(Control.Slow);
            held.Press(Control.Shot);
            held.Press(Control.Bomb);
            held.Clear();
            Assert.That(held.Horizontal, Is.EqualTo(0));
            Assert.That(held.Slow, Is.False);
            Assert.That(held.TakeShot(), Is.False);
            Assert.That(held.TakeBomb(), Is.False);
        }

        [Test]
        public void UndefinedControl_Throws()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new HeldControls().Press((Control)99));
        }
    }
}
