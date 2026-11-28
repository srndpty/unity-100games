using Game.Core;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>強さ：少ない回数でもランダム打ちに勝ち越し、回数の多い方が勝ち越す。特定の手数・軌道は期待しない。</summary>
    public sealed class StrengthTests
    {
        /// <summary>手番の側の手を決める（playouts が 0 ならランダム打ち）。</summary>
        private static int Choose(GoGame game, int playouts, int seed, SeededRandom random)
        {
            if (playouts > 0)
            {
                var search = new Mcts(game, new MctsConfig(), playouts, RandomSeries.Derive(seed, game.MoveCount, 7));
                search.Run(playouts);
                return search.BestMove;
            }

            var board = game.CopyBoard();
            var candidates = game.LegalPoints().FindAll(p => !board.IsOwnEye(p, game.ToMove));
            return candidates.Count == 0 ? Points.Pass : candidates[random.Next(candidates.Count)];
        }

        /// <summary>1 局を打ち、黒の勝ちなら 1、白なら -1、引き分けは 0（盤をそのまま面積で数える）。</summary>
        private static int Play(int blackPlayouts, int whitePlayouts, int seed)
        {
            var game = new GoGame(Lab.Config);
            var random = new SeededRandom(seed);
            while (!game.IsOver)
            {
                var playouts = game.ToMove == Stone.Black ? blackPlayouts : whitePlayouts;
                var move = Choose(game, playouts, seed, random);
                var verdict = move == Points.Pass ? game.TryPass(game.ToMove) : game.TryPlay(game.ToMove, move);
                Assert.That(verdict, Is.EqualTo(MoveVerdict.Ok));
            }

            var winner = AreaScore.Compute(game.CopyBoard(), game.Komi).Winner;
            return winner == Stone.Black ? 1 : winner == Stone.White ? -1 : 0;
        }

        /// <summary>a と b を色を入れ替えて games 局打ち、a の勝ち数を返す。</summary>
        private static int Wins(int a, int b, int games)
        {
            var wins = 0;
            for (var i = 0; i < games; i++)
            {
                var aIsBlack = i % 2 == 0;
                var result = aIsBlack ? Play(a, b, 100 + i) : Play(b, a, 100 + i);
                if ((aIsBlack && result > 0) || (!aIsBlack && result < 0)) { wins++; }
            }

            return wins;
        }

        [Test]
        public void SmallSearch_BeatsRandomPlay()
        {
            Assert.That(Wins(150, 0, 6), Is.GreaterThanOrEqualTo(5));
        }

        [Test]
        public void MorePlayouts_BeatFewer()
        {
            Assert.That(Wins(300, 30, 6), Is.GreaterThanOrEqualTo(5));
        }
    }
}
