using System.Collections.Generic;
using NUnit.Framework;
using static Game.Core.Tests.SolitaireTestKit;

namespace Game.Core.Tests
{
    public sealed class AutoCompleteTests
    {
        [Test]
        public void AvailableOnlyWithEmptyStockAndWasteAndNoFaceDownCards()
        {
            var ready = Parse("f1: AH 2H", "t1: KS QH JS TH 9S 8H 7S 6H 5S 4H 3S", "t2: *");
            Assert.That(AutoComplete.IsAvailable(ready), Is.False, "t2 の * は一番上だけ表なので伏せ札がある");

            Assert.That(AutoComplete.IsAvailable(Parse("waste: *")), Is.False, "捨て札がある");
            Assert.That(AutoComplete.IsAvailable(Parse("stock: *")), Is.False, "山札がある");
            Assert.That(AutoComplete.IsAvailable(Parse("stock: *", "t1: QH | KS")), Is.False);
        }

        [Test]
        public void SolvedTableIsNotAvailable()
        {
            var solved = Parse(
                "f1: AH 2H 3H 4H 5H 6H 7H 8H 9H TH JH QH KH",
                "f2: AD 2D 3D 4D 5D 6D 7D 8D 9D TD JD QD KD",
                "f3: AC 2C 3C 4C 5C 6C 7C 8C 9C TC JC QC KC",
                "f4: AS 2S 3S 4S 5S 6S 7S 8S 9S TS JS QS KS");
            Assert.That(AutoComplete.IsAvailable(solved), Is.False);
            Assert.That(AutoComplete.NextMove(solved), Is.Null);
        }

        [Test]
        public void NextMoveSendsTheLowestRankFirst()
        {
            var table = Parse(
                "f1: AH 2H 3H",
                "f2: AC",
                "t1: KS QH JS TH 9S 8H 7S 6H 5S 4H",
                "t2: KH QS JH TS 9H 8S 7H 6S 5H 4S 3D 2C",
                "t3: KD QC JD TC 9D 8C 7D 6C 5D 4C",
                "t4: KC QD JC TD 9C 8D 7C 6D 5C 4D 3C 2D AS",
                "t5: 3S",
                "t6: 2S",
                "t7: AD");
            Assert.That(AutoComplete.IsAvailable(table), Is.True);
            var move = AutoComplete.NextMove(table);
            Assert.That(move?.From, Is.EqualTo(T(4)), "A が 2 枚あるうち左の列");
            Assert.That(move?.To.Kind, Is.EqualTo(PileKind.Foundation));
        }

        /// <summary>
        /// 組札が全部揃った盤から、組札→場札・場札→場札の合法手を乱数で逆にたどって「伏せ札のない盤」を作り、
        /// 自動で仕上げると必ず 52 枚が組札に入ることを確かめる。
        /// </summary>
        [Test]
        public void AlwaysFinishesFromRandomAllFaceUpTables()
        {
            var moves = new List<Command>();
            var tested = 0;
            for (var seed = 1; seed <= 200; seed++)
            {
                var table = Parse(
                    "f1: AH 2H 3H 4H 5H 6H 7H 8H 9H TH JH QH KH",
                    "f2: AD 2D 3D 4D 5D 6D 7D 8D 9D TD JD QD KD",
                    "f3: AC 2C 3C 4C 5C 6C 7C 8C 9C TC JC QC KC",
                    "f4: AS 2S 3S 4S 5S 6S 7S 8S 9S TS JS QS KS");
                var random = new SeededRandom(seed);
                var steps = 20 + random.Next(200);
                for (var i = 0; i < steps; i++)
                {
                    MoveRules.LegalMoves(table, moves);
                    moves.RemoveAll(m => m.Kind != CommandKind.Move || m.To.Kind == PileKind.Foundation);
                    if (moves.Count == 0) { break; }
                    table.Execute(moves[random.Next(moves.Count)]);
                }

                if (table.FoundationTotal == Card.DeckSize) { continue; }
                Assert.That(AutoComplete.IsAvailable(table), Is.True, $"シード {seed}");
                var game = new SolitaireGame(Config(), table);
                for (var i = 0; i < Card.DeckSize && game.CanAutoComplete; i++)
                {
                    Assert.That(game.Apply(AutoComplete.NextMove(game.Table)!.Value), Is.EqualTo(MoveError.None));
                }

                Assert.That(game.Phase, Is.EqualTo(GamePhase.Won), $"シード {seed}\n{TableText.Format(table)}");
                tested++;
            }

            Assert.That(tested, Is.GreaterThan(150));
        }

        [Test]
        public void GameOffersAutoCompleteOnlyWhilePlaying()
        {
            var table = Parse("f1: AH 2H 3H 4H 5H 6H 7H 8H 9H TH JH QH KH",
                "f2: AD 2D 3D 4D 5D 6D 7D 8D 9D TD JD QD KD",
                "f3: AC 2C 3C 4C 5C 6C 7C 8C 9C TC JC QC KC",
                "f4: AS 2S 3S 4S 5S 6S 7S 8S 9S TS JS QS",
                "t1: KS");
            var game = new SolitaireGame(Config(), table);
            Assert.That(game.CanAutoComplete, Is.True);
            game.Resign();
            Assert.That(game.CanAutoComplete, Is.False);
        }
    }
}
