using System.Collections.Generic;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>再現性：同じシードとコマンドの列なら同じ結果。ハッシュは Mono と .NET で同じ固定値。</summary>
    public sealed class BattleReplayTests
    {
        /// <summary>シード 1 のトレーナー戦（トウマ）を決まった手で最後まで進めたときの状態のハッシュ。</summary>
        public const uint TrainerBattleSeed1Hash = 443020148u;

        private static Battle Play(int seed)
        {
            var party = new[] { Lab.Mon("mossling", 15), Lab.Mon("cinderpup", 15), Lab.Mon("ripplet", 15) };
            var battle = Lab.Trainer(party, "touma", seed);
            var turn = 0;
            while (!battle.IsOver && turn < 500)
            {
                turn++;
                if (battle.Phase == BattlePhase.ForcedSwitch)
                {
                    for (int i = 0; i < battle.Party.Count; i++)
                    {
                        if (battle.Submit(BattleCommand.Switch(i)).Accepted) { break; }
                    }

                    continue;
                }

                var me = battle.PlayerMonster;
                if (me.Hp * 3 < me.MaxHp && battle.Potions > 0)
                {
                    battle.Submit(BattleCommand.Potion(battle.Active));
                    continue;
                }

                var slot = turn % me.Moves.Count;
                if (!battle.Submit(BattleCommand.Fight(slot)).Accepted) { battle.Submit(BattleCommand.Fight(0)); }
            }

            return battle;
        }

        [Test]
        public void SameSeedAndCommandsGiveTheSameBattle()
        {
            var a = Play(1);
            var b = Play(1);
            Assert.That(a.IsOver, Is.True);
            Assert.That(a.Hash(), Is.EqualTo(b.Hash()));
            Assert.That(a.Log.Count, Is.EqualTo(b.Log.Count));
            Assert.That(Play(2).Hash(), Is.Not.EqualTo(a.Hash()));
        }

        [Test]
        public void FixedHashMatchesOnMonoAndDotnet()
        {
            Assert.That(Play(1).Hash(), Is.EqualTo(TrainerBattleSeed1Hash));
        }

        [Test]
        public void CloneIsIndependent()
        {
            var battle = Lab.Wild(Lab.Mon("mossling", 10), Lab.Mon("brinecrab", 10), seed: 7);
            var copy = battle.Clone();
            Assert.That(copy.Hash(), Is.EqualTo(battle.Hash()));
            battle.Submit(BattleCommand.Fight(0));
            Assert.That(copy.Hash(), Is.Not.EqualTo(battle.Hash()));
            Assert.That(copy.Turn, Is.EqualTo(1));
            copy.Submit(BattleCommand.Fight(0));
            Assert.That(copy.Hash(), Is.EqualTo(battle.Hash()), "複製も同じ手で同じ状態になる");
        }

        [Test]
        public void ManyRandomBattlesEndWithinTheTurnLimit_AndKeepInvariants()
        {
            var species = new List<SpeciesDef>(Lab.Data.Species);
            for (int seed = 1; seed <= 200; seed++)
            {
                var rng = new IntGeometry.RandomSeries(seed);
                var party = new List<Monster>();
                var count = rng.Range(1, 6);
                for (int i = 0; i < count; i++) { party.Add(Monster.Create(species[rng.Next(6)], rng.Range(2, 20), Lab.Data.Rules)); }
                var battle = seed % 3 == 0
                    ? Lab.Trainer(party, Lab.Data.Trainers[seed % 3].Id, seed)
                    : Lab.Wild(party, Monster.Create(species[rng.Next(6)], rng.Range(2, 20), Lab.Data.Rules), seed);
                var turns = 0;
                while (!battle.IsOver)
                {
                    Assert.That(++turns, Is.LessThan(2000), $"シード {seed} が終わらない");
                    var kind = rng.Next(5);
                    var command = kind switch
                    {
                        0 => BattleCommand.Switch(rng.Next(battle.Party.Count)),
                        1 => BattleCommand.Potion(rng.Next(battle.Party.Count)),
                        2 => BattleCommand.Ball(),
                        3 => BattleCommand.Run(),
                        _ => BattleCommand.Fight(rng.Next(4)),
                    };
                    battle.Submit(command);
                    foreach (var m in battle.Party)
                    {
                        Assert.That(m.Hp, Is.InRange(0, m.MaxHp));
                        Assert.That(m.Moves.Count, Is.InRange(1, 4));
                    }

                    foreach (var s in battle.PlayerStages) { Assert.That(s, Is.InRange(-3, 3)); }
                    Assert.That(battle.Balls, Is.GreaterThanOrEqualTo(0));
                    Assert.That(battle.Potions, Is.GreaterThanOrEqualTo(0));
                    if (battle.Phase == BattlePhase.ChooseCommand) { Assert.That(battle.PlayerMonster.IsFainted, Is.False); }
                }
            }
        }
    }
}
