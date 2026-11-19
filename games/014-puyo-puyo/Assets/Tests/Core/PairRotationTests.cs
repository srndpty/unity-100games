using NUnit.Framework;
using static Game.Core.Tests.SporeTestKit;

namespace Game.Core.Tests
{
    public sealed class PairRotationTests
    {
        private static ActivePair At(int x, int y, PairOrientation orientation) => new(EmberPair, x, y, orientation);

        [TestCase(PairOrientation.Up, true, PairOrientation.Right)]
        [TestCase(PairOrientation.Right, true, PairOrientation.Down)]
        [TestCase(PairOrientation.Down, true, PairOrientation.Left)]
        [TestCase(PairOrientation.Left, true, PairOrientation.Up)]
        [TestCase(PairOrientation.Up, false, PairOrientation.Left)]
        [TestCase(PairOrientation.Left, false, PairOrientation.Down)]
        public void 空いていればその場で回る(PairOrientation from, bool clockwise, PairOrientation to)
        {
            var turned = PairRotation.Rotate(Board.Empty, At(2, 5, from), clockwise);
            Assert.That(turned, Is.EqualTo(At(2, 5, to)));
        }

        [Test]
        public void 右の壁では軸が左へ押し出される()
        {
            var turned = PairRotation.Rotate(Board.Empty, At(Board.Width - 1, 5, PairOrientation.Up), true);
            Assert.That(turned, Is.EqualTo(At(Board.Width - 2, 5, PairOrientation.Right)));
        }

        [Test]
        public void 左の壁では軸が右へ押し出される()
        {
            var turned = PairRotation.Rotate(Board.Empty, At(0, 5, PairOrientation.Up), false);
            Assert.That(turned, Is.EqualTo(At(1, 5, PairOrientation.Left)));
        }

        [Test]
        public void 床では軸が上へ押し出される()
        {
            var turned = PairRotation.Rotate(Board.Empty, At(2, 0, PairOrientation.Right), true);
            Assert.That(turned, Is.EqualTo(At(2, 1, PairOrientation.Down)));
        }

        [Test]
        public void 積んだ胞子の上でも軸が上へ押し出される()
        {
            var board = Parse("..E...");
            var turned = PairRotation.Rotate(board, At(2, 1, PairOrientation.Right), true);
            Assert.That(turned, Is.EqualTo(At(2, 2, PairOrientation.Down)));
        }

        [Test]
        public void 天井では軸が下へ押し出される()
        {
            var turned = PairRotation.Rotate(Board.Empty, At(2, Board.Height - 1, PairOrientation.Right), false);
            Assert.That(turned, Is.EqualTo(At(2, Board.Height - 2, PairOrientation.Up)));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void 一列の井戸では軸と子が入れ替わる(bool clockwise)
        {
            var board = Parse("E.E...", "E.E...", "E.E...");
            var turned = PairRotation.Rotate(board, At(1, 0, PairOrientation.Up), clockwise);
            Assert.That(turned, Is.EqualTo(At(1, 1, PairOrientation.Down)));
            var back = PairRotation.Rotate(board, turned!.Value, clockwise);
            Assert.That(back, Is.EqualTo(At(1, 0, PairOrientation.Up)));
        }

        [Test]
        public void 横向きで上下とも塞がると回らない()
        {
            var board = Parse("..E...", "......", "..E...");
            Assert.That(PairRotation.Rotate(board, At(2, 1, PairOrientation.Right), true), Is.Null);
            Assert.That(PairRotation.Rotate(board, At(2, 1, PairOrientation.Right), false), Is.Null);
        }
    }
}
