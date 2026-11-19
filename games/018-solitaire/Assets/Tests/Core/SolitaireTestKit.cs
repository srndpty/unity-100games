using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Core.Tests
{
    /// <summary>テストで使う盤と、乱数で遊ぶ・素朴に探索する道具。</summary>
    public static class SolitaireTestKit
    {
        /// <summary>場札の一番上の札。すべて太陽（赤）の偶数と K なので、互いに重ならず組札にも行けない。</summary>
        public static readonly string[] StuckTops = { "2S", "4S", "6S", "8S", "TS", "QS", "KS" };

        public static SolitaireConfig Config(int drawCount = 1, int searchStates = 2_000) =>
            new() { DrawCount = drawCount, DeadlockSearchStates = searchStates };

        public static Table Parse(params string[] lines) => TableText.Parse(string.Join("\n", lines));

        public static PileId T(int column) => PileId.Tableau(column - 1);

        public static PileId F(int slot) => PileId.Foundation(slot - 1);

        /// <summary>
        /// 組札・捨て札が空で、場札 7 列の一番上が <see cref="StuckTops"/>、残りは伏せ札という盤。
        /// stock に書いた札は山札に（左が下）、残りの札を伏せ札として左の列から順に配る。
        /// </summary>
        public static Table StuckTable(string stock = "", string waste = "", int drawCount = 1)
        {
            var stockCards = Cards(stock);
            var wasteCards = Cards(waste);
            var tops = StuckTops.Select(Card.Parse).ToArray();
            var used = new HashSet<Card>(stockCards.Concat(wasteCards).Concat(tops));
            var hidden = Enumerable.Range(0, Card.DeckSize).Select(Card.FromId).Where(c => !used.Contains(c)).ToList();
            var piles = new List<Card>[PileId.Count];
            for (var i = 0; i < piles.Length; i++) { piles[i] = new List<Card>(); }
            for (var i = 0; i < hidden.Count; i++) { piles[PileId.Tableau(i % PileId.TableauCount).Index].Add(hidden[i]); }
            var faceDown = new int[PileId.TableauCount];
            for (var column = 0; column < PileId.TableauCount; column++)
            {
                var pile = piles[PileId.Tableau(column).Index];
                faceDown[column] = pile.Count;
                pile.Add(tops[column]);
            }

            piles[PileId.Stock.Index].AddRange(stockCards);
            piles[PileId.Waste.Index].AddRange(wasteCards);
            return Table.Create(drawCount, piles, faceDown);
        }

        /// <summary>
        /// 合法手から乱数で選んで遊ぶ。undoRate の割合で取り消しも混ぜる。毎手の後に onStep を呼ぶ。終わるか maxSteps で止まる。
        /// </summary>
        public static void PlayRandomly(SolitaireGame game, int seed, int maxSteps, double undoRate = 0, Action<SolitaireGame>? onStep = null)
        {
            var random = new SeededRandom(seed);
            var moves = new List<Command>();
            for (var step = 0; step < maxSteps && !game.IsOver; step++)
            {
                if (undoRate > 0 && game.CanUndo && random.Next(1000) < undoRate * 1000)
                {
                    if (!game.Undo()) { throw new InvalidOperationException("取り消せませんでした。"); }
                }
                else
                {
                    MoveRules.LegalMoves(game.Table, moves);
                    if (moves.Count == 0) { break; }
                    var error = game.Apply(moves[random.Next(moves.Count)]);
                    if (error != MoveError.None) { throw new InvalidOperationException($"合法手が拒否されました：{error}"); }
                }

                onStep?.Invoke(game);
            }
        }

        /// <summary>
        /// 前進（組札が増える・伏せ札が開く・山札と捨て札が減る）できる局面に届くかを、並べ替えの同一視をせずに幅優先で調べる。
        /// 検出器とは別の素朴な実装。上限を超えたら null。
        /// </summary>
        public static bool? NaiveReachesProgress(IReadOnlyTable start, int maxStates)
        {
            var foundation = start.FoundationTotal;
            var stockAndWaste = start.CountOf(PileId.Stock) + start.CountOf(PileId.Waste);
            var hidden = PileId.All.Where(p => p.Kind == PileKind.Tableau).Sum(start.FaceDownCountOf);
            var visited = new HashSet<string> { TableText.Format(start) };
            var queue = new Queue<Table>();
            queue.Enqueue(Table.CopyOf(start));
            var moves = new List<Command>();
            while (queue.Count > 0)
            {
                var table = queue.Dequeue();
                MoveRules.LegalMoves(table, moves);
                foreach (var move in moves.ToArray())
                {
                    var next = Table.CopyOf(table);
                    next.Execute(move);
                    if (next.FoundationTotal > foundation ||
                        next.CountOf(PileId.Stock) + next.CountOf(PileId.Waste) < stockAndWaste ||
                        PileId.All.Where(p => p.Kind == PileKind.Tableau).Sum(next.FaceDownCountOf) < hidden)
                    {
                        return true;
                    }

                    if (visited.Add(TableText.Format(next)))
                    {
                        if (visited.Count > maxStates) { return null; }
                        queue.Enqueue(next);
                    }
                }
            }

            return false;
        }

        private static List<Card> Cards(string text) =>
            text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Card.Parse).ToList();
    }
}
