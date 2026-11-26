using System.Collections.Generic;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>戦闘のフェーズ・受け付けない操作・交代・行動順。</summary>
    public sealed class PhaseTests
    {
        private static void AssertRejectedWithoutChange(Battle battle, BattleCommand command, RejectReason reason)
        {
            var hash = battle.Hash();
            var rng = battle.RngState;
            var log = battle.Log.Count;
            var result = battle.Submit(command);
            Assert.That(result.Accepted, Is.False, command.ToString());
            Assert.That(result.Reason, Is.EqualTo(reason), command.ToString());
            Assert.That(result.Events, Is.Empty);
            Assert.That(battle.Hash(), Is.EqualTo(hash), "拒否は状態を変えない");
            Assert.That(battle.RngState, Is.EqualTo(rng), "拒否は乱数を引かない");
            Assert.That(battle.Log.Count, Is.EqualTo(log), "拒否は出来事を残さない");
        }

        [Test]
        public void InvalidCommandsAreRejectedWithoutAnyChange()
        {
            var tired = Lab.Mon("mossling", 10, "vinewhip", "growl");
            var battle = Lab.Wild(new[] { tired, Lab.Mon("ripplet", 10), Lab.Mon("cinderpup", 3) }, Lab.Mon("brinecrab", 5), potions: 1);
            battle.Party[0].Moves[1].Pp = 0;
            battle.Party[2].Hp = 0;
            AssertRejectedWithoutChange(battle, BattleCommand.Fight(1), RejectReason.NoPp);
            AssertRejectedWithoutChange(battle, BattleCommand.Fight(2), RejectReason.NoSuchMove);
            AssertRejectedWithoutChange(battle, BattleCommand.Fight(-1), RejectReason.NoSuchMove);
            AssertRejectedWithoutChange(battle, BattleCommand.Switch(0), RejectReason.AlreadyActive);
            AssertRejectedWithoutChange(battle, BattleCommand.Switch(2), RejectReason.MemberFainted);
            AssertRejectedWithoutChange(battle, BattleCommand.Switch(3), RejectReason.NoSuchMember);
            AssertRejectedWithoutChange(battle, BattleCommand.Potion(0), RejectReason.FullHp);
            AssertRejectedWithoutChange(battle, BattleCommand.Potion(2), RejectReason.MemberFainted);

            var noBalls = Lab.Wild(Lab.Mon("mossling", 10), Lab.Mon("brinecrab", 5), balls: 0, potions: 0);
            AssertRejectedWithoutChange(noBalls, BattleCommand.Ball(), RejectReason.NoBall);
            noBalls.Party[0].Hp = 1;
            AssertRejectedWithoutChange(noBalls, BattleCommand.Potion(0), RejectReason.NoPotion);

            var full = Lab.Wild(Lab.Mon("mossling", 10), Lab.Mon("brinecrab", 5), hasRoom: false);
            AssertRejectedWithoutChange(full, BattleCommand.Ball(), RejectReason.NoRoom);
        }

        [Test]
        public void TrainerBattlesRejectBallsAndRunning()
        {
            var battle = Lab.Trainer(new[] { Lab.Mon("mossling", 10) }, "saki");
            AssertRejectedWithoutChange(battle, BattleCommand.Ball(), RejectReason.TrainerBattle);
            AssertRejectedWithoutChange(battle, BattleCommand.Run(), RejectReason.TrainerBattle);
        }

        [Test]
        public void ForcedSwitchDoesNotGiveTheFoeAFreeTurn()
        {
            var battle = Lab.Wild(new[] { Lab.Mon("mossling", 2, "growl"), Lab.Mon("ripplet", 20, "harden") }, Lab.Mon("cinderpup", 20, "blaze"), seed: 4);
            while (battle.Phase == BattlePhase.ChooseCommand) { battle.Submit(BattleCommand.Fight(0)); }
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.ForcedSwitch));
            Assert.That(battle.PlayerMonster.IsFainted, Is.True);
            var turn = battle.Turn;

            AssertRejectedWithoutChange(battle, BattleCommand.Fight(0), RejectReason.MustSwitch);
            AssertRejectedWithoutChange(battle, BattleCommand.Run(), RejectReason.MustSwitch);
            AssertRejectedWithoutChange(battle, BattleCommand.Potion(1), RejectReason.MustSwitch);
            AssertRejectedWithoutChange(battle, BattleCommand.Switch(0), RejectReason.MemberFainted);

            var events = battle.Submit(BattleCommand.Switch(1)).Events;
            Assert.That(Lab.First(events, BattleEventKind.SentOut, player: true), Is.Not.Null);
            Assert.That(Lab.First(events, BattleEventKind.UsedMove, player: false), Is.Null, "強制交代では相手は動かない");
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.ChooseCommand));
            Assert.That(battle.Turn, Is.EqualTo(turn + 1));
            Assert.That(battle.Active, Is.EqualTo(1));
        }

        [Test]
        public void LosingTheLastMemberEndsTheBattle_ThenEverythingIsRejected()
        {
            var battle = Lab.Wild(Lab.Mon("mossling", 2, "growl"), Lab.Mon("cinderpup", 20, "blaze"), seed: 2);
            while (!battle.IsOver) { battle.Submit(BattleCommand.Fight(0)); }
            Assert.That(battle.Outcome, Is.EqualTo(BattleOutcome.Lost));
            Assert.That(Lab.Count(battle.Log, BattleEventKind.Lost), Is.EqualTo(1));
            foreach (var c in new[] { BattleCommand.Fight(0), BattleCommand.Switch(0), BattleCommand.Potion(0), BattleCommand.Ball(), BattleCommand.Run() })
            {
                AssertRejectedWithoutChange(battle, c, RejectReason.BattleOver);
            }
        }

        [Test]
        public void TrainerSendsTheNextMonster_AndTheBattleIsWonAfterTheLast()
        {
            var battle = Lab.Trainer(new[] { Lab.Mon("cinderpup", 20, "blaze", "spark") }, "oboro", seed: 5);
            var sentOut = 1;
            while (!battle.IsOver)
            {
                var events = battle.Submit(BattleCommand.Fight(0)).Events;
                sentOut += Lab.Count(new List<BattleEvent>(events).FindAll(e => !e.Player), BattleEventKind.SentOut);
            }

            Assert.That(battle.Outcome, Is.EqualTo(BattleOutcome.Won));
            Assert.That(sentOut, Is.EqualTo(Lab.Data.Trainer("oboro").Party.Count));
            Assert.That(battle.Foes, Has.All.Matches<Monster>(m => m.IsFainted));
        }

        [Test]
        public void PriorityBeatsSpeed()
        {
            var battle = Lab.Wild(Lab.Mon("brinecrab", 20, "quickjab"), Lab.Mon("wickmoth", 20, "spark"), seed: 1);
            Assert.That(battle.FoeMonster.Speed, Is.GreaterThan(battle.PlayerMonster.Speed));
            var events = battle.Submit(BattleCommand.Fight(0)).Events;
            Assert.That(Lab.First(events, BattleEventKind.UsedMove)!.Player, Is.True, "優先度 1 の すばやづき が先");
        }

        [Test]
        public void FasterGoesFirst_SwitchGoesBeforeMoves()
        {
            var battle = Lab.Wild(new[] { Lab.Mon("brinecrab", 20, "splash"), Lab.Mon("ripplet", 20, "harden") }, Lab.Mon("wickmoth", 20, "spark"), seed: 1);
            var events = battle.Submit(BattleCommand.Fight(0)).Events;
            Assert.That(Lab.First(events, BattleEventKind.UsedMove)!.Player, Is.False, "素早い相手が先");

            var hp = battle.Party[1].Hp;
            var swap = battle.Submit(BattleCommand.Switch(1)).Events;
            Assert.That(swap[0].Kind, Is.EqualTo(BattleEventKind.Withdrew));
            var hit = Lab.First(swap, BattleEventKind.Damaged, player: true);
            if (hit != null)
            {
                Assert.That(hit.Slot, Is.EqualTo(1), "相手の技は交代先が受ける");
                Assert.That(battle.Party[1].Hp, Is.EqualTo(hp - hit.Amount));
            }
        }

        [Test]
        public void SpeedTieIsDecidedByTheBattleRng()
        {
            var firsts = new HashSet<bool>();
            for (int seed = 1; seed <= 30; seed++)
            {
                var battle = Lab.Wild(Lab.Mon("mossling", 10, "tackle"), Lab.Mon("mossling", 10, "tackle"), seed);
                firsts.Add(Lab.First(battle.Submit(BattleCommand.Fight(0)).Events, BattleEventKind.UsedMove)!.Player);
            }

            Assert.That(firsts, Is.EquivalentTo(new List<bool> { true, false }));
        }

        [Test]
        public void StruggleIsUsedWhenNoPpRemains_AndSpendsNone()
        {
            var battle = Lab.Wild(Lab.Mon("mossling", 10, "tackle"), Lab.Mon("brinecrab", 20, "harden"));
            battle.Party[0].Moves[0].Pp = 0;
            var events = battle.Submit(BattleCommand.Fight(0)).Events;
            var used = Lab.First(events, BattleEventKind.UsedMove, player: true);
            Assert.That(used!.Move!.Id, Is.EqualTo("struggle"));
            Assert.That(battle.Party[0].Moves[0].Pp, Is.Zero);
        }

        [Test]
        public void PotionHealsUpToTheRuleAmount()
        {
            var battle = Lab.Wild(new[] { Lab.Mon("mossling", 20, "harden"), Lab.Mon("ripplet", 20, "harden") }, Lab.Mon("brinecrab", 20, "harden"), potions: 2);
            battle.Party[1].Hp = battle.Party[1].MaxHp - 5;
            battle.Submit(BattleCommand.Potion(1));
            Assert.That(battle.Party[1].Hp, Is.EqualTo(battle.Party[1].MaxHp), "足りない分だけ");
            battle.Party[0].Hp = 1;
            battle.Submit(BattleCommand.Potion(0));
            Assert.That(battle.Party[0].Hp, Is.EqualTo(1 + Lab.Data.Rules.PotionHeal));
            Assert.That(battle.Potions, Is.Zero);
        }

        [Test]
        public void RunOddsFollowTheFormula()
        {
            Assert.That(Formula.RunOdds(50, 100, 0), Is.EqualTo(64));
            Assert.That(Formula.RunOdds(50, 100, 2), Is.EqualTo(124));
            Assert.That(Formula.RunOdds(200, 100, 0), Is.EqualTo(256), "速ければ必ず逃げる");
            var battle = Lab.Wild(Lab.Mon("wickmoth", 20, "spark"), Lab.Mon("brinecrab", 5, "harden"));
            battle.Submit(BattleCommand.Run());
            Assert.That(battle.Outcome, Is.EqualTo(BattleOutcome.Ran));
        }
    }
}
