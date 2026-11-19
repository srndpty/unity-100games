using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Game.Core;

namespace Game.App
{
    /// <summary>操作中のペアの置き場所を1手だけ読んで決め、必要な操作の列を返す。CPU と撮影・テストの自動操縦で共用する。</summary>
    public static class PairPlanner
    {
        private const int OrientationCount = 4;
        private const long TopOutPenalty = 1000000;
        private const int ConnectionWeight = 8;
        private const int SpawnColumnWeight = 20;

        /// <summary>最も評価の高い置き方の操作列を返す。ペアがなければ空を返す。</summary>
        /// <param name="config">ルール設定。</param>
        /// <param name="field">操作する盤面。</param>
        /// <param name="random">わざと外すときの乱数。mistakePercent が 0 なら使わない。</param>
        /// <param name="mistakePercent">評価を無視して適当に置く確率（0〜100）。</param>
        public static IReadOnlyList<GameCommand> Plan(SporeConfig config, PlayerField field, SeededRandom? random = null, int mistakePercent = 0)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            if (field == null) { throw new ArgumentNullException(nameof(field)); }
            if (mistakePercent < 0 || mistakePercent > 100) { throw new ArgumentOutOfRangeException(nameof(mistakePercent), mistakePercent, "0〜100 の範囲で指定してください。"); }
            if (mistakePercent > 0 && random == null) { throw new ArgumentNullException(nameof(random)); }
            if (field.Active is not ActivePair active) { return Array.AsReadOnly(Array.Empty<GameCommand>()); }

            var candidates = Enumerate(field.Board, active);
            var chosen = mistakePercent > 0 && random!.Next(100) < mistakePercent
                ? candidates[random.Next(candidates.Count)]
                : Best(config, field.Board, candidates);
            return ToCommands(chosen);
        }

        private static Candidate Best(SporeConfig config, Board board, List<Candidate> candidates)
        {
            var best = candidates[0];
            var bestScore = long.MinValue;
            foreach (var candidate in candidates)
            {
                var score = Evaluate(config, board, candidate.Pair);
                if (score <= bestScore) { continue; }
                bestScore = score;
                best = candidate;
            }

            return best;
        }

        // 回転してから左右へ動かして届く置き方を列挙する。今の位置のままの置き方が必ず含まれる。
        private static List<Candidate> Enumerate(Board board, ActivePair active)
        {
            var candidates = new List<Candidate>();
            var turned = active;
            for (var turns = 0; turns < OrientationCount; turns++)
            {
                candidates.Add(new Candidate(turned, turns, 0));
                AddShifts(candidates, board, turned, turns, -1);
                AddShifts(candidates, board, turned, turns, 1);
                if (PairRotation.Rotate(board, turned, true) is not ActivePair next) { break; }
                turned = next;
            }

            return candidates;
        }

        private static void AddShifts(List<Candidate> candidates, Board board, ActivePair turned, int turns, int direction)
        {
            for (var shift = direction; board.Fits(turned.Moved(shift, 0)); shift += direction)
            {
                candidates.Add(new Candidate(turned.Moved(shift, 0), turns, shift));
            }
        }

        private static long Evaluate(SporeConfig config, Board board, ActivePair pair)
        {
            var landed = pair;
            while (board.Fits(landed.Moved(0, -1))) { landed = landed.Moved(0, -1); }
            var outcome = ChainResolver.ResolveAll(config, board.Place(landed));
            var result = outcome.Board;
            if (result.At(Board.SpawnX, Board.SpawnY) != Cell.Empty) { return -TopOutPenalty; }

            var score = outcome.Score + (Connections(result) * (long)ConnectionWeight);
            for (var x = 0; x < Board.Width; x++)
            {
                var height = ColumnHeight(result, x);
                score -= height * height;
                if (x == Board.SpawnX) { score -= height * SpawnColumnWeight; }
            }

            return score;
        }

        private static int Connections(Board board)
        {
            var count = 0;
            for (var y = 0; y < Board.VisibleHeight; y++)
            {
                for (var x = 0; x < Board.Width; x++)
                {
                    var cell = board.At(x, y);
                    if (!cell.IsSpore()) { continue; }
                    if (x + 1 < Board.Width && board.At(x + 1, y) == cell) { count++; }
                    if (y + 1 < Board.VisibleHeight && board.At(x, y + 1) == cell) { count++; }
                }
            }

            return count;
        }

        private static int ColumnHeight(Board board, int x)
        {
            for (var y = Board.Height - 1; y >= 0; y--)
            {
                if (board.At(x, y) != Cell.Empty) { return y + 1; }
            }

            return 0;
        }

        private static ReadOnlyCollection<GameCommand> ToCommands(Candidate candidate)
        {
            var commands = new List<GameCommand>();
            if (candidate.Turns == OrientationCount - 1)
            {
                commands.Add(GameCommand.RotateCounterClockwise);
            }
            else
            {
                for (var i = 0; i < candidate.Turns; i++) { commands.Add(GameCommand.RotateClockwise); }
            }

            for (var i = 0; i < Math.Abs(candidate.Shift); i++)
            {
                commands.Add(candidate.Shift < 0 ? GameCommand.MoveLeft : GameCommand.MoveRight);
            }

            commands.Add(GameCommand.SoftDropOn);
            return commands.AsReadOnly();
        }

        private readonly struct Candidate
        {
            public Candidate(ActivePair pair, int turns, int shift)
            {
                Pair = pair;
                Turns = turns;
                Shift = shift;
            }

            public ActivePair Pair { get; }
            public int Turns { get; }
            public int Shift { get; }
        }
    }
}
