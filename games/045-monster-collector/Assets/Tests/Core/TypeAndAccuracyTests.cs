using System;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>題材固有テスト：相性倍率と命中判定が仕様どおり。</summary>
    public sealed class TypeAndAccuracyTests
    {
        [TestCase("ember", "moss", 200)]
        [TestCase("ember", "tide", 50)]
        [TestCase("ember", "ember", 50)]
        [TestCase("tide", "ember", 200)]
        [TestCase("tide", "moss", 50)]
        [TestCase("tide", "tide", 50)]
        [TestCase("moss", "tide", 200)]
        [TestCase("moss", "ember", 50)]
        [TestCase("moss", "moss", 50)]
        public void ChartMatchesTheRuleTable(string attack, string defend, int percent)
        {
            var d = Lab.Data;
            Assert.That(d.Effectiveness(d.Type(attack), d.Type(defend)), Is.EqualTo(percent));
        }

        [Test]
        public void TypelessMovesAreNeutral()
        {
            var d = Lab.Data;
            foreach (var t in d.Types) { Assert.That(d.Effectiveness(null, t), Is.EqualTo(100)); }
        }

        [Test]
        public void DamageAppliesStabThenEffectivenessThenRandom_TruncatingEachStep()
        {
            // 基礎：(2×10/5+2)=6 × 40 × 20 / 15 = 320、/50 = 6、+2 = 8
            Assert.That(Formula.Damage(10, 40, 20, 15, false, 100, 100), Is.EqualTo(8));
            Assert.That(Formula.Damage(10, 40, 20, 15, true, 100, 100), Is.EqualTo(12), "一致補正 8×150/100");
            Assert.That(Formula.Damage(10, 40, 20, 15, true, 200, 100), Is.EqualTo(24), "12×200/100");
            Assert.That(Formula.Damage(10, 40, 20, 15, true, 50, 100), Is.EqualTo(6), "12×50/100");
            Assert.That(Formula.Damage(10, 40, 20, 15, true, 50, 85), Is.EqualTo(5), "6×85/100=5.1 は 5");

            // 掛ける順で結果が変わる例：基礎 7 → 一致補正 10 → いまひとつ 5。逆順なら 7×50/100=3 → ×150/100=4。
            Assert.That(Formula.Damage(5, 65, 20, 20, false, 100, 100), Is.EqualTo(7));
            Assert.That(Formula.Damage(5, 65, 20, 20, true, 50, 100), Is.EqualTo(5));
            Assert.That(Formula.Damage(1, 1, 1, 255, false, 50, 85), Is.EqualTo(1), "最低 1");
        }

        [Test]
        public void BattleDamageUsesTheChart_SuperEffectiveAndResisted()
        {
            // 灯の技で苔（抜群）と潮（いまひとつ）を打ち、出来事の相性と量を比べる。
            var attacker = Lab.Mon("cinderpup", 20, "spark");
            var moss = Lab.Wild(attacker.Clone(), Lab.Mon("burrowroot", 20, "harden"), seed: 3);
            var tide = Lab.Wild(attacker.Clone(), Lab.Mon("brinecrab", 20, "harden"), seed: 3);
            var a = Lab.First(moss.Submit(BattleCommand.Fight(0)).Events, BattleEventKind.Damaged, player: false);
            var b = Lab.First(tide.Submit(BattleCommand.Fight(0)).Events, BattleEventKind.Damaged, player: false);
            Assert.That(a, Is.Not.Null);
            Assert.That(b, Is.Not.Null);
            Assert.That(a!.Effectiveness, Is.EqualTo(200));
            Assert.That(b!.Effectiveness, Is.EqualTo(50));
            Assert.That(a.Amount, Is.GreaterThan(b.Amount * 2));
        }

        [Test]
        public void ChangingTheChartDataChangesTheResult()
        {
            var data = Lab.ParseWith("Types.txt", "chart ember moss 200", "chart ember moss 50");
            Assert.That(data.Effectiveness(data.Type("ember"), data.Type("moss")), Is.EqualTo(50));
            Assert.That(MonsterDataValidator.Validate(data), Is.Empty);
        }

        /// <summary>
        /// 野生戦で味方が先に動くときの乱数の順：相手の技の抽選 → 味方の命中 → 味方のダメージの乱数 → 相手の命中 → …。
        /// 命中は Next(100) &lt; 命中。
        /// </summary>
        [TestCase(84, true)]
        [TestCase(85, false)]
        [TestCase(0, true)]
        [TestCase(99, false)]
        public void HitIsRollBelowAccuracy(int roll, bool hits)
        {
            var state = Lab.StateWhere(r =>
            {
                r.Next(1);
                return r.Next(100) == roll;
            });
            var battle = Lab.Wild(Lab.Mon("cinderpup", 20, "blaze"), Lab.Mon("brinecrab", 5, "harden"));
            battle.SetRngStateForTest(state);
            var events = battle.Submit(BattleCommand.Fight(0)).Events;
            var first = events[1];
            Assert.That(events[0].Kind, Is.EqualTo(BattleEventKind.UsedMove));
            Assert.That(events[0].Player, Is.True, "素早い味方が先");
            Assert.That(first.Kind, Is.EqualTo(hits ? BattleEventKind.Damaged : BattleEventKind.Missed), $"命中 85 に乱数 {roll}");
        }

        [Test]
        public void AccuracyOneHundredStillDrawsOnce()
        {
            var battle = Lab.Wild(Lab.Mon("cinderpup", 20, "tackle"), Lab.Mon("brinecrab", 20, "tackle"), seed: 9);
            var predicted = RandomSeries.FromState(battle.RngState);
            battle.Submit(BattleCommand.Fight(0));

            // 相手の技の抽選・味方の命中・味方のダメージの乱数・相手の命中・相手のダメージの乱数 = 5 回。
            for (int i = 0; i < 5; i++) { predicted.NextUInt(); }
            Assert.That(battle.RngState, Is.EqualTo(predicted.State));
        }

        [Test]
        public void MissSkipsTheRestOfTheEffects_IncludingTheStatus()
        {
            var state = Lab.StateWhere(r =>
            {
                r.Next(1);
                return r.Next(100) >= 75;
            });
            var battle = Lab.Wild(Lab.Mon("mossling", 20, "sleepspore"), Lab.Mon("brinecrab", 5, "harden"));
            battle.SetRngStateForTest(state);
            var events = battle.Submit(BattleCommand.Fight(0)).Events;
            Assert.That(Lab.Count(events, BattleEventKind.Missed), Is.EqualTo(1));
            Assert.That(Lab.Count(events, BattleEventKind.StatusInflicted), Is.Zero);
            Assert.That(battle.FoeMonster.Status, Is.Null);
        }
    }
}
