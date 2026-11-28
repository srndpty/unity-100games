using System;
using System.Collections.Generic;
using IntGeometry;

namespace Game.Core
{
    /// <summary>
    /// 回数で止める UCT 型のモンテカルロ木探索。手番ごとに木を作り直す。
    /// <list type="bullet">
    /// <item>選択：(1 − β) × 勝率 + β × AMAF の勝率 + 係数 × √(親の訪問) ÷ (1 + 子の訪問)。β = √(k ÷ (3n + k))（RAVE、n は子の訪問、k は等価の回数）。
    /// AMAF は「その節点より後（木とプレイアウト）で、その手を同じ側が最初に打った」回の勝敗。全て整数で計算する（Mono と .NET で同じ手になる）。
    /// 訪問も AMAF もない子は、展開時に乱数で並べた順に先に選ぶ。</item>
    /// <item>展開：同型反復まで判定した合法手から自分の眼を埋める手を除いたものとパス。ただし眼を埋めると別々の連がつながる手は残す。
    /// 根のパスは「相手が直前にパスした」か「ほかに手がない」ときだけ。</item>
    /// <item>終局：2 連続パスか、対局の手数の上限（<see cref="GoConfig.MoveLimit"/>、パスも数える）に届いた節点は展開もプレイアウトもせず、そのまま面積で数える。
    /// プレイアウトも上限までの残りの手数で打ち切る（対局で起きない先の局面で評価しない）。</item>
    /// <item>評価：<see cref="Playout"/> の勝敗（勝ち 2・引き分け 1・負け 0 を足す）。</item>
    /// <item>根ですぐ終局する手（相手がパスした後のパス、手数の上限まで残り 1 手ならどの手も）は、終局後と同じ死石の推定
    /// （<see cref="OwnershipSearch"/>、試合シードを渡したとき）で死石を除いて数える。推定は <see cref="Run"/> の回数の中で木より先に少しずつ回す。</item>
    /// </list>
    /// <see cref="Run"/> を何回に分けて呼んでも、総数が同じなら同じ木（同じ手）になる。
    /// </summary>
    public sealed class Mcts
    {
        private const long Scale = 1_000_000;

        private readonly GoGame _root;
        private readonly MctsConfig _config;
        private readonly RandomSeries _random;
        private readonly Node _rootNode;
        private readonly List<int> _sequence = new();
        private readonly List<(Node Node, int Start)> _path = new();
        private readonly Stone[] _first = new Stone[Points.Count];
        private readonly List<EndCase> _ends = new();
        private readonly Dictionary<int, Stone> _endWinners = new();

