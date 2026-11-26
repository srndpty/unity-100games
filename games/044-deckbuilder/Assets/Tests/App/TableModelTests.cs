using System;
using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>出来事の記録だけから作った卓が、Core の状態と一致する（出来事が漏れなく記録されている）。</summary>
    public sealed class TableModelTests
    {
        private static readonly StatusKind[] _kinds =
        {
            StatusKind.Strength, StatusKind.Weak, StatusKind.Vulnerable, StatusKind.Poison, StatusKind.Bulwark, StatusKind.Miasma,
        };

        private static void AssertSame(TableModel table, Battle battle)
        {
            Assert.That(table.Turn, Is.EqualTo(battle.Turn), "ターン");
            Assert.That(table.Energy, Is.EqualTo(battle.Energy), "エネルギー");
            Assert.That(table.Phase, Is.EqualTo(battle.Phase), "段階");
            Assert.That(table.Player.Hp, Is.EqualTo(battle.Player.Hp), "自機 HP");
            Assert.That(table.Player.Block, Is.EqualTo(battle.Player.Block), "自機ブロック");
            Assert.That(table.Seats.Count - 1, Is.EqualTo(battle.Enemies.Count), "敵の数");
            foreach (var kind in _kinds)
            {
                Assert.That(table.Player.Status(kind), Is.EqualTo(battle.Player.Status(kind)), $"自機 {kind}");
            }

            foreach (var enemy in battle.Enemies)
            {
                var seat = table.SeatById(enemy.Id)!;
                Assert.That(seat.Def, Is.SameAs(enemy.Def));
                Assert.That(seat.Hp, Is.EqualTo(enemy.Hp), $"敵 {enemy.Id} HP");
                Assert.That(seat.Block, Is.EqualTo(enemy.Block), $"敵 {enemy.Id} ブロック");
                Assert.That(seat.Intent, Is.SameAs(enemy.Intent), $"敵 {enemy.Id} 意図");
                foreach (var kind in _kinds)
                {
                    Assert.That(seat.Status(kind), Is.EqualTo(enemy.Status(kind)), $"敵 {enemy.Id} {kind}");
                }
            }

            Assert.That(table.Hand.Select(c => c.Id), Is.EqualTo(battle.Hand.Select(c => c.Id)), "手札（順番まで）");
            Assert.That(table.Discard.Select(c => c.Id), Is.EqualTo(battle.DiscardPile.Select(c => c.Id)), "捨て札（順番まで）");
            Assert.That(table.Exhaust.Select(c => c.Id), Is.EqualTo(battle.ExhaustPile.Select(c => c.Id)), "廃棄（順番まで）");
            Assert.That(table.DrawSorted().Select(c => c.Id), Is.EqualTo(battle.DrawPile.Select(c => c.Id).OrderBy(i => i)), "山札（中身）");
            Assert.That(table.Resolving, Is.Null);
        }

        [Test]
        public void ReplayingAllEventsReproducesTheCoreState([Range(1, 30)] int seed)
        {
            var data = AppLab.Data;
            var ids = data.Cards.Select(c => c.Id).Concat(data.StarterDeck.Select(c => c.Id)).ToArray();
            var deck = ids.Select((id, i) => new CardRef(i + 1, data.Card(id))).ToArray();
            var battle = new Battle(data, new DeckConfig(), deck, 70, 70, data.Encounters[seed % data.Encounters.Count], seed, 0);
            var playback = new EventPlayback(battle, data, new PlaybackTiming());
            var rng = new GameRng(seed * 7);
            for (int step = 0; step < 200 && !battle.IsOver; step++)
            {
                var playable = battle.Hand.Where(battle.Affordable).ToList();
                if (playable.Count == 0 || rng.Next(playable.Count + 1) == 0)
                {
                    battle.EndTurn();
                }
                else
                {
                    var card = playable[rng.Next(playable.Count)];
                    var alive = battle.Enemies.Where(e => e.Alive).ToList();
                    battle.Play(card.Id, card.Def.NeedsTarget ? alive[rng.Next(alive.Count)].Id : null);
                }

                playback.Finish();
                AssertSame(playback.Table, battle);
            }
        }

        [Test]
        public void TheTableLagsBehindTheCoreUntilTheHitIsPlayed()
        {
            var flow = AppLab.InBattleWithStrikeAndGuard();
            var battle = flow.Run!.Battle!;
            var enemy = battle.Enemies.First(e => e.Alive);
            var before = enemy.Hp;
            var strike = AppLab.InHand(flow, "strike");
            flow.Apply(new Intent(IntentKind.SelectCard, strike.Id));
            flow.Apply(new Intent(IntentKind.ClickEnemy, enemy.Id));
            Assert.That(enemy.Hp, Is.LessThan(before), "Core はすぐに最後まで解決する");

            flow.Step();
            var seat = flow.Playback!.Table.SeatById(enemy.Id)!;
            Assert.That(flow.Playback.Current!.Value.Kind, Is.EqualTo(EventKind.CardPlayed));
            Assert.That(seat.Hp, Is.EqualTo(before), "カードを出す演出の間は、まだ当たっていない");
            for (int i = 0; i < 200 && flow.Playback.Current!.Value.Kind != EventKind.Damaged; i++) { flow.Step(); }
            Assert.That(seat.Hp, Is.EqualTo(enemy.Hp));
        }

        [Test]
        public void PlaybackIsDrivenByTicksAndSpeedScalesIt()
        {
            int TicksToFinish(int speed)
            {
                var flow = AppLab.NewFlow();
                flow.Apply(new Intent(IntentKind.Confirm));
                flow.Apply(new Intent(IntentKind.Option, 0));
                var timing = new PlaybackTiming { SpeedPercent = speed };
                var playback = new EventPlayback(flow.Run!.Battle!, AppLab.Data, timing);
                var ticks = 0;
                while (playback.Busy)
                {
                    playback.Step();
                    ticks++;
                }

                return ticks;
            }

            var normal = TicksToFinish(100);
            Assert.That(normal, Is.GreaterThan(30));
            Assert.That(TicksToFinish(200), Is.LessThan(normal));
            Assert.That(TicksToFinish(100), Is.EqualTo(normal));
        }

        [Test]
        public void TimingIsValidated()
        {
            Assert.That(() => new PlaybackTiming { SpeedPercent = 0 }.Validate(), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => new PlaybackTiming { Damaged = -1 }.Validate(), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => AppLab.NewFlow().Apply(new Intent(IntentKind.Confirm)), Throws.Nothing);
            Assert.That(() => new DeckFlow(AppLab.Data, new DeckConfig(), new PlaybackTiming { Ending = 601 }, 1), Throws.InstanceOf<ArgumentOutOfRangeException>());
        }
    }
}
