using System.Collections.Generic;
using System.Linq;
using Game.Core;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>MCTS：再現性・合法性・詰めの局面・パス。</summary>
    public sealed class MctsTests
    {
        private static Mcts Search(GoGame game, int playouts, int seed = 1, MctsConfig? config = null)
        {
            var search = new Mcts(game, config ?? new MctsConfig(), playouts, new RandomSeries(seed));
            search.Run(playouts);
            return search;
        }

        [Test]
        public void SameSeedAndTotal_GivesTheSameTree_HoweverTheRunsAreSplit()
        {
            var game = Lab.RandomGame(11, 20);
            var whole = Search(game, 600);
            foreach (var chunk in new[] { 1, 7, 599 })
            {
                var split = new Mcts(game, new MctsConfig(), 600, new RandomSeries(1));
                while (!split.IsDone) { split.Run(chunk); }
                Assert.That(split.Done, Is.EqualTo(600));
                Assert.That(split.BestMove, Is.EqualTo(whole.BestMove), $"{chunk} ずつ");
                Assert.That(split.RootChildren(), Is.EqualTo(whole.RootChildren()), $"{chunk} ずつ");
            }
        }

        [Test]
        public void Run_StopsAtTheTarget()
        {
            var search = new Mcts(new GoGame(Lab.Config), new MctsConfig(), 10, new RandomSeries(1));
            Assert.That(search.Run(7), Is.EqualTo(7));
            Assert.That(search.Run(7), Is.EqualTo(3));
            Assert.That(search.Run(7), Is.EqualTo(0));
            Assert.That(search.IsDone, Is.True);
            Assert.That(search.RootChildren().Sum(c => c.Visits), Is.EqualTo(10));
        }

        [Test]
        public void DifferentSeeds_CanGiveDifferentTrees()
        {
            var game = Lab.RandomGame(5, 10);
            var a = Search(game, 300, 1).RootChildren();
            var b = Search(game, 300, 2).RootChildren();
            Assert.That(a, Is.Not.EqualTo(b));
        }

        [TestCase(3)]
        [TestCase(8)]
        public void EveryExpandedPath_IsLegalForTheRealGame(int seed)
        {
            var game = Lab.RandomGame(seed, 30);
            AssertPathsLegal(game, Search(game, 800, seed));
        }

        [Test]
        public void ExpandedPaths_RespectKoAndSuperko()
        {
            // 三コウの局面：木の中でも即時の取り返しと同型反復を打たない。
            var game = Lab.Game(@"
                . X O . . . X O .
                X O . O . X O . O
                . X O . . . X O .
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                . O X . . . . . .
                O X . X . . . . .
                . O X . . . . . .");
            Lab.Play(game, "C8");
            var search = Search(game, 1500);
            AssertPathsLegal(game, search);
            Assert.That(search.RootChildren().Select(c => c.Move), Has.No.Member(Points.Parse("B8")), "白の即時の取り返し");
        }

        [Test]
        public void CandidatePoints_MatchTheGameRules_OnEveryPosition()
        {
            // 探索が展開する手 = 対局が受け付ける手から、自分の眼を埋める手を除いたもの（047）。
            foreach (var seed in new[] { 1, 2, 3, 4 })
            {
                Lab.RandomGame(seed, 200, game =>
                {
                    if (game.IsOver) { return; }
                    var board = game.CopyBoard();
                    var expected = game.LegalPoints().Where(p => !board.IsOwnEye(p, game.ToMove)).ToList();
                    Assert.That(Mcts.CandidatePoints(game, board, game.ToMove, null, null), Is.EqualTo(expected), $"seed {seed} の {game.MoveCount} 手目");
                });
            }
        }

        [Test]
        public void CapturesTheBigGroupInAtari()
        {
            // 白の 4 子（D5〜G5）の呼吸点は H5 だけ。白は隅にも石を持ち、取らなければ形勢は互角に近い。
            var game = Lab.Game(@"
                . . . . . . . . .
                . O O . . . . . .
                . O . . . . . . .
                . . . X X X X . .
                . . X O O O O . .
                . . . X X X X . .
                . . . . . . . O .
                . . . . . . O O .
                . . . . . . . . .");
            foreach (var seed in new[] { 1, 2, 3 })
            {
                Assert.That(Search(game, 1000, seed).BestMove, Is.EqualTo(Points.Parse("H5")), $"seed {seed}");
            }
        }

        [Test]
        public void SavesItsOwnGroupInAtari()
        {
            // 黒の 4 子（D5〜G5）の呼吸点は H5 だけ。白に取られる前に H5 へ伸びる（伸びると呼吸点が 3 つ）。
            var game = Lab.Game(@"
                . . . . . . . . .
                . X X . . . . . .
                . X . . . . . . .
                . . . O O O O . .
                . . O X X X X . .
                . . . O O O O . .
                . . . . . . . X .
                . . . . . . X X .
                . . . . . . . . .");
            Assert.That(game.LibertyCount(Points.Parse("D5")), Is.EqualTo(1));
            foreach (var seed in new[] { 1, 2, 3 })
            {
                Assert.That(Search(game, 1000, seed).BestMove, Is.EqualTo(Points.Parse("H5")), $"seed {seed}");
            }
        }

        [Test]
        public void OnlyOwnEyesLeft_Passes()
        {
            var game = Lab.Game(@"
                X . X X X X X X X
                X X X X X X X X X
                X X X X X X X X X
                X X X X X X X X X
                X X X X X X X X X
                X X X X X X X X X
                X X X X X X X X X
                X X X X X X X X X
                X X X X X X X X .");
            var search = Search(game, 50);
            Assert.That(search.BestMove, Is.EqualTo(Points.Pass));
            Assert.That(search.RootChildren().Select(c => c.Move), Is.EqualTo(new[] { Points.Pass }), "眼を埋める手は候補にしない");
        }

        [Test]
        public void AfterTheOpponentPasses_PassesWhenAlreadyWinning()
        {
            // 黒 45・白 36、コミ 7 で黒の 2 目勝ち。白がパスしたら、黒もパスして終える。
            var game = Lab.Game(@"
                . . . . X O . . .
                . . . . X O . . .
                . . . . X O . . .
                . . . . X O . . .
                . . . . X O . . .
                . . . . X O . . .
                . . . . X O . . .
                . . . . X O . . .
                . . . . X O . . .", Stone.White);
            Assert.That(game.TryPass(Stone.White), Is.EqualTo(MoveVerdict.Ok));
            Assert.That(Search(game, 500).BestMove, Is.EqualTo(Points.Pass));
        }

        [Test]
        public void AtTheRoot_DoesNotPassFirst_WhileMovesRemain()
        {
            var children = Search(new GoGame(Lab.Config), 100).RootChildren();
            Assert.That(children.Select(c => c.Move), Has.No.Member(Points.Pass));
        }

        [Test]
        public void Playouts_EndWithinTheMoveLimit()
        {
            var random = new RandomSeries(9);
            for (var i = 0; i < 50; i++)
            {
                var board = new Board();
                var moves = Playout.Run(board, Stone.Black, 0, Points.Pass, Points.Pass, random, 30);
                Assert.That(moves, Is.LessThanOrEqualTo(30));
                board = new Board();
                moves = Playout.Run(board, Stone.Black, 0, Points.Pass, Points.Pass, random, Points.Count * 3);
                Assert.That(moves, Is.LessThan(Points.Count * 3), "ふつうは連続パスで終わる");
                Lab.AssertChainsMatchNaive(board, $"プレイアウト {i}");
            }
        }

        [Test]
        public void Search_RejectsAFinishedGame_AndBadArguments()
        {
            var game = new GoGame(Lab.Config);
            Assert.That(() => new Mcts(game, new MctsConfig(), 0, new RandomSeries(1)), Throws.Exception);
            Assert.That(() => new Mcts(game, new MctsConfig { ExplorationPermille = -1 }, 10, new RandomSeries(1)), Throws.ArgumentException);
            game.TryPass(Stone.Black);
            game.TryPass(Stone.White);
            Assert.That(() => new Mcts(game, new MctsConfig(), 10, new RandomSeries(1)), Throws.ArgumentException);
        }

        [Test]
        public void Search_DoesNotChangeTheGame()
        {
            var game = Lab.RandomGame(4, 25);
            var before = game.StateHash();
            Search(game, 300);
            Assert.That(game.StateHash(), Is.EqualTo(before));
        }

        private static void AssertPathsLegal(GoGame game, Mcts search)
        {
            var paths = search.ExpandedPaths();
            Assert.That(paths.Count, Is.GreaterThan(10));
            foreach (var path in paths)
            {
                var copy = game.Clone();
                for (var i = 0; i < path.Length; i++)
                {
                    Assert.That(copy.IsOver, Is.False, $"終局の後の手 {Describe(path)}");
                    var verdict = path[i] == Points.Pass ? copy.TryPass(copy.ToMove) : copy.TryPlay(copy.ToMove, path[i]);
                    Assert.That(verdict, Is.EqualTo(MoveVerdict.Ok), Describe(path));
                }
            }
        }

        private static string Describe(IEnumerable<int> path) => string.Join(" ", path.Select(Points.Name));
    }
}
