using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>捕獲・経験値・レベルアップ・技を覚える。</summary>
    public sealed class CaptureAndGrowthTests
    {
        [Test]
        public void CaptureValueFollowsTheFormula()
        {
            Assert.That(Formula.CaptureValue(45, 30, 30, 100), Is.EqualTo(15), "満タン：45×30/90");
            Assert.That(Formula.CaptureValue(45, 1, 30, 100), Is.EqualTo(44), "残り 1：45×88/90");
            Assert.That(Formula.CaptureValue(45, 1, 30, 200), Is.EqualTo(88), "眠り 200%");
            Assert.That(Formula.CaptureValue(45, 1, 30, 150), Is.EqualTo(66), "毒 150%：44×150/100");
            Assert.That(Formula.CaptureValue(190, 1, 30, 200), Is.EqualTo(Formula.CertainRoll), "上限 255 で確定");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void BallSucceedsWhenRollIsBelowTheValue(bool success)
        {
            var wild = Lab.Mon("wickmoth", 5, "spark");
            var value = Formula.CaptureValue(wild.Species.CatchRate, wild.Hp, wild.MaxHp, 100);
            var battle = Lab.Wild(Lab.Mon("ripplet", 20, "harden"), wild, balls: 2);

            // 野生の技の抽選 → 捕獲の判定 Next(256)
            var state = Lab.StateWhere(r =>
            {
                r.Next(1);
                var roll = r.Next(256);
                return success ? roll == value - 1 : roll == value;
            });
            battle.SetRngStateForTest(state);
            var events = battle.Submit(BattleCommand.Ball()).Events;
            Assert.That(battle.Balls, Is.EqualTo(1));
            if (success)
            {
                Assert.That(battle.Outcome, Is.EqualTo(BattleOutcome.Captured));
                Assert.That(battle.Captured!.Species.Id, Is.EqualTo("wickmoth"));
                Assert.That(Lab.First(events, BattleEventKind.BallThrown)!.Amount, Is.EqualTo(3));
                Assert.That(Lab.First(events, BattleEventKind.UsedMove, player: false), Is.Null, "捕まえたら相手は動かない");
            }
            else
            {
                Assert.That(battle.Phase, Is.EqualTo(BattlePhase.ChooseCommand));
                Assert.That(Lab.First(events, BattleEventKind.BrokeFree), Is.Not.Null);
                Assert.That(Lab.First(events, BattleEventKind.UsedMove, player: false), Is.Not.Null, "失敗したら相手の番");
            }
        }

        [Test]
        public void CapturedMonsterLosesItsStatus()
        {
            var wild = Lab.Mon("brinecrab", 3, "harden");
            var battle = Lab.Wild(Lab.Mon("ripplet", 20, "harden"), wild);
            battle.FoeMonster.SetStatus(Lab.Data.Status("sleep"), 9);
            battle.FoeMonster.Hp = 1;
            battle.Submit(BattleCommand.Ball());
            Assert.That(battle.Outcome, Is.EqualTo(BattleOutcome.Captured), "捕獲率 190・残り 1・眠りは確定");
            Assert.That(battle.Captured!.Status, Is.Null);
        }

        [Test]
        public void ExpIsSharedByTheParticipantsStillStanding()
        {
            var a = Lab.Mon("mossling", 5, "harden");
            var b = Lab.Mon("cinderpup", 20, "blaze");
            var c = Lab.Mon("ripplet", 5, "harden");
            var battle = Lab.Wild(new[] { a, b, c }, Lab.Mon("burrowroot", 5, "harden"));
            battle.Submit(BattleCommand.Fight(0));
            battle.Submit(BattleCommand.Switch(1));
            while (!battle.IsOver) { battle.Submit(BattleCommand.Fight(0)); }

            var total = Formula.ExpGain(Lab.Data.Rules, Lab.Data.SpeciesOf("burrowroot"), 5, false);
            var gains = battle.Log.Where(e => e.Kind == BattleEventKind.ExpGained).ToList();
            Assert.That(gains.Select(g => g.Slot), Is.EquivalentTo(new List<int> { 0, 1 }), "出ていない控えは得ない");
            Assert.That(gains.All(g => g.Amount == total / 2), Is.True);
        }

        [Test]
        public void TrainerBattlesGiveMoreExp()
        {
            var species = Lab.Data.SpeciesOf("wickmoth");
            var wild = Formula.ExpGain(Lab.Data.Rules, species, 10, false);
            var trainer = Formula.ExpGain(Lab.Data.Rules, species, 10, true);
            Assert.That(trainer, Is.EqualTo(wild * Lab.Data.Rules.TrainerExpPercent / 100));
        }

        [Test]
        public void GainingLotsOfExpRaisesSeveralLevels_AndStopsAtTheCap()
        {
            var rules = Lab.Data.Rules;
            var m = Lab.Mon("mossling", 5);
            var hpLost = 3;
            m.Hp -= hpLost;
            var levels = m.GainExp(rules, Formula.ExpForLevel(rules, 8) - m.Exp);
            Assert.That(levels, Is.EqualTo(new List<int> { 6, 7, 8 }));
            Assert.That(m.Hp, Is.EqualTo(m.MaxHp - hpLost), "上がった最大 HP の分だけ HP も増える");

            m.GainExp(rules, int.MaxValue);
            Assert.That(m.Level, Is.EqualTo(rules.LevelCap));
            Assert.That(m.Exp, Is.EqualTo(Formula.ExpForLevel(rules, rules.LevelCap)));
        }

        [Test]
        public void CreatedMonstersKnowTheLastFourMoves()
        {
            var m = Lab.Mon("cinderpup", 20);
            Assert.That(m.Moves.Select(s => s.Move.Id), Is.EqualTo(new List<string> { "spark", "smolder", "cinderfang", "blaze" }));
            Assert.That(Lab.Mon("cinderpup", 1).Moves.Select(s => s.Move.Id), Is.EqualTo(new List<string> { "tackle", "growl" }));
        }

        [Test]
        public void LevelUpLearnsIntoAnEmptySlot_OrAsksWhenFull()
        {
            // 経験値の量が多い相手を倒して、レベル 4 → 5 で ひのこ（3 つ目の枠）。
            var pup = Lab.Mon("cinderpup", 4);
            pup.GainExp(Lab.Data.Rules, Formula.ExpForLevel(Lab.Data.Rules, 5) - pup.Exp - 1);
            var battle = Lab.Wild(pup, Lab.Mon("wickmoth", 2, "spark"));
            battle.Party[0].Hp = battle.Party[0].MaxHp;
            while (!battle.IsOver) { battle.Submit(BattleCommand.Fight(0)); }
            Assert.That(Lab.First(battle.Log, BattleEventKind.LearnedMove)!.Move!.Id, Is.EqualTo("spark"));
            Assert.That(battle.Party[0].Moves.Count, Is.EqualTo(3));

            // 4 枠が埋まっていると覚えずに、戦闘の後に選ぶ。
            var full = Lab.Mon("cinderpup", 12, "tackle", "growl", "spark", "smolder");
            full.GainExp(Lab.Data.Rules, Formula.ExpForLevel(Lab.Data.Rules, 13) - full.Exp - 1);
            var b2 = Lab.Wild(full, Lab.Mon("wickmoth", 2, "spark"));
            while (!b2.IsOver) { b2.Submit(BattleCommand.Fight(2)); }
            Assert.That(b2.Pending.Single().Move.Id, Is.EqualTo("cinderfang"));
            Assert.That(Lab.First(b2.Log, BattleEventKind.WantsToLearn), Is.Not.Null);
            Assert.That(b2.Party[0].Moves.Count, Is.EqualTo(4));
        }
    }
}
