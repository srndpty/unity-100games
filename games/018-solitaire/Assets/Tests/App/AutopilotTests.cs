using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class AutopilotTests
    {
        [Test]
        public void PrefersFoundationMoves()
        {
            var table = TableText.Parse("stock: *\nwaste: AH\nt1: 9C\nt2: TD");
            var move = new Autopilot(1).Next(table);
            Assert.That(move?.From, Is.EqualTo(PileId.Waste));
            Assert.That(move?.To.Kind, Is.EqualTo(PileKind.Foundation));
        }

        [Test]
        public void ReturnsNullWhenNoMoveIsLegal()
        {
            // 山札・捨て札が空で、場札の一番上（KS とハート・ダイヤの札）はどこにも置けない。空いた列もない。
            var table = TableText.Parse("t2: 5H\nt3: 7H\nt4: 9H\nt5: JH\nt6: 3H\nt7: 5D\nt1: *");
            Assert.That(new Autopilot(1).Next(table), Is.Null);
        }

        [Test]
        public void SameSeedPlaysTheSameGameAndEveryChoiceIsLegal()
        {
            string Play(int seed)
            {
                var game = new SolitaireGame(new SolitaireConfig(), 3);
                var pilot = new Autopilot(seed);
                for (var i = 0; i < 200 && !game.IsOver; i++)
                {
                    var move = pilot.Next(game.Table);
                    if (move == null) { break; }
                    Assert.That(game.Apply(move.Value), Is.EqualTo(MoveError.None));
                }

                return TableText.Format(game.Table);
            }

            Assert.That(Play(5), Is.EqualTo(Play(5)));
        }

        [Test]
        public void DemoTablesAreValidAndBehaveAsDescribed()
        {
            var win = new SolitaireGame(new SolitaireConfig(), DemoTables.OneMoveFromWin());
            Assert.That(win.SendToFoundation(PileId.Tableau(2)), Is.EqualTo(MoveError.None));
            Assert.That(win.Phase, Is.EqualTo(GamePhase.Won));

            foreach (var draw in new[] { 1, 3 })
            {
                var stuck = new SolitaireGame(new SolitaireConfig(), DemoTables.OneMoveFromStuck(draw));
                Assert.That(stuck.Phase, Is.EqualTo(GamePhase.Playing));
                Assert.That(stuck.Apply(DemoTables.StuckMove), Is.EqualTo(MoveError.None));
                Assert.That(stuck.Phase, Is.EqualTo(GamePhase.Stuck), $"{draw} 枚めくり");
            }
        }
    }
}
