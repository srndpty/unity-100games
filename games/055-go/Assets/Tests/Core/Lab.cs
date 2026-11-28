using System.Collections.Generic;
using Game.Core;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>テストの局面作りと、素朴な参照実装（塗りつぶしで数え直す）。</summary>
    internal static class Lab
    {
        public static GoConfig Config => new();

        public static GoGame Game(string text, Stone toMove = Stone.Black) => new(Config, BoardText.Parse(text), toMove);

        public static void Play(GoGame game, string point)
        {
            var verdict = game.TryPlay(game.ToMove, Points.Parse(point));
            Assert.That(verdict, Is.EqualTo(MoveVerdict.Ok), $"{game.ToMove.Label()} {point}");
        }

        /// <summary>
        /// 乱択の 1 局：合法手（自分の眼を埋める手を除く）から一様に選び、なければパス。
        /// 各手の後に <paramref name="after"/> を呼ぶ。
        /// </summary>
        public static GoGame RandomGame(int seed, int maxMoves, System.Action<GoGame>? after = null, GoGame? start = null)
        {
            var game = start ?? new GoGame(Config);
            var random = new SeededRandom(seed);
            while (!game.IsOver && game.MoveCount < maxMoves)
            {
                var board = game.CopyBoard();
                var candidates = new List<int>();
                foreach (var p in game.LegalPoints())
                {
                    if (!board.IsOwnEye(p, game.ToMove)) { candidates.Add(p); }
                }

                var verdict = candidates.Count == 0 ? game.TryPass(game.ToMove) : game.TryPlay(game.ToMove, candidates[random.Next(candidates.Count)]);
                Assert.That(verdict, Is.EqualTo(MoveVerdict.Ok));
                after?.Invoke(game);
            }

            return game;
        }

        /// <summary>参照実装：その点を含む連の石と呼吸点を塗りつぶしで求める。</summary>
        public static (HashSet<int> Stones, HashSet<int> Liberties) NaiveChain(Board board, int start)
        {
            var color = board[start];
            var stones = new HashSet<int> { start };
            var liberties = new HashSet<int>();
            var stack = new Stack<int>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                var p = stack.Pop();
                var column = Points.Column(p);
                var row = Points.Row(p);
                foreach (var (dc, dr) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    var c = column + dc;
                    var r = row + dr;
                    if (c < 0 || c >= Points.Size || r < 0 || r >= Points.Size) { continue; }
                    var n = (r * Points.Size) + c;
                    if (board[n] == Stone.Empty) { liberties.Add(n); }
                    else if (board[n] == color && stones.Add(n)) { stack.Push(n); }
                }
            }

            return (stones, liberties);
        }

        public static HashSet<int> ToSet(PointSet set)
        {
            var result = new HashSet<int>();
            foreach (var p in set) { result.Add(p); }
            return result;
        }

        /// <summary>差分で持つ連・呼吸点・連の番号が、参照実装の数え直しと一致することを確かめる。</summary>
        public static void AssertChainsMatchNaive(Board board, string context)
        {
            for (var p = 0; p < Points.Count; p++)
            {
                if (board[p] == Stone.Empty)
                {
                    Assert.That(board.ChainAt(p).IsEmpty, Is.True, $"{context}: 空点 {Points.Name(p)} に連がある");
                    Assert.That(board.ChainId(p), Is.EqualTo(-1), $"{context}: 空点 {Points.Name(p)} の連の番号");
                    continue;
                }

                var (stones, liberties) = NaiveChain(board, p);
                Assert.That(ToSet(board.ChainAt(p)), Is.EquivalentTo(stones), $"{context}: {Points.Name(p)} の連の石");
                Assert.That(ToSet(board.LibertiesAt(p)), Is.EquivalentTo(liberties), $"{context}: {Points.Name(p)} の呼吸点");
                Assert.That(board.LibertyCount(p), Is.EqualTo(liberties.Count), $"{context}: {Points.Name(p)} の呼吸点の数");
                foreach (var q in stones)
                {
                    Assert.That(board.ChainId(q), Is.EqualTo(board.ChainId(p)), $"{context}: 同じ連の番号");
                }
            }

            // 別の連は別の番号。
            for (var p = 0; p < Points.Count; p++)
            {
                for (var q = p + 1; q < Points.Count; q++)
                {
                    if (board[p] == Stone.Empty || board[q] == Stone.Empty) { continue; }
                    if (board.ChainAt(p).Contains(q)) { continue; }
                    Assert.That(board.ChainId(p), Is.Not.EqualTo(board.ChainId(q)), $"{context}: 別の連 {Points.Name(p)} と {Points.Name(q)}");
                }
            }
        }
    }
}
