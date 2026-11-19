using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    /// <summary>撮影とテストで使う、決まった局面。</summary>
    public static class DemoTables
    {
        /// <summary>組札に 51 枚、残りの波の K が場札にある。組札へ送れば勝ち。</summary>
        public static Table OneMoveFromWin() => TableText.Parse(string.Join(
            "\n",
            "f1: AS 2S 3S 4S 5S 6S 7S 8S 9S TS JS QS KS",
            "f2: AF 2F 3F 4F 5F 6F 7F 8F 9F TF JF QF KF",
            "f3: AM 2M 3M 4M 5M 6M 7M 8M 9M TM JM QM KM",
            "f4: AW 2W 3W 4W 5W 6W 7W 8W 9W TW JW QW",
            "t3: KW"));

        /// <summary>
        /// 場札の一番上が太陽（赤）の 2・4・6・8・10・Q・K で、残りは伏せ札。捨て札の上の QM を KS に置くと、
        /// 残りの捨て札（3F・5F）はどこにも置けず手詰まりになる。
        /// </summary>
        public static Table OneMoveFromStuck(int drawCount)
        {
            var tops = new[] { "2S", "4S", "6S", "8S", "TS", "QS", "KS" };
            var waste = new[] { "3F", "5F", "QM" };
            var used = new HashSet<Card>();
            foreach (var text in tops) { used.Add(Card.Parse(text)); }
            foreach (var text in waste) { used.Add(Card.Parse(text)); }

            var piles = new List<Card>[PileId.Count];
            for (var i = 0; i < piles.Length; i++) { piles[i] = new List<Card>(); }
            var next = 0;
            for (var id = 0; id < Card.DeckSize; id++)
            {
                var card = Card.FromId(id);
                if (used.Contains(card)) { continue; }
                piles[PileId.Tableau(next % PileId.TableauCount).Index].Add(card);
                next++;
            }

            var faceDown = new int[PileId.TableauCount];
            for (var column = 0; column < PileId.TableauCount; column++)
            {
                var pile = piles[PileId.Tableau(column).Index];
                faceDown[column] = pile.Count;
                pile.Add(Card.Parse(tops[column]));
            }

            foreach (var text in waste) { piles[PileId.Waste.Index].Add(Card.Parse(text)); }
            return Table.Create(drawCount, piles, faceDown);
        }

        /// <summary><see cref="OneMoveFromStuck"/> で手詰まりにする手。</summary>
        public static Command StuckMove => Command.Move(PileId.Waste, PileId.Tableau(6));
    }
}
