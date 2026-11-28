using Game.Core;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>題材固有：単純コウの即時取り返し禁止。あわせて同型反復（三コウ）と打って返し。</summary>
    public sealed class KoTests
    {
        // 左上と右上は黒が取れるコウ（C8・H8）、左下は白が取れるコウ（C2）。
        private const string ThreeKos = @"
            . X O . . . X O .
            X O . O . X O . O
            . X O . . . X O .
            . . . . . . . . .
            . . . . . . . . .
            . . . . . . . . .
            . O X . . . . . .
            O X . X . . . . .
            . O X . . . . . .";

        [Test]
        public void ImmediateRetake_IsRejectedAsKo_AndChangesNothing()
        {
            var game = Lab.Game(ThreeKos);
            Lab.Play(game, "C8");
            Assert.That(game[Points.Parse("B8")], Is.EqualTo(Stone.Empty), "白 B8 を取った");
            var before = game.StateHash();
            Assert.That(game.TryPlay(Stone.White, Points.Parse("B8")), Is.EqualTo(MoveVerdict.Ko));
            Assert.That(game.StateHash(), Is.EqualTo(before));
            Assert.That(game.ToMove, Is.EqualTo(Stone.White));
            Assert.That(game[Points.Parse("C8")], Is.EqualTo(Stone.Black));
        }

        [Test]
        public void Retake_AfterMovesElsewhere_IsLegal()
        {
            var game = Lab.Game(ThreeKos);
            Lab.Play(game, "C8");
            Lab.Play(game, "E5"); // 白はコウ材
            Lab.Play(game, "F5"); // 黒が受ける
            Lab.Play(game, "B8"); // 白の取り返し
            Assert.That(game[Points.Parse("C8")], Is.EqualTo(Stone.Empty));
            Assert.That(game.Check(Stone.Black, Points.Parse("C8")), Is.EqualTo(MoveVerdict.Ko), "今度は黒の即時の取り返しが禁止");
        }

        [Test]
        public void Retake_AfterAPass_IsStillTheSamePositionOnlyIfItRepeats()
        {
            // 黒が取って白がパス。黒が別の手を打てば、白の取り返しは新しい盤面なので合法。
            var game = Lab.Game(ThreeKos);
            Lab.Play(game, "C8");
            Assert.That(game.TryPass(Stone.White), Is.EqualTo(MoveVerdict.Ok));
            Lab.Play(game, "E5");
            Assert.That(game.Check(Stone.White, Points.Parse("B8")), Is.EqualTo(MoveVerdict.Ok));
        }

        [Test]
        public void Snapback_IsNotKo()
        {
            // 黒 J1 のほうり込みを白 H1 で取ると、白 5 子の呼吸点は J1 だけ。黒 J1 で 5 子を取り返す（1 子のコウではない）。
            var game = Lab.Game(@"
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . X X X
                . . . . . X O O O
                . . . . . X O . .");
            Lab.Play(game, "J1");
            Lab.Play(game, "H1");
            Assert.That(game[Points.Parse("J1")], Is.EqualTo(Stone.Empty), "ほうり込んだ石は取られた");
            Assert.That(game.Check(Stone.Black, Points.Parse("J1")), Is.EqualTo(MoveVerdict.Ok));
            Lab.Play(game, "J1");
            Assert.That(game.Captures(Stone.Black), Is.EqualTo(5));
            Assert.That(game.Captures(Stone.White), Is.EqualTo(1));
        }

        [Test]
        public void TripleKoCycle_IsRejectedAsSuperko()
        {
            // 三コウ：黒 C8、白 C2、黒 H8、白 B8、黒 B2 と取り合うと、白 G8 で最初の盤面に戻る。
            var game = Lab.Game(ThreeKos);
            var start = game.CopyBoard().CopyCells();
            Lab.Play(game, "C8");
            Lab.Play(game, "C2");
            Lab.Play(game, "H8");
            Lab.Play(game, "B8");
            Lab.Play(game, "B2");
            var before = game.StateHash();
            Assert.That(game.TryPlay(Stone.White, Points.Parse("G8")), Is.EqualTo(MoveVerdict.Superko));
            Assert.That(game.StateHash(), Is.EqualTo(before));

            // 拒否した手を盤だけで打つと、確かに最初の盤面になる。
            var board = game.CopyBoard();
            board.Play(Points.Parse("G8"), Stone.White);
            Assert.That(board.CopyCells(), Is.EqualTo(start));
        }

        [Test]
        public void HashCollisions_DoNotCauseFalseRejections()
        {
            // 全ての鍵が 0 の表ではどの盤面もハッシュ 0（必ず衝突する）。盤面の比較で確定するので、判定は標準の表と同じ。
            var colliding = Zobrist.FromKeys(new ulong[Points.Count * 2]);
            var reference = Lab.RandomGame(7, 120);
            var game = new GoGame(Lab.Config, new Board(colliding), Stone.Black);
            var standard = new GoGame(Lab.Config);
            for (var i = 0; i < reference.MoveCount; i++)
            {
                for (var p = 0; p < Points.Count; p++)
                {
                    Assert.That(game.Check(game.ToMove, p), Is.EqualTo(standard.Check(standard.ToMove, p)), $"{i} 手目の {Points.Name(p)}");
                }

                var move = reference.Move(i);
                if (move == Points.Pass)
                {
                    game.TryPass(game.ToMove);
                    standard.TryPass(standard.ToMove);
                }
                else
                {
                    Assert.That(game.TryPlay(game.ToMove, move), Is.EqualTo(MoveVerdict.Ok));
                    Assert.That(standard.TryPlay(standard.ToMove, move), Is.EqualTo(MoveVerdict.Ok));
                }
            }
        }

        [Test]
        public void HashCollisions_StillDetectTheRealRepeat()
        {
            var colliding = Zobrist.FromKeys(new ulong[Points.Count * 2]);
            var game = new GoGame(Lab.Config, Board.FromCells(BoardText.ParseCells(ThreeKos), colliding), Stone.Black);
            Lab.Play(game, "C8");
            Assert.That(game.Check(Stone.White, Points.Parse("B8")), Is.EqualTo(MoveVerdict.Ko));
        }
    }
}
