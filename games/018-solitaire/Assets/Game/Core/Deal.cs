using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>シードから配札する。</summary>
    public static class Deal
    {
        /// <summary>
        /// 52 枚をシードでシャッフルし（Fisher–Yates）、場札 7 列へ 1〜7 枚を配る。配る順は 1 段ずつ左から（1 段目は 7 列、2 段目は 2〜7 列 …）。
        /// 各列の一番上だけ表。残り 24 枚が山札（並べた順に積み、最後の札が一番上）。
        /// </summary>
        public static Table Create(int seed, int drawCount)
        {
            var deck = Shuffled(seed);
            var piles = new List<Card>[PileId.Count];
            for (var i = 0; i < piles.Length; i++) { piles[i] = new List<Card>(); }
            var next = 0;
            for (var row = 0; row < PileId.TableauCount; row++)
            {
                for (var column = row; column < PileId.TableauCount; column++)
                {
                    piles[PileId.Tableau(column).Index].Add(deck[next++]);
                }
            }

            for (; next < deck.Length; next++) { piles[PileId.Stock.Index].Add(deck[next]); }

            var faceDown = new int[PileId.TableauCount];
            for (var column = 0; column < faceDown.Length; column++) { faceDown[column] = column; }
            return Table.Create(drawCount, piles, faceDown);
        }

        /// <summary>シャッフルした 52 枚。</summary>
        public static Card[] Shuffled(int seed)
        {
            var deck = new Card[Card.DeckSize];
            for (var i = 0; i < deck.Length; i++) { deck[i] = Card.FromId(i); }
            var random = new SeededRandom(seed);
            for (var i = deck.Length - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (deck[i], deck[j]) = (deck[j], deck[i]);
            }

            return deck;
        }
    }
}
