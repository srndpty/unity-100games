using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    /// <summary>
    /// 合法手から 1 手選ぶ操縦役（撮影とテストで共有）。組札へ送る手、伏せ札を開く手、捨て札を出す手、めくり、その他の順に優先し、
    /// 同じ優先度の中はシード付き乱数で選ぶ。勝てることは保証しない。
    /// </summary>
    public sealed class Autopilot
    {
        private readonly SeededRandom _random;
        private readonly List<Command> _moves = new();
        private readonly List<Command> _best = new();

        public Autopilot(int seed)
        {
            _random = new SeededRandom(seed);
        }

        public Command? Next(IReadOnlyTable table)
        {
            if (table == null) { throw new ArgumentNullException(nameof(table)); }
            MoveRules.LegalMoves(table, _moves);
            var bestScore = int.MinValue;
            _best.Clear();
            foreach (var move in _moves)
            {
                var score = Score(table, move);
                if (score > bestScore)
                {
                    bestScore = score;
                    _best.Clear();
                }

                if (score == bestScore) { _best.Add(move); }
            }

            return _best.Count == 0 ? null : _best[_random.Next(_best.Count)];
        }

        private static int Score(IReadOnlyTable table, Command move)
        {
            if (move.Kind != CommandKind.Move) { return 1; }
            if (move.From.Kind == PileKind.Foundation) { return 0; }
            if (move.To.Kind == PileKind.Foundation) { return 5; }
            var faceDown = table.FaceDownCountOf(move.From);
            if (move.From.Kind == PileKind.Tableau && faceDown > 0 && move.Count == table.CountOf(move.From) - faceDown) { return 4; }
            if (move.From.Kind == PileKind.Waste) { return 3; }

            // 列を空けるだけで伏せ札のない K の移動や、列の途中から別の列への付け替えは同じ局面を行き来しやすいので最後にする。
            return 0;
        }
    }
}
