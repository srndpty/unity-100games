using Game.Core;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>題材固有：自殺手を拒否。</summary>
    public sealed class SuicideTests
    {
        [Test]
        public void SingleStoneSuicide_InTheCorner_IsRejected_AndNothingChanges()
        {
            var game = Lab.Game(@"
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                O . . . . . . . .
                . O . . . . . . .");
            var before = game.StateHash();
            var hash = game.Hash;
            Assert.That(game.TryPlay(Stone.Black, Points.Parse("A1")), Is.EqualTo(MoveVerdict.Suicide));
            Assert.That(game[Points.Parse("A1")], Is.EqualTo(Stone.Empty));
            Assert.That(game.Hash, Is.EqualTo(hash));
            Assert.That(game.StateHash(), Is.EqualTo(before));
            Assert.That(game.ToMove, Is.EqualTo(Stone.Black));
            Assert.That(game.MoveCount, Is.EqualTo(0));
        }

        [Test]
        public void SingleStoneSuicide_InTheMiddle_IsRejected()
        {
            var game = Lab.Game(@"
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                . . . . O . . . .
                . . . O . O . . .
                . . . . O . . . .
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .");
            Assert.That(game.Check(Stone.Black, Points.Parse("E5")), Is.EqualTo(MoveVerdict.Suicide));
        }

        [Test]
        public void MultiStoneSuicide_JoiningAChainWithNoLibertyLeft_IsRejected()
        {
            // 黒 A1 の呼吸点は B1 だけ。B1 に打つと A1・B1 の連の呼吸点が 0 になり、白はどれも取れない。
            var game = Lab.Game(@"
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                O O . . . . . . .
                X . O . . . . . .");
            Assert.That(game.TryPlay(Stone.Black, Points.Parse("B1")), Is.EqualTo(MoveVerdict.Suicide));
            Assert.That(game[Points.Parse("A1")], Is.EqualTo(Stone.Black), "元の石は残る");
            Assert.That(game.LibertyCount(Points.Parse("A1")), Is.EqualTo(1));
        }

        [Test]
        public void ZeroLibertyPoint_ThatCaptures_IsLegal()
        {
            // A1 の四方は白だが、白 A2 と白 B1 はどちらも A1 だけが呼吸点。黒 A1 で両方取れる。
            var game = Lab.Game(@"
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                X . . . . . . . .
                O X . . . . . . .
                . O X . . . . . .");
            Lab.Play(game, "A1");
            Assert.That(game[Points.Parse("A2")], Is.EqualTo(Stone.Empty));
            Assert.That(game[Points.Parse("B1")], Is.EqualTo(Stone.Empty));
            Assert.That(game.Captures(Stone.Black), Is.EqualTo(2));
            Assert.That(game.LibertyCount(Points.Parse("A1")), Is.EqualTo(2), "取った跡が呼吸点になる");
        }

        [Test]
        public void WhiteSuicide_IsRejectedToo()
        {
            var game = Lab.Game(@"
                . . . . . . . X .
                . . . . . . . . X
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .", Stone.White);
            Assert.That(game.TryPlay(Stone.White, Points.Parse("J9")), Is.EqualTo(MoveVerdict.Suicide));
            Assert.That(game.ToMove, Is.EqualTo(Stone.White));
        }
    }
}
