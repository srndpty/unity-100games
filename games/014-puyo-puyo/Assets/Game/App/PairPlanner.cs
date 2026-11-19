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
            return chosen.Commands;
        }

        /// <summary>回転してから左右へ動かして届く置き方を列挙する。今の位置のままの置き方が必ず含まれる。</summary>
        public static IReadOnlyList<PairPlacement> Placements(Board board, ActivePair active)
        {
            if (board == null) { throw new ArgumentNullException(nameof(board)); }
            return Enumerate(board, active).AsReadOnly();
        }

        private static PairPlacement Best(SporeConfig config, Board board, List<PairPlacement> candidates)
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

        private static List<PairPlacement> Enumerate(Board board, ActivePair active)
        {
            var candidates = new List<PairPlacement>();
            var counterClockwise = PairRotation.Rotate(board, active, false);
            var turned = active;
            for (var turns = 0; turns < OrientationCount; turns++)
            {
                var rotation = RotationCommands(turns, counterClockwise == turned);
                candidates.Add(new PairPlacement(turned, rotation, 0));
                AddShifts(candidates, board, turned, rotation, -1);
                AddShifts(candidates, board, turned, rotation, 1);
                if (PairRotation.Rotate(board, turned, true) is not ActivePair next) { break; }
                turned = next;
            }

            return candidates;
        }

        // 回転は押し出しで軸が動くため、右3回と左1回は結果が同じときだけ置き換えられる。
        private static List<GameCommand> RotationCommands(int turns, bool sameAsCounterClockwise)
        {
            var commands = new List<GameCommand>();
            if (turns == OrientationCount - 1 && sameAsCounterClockwise)
            {
                commands.Add(GameCommand.RotateCounterClockwise);
                return commands;
            }

            for (var i = 0; i < turns; i++) { commands.Add(GameCommand.RotateClockwise); }
            return commands;
        }

        private static void AddShifts(List<PairPlacement> candidates, Board board, ActivePair turned, List<GameCommand> rotation, int direction)
        {
            for (var shift = direction; board.Fits(turned.Moved(shift, 0)); shift += direction)
            {
                candidates.Add(new PairPlacement(turned.Moved(shift, 0), rotation, shift));
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
    }

    /// <summary>操作中のペアの置き方と、そこへ届く操作の列。</summary>
    public sealed class PairPlacement
    {
        internal PairPlacement(ActivePair pair, List<GameCommand> rotation, int shift)
        {
            Pair = pair;
            var commands = new List<GameCommand>(rotation);
            for (var i = 0; i < Math.Abs(shift); i++)
            {
                commands.Add(shift < 0 ? GameCommand.MoveLeft : GameCommand.MoveRight);
            }

            commands.Add(GameCommand.SoftDropOn);
            Commands = commands.AsReadOnly();
        }

        /// <summary>操作を終えた直後のペア。</summary>
        public ActivePair Pair { get; }

        /// <summary>回転、左右移動、ソフトドロップの順の操作。</summary>
        public ReadOnlyCollection<GameCommand> Commands { get; }
    }
}
