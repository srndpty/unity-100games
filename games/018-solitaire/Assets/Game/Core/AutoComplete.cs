using System;

namespace Game.Core
{
    /// <summary>
    /// 自動で仕上げる（残りの札を組札へ送る）。山札と捨て札が空で、場札に伏せ札がなければ使える。
    /// この状態からは必ず勝てる：場札の各列は色違いで 1 つずつ下がる並びなので、残っている札のうち最も小さいランクの札は
    /// どこかの列の一番上にあり（上に乗れる、より小さい札がない）、そのスートの組札は 1 つ下まで積み終わっている。
    /// </summary>
    public static class AutoComplete
    {
        public static bool IsAvailable(IReadOnlyTable table)
        {
            if (table == null) { throw new ArgumentNullException(nameof(table)); }
            if (table.FoundationTotal == Card.DeckSize) { return false; }
            if (table.CountOf(PileId.Stock) > 0 || table.CountOf(PileId.Waste) > 0) { return false; }
            for (var column = 0; column < PileId.TableauCount; column++)
            {
                if (table.FaceDownCountOf(PileId.Tableau(column)) > 0) { return false; }
            }

            return true;
        }

        /// <summary>次に組札へ送る手：組札へ送れる場札の一番上の札のうち、ランクの最も小さいもの。なければ null。</summary>
        public static Command? NextMove(IReadOnlyTable table)
        {
            if (table == null) { throw new ArgumentNullException(nameof(table)); }
            Command? best = null;
            var bestRank = int.MaxValue;
            for (var column = 0; column < PileId.TableauCount; column++)
            {
                var pile = PileId.Tableau(column);
                var count = table.CountOf(pile);
                if (count == 0) { continue; }
                var rank = table.CardAt(pile, count - 1).Rank;
                if (rank >= bestRank || MoveRules.FoundationMoveFrom(table, pile) is not Command move) { continue; }
                best = move;
                bestRank = rank;
            }

            return best;
        }
    }
}
