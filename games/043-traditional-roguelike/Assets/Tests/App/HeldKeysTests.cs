using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>押しているキーから方向を決める。</summary>
    public sealed class HeldKeysTests
    {
        [Test]
        public void AxesCombineAndTheLatestKeyWins()
        {
            var keys = new HeldKeys();
            Assert.That(keys.Direction(), Is.Null);
            keys.Press(Control.Up, 0);
            Assert.That(keys.Direction(), Is.EqualTo(Direction.North));
            keys.Press(Control.Left, 1);
            Assert.That(keys.Direction(), Is.EqualTo(Direction.NorthWest));
            keys.Press(Control.Right, 0);
            Assert.That(keys.Direction(), Is.EqualTo(Direction.NorthEast), "後から押した右");
            keys.Release(Control.Right, 0);
            Assert.That(keys.Direction(), Is.EqualTo(Direction.NorthWest));
        }

        [Test]
        public void PadOverridesAxesAndShiftRequiresTwoAxes()
        {
            var keys = new HeldKeys();
            keys.Press(Control.Down, 0);
            keys.Press(Control.Pad, (int)Direction.West);
            Assert.That(keys.Direction(), Is.EqualTo(Direction.West));
            keys.Release(Control.Pad, (int)Direction.West);
            keys.Press(Control.Diagonal, 0);
            Assert.That(keys.Direction(), Is.Null);
            keys.Press(Control.Right, 1);
            Assert.That(keys.Direction(), Is.EqualTo(Direction.SouthEast));
            keys.Press(Control.Pad, (int)Direction.North);
            Assert.That(keys.Direction(), Is.EqualTo(Direction.North), "テンキーは Shift でもそのまま");
        }

        [Test]
        public void SamePurposeOnTwoPhysicalKeysIsHeldSeparately()
        {
            var keys = new HeldKeys();
            keys.Press(Control.Left, 0);
            keys.Press(Control.Left, 1);
            keys.Release(Control.Left, 0);
            Assert.That(keys.Direction(), Is.EqualTo(Direction.West));
            keys.Release(Control.Left, 1);
            Assert.That(keys.Direction(), Is.Null);
            Assert.That(keys.AnyDirectionKey, Is.False);
        }
    }
}
