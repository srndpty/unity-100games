using System;
using System.Collections.Generic;

namespace Game.Core.Tests
{
    internal static class OthelloTestKit
    {
        public const int MaxSteps = 10_000;

        private static readonly (int Dx, int Dy)[] _directions =
        {
            (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1),
        };

        /// <summary>演出の待ち時間がない設定。</summary>
        public static OthelloConfig InstantConfig() => new()
        {
            FlipTicks = 0,
            PassTicks = 0,
            CpuThinkTicks = 0,
        };

        public static Position Parse(Side toMove, params string[] rows) => BoardText.Parse(toMove, rows);

        public static Square S(string name) => Square.Parse(name);

        public static ulong Bits(params string[] names)
        {
            var bits = 0UL;
            foreach (var name in names) { bits |= Square.Parse(name).Bit; }
            return bits;
        }

        /// <summary>
        /// ビット演算を使わない参照実装。64マスそれぞれから8方向へ1マスずつ歩き、
        /// 相手の石が1つ以上続いた先に自分の石があれば、その間を返す石とする。
        /// </summary>
        public static ulong NaiveFlips(Position position, Square square)
        {
            if (position.At(square) != Disc.Empty) { return 0; }
            var own = position.ToMove.ToDisc();
            var other = position.ToMove.Opponent().ToDisc();
            var flips = 0UL;
            foreach (var (dx, dy) in _directions)
            {
                var line = 0UL;
                var x = square.X + dx;
                var y = square.Y + dy;
                while (x >= 0 && x < Square.Size && y >= 0 && y < Square.Size && position.At(new Square(x, y)) == other)
                {
                    line |= new Square(x, y).Bit;
                    x += dx;
                    y += dy;
                }

                var closed = x >= 0 && x < Square.Size && y >= 0 && y < Square.Size && position.At(new Square(x, y)) == own;
                if (closed && line != 0) { flips |= line; }
            }

            return flips;
        }

        public static ulong NaiveLegalMoves(Position position)
        {
            var moves = 0UL;
            for (var i = 0; i < Square.Count; i++)
            {
                var square = Square.FromIndex(i);
                if (NaiveFlips(position, square) != 0) { moves |= square.Bit; }
            }

            return moves;
        }

        /// <summary>
        /// シード付き乱数で合法手を選び、終局まで打つ。パスを含めて、途中のすべての局面（終局を含む）を visit に渡す。
        /// </summary>
        public static Position RandomPlayout(int seed, Action<Position>? visit = null)
        {
            var random = new SeededRandom(seed);
            var position = Position.Initial;
            for (var i = 0; i < MaxSteps; i++)
            {
                visit?.Invoke(position);
                if (position.IsTerminal) { return position; }
                var moves = position.LegalMoves();
                if (moves == 0)
                {
                    position = position.Pass();
                    continue;
                }

                var squares = Game.Core.Bits.ToSquares(moves);
                position = position.Play(squares[random.Next(squares.Count)]);
            }

            throw new InvalidOperationException("終局しませんでした。");
        }

        /// <summary>枝刈りなしの Minimax（negamax 形式）。αβ の値と照合するための参照実装。</summary>
        public static int PlainMinimax(Position position, int depth)
        {
            if (depth == 0) { return Evaluator.Evaluate(position); }
            var moves = position.LegalMoves();
            if (moves == 0)
            {
                if (!position.HasLegalMove(position.ToMove.Opponent())) { return Evaluator.Terminal(position); }
                return -PlainMinimax(position.Pass(), depth);
            }

            var best = -Searcher.Infinity;
            foreach (var square in Game.Core.Bits.ToSquares(moves))
            {
                best = Math.Max(best, -PlainMinimax(position.Play(square), depth - 1));
            }

            return best;
        }

        /// <summary>着手待ちか終局まで進める。</summary>
        public static void RunUntilIdle(OthelloGame game)
        {
            for (var i = 0; i < MaxSteps && !game.AcceptsMove && !game.IsOver; i++) { game.Tick(); }
        }

        /// <summary>両者とも探索で手を選び、終局まで打つ。</summary>
        public static OthelloGame PlaySearchGame(OthelloConfig config, int blackDepth, int whiteDepth, int seed)
        {
            var game = new OthelloGame(config);
            var random = new SeededRandom(seed);
            for (var i = 0; i < MaxSteps && !game.IsOver; i++)
            {
                RunUntilIdle(game);
                if (game.IsOver) { break; }
                var depth = game.ToMove == Side.Black ? blackDepth : whiteDepth;
                var result = Searcher.Search(game.Position, depth, random);
                if (game.TryPlay(game.ToMove, result.Move) != PlayVerdict.Ok) { throw new InvalidOperationException("探索が非合法手を返しました。"); }
            }

            RunUntilIdle(game);
            return game;
        }

        public static IReadOnlyList<Position> SamplePositions(int games, int firstSeed)
        {
            var positions = new List<Position>();
            for (var g = 0; g < games; g++) { RandomPlayout(firstSeed + g, positions.Add); }
            return positions;
        }
    }
}
