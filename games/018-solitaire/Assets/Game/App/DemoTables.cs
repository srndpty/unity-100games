using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    /// <summary>撮影とテストで使う、決まった局面。</summary>
    public static class DemoTables
    {
        /// <summary>組札に 51 枚、残りのスペードの K が場札にある。組札へ送れば勝ち。</summary>
        public static Table OneMoveFromWin() => TableText.Parse(string.Join(
            "\n",
            "f1: AH 2H 3H 4H 5H 6H 7H 8H 9H TH JH QH KH",
            "f2: AD 2D 3D 4D 5D 6D 7D 8D 9D TD JD QD KD",
            "f3: AC 2C 3C 4C 5C 6C 7C 8C 9C TC JC QC KC",
            "f4: AS 2S 3S 4S 5S 6S 7S 8S 9S TS JS QS",
            "t3: KS"));

        /// <summary>
        /// 場札の一番上がハート（赤）の 2・4・6・8・10・Q・K で、残りは伏せ札。捨て札の上の QC を KH に置くと、
        /// 残りの捨て札（3D・5D）はどこにも置けず手詰まりになる。
        /// </summary>
        public static Table OneMoveFromStuck(int drawCount)
        {
            var tops = new[] { "2H", "4H", "6H", "8H", "TH", "QH", "KH" };
            var waste = new[] { "3D", "5D", "QC" };
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
