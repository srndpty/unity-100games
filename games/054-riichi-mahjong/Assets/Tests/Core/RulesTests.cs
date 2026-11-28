using System;
using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class RulesTests
    {
        [Test]
        public void PinfuTsumoAndRonUseDifferentFu()
        {
            var hand = new[] { 0, 1, 2, 3, 4, 5, 9, 10, 11, 21, 22, 23, 15, 15 };
            var tsumo = Scoring.Evaluate(hand, Array.Empty<Group>(), 23, 28, WinContext.Tsumo, Array.Empty<int>())!;
            Assert.That((tsumo.Fu, tsumo.Han, tsumo.Tsumo(false, false), tsumo.Tsumo(false, true)), Is.EqualTo((20, 2, 400, 700)));
            var ron = Scoring.Evaluate(hand, Array.Empty<Group>(), 23, 28, WinContext.None, Array.Empty<int>())!;
            Assert.That((ron.Fu, ron.Han, ron.Ron(false), ron.Ron(true)), Is.EqualTo((30, 1, 1000, 1500)));
        }

        [Test]
        public void SevenPairsAndOrphansUseTheirSpecialForms()
        {
            var seven = new[] { 0, 0, 4, 4, 8, 8, 12, 12, 19, 19, 23, 23, 31, 31 };
            var score = Scoring.Evaluate(seven, Array.Empty<Group>(), 31, 28, WinContext.None, Array.Empty<int>())!;
            Assert.That((score.Fu, score.Han, score.Ron(false)), Is.EqualTo((25, 2, 1600)));
            var orphans = new[] { 0, 8, 9, 17, 18, 26, 27, 28, 29, 30, 31, 32, 33, 33 };
            score = Scoring.Evaluate(orphans, Array.Empty<Group>(), 33, 27, WinContext.Tsumo, Array.Empty<int>())!;
            Assert.That(score.Yakuman, Is.EqualTo(1));
        }

        [Test]
        public void DoraDoesNotCreateYakuAndIndicatorsWrap()
        {
            var hand = new[] { 3, 4, 5, 9, 10, 11, 21, 22, 23, 15, 15 };
            var melds = new[] { new Group(0, MeldKind.Chi) };
            var indicators = new[] { 14 };
            Assert.That(Scoring.Evaluate(hand, melds, 23, 28, WinContext.None, indicators), Is.Null);
            var wrapped = new[] { 0, 9, 18, 27, 31 };
            Assert.That(new[] { Tiles.Dora(8), Tiles.Dora(17), Tiles.Dora(26), Tiles.Dora(30), Tiles.Dora(33) },
                Is.EqualTo(wrapped));
        }

        [Test]
        public void ClosedKanTransfersFourPhysicalTilesAndDrawsFromDeadWall()
        {
            var wall = Enumerable.Range(0, Tiles.Total).ToArray();
            for (var i = 1; i < 4; i++)
            {
                (wall[i], wall[i * 4]) = (wall[i * 4], wall[i]);
            }

            var game = new Match(7, _ => wall);
            Assert.That(game.Apply(0, game.Revision, new Move(MoveKind.ClosedKan, 0)), Is.True);
            Assert.That(game.Seats[0].Calls.Single().Ids.Count, Is.EqualTo(4));
            Assert.That(game.Drawn, Is.EqualTo(135));
            Assert.That(game.Remaining, Is.EqualTo(68));
            var indicators = new[] { 122, 124 };
            Assert.That(game.DoraIndicators, Is.EqualTo(indicators));
            Assert.That(game.PhysicalTiles().OrderBy(id => id), Is.EqualTo(Enumerable.Range(0, Tiles.Total)));
        }

        [TestCase("112233m445566p77s", 2, "二盃口")]
        [TestCase("112233m456p789s55s", 2, "一盃口")]
        [TestCase("234456m45655p456s", 22, "断么九")]
        [TestCase("234456m45655p456s", 22, "三色同順")]
        [TestCase("123789m123p11122z", 28, "混全帯么九")]
        [TestCase("123789m123p11199s", 26, "純全帯么九")]
        [TestCase("12345678911122m", 7, "一気通貫")]
        [TestCase("12345678911122m", 7, "清一色")]
        [TestCase("123456789m11122z", 28, "混一色")]
        [TestCase("111999m111p11122z", 9, "混老頭")]
        [TestCase("111999m111p11122z", 9, "対々和")]
        [TestCase("111999m111p11122z", 27, "場風 東")]
        [TestCase("111m111p111234s55z", 20, "三色同刻")]
        [TestCase("111m111p111234s55z", 21, "三暗刻")]
        [TestCase("123m789p55566677z", 33, "小三元")]
        [TestCase("111m222p333s55522z", 28, "四暗刻")]
        [TestCase("123m99p555666777z", 33, "大三元")]
        public void KnownYaku(string notation, int winning, string yaku)
        {
            var hand = Parse(notation);
            var score = Scoring.Evaluate(hand, Array.Empty<Group>(), winning, 28, WinContext.None, Array.Empty<int>());
            Assert.That(score?.Yaku, Does.Contain(yaku));
        }

        [TestCase(WinContext.Riichi, "リーチ")]
        [TestCase(WinContext.DoubleRiichi, "ダブルリーチ")]
        [TestCase(WinContext.Riichi | WinContext.Ippatsu, "一発")]
        [TestCase(WinContext.Tsumo, "門前ツモ")]
        [TestCase(WinContext.Tsumo | WinContext.LastDraw, "海底摸月")]
        [TestCase(WinContext.LastDiscard, "河底撈魚")]
        [TestCase(WinContext.Tsumo | WinContext.Rinshan, "嶺上開花")]
        [TestCase(WinContext.RobbingKan, "槍槓")]
        public void SituationalYaku(WinContext flags, string yaku)
        {
            var score = Scoring.Evaluate(_pinfuWait.Append(23), Array.Empty<Group>(), 23, 28, flags, Array.Empty<int>());
            Assert.That(score?.Yaku, Does.Contain(yaku));
        }

        [Test]
        public void ThreeKansAndSeatWindUseMeldsInScoring()
        {
            var hand = Parse("123m22p");
            var melds = new[] { new Group(27, MeldKind.ClosedKan), new Group(28, MeldKind.OpenKan), new Group(31, MeldKind.AddedKan) };
            var score = Scoring.Evaluate(hand, melds, 2, 28, WinContext.Tsumo, Array.Empty<int>());
            Assert.That(score?.Yaku, Does.Contain("三槓子"));
            Assert.That(score?.Yaku, Does.Contain("自風"));
            Assert.That(score?.Yaku, Does.Contain("役牌 白"));
        }

        private static int[] Parse(string notation)
        {
            var tiles = new System.Collections.Generic.List<int>();
            var digits = new System.Collections.Generic.List<int>();
            foreach (var symbol in notation)
            {
                if (symbol is >= '1' and <= '9') { digits.Add(symbol - '1'); }
                else
                {
                    var start = "mpsz".IndexOf(symbol) * 9;
                    tiles.AddRange(digits.Select(digit => start + digit));
                    digits.Clear();
                }
            }

            return tiles.ToArray();
        }

        private static readonly int[] _pinfuWait = { 0, 1, 2, 3, 4, 5, 9, 10, 11, 21, 22, 15, 15 };

        [Test]
        public void MultipleRonBeatsCallsAndEachWinnerIsPaid()
        {
            var seats = new[] { 1, 2 };
            var wall = Deal(seats, false);
            var game = new Match(0, _ => wall);
            var ron = new Move(MoveKind.Ron);
            Assert.That(game.Apply(0, game.Revision, new Move(MoveKind.Discard, game.Drawn)), Is.True);
            var revision = game.Revision;
            Assert.That(game.Apply(2, revision, ron), Is.True);
            Assert.That(game.Apply(2, revision, ron), Is.False);
            Assert.That(game.Apply(1, revision, ron), Is.True);
            PassAll(game);
            Assert.That(game.Phase, Is.EqualTo(Phase.HandResult));
            Assert.That(game.Result!.Wins.Select(w => w.Seat), Is.EqualTo(Enumerable.Range(1, 2)));
            Assert.That(game.Result.Differences[0], Is.EqualTo(-game.Result.Differences[1] - game.Result.Differences[2]));
            Assert.That(game.Result.Differences[1], Is.GreaterThan(0));
        }

        [Test]
        public void OwnDiscardOnAnyWaitPreventsRonButAllowsTsumo()
        {
            var seats = new[] { 0 };
            var wall = Deal(seats, true);
            var game = new Match(0, _ => wall);
            Assert.That(game.Apply(0, game.Revision, new Move(MoveKind.Discard, game.Drawn)), Is.True);
            PassAll(game);
            Assert.That(game.Current, Is.EqualTo(1));
            Assert.That(game.Apply(1, game.Revision, new Move(MoveKind.Discard, game.Drawn)), Is.True);
            Assert.That(game.LegalActions(0).Contains(new Move(MoveKind.Ron)), Is.False);
            PassAll(game);
            for (var seat = 2; seat <= 3; seat++)
            {
                Assert.That(game.Current, Is.EqualTo(seat));
                Assert.That(game.Apply(seat, game.Revision, new Move(MoveKind.Discard, game.Drawn)), Is.True);
                PassAll(game);
            }

            Assert.That(game.Current, Is.Zero);
            Assert.That(game.LegalActions(0).Contains(new Move(MoveKind.Tsumo)), Is.True);
        }

        [Test]
        public void MissedRonClearsOnOwnDrawAndAddedKanCanBeRobbed()
        {
            var wall = Enumerable.Repeat(-1, Tiles.Total).ToArray();
            var pool = Enumerable.Range(0, Tiles.Total).ToList();
            void Place(int position, int tile)
            {
                var id = pool.First(p => Tiles.Type(p) == tile);
                wall[position] = id;
                pool.Remove(id);
            }

            for (var i = 0; i < _pinfuWait.Length; i++) { Place(i * 4 + 2, _pinfuWait[i]); }

            Place(1, 23);
            Place(5, 23);
            Place(52, 23);
            Place(53, 33);
            Place(54, 32);
            Place(55, 30);
            Place(56, 23);
            var next = 0;
            for (var i = 0; i < wall.Length; i++)
            {
                if (wall[i] < 0) { wall[i] = pool[next++]; }
            }

            var game = new Match(0, _ => wall);
            var ron = new Move(MoveKind.Ron);
            Assert.That(game.Apply(0, game.Revision, new Move(MoveKind.Discard, game.Drawn)), Is.True);
            Assert.That(game.LegalActions(2).Contains(ron), Is.True);
            Assert.That(game.Apply(1, game.Revision, new Move(MoveKind.Pon, 23)), Is.True);
            PassAll(game);
            Assert.That(game.Phase, Is.EqualTo(Phase.AfterCall));
            var drop = game.LegalActions(1).First(move => move.Kind == MoveKind.Discard);
            Assert.That(game.Apply(1, game.Revision, drop), Is.True);
            Assert.That(game.LegalActions(2).Contains(ron), Is.False);
            PassAll(game);
            for (var i = 0; i < 3; i++)
            {
                Assert.That(game.Apply(game.Current, game.Revision, new Move(MoveKind.Discard, game.Drawn)), Is.True);
                PassAll(game);
            }

            Assert.That(game.Current, Is.EqualTo(1));
            Assert.That(game.Apply(1, game.Revision, new Move(MoveKind.AddedKan, 23)), Is.True);
            Assert.That(game.Phase, Is.EqualTo(Phase.KanReaction));
            Assert.That(game.Apply(2, game.Revision, ron), Is.True);
            PassAll(game);
            Assert.That(game.Result!.Wins.Single().Score.Yaku, Does.Contain("槍槓"));
            Assert.That(game.Seats[1].Calls.Single().Group.Kind, Is.EqualTo(MeldKind.Pon));
            Assert.That(game.DoraIndicators.Count, Is.EqualTo(1));
            Assert.That(game.PhysicalTiles().OrderBy(id => id), Is.EqualTo(Enumerable.Range(0, Tiles.Total)));
        }

        [Test]
        public void RonOnDeclarationDiscardDoesNotTakeRiichiDeposit()
        {
            var seats = new[] { 0, 1 };
            var wall = Deal(seats, false);
            var game = new Match(0, _ => wall);
            Assert.That(game.Apply(0, game.Revision, new Move(MoveKind.Riichi, game.Drawn)), Is.True);
            Assert.That(game.Apply(1, game.Revision, new Move(MoveKind.Ron)), Is.True);
            PassAll(game);
            Assert.That(game.Seats[0].Riichi, Is.False);
            Assert.That(game.Sticks, Is.Zero);
            var win = game.Result!.Wins.Single();
            Assert.That(game.Seats[0].Points, Is.EqualTo(25000 - win.Score.Ron(false)));
        }

        private static int[] Deal(int[] seats, bool furiten)
        {
            var wall = Enumerable.Repeat(-1, Tiles.Total).ToArray();
            var pool = Enumerable.Range(0, Tiles.Total).ToList();
            void Place(int position, int tile)
            {
                var id = pool.First(p => Tiles.Type(p) == tile);
                wall[position] = id;
                pool.Remove(id);
            }

            foreach (var seat in seats)
            {
                for (var i = 0; i < _pinfuWait.Length; i++) { Place(i * 4 + seat, _pinfuWait[i]); }
            }

            Place(52, furiten ? 20 : 23);
            if (furiten)
            {
                Place(53, 23);
                Place(56, 23);
            }

            var next = 0;
            for (var i = 0; i < wall.Length; i++)
            {
                if (wall[i] < 0) { wall[i] = pool[next++]; }
            }

            return wall;
        }

        private static void PassAll(Match game)
        {
            var revision = game.Revision;
            var pass = new Move(MoveKind.Pass);
            for (var seat = 0; seat < 4 && game.Revision == revision; seat++)
            {
                if (game.LegalActions(seat).Contains(pass)) { Assert.That(game.Apply(seat, revision, pass), Is.True); }
            }
        }

        [Test]
        public void StaleAndInvalidOperationsDoNotMutate()
        {
            var game = new Match(42);
            var id = game.Drawn;
            Assert.That(game.Apply(-1, game.Revision, new Move(MoveKind.Discard, id)), Is.False);
            Assert.That(game.Apply(0, game.Revision - 1, new Move(MoveKind.Discard, id)), Is.False);
            Assert.That(game.Apply(0, game.Revision, new Move(MoveKind.Discard, Tiles.Total)), Is.False);
            Assert.That(game.Drawn, Is.EqualTo(id));
            Assert.That(game.PhysicalTiles().Distinct().Count(), Is.EqualTo(136));
        }

        [TestCase("112233m445566p77s", 2, 28, "二盃口", 40, 3, 0, 1280, 5200)]
        [TestCase("111m222p333s55522z", 28, 28, "四暗刻", 0, 0, 1, 8000, 32000)]
        [TestCase("111m222p333s55522z", 31, 28, "役牌 白|対々和|三暗刻", 60, 5, 0, 2000, 8000)]
        [TestCase("111m55566677722z", 28, 28, "四暗刻|大三元", 0, 0, 2, 16000, 64000)]
        [TestCase("123m789p55566677z", 33, 28, "役牌 白|役牌 發|混全帯么九|小三元", 50, 6, 0, 3000, 12000)]
        public void ChoosesOneExactScoringInterpretation(string notation, int tile, int wind, string yaku,
            int fu, int han, int yakuman, int basic, int ron)
        {
            var score = Scoring.Evaluate(Parse(notation), Array.Empty<Group>(), tile, wind, WinContext.None, Array.Empty<int>())!;
            Assert.That(score, Is.Not.Null);
            Assert.That(score.Yaku, Is.EquivalentTo(yaku.Split('|')));
            Assert.That((score.Fu, score.Han, score.Yakuman, score.Basic, score.Ron(false)),
                Is.EqualTo((fu, han, yakuman, basic, ron)));
        }

        [Test]
        public void DoubleWindPairContributesFourFuAndPreventsPinfu()
        {
            var score = Scoring.Evaluate(Parse("123456m123p456s11z"), Array.Empty<Group>(), 23, 27,
                WinContext.Riichi, Array.Empty<int>())!;
            Assert.That(score.Yaku.Single(), Is.EqualTo("リーチ"));
            Assert.That(score.FuItems, Does.Contain("雀頭 4符"));
            Assert.That((score.Fu, score.Han, score.Basic, score.Ron(true)), Is.EqualTo((40, 1, 320, 2000)));
        }


        [Test]
        public void YakulessCompletionDoesNotPreventNextLegalRon()
        {
            var types = new[] { 1, 2, 12, 13, 14, 21, 22, 23, 24, 24, 10, 11, 31 };
            var wall = Enumerable.Repeat(-1, Tiles.Total).ToArray();
            var ids = Enumerable.Range(0, Tiles.Total).ToList();
            void Place(int position, int type)
            {
                var id = ids.First(tile => Tiles.Type(tile) == type);
                wall[position] = id;
                ids.Remove(id);
            }

            for (var i = 0; i < types.Length; i++) { Place(i * 4 + 1, types[i]); }
            Place(52, 12);
            Place(53, 0);
            Place(54, 3);
            var next = 0;
            for (var i = 0; i < wall.Length; i++)
            {
                if (wall[i] < 0) { wall[i] = ids[next++]; }
            }

            var game = new Match(0, _ => wall);
            var ron = new Move(MoveKind.Ron);
            Assert.That(game.Apply(0, game.Revision, new Move(MoveKind.Discard, game.Drawn)), Is.True);
            Assert.That(game.Apply(1, game.Revision, new Move(MoveKind.Chi, 10)), Is.True);
            PassAll(game);
            Assert.That(game.Apply(1, game.Revision, new Move(MoveKind.Discard, wall[49])), Is.True);
            PassAll(game);
            var waits = Scoring.Waits(game.Hand(1).Select(Tiles.Type), game.Seats[1].Calls.Select(m => m.Group).ToArray());
            Assert.That(waits.Contains(0), Is.True);
            Assert.That(waits.Contains(3), Is.True);
            Assert.That(game.Apply(2, game.Revision, new Move(MoveKind.Discard, game.Drawn)), Is.True);
            Assert.That(game.LegalActions(1).Contains(ron), Is.False);
            PassAll(game);
            Assert.That(game.Apply(3, game.Revision, new Move(MoveKind.Discard, game.Drawn)), Is.True);
            Assert.That(game.Apply(1, game.Revision, ron), Is.True);
            PassAll(game);
            Assert.That(game.Result!.Wins.Single().Score.Yaku, Does.Contain("断么九"));
        }


        [Test]
        public void FourKansKeepIndicatorsSeparateFromAllReplacementDraws()
        {
            var wall = Enumerable.Repeat(-1, Tiles.Total).ToArray();
            for (var i = 0; i < 14; i++) { wall[i * 4] = i; }
            wall[135] = 14;
            wall[134] = 15;
            wall[133] = 133;
            wall[132] = 132;
            var remaining = Enumerable.Range(0, Tiles.Total).Except(wall).GetEnumerator();
            using (remaining)
            {
                for (var i = 0; i < wall.Length; i++)
                {
                    if (wall[i] >= 0) { continue; }

                    Assert.That(remaining.MoveNext(), Is.True);
                    wall[i] = remaining.Current;
                }
            }

            var match = new Match(0, _ => wall);
            for (var kan = 0; kan < 4; kan++)
            {
                Assert.That(match.Apply(0, match.Revision, new Move(MoveKind.ClosedKan, kan)), Is.True);
                Assert.That(match.Drawn, Is.EqualTo(wall[135 - kan]));
                Assert.That(match.DoraIndicators, Is.EqualTo(Enumerable.Range(0, kan + 2).Select(i => wall[122 + 2 * i])));
                Assert.That(match.PhysicalTiles().Distinct().Count(), Is.EqualTo(Tiles.Total));
            }

            Assert.That(match.Remaining, Is.EqualTo(65));
            Assert.That(match.Seats[0].Calls.Count, Is.EqualTo(4));
        }


        [Test]
        public void SeatViewKeepsOtherHandsDrawsAndLegalActionsPrivate()
        {
            var match = new Match(54);
            var east = match.ViewFor(0);
            var south = match.ViewFor(1);
            Assert.That(east, Is.SameAs(match.ViewFor(0)));
            Assert.That(east.Seats, Is.SameAs(south.Seats));
            Assert.That(east.Hand.Intersect(south.Hand), Is.Empty);
            Assert.That(east.Hand.Contains(east.Drawn), Is.True);
            Assert.That(south.Drawn, Is.EqualTo(-1));
            Assert.That(south.LegalActions, Is.Empty);
            Assert.That(south.Seats[0].HandCount, Is.EqualTo(14));
            Assert.That(south.Result, Is.Null);
            Assert.That(east.Target, Is.EqualTo(-1));

            Assert.That(match.Apply(0, east.Revision, new Move(MoveKind.Discard, east.Drawn)), Is.True);
            PassAll(match);

            Assert.That(south.Current, Is.EqualTo(1));
            Assert.That(south.Hand.Contains(south.Drawn), Is.True);
            Assert.That(east.Drawn, Is.EqualTo(-1));
            Assert.That(east.LegalActions, Is.Empty);
            Assert.That(east.Seats[1].HandCount, Is.EqualTo(14));
            Assert.That(east.Result, Is.Null);
        }

        [Test]
        public void FullStateQueriesAreOutsideThePublicApi()
        {
            Assert.That(typeof(Match).GetMethod(nameof(Match.Hand)), Is.Null);
            Assert.That(typeof(Match).GetMethod(nameof(Match.PhysicalTiles)), Is.Null);
            Assert.That(typeof(Match).GetMethod(nameof(Match.LegalActions)), Is.Null);
            Assert.That(typeof(Match).GetProperty(nameof(Match.Drawn)), Is.Null);
            Assert.That(typeof(SeatView).GetMethod(nameof(Match.ViewFor)), Is.Null);
            Assert.That(typeof(PublicSeat).GetMember(nameof(Seat.Hand)), Is.Empty);
        }

    }
}
