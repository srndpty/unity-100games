using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>戦闘・レベル・死亡。</summary>
    public sealed class CombatTests
    {
        [Test]
        public void PlayerDamageStaysWithinTheFormulaAndMissesSometimes()
        {
            var run = Lab.Center(1);
            var golem = Lab.Beside(run, MonsterKind.Golem, Direction.East, hp: 100_000);
            golem.SleepTurns = 100_000;
            int hits = 0, misses = 0, min = int.MaxValue, max = 0;
            for (int i = 0; i < 1000; i++)
            {
                Assert.That(run.Submit(Command.Move(Direction.East)).Accepted, Is.True);
                foreach (var e in Lab.EventsOf(run, EventKind.Attack))
                {
                    if (e.Actor != 0) { continue; }
                    if (e.Value < 0) { misses++; }
                    else
                    {
                        hits++;
                        min = Math.Min(min, e.Value);
                        max = Math.Max(max, e.Value);
                    }
                }
            }

            var atk = run.Player.Attack;
            var def = run.Config.StatsOf(MonsterKind.Golem).Defense;
            Assert.That(min, Is.GreaterThanOrEqualTo(Math.Max(1, (atk * 87 / 100) - def)));
            Assert.That(max, Is.LessThanOrEqualTo(Math.Max(1, (atk * 112 / 100) - def)));
            Assert.That(misses, Is.InRange(50, 160), "命中 90%");
            Assert.That(hits + misses, Is.EqualTo(1000));
        }

        [Test]
        public void DamageIsAtLeastOne()
        {
            var run = Lab.Center(2);
            run.Player.Attack = 1;
            var golem = Lab.Beside(run, MonsterKind.Golem, Direction.West, hp: 100_000);
            golem.SleepTurns = 100_000;
            for (int i = 0; i < 100; i++)
            {
                run.Submit(Command.Move(Direction.West));
                foreach (var e in Lab.EventsOf(run, EventKind.Attack))
                {
                    if (e.Actor == 0 && e.Value >= 0) { Assert.That(e.Value, Is.EqualTo(1)); }
                }
            }
        }

        [Test]
        public void KillingGivesExperienceRemovesTheMonsterAndLevelsUp()
        {
            var run = Lab.Center(3);
            var skitter = Lab.Beside(run, MonsterKind.Skitter, Direction.North, hp: 1);
            run.Player.Exp = run.Config.LevelExp[0] - 1;
            var hpBefore = run.Player.MaxHp;
            var atkBefore = run.Player.Attack;
            for (int i = 0; i < 50 && run.Monsters.Count > 0; i++) { run.Submit(Command.Move(Direction.North)); }
            Assert.That(run.Monsters, Is.Empty);
            Assert.That(run.Scheduler.Contains(skitter.Id), Is.False);
            Assert.That(run.Kills, Is.EqualTo(1));
            Assert.That(run.Player.Level, Is.EqualTo(2));
            Assert.That(run.Player.MaxHp, Is.EqualTo(hpBefore + run.Config.LevelHp));
            Assert.That(run.Player.Attack, Is.EqualTo(atkBefore + run.Config.LevelAttack));
            Assert.That(run.Player.Defense, Is.EqualTo(run.Config.PlayerDefense + 1), "2 レベル目で防御 +1");
            Assert.That(run.View.Level, Is.EqualTo(2));
        }

        [Test]
        public void MultipleLevelsCanBeGainedAtOnceButNotPastTheMaximum()
        {
            var run = Lab.Center(4);
            Lab.Beside(run, MonsterKind.Skitter, Direction.North, hp: 1);
            run.Player.Exp = 1_000_000;
            for (int i = 0; i < 50 && run.Monsters.Count > 0; i++) { run.Submit(Command.Move(Direction.North)); }
            Assert.That(run.Player.Level, Is.EqualTo(run.Config.MaxLevel));
            Assert.That(run.View.NextExp, Is.EqualTo(-1));
        }

        [Test]
        public void AdjacentMonsterAttacksAndCanKillThePlayer()
        {
            var run = Lab.Center(5);
            Lab.Beside(run, MonsterKind.Golem, Direction.South);
            run.Player.Hp = 1;
            for (int i = 0; i < 100 && run.State == RunState.Playing; i++) { run.Submit(Command.Wait()); }
            Assert.That(run.State, Is.EqualTo(RunState.Dead));
            Assert.That(run.DeathCause, Is.EqualTo(DeathCause.Monster));
            Assert.That(run.Killer, Is.EqualTo(MonsterKind.Golem));
            Assert.That(run.View.Hp, Is.EqualTo(0));
            Assert.That(Lab.EventsOf(run, EventKind.Died), Has.Count.EqualTo(1));
            Assert.That(run.Submit(Command.Wait()).Reason, Is.EqualTo(RejectReason.NotPlaying));
        }

        [Test]
        public void DiagonalAttackThroughAWallCornerIsBlockedBothWays()
        {
            for (int seed = 1; seed <= 40; seed++)
            {
                var run = Lab.Quiet(seed);
                if (!Lab.TryCornerCut(run, out var at, out var dir)) { continue; }
                run.Teleport(at);
                var m = run.AddMonster(MonsterKind.Skitter, at + dir.Offset());
                run.Refresh();
                Assert.That(run.Submit(Command.Move(dir)).Reason, Is.EqualTo(RejectReason.Blocked), "自機も角越しに殴れない");
                var hp = run.Player.Hp;
                m.Hp = 1000;
                run.Player.Hp = 1000;
                for (int i = 0; i < 3; i++)
                {
                    run.Submit(Command.Wait());
                    foreach (var e in Lab.EventsOf(run, EventKind.Attack))
                    {
                        Assert.That(Directions.TryBetween(e.Position, run.Player.Position, out var d) && run.Grid.CanStep(e.Position, d), Is.True, "敵の攻撃は角を越えない");
                    }
                }

                Assert.That(hp, Is.GreaterThan(0));
                return;
            }

            Assert.Fail("角抜けの場所が見つかりません");
        }
    }
}
