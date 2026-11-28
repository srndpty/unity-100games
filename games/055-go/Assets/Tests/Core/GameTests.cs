using Game.Core;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>手番・パス・投了・終局と、拒否で状態が変わらないこと。</summary>
    public sealed class GameTests
    {
        [Test]
        public void BlackMovesFirst_AndTurnsAlternate()
        {
            var game = new GoGame(Lab.Config);
            Assert.That(game.ToMove, Is.EqualTo(Stone.Black));
            Assert.That(game.TryPlay(Stone.White, Points.Parse("E5")), Is.EqualTo(MoveVerdict.NotYourTurn));
            Lab.Play(game, "E5");
            Assert.That(game.ToMove, Is.EqualTo(Stone.White));
            Assert.That(game.LastMove, Is.EqualTo(Points.Parse("E5")));
        }

        [TestCase(-2)]
        [TestCase(81)]
        [TestCase(int.MaxValue)]
        public void OffBoard_IsRejected(int point)
        {
            var game = new GoGame(Lab.Config);
            Assert.That(game.TryPlay(Stone.Black, point), Is.EqualTo(MoveVerdict.OffBoard));
            Assert.That(game.MoveCount, Is.EqualTo(0));
        }

        [Test]
        public void Occupied_IsRejected()
        {
            var game = new GoGame(Lab.Config);
            Lab.Play(game, "E5");
            var before = game.StateHash();
            Assert.That(game.TryPlay(Stone.White, Points.Parse("E5")), Is.EqualTo(MoveVerdict.Occupied));
            Assert.That(game.StateHash(), Is.EqualTo(before));
        }

        [Test]
        public void TwoConsecutivePasses_EndTheGame_AndLaterMovesAreRejected()
        {
            var game = new GoGame(Lab.Config);
            Lab.Play(game, "E5");
            Assert.That(game.TryPass(Stone.White), Is.EqualTo(MoveVerdict.Ok));
            Lab.Play(game, "D5"); // パスの後に石を打つと連続が切れる
            Assert.That(game.ConsecutivePasses, Is.EqualTo(0));
            Assert.That(game.TryPass(Stone.White), Is.EqualTo(MoveVerdict.Ok));
            Assert.That(game.IsOver, Is.False);
            Assert.That(game.TryPass(Stone.Black), Is.EqualTo(MoveVerdict.Ok));
            Assert.That(game.End, Is.EqualTo(GameEnd.TwoPasses));

            var before = game.StateHash();
            Assert.That(game.TryPlay(Stone.White, Points.Parse("A1")), Is.EqualTo(MoveVerdict.GameOver));
            Assert.That(game.TryPass(Stone.White), Is.EqualTo(MoveVerdict.GameOver));
            Assert.That(game.TryResign(Stone.White), Is.EqualTo(MoveVerdict.GameOver));
            Assert.That(game.LegalPoints(), Is.Empty);
            Assert.That(game.StateHash(), Is.EqualTo(before));
        }

        [Test]
        public void Resign_EndsTheGame_Once()
        {
            var game = new GoGame(Lab.Config);
            Lab.Play(game, "E5");
            Assert.That(game.TryResign(Stone.Black), Is.EqualTo(MoveVerdict.Ok), "手番でなくても投了できる");
            Assert.That(game.End, Is.EqualTo(GameEnd.Resignation));
            Assert.That(game.Resigned, Is.EqualTo(Stone.Black));
            Assert.That(game.TryResign(Stone.White), Is.EqualTo(MoveVerdict.GameOver), "二重の投了");
            Assert.That(game.Resigned, Is.EqualTo(Stone.Black));
        }

        [Test]
        public void MoveLimit_EndsTheGame()
        {
            var config = Lab.Config;
            config.MoveLimit = 3;
            var game = new GoGame(config);
            Lab.Play(game, "E5");
            game.TryPass(Stone.White);
            Assert.That(game.IsOver, Is.False);
            Lab.Play(game, "D5");
            Assert.That(game.End, Is.EqualTo(GameEnd.MoveLimit));
        }

        [Test]
        public void SecondPass_OnTheMoveLimit_EndsAsTwoPasses()
        {
            var config = Lab.Config;
            config.MoveLimit = 2;
            var game = new GoGame(config);
            game.TryPass(Stone.Black);
            game.TryPass(Stone.White);
            Assert.That(game.End, Is.EqualTo(GameEnd.TwoPasses));
        }

        [Test]
        public void Clone_IsIndependent()
        {
            var game = Lab.RandomGame(3, 40);
            var copy = game.Clone();
            Assert.That(copy.StateHash(), Is.EqualTo(game.StateHash()));
            var point = copy.LegalPoints()[0];
            copy.TryPlay(copy.ToMove, point);
            Assert.That(game[point], Is.EqualTo(Stone.Empty));
            Assert.That(copy.StateHash(), Is.Not.EqualTo(game.StateHash()));
        }

        [Test]
        public void StateHash_IncludesTheKomiAndTheMoveLimit()
        {
            var plain = new GoGame(Lab.Config).StateHash();
            Assert.That(new GoGame(new GoConfig { Komi = 6 }).StateHash(), Is.Not.EqualTo(plain));
            Assert.That(new GoGame(new GoConfig { MoveLimit = 299 }).StateHash(), Is.Not.EqualTo(plain));
        }

        [Test]
        public void StateHash_DiffersWhenOnlyTheHistoryDiffers()
        {
            // 同じ盤・同じ手番でも、通った盤面が違えば以後の同型反復が変わるので別の値。
            var a = new GoGame(Lab.Config);
            Lab.Play(a, "C3");
            Lab.Play(a, "G7");
            Lab.Play(a, "C7");
            Lab.Play(a, "G3");
            var b = new GoGame(Lab.Config);
            Lab.Play(b, "C7");
            Lab.Play(b, "G3");
            Lab.Play(b, "C3");
            Lab.Play(b, "G7");
            Assert.That(a.Hash, Is.EqualTo(b.Hash), "盤面は同じ");
            Assert.That(a.StateHash(), Is.Not.EqualTo(b.StateHash()));
        }

        [Test]
        public void PointNames_RoundTrip_AndSkipTheLetterI()
        {
            Assert.That(Points.Name(0), Is.EqualTo("A1"));
            Assert.That(Points.Name(80), Is.EqualTo("J9"));
            Assert.That(Points.Name(Points.At(7, 0)), Is.EqualTo("H1"));
            Assert.That(Points.Name(Points.At(8, 0)), Is.EqualTo("J1"));
            Assert.That(Points.TryParse("I5", out _), Is.False);
            Assert.That(Points.TryParse("A0", out _), Is.False);
            Assert.That(Points.TryParse("K1", out _), Is.False);
            for (var p = 0; p < Points.Count; p++) { Assert.That(Points.Parse(Points.Name(p)), Is.EqualTo(p)); }
        }

        [Test]
        public void FixedRandomGame_HasAFixedFinalHash()
        {
            // 整数だけの規則なので、.NET と Mono（Unity）で同じ値になる。
            var game = Lab.RandomGame(2026, 300);
            Assert.That(game.StateHash(), Is.EqualTo(0xDA1BDA78u), $"最終ハッシュ 0x{game.StateHash():X8}（{game.MoveCount} 手、{game.End}）");
        }
    }
}