        /// <param name="endSeed">
        /// 試合シード。渡すと、根ですぐ終局する手を、終局後と同じ系列（<see cref="Ownership.Series"/>）の死石の推定で死石を除いて数える。
        /// null なら盤のまま数える。
        /// </param>
        public Mcts(GoGame game, MctsConfig config, int playouts, RandomSeries random, int? endSeed = null)
        {
            if (game == null) { throw new ArgumentNullException(nameof(game)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            var errors = config.Validate();
            if (errors.Count > 0) { throw new ArgumentException("探索の設定が範囲外です: " + string.Join(" / ", errors), nameof(config)); }
            if (game.IsOver) { throw new ArgumentException("終局した対局は探索できません。", nameof(game)); }
            if (playouts < 1) { throw new ArgumentOutOfRangeException(nameof(playouts), playouts, "1 以上を指定してください。"); }
            _root = game.Clone();
            _config = config.Copy();
            _random = random ?? throw new ArgumentNullException(nameof(random));
            Target = playouts;
            Side = game.ToMove;
            _rootNode = new Node(Points.Pass, Side.Opponent(), null);
            if (endSeed.HasValue) { PrepareEnds(endSeed.Value); }
        }

        /// <summary>探索する側（根の手番）。</summary>
        public Stone Side { get; }

        /// <summary>回すプレイアウトの総数。</summary>
        public int Target { get; }

        /// <summary>回し終えたプレイアウトの数。</summary>
        public int Done { get; private set; }

        public bool IsDone => Done >= Target && _endWinners.Count == _ends.Count;

        /// <summary>根ですぐ終局する手の数（死石の推定で数える手）。</summary>
        public int EndMoveCount => _ends.Count;

        /// <summary>根の手 <paramref name="move"/> で終局したときの死石の推定（推定しない手なら null）。</summary>
        public OwnershipSearch? EndEstimateOf(int move)
        {
            foreach (var e in _ends)
            {
                if (e.Move == move) { return e.Search; }
            }

            return null;
        }

        /// <summary>最多訪問の手（同数なら点の番号の小さい方、パスは最後）。まだ 1 回も回していなければ例外。</summary>
        public int BestMove => Best().Move;

        /// <summary>最多訪問の手の勝率（‰、探索する側から見て）。</summary>
        public int BestWinPermille
        {
            get
            {
                var best = Best();
                return best.Visits == 0 ? 500 : (int)(best.Wins2 * 1000 / (2L * best.Visits));
            }
        }

        /// <summary>根の子（手・訪問・勝ち点の 2 倍）。表示とテスト用。</summary>
        public IReadOnlyList<(int Move, int Visits, long Wins2)> RootChildren()
        {
            var list = new List<(int, int, long)>();
            if (_rootNode.Children == null) { return list; }
            foreach (var c in _rootNode.Children) { list.Add((c.Move, c.Visits, c.Wins2)); }
            return list;
        }

        /// <summary>
        /// 最大 <paramref name="maxPlayouts"/> 回（残りがそれより少なければ残りだけ）回す。戻り値は回した数（死石の推定のプレイアウトを含む）。
        /// 死石の推定があれば、それを先に回し終えてから木を回す。
        /// </summary>
        public int Run(int maxPlayouts)
        {
            if (maxPlayouts < 0) { throw new ArgumentOutOfRangeException(nameof(maxPlayouts)); }
            var used = 0;
            foreach (var e in _ends)
            {
                if (_endWinners.ContainsKey(e.Move)) { continue; }
                used += e.Search.Run(maxPlayouts - used);
                if (!e.Search.IsDone) { return used; }
                var dead = e.Search.EstimateDead(_config.DeadPercent);
                _endWinners.Add(e.Move, AreaScore.Compute(e.After.CopyBoard(), dead, _root.Komi).Winner);
            }

            var count = Math.Min(maxPlayouts - used, Target - Done);
            for (var i = 0; i < count; i++) { Iterate(); }
            Done += count;
            return used + count;
        }

        /// <summary>展開した全ての節点への手の列（テストで全ての手が合法かを確かめる）。</summary>
        internal List<int[]> ExpandedPaths()
        {
            var result = new List<int[]>();
            var path = new List<int>();
            void Walk(Node node)
            {
                if (node.Children == null) { return; }
                foreach (var c in node.Children)
                {
                    path.Add(c.Move);
                    result.Add(path.ToArray());
                    Walk(c);
                    path.RemoveAt(path.Count - 1);
                }
            }

            Walk(_rootNode);
            return result;
        }

        /// <summary>探索の局面で展開する手（同型反復まで判定、別々の連をつながない眼埋めを除く）。パスは含めない。</summary>
        internal static List<int> CandidatePoints(GoGame root, Board board, Stone toMove, List<ulong>? pathHashes, List<Stone[]>? pathCells)
        {
            var list = new List<int>();
            for (var p = 0; p < Points.Count; p++)
            {
                if (board[p] != Stone.Empty || (board.IsOwnEye(p, toMove) && !board.JoinsChains(p))) { continue; }
                var preview = board.Preview(p, toMove);
                if (!preview.IsOk) { continue; }
                if (root.Repeats(board, p, toMove, preview, pathHashes, pathCells)) { continue; }
                list.Add(p);
            }

            return list;
        }

        private Node Best()
        {
            var children = _rootNode.Children;
            if (children == null || children.Length == 0) { throw new InvalidOperationException("まだ探索していません。"); }

            // 相手がパスした後のパスは終局。数えて勝っている（全て勝ち）なら、ほかの手と訪問が並んでもパスで終える。
            if (_root.ConsecutivePasses > 0)
            {
                foreach (var c in children)
                {
                    if (c.Move == Points.Pass && c.Visits > 0 && c.Wins2 == 2L * c.Visits) { return c; }
                }
            }

            Node? best = null;
            foreach (var c in children)
            {
                if (best == null || c.Visits > best.Visits || (c.Visits == best.Visits && Order(c.Move) < Order(best.Move))) { best = c; }
            }

            return best!;
        }

        private static int Order(int move) => move == Points.Pass ? Points.Count : move;

        private void Iterate()
        {
            var board = _root.CopyBoard();
            var toMove = _root.ToMove;
            var passes = _root.ConsecutivePasses;
            var moveCount = _root.MoveCount;
            var moveLimit = _root.Config.MoveLimit;
            var koPoint = Points.Pass;
            var lastMove = _root.LastMove ?? Points.Pass;
            var pathHashes = new List<ulong>();
            var pathCells = new List<Stone[]>();
            var node = _rootNode;
            _sequence.Clear();
            _path.Clear();
            _path.Add((_rootNode, 0));

            bool IsTerminal() => passes >= 2 || moveCount >= moveLimit;

            void Apply(int move)
            {
                lastMove = move;
                moveCount++;
                if (move != Points.Pass) { _sequence.Add((move << 2) | (int)toMove); }
                if (move == Points.Pass)
                {
                    passes++;
                    koPoint = Points.Pass;
                }
                else
                {
                    var captured = board.Play(move, toMove);
                    koPoint = Playout.KoPointAfter(board, move, captured);
                    passes = 0;
                    pathHashes.Add(board.Hash);
                    pathCells.Add(board.CopyCells());
                }

                toMove = toMove.Opponent();
            }

            // 選択：展開済みの節点を下る。
            while (node.Children != null && node.Children.Length > 0 && !IsTerminal())
            {
                node = Select(node);
                Apply(node.Move);
                _path.Add((node, _sequence.Count));
            }

            // 展開：根か、一度評価した葉を展開して最初の子へ進む。
            if (!IsTerminal() && node.Children == null && (node == _rootNode || node.Visits > 0))
            {
                Expand(node, board, toMove, pathHashes, pathCells);
                node = node.Children![0];
                Apply(node.Move);
                _path.Add((node, _sequence.Count));
            }

            // 評価。根の手ですぐ終局したなら、死石を除いた計点（推定は Run が先に済ませている）。
            Stone winner;
            if (_path.Count == 2 && _endWinners.TryGetValue(_path[1].Node.Move, out var endWinner))
            {
                winner = endWinner;
            }
            else
            {
                if (!IsTerminal()) { Playout.Run(board, toMove, passes, koPoint, lastMove, _random, Math.Min(_config.PlayoutMoveLimit, moveLimit - moveCount), _sequence); }
                winner = Playout.Winner(board, _root.Komi);
            }

            // 逆伝播：各節点の手を打った側から見た勝ち点。子の AMAF は、その節点より後に同じ側が最初に打った手。
            foreach (var (n, start) in _path)
            {
                n.Visits++;
                n.Wins2 += Reward(winner, n.Mover);
                if (n.Children == null || _config.RaveEquivalence == 0) { continue; }
                Array.Clear(_first, 0, _first.Length);
                for (var i = _sequence.Count - 1; i >= start; i--) { _first[_sequence[i] >> 2] = (Stone)(_sequence[i] & 3); }
                foreach (var c in n.Children)
                {
                    if (c.Move == Points.Pass || _first[c.Move] != c.Mover) { continue; }
                    c.AmafVisits++;
                    c.AmafWins2 += Reward(winner, c.Mover);
                }
            }
        }

        /// <summary>
        /// 根ですぐ終局する手を挙げ、それぞれの終局の局面で死石の推定を用意する（展開と同じ手：候補とパスの規則）。
        /// 系列は終局後の Flow と同じ（試合シード × 終局の手数 × 推定）なので、同じ死石になる。
        /// </summary>
        private void PrepareEnds(int seed)
        {
            var lastMove = _root.MoveCount + 1 >= _root.Config.MoveLimit;
            if (!lastMove && _root.ConsecutivePasses == 0) { return; }
            var moves = new List<int>();
            if (lastMove) { moves.AddRange(CandidatePoints(_root, _root.CopyBoard(), Side, new List<ulong>(), new List<Stone[]>())); }
            if (_root.ConsecutivePasses > 0 || moves.Count == 0) { moves.Add(Points.Pass); }
            foreach (var move in moves)
            {
                var after = _root.Clone();
                var verdict = move == Points.Pass ? after.TryPass(Side) : after.TryPlay(Side, move);
                if (verdict != MoveVerdict.Ok || !after.IsOver) { throw new InvalidOperationException($"{Points.Name(move)} で終局しません（{verdict}）。"); }
                _ends.Add(new EndCase(move, after, new OwnershipSearch(after, _config.OwnershipPlayouts, _config.PlayoutMoveLimit, Ownership.Series(seed, after.MoveCount))));
            }
        }

        private static int Reward(Stone winner, Stone mover) => winner == mover ? 2 : winner == Stone.Empty ? 1 : 0;

        private void Expand(Node node, Board board, Stone toMove, List<ulong> pathHashes, List<Stone[]> pathCells)
        {
            var moves = CandidatePoints(_root, board, toMove, pathHashes, pathCells);
            var opponentPassed = node == _rootNode ? _root.ConsecutivePasses > 0 : node.Move == Points.Pass;
            if (node != _rootNode || opponentPassed || moves.Count == 0) { moves.Add(Points.Pass); }

            // 未訪問の子を選ぶ順序を乱数で決める（Fisher-Yates）。
            for (var i = moves.Count - 1; i > 0; i--)
            {
                var j = _random.Next(i + 1);
                (moves[i], moves[j]) = (moves[j], moves[i]);
            }

            var children = new Node[moves.Count];
            for (var i = 0; i < moves.Count; i++) { children[i] = new Node(moves[i], toMove, node); }
            node.Children = children;
        }

        private Node Select(Node node)
        {
            var children = node.Children!;
            var sqrtParent = IntMath.Sqrt((long)node.Visits * Scale); // √N × 1000
            Node? best = null;
            var bestScore = long.MinValue;
            long k = _config.RaveEquivalence;
            foreach (var c in children)
            {
                long score;
                if (c.Visits == 0 && c.AmafVisits == 0)
                {
                    score = long.MaxValue;
                }
                else
                {
                    var q = c.Visits == 0 ? 0 : c.Wins2 * Scale / (2L * c.Visits);
                    var beta = 0L; // β × 1000
                    if (k > 0 && c.AmafVisits > 0) { beta = IntMath.Sqrt(k * Scale / ((3L * c.Visits) + k)); }
                    var amaf = c.AmafVisits == 0 ? 0 : c.AmafWins2 * Scale / (2L * c.AmafVisits);
                    var u = _config.ExplorationPermille * sqrtParent / (1L + c.Visits);
                    score = ((((1000 - beta) * q) + (beta * amaf)) / 1000) + u;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }

            return best!;
        }

        private sealed class EndCase
        {
            public EndCase(int move, GoGame after, OwnershipSearch search)
            {
                Move = move;
                After = after;
                Search = search;
            }

            public int Move { get; }

            public GoGame After { get; }

            public OwnershipSearch Search { get; }
        }

        private sealed class Node
        {
            public Node(int move, Stone mover, Node? parent)
            {
                Move = move;
                Mover = mover;
                Parent = parent;
            }

            /// <summary>この節点に至る手（根は Pass）。</summary>
            public int Move { get; }

            /// <summary>その手を打った側。</summary>
            public Stone Mover { get; }

            public Node? Parent { get; }

            public Node[]? Children { get; set; }

            public int Visits { get; set; }

            /// <summary>打った側から見た勝ち点の和（勝ち 2・引き分け 1）。</summary>
            public long Wins2 { get; set; }

            /// <summary>AMAF：親の節点より後でこの手を同じ側が最初に打った回数と、その勝ち点の和。</summary>
            public int AmafVisits { get; set; }

            public long AmafWins2 { get; set; }
        }
    }
}
