using System.Collections.Generic;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class BattleTests
    {
        [Test]
        public void DefendAppliesAtTurnStartAndElementalWeaknessBoostsSpellDamage()
        {
            var data = DefaultGameData.Create();
            var party = new Party(data);
            var inv = new Inventory(data);
            for (var i = 0; i < party.Count; i++)
            {
                party[i].AddExp(i, data.ExpTable.ExpForLevel(7));
                party[i].FullRestore();
            }

            // ヴァルグ（素早さ 15）が先行しても、ターン先頭で構えた防御で被ダメージが半減することを検証
            var bossGroup = data.GetGroup("group_boss");
            var battleGuard = new BattleState(data, bossGroup);
            var partyGuard = party.Clone();
            // 全員 Agi < 4 にしない場合でも、同一乱数で ComputePhysicalDamage の半減と ResolveTurn の wasGuarded を両方検証する
            var heavyAtk = (bossGroup.EnemyIds.Count > 0 ? (data.GetEnemy("boss_valug").Atk * 3) / 2 : 0);
            var directGuarded = BattleResolver.ComputePhysicalDamage(heavyAtk, partyGuard[0].Def, isTargetDefending: true, new GameRng(42));
            var directOpen = BattleResolver.ComputePhysicalDamage(heavyAtk, partyGuard[0].Def, isTargetDefending: false, new GameRng(42));
            Assert.That(directGuarded, Is.EqualTo(directOpen / 2));

            var heavyOverride = new EnemyDecision?[] { new EnemyDecision(EnemyActionKind.HeavyAttack, string.Empty, 0) };
            // 素早さ 1 の味方（揺らぎ 0）より敵が必ず先に行動する状況でも防御が効くこと
            var slowRaw = DefaultGameData.CreateRaw();
            slowRaw.Classes[0].BaseAgi = 1;
            slowRaw.Classes[0].AgiGain = 0;
            var slowData = GameData.Validate(slowRaw);
            var slowPartyGuard = new Party(slowData);
            var slowPartyOpen = new Party(slowData);
            var slowBattleGuard = new BattleState(slowData, slowData.GetGroup("group_boss"));
            var slowBattleOpen = new BattleState(slowData, slowData.GetGroup("group_boss"));

            var guardEvents = BattleResolver.ResolveTurn(
                slowData,
                slowBattleGuard,
                slowPartyGuard,
                new Inventory(slowData),
                new[] { BattleCommand.Defend(), BattleCommand.Defend(), BattleCommand.Defend() },
                new GameRng(42),
                heavyOverride);

            BattleResolver.ResolveTurn(
                slowData,
                slowBattleOpen,
                slowPartyOpen,
                new Inventory(slowData),
                new[] { BattleCommand.Attack(0), BattleCommand.Defend(), BattleCommand.Defend() },
                new GameRng(42),
                heavyOverride);

            var sawGuardedHit = false;
            for (var i = 0; i < guardEvents.Count; i++)
            {
                if (guardEvents[i].Kind == BattleEventKind.DamageDealt && guardEvents[i].WasGuarded)
                {
                    sawGuardedHit = true;
                }
            }

            Assert.That(sawGuardedHit, Is.True);
            var dmgGuarded = slowPartyGuard[0].MaxHp - slowPartyGuard[0].Hp;
            var dmgOpen = slowPartyOpen[0].MaxHp - slowPartyOpen[0].Hp;
            Assert.That(dmgGuarded, Is.EqualTo(dmgOpen / 2));

            // 氷弱点（frost_lance: Power 30, Frost）が 1.5 倍になること
            var frostSpell = data.GetSpell("frost_lance");
            var rngA = new GameRng(100);
            var rngB = new GameRng(100);
            var weakDmg = BattleResolver.ComputeSpellDamage(frostSpell, SpellElement.Frost, false, rngA, out var hitWeak);
            var normalDmg = BattleResolver.ComputeSpellDamage(frostSpell, SpellElement.Fire, false, rngB, out var hitNormal);
            Assert.That(hitWeak, Is.True);
            Assert.That(hitNormal, Is.False);
            Assert.That(weakDmg, Is.EqualTo((normalDmg * 3) / 2));
        }

        [Test]
        public void PhysicalDamageFollowsPercentRangeAndNeverDropsBelowOne()
        {
            // (ATK, DEF): 基礎 0（ATK 9 の魔法使い → DEF 22 の衛兵）、基礎 1、基礎 2（煤スライム → 防具を着た戦士）、基礎 50
            var cases = new[] { (9, 22), (6, 8), (13, 16), (104, 8) };
            foreach (var (atk, def) in cases)
            {
                var baseDamage = System.Math.Max(0, (atk / 2) - (def / 4));
                var lo = System.Math.Max(1, baseDamage * BattleResolver.PhysicalPercentMin / 100);
                var hi = System.Math.Max(1, baseDamage * BattleResolver.PhysicalPercentMax / 100);
                var seen = new HashSet<int>();
                for (var seed = 1; seed <= 400; seed++)
                {
                    var open = BattleResolver.ComputePhysicalDamage(atk, def, isTargetDefending: false, new GameRng(seed));
                    var guarded = BattleResolver.ComputePhysicalDamage(atk, def, isTargetDefending: true, new GameRng(seed));
                    Assert.That(open, Is.InRange(lo, hi), $"ATK {atk} DEF {def}");
                    Assert.That(guarded, Is.EqualTo(System.Math.Max(1, open / 2)), $"ATK {atk} DEF {def}");
                    seen.Add(open);
                }

                Assert.That(seen.Contains(lo) && seen.Contains(hi), Is.True, $"ATK {atk} DEF {def} は両端の倍率を引く");
            }

            // 倍率は 88〜112% の各整数を引く（3 択ではない）
            var percents = new HashSet<int>();
            for (var seed = 1; seed <= 2000; seed++)
            {
                percents.Add(BattleResolver.ComputePhysicalDamage(200, 0, isTargetDefending: false, new GameRng(seed)));
            }

            Assert.That(percents.Count, Is.EqualTo(BattleResolver.PhysicalPercentMax - BattleResolver.PhysicalPercentMin + 1));
        }

        [Test]
        public void SpellDamageStaysExactForHugePowerWithWeaknessAndDefend()
        {
            var raw = DefaultGameData.CreateRaw();
            var frostIndex = System.Array.FindIndex(raw.Spells, s => s.Id == "frost_lance");
            foreach (var power in new[] { 1, 30, 400_000_000, int.MaxValue })
            {
                raw.Spells[frostIndex].Power = power;
                var spell = GameData.Validate(raw).GetSpell("frost_lance");
                for (var seed = 1; seed <= 200; seed++)
                {
                    var percent = new GameRng(seed).Range(BattleResolver.SpellPercentMin, BattleResolver.SpellPercentMax);
                    var expectedNormal = System.Math.Max(1L, (long)power * percent / 100);
                    var expectedWeak = expectedNormal * 3 / 2;
                    var normal = BattleResolver.ComputeSpellDamage(spell, SpellElement.Fire, false, new GameRng(seed), out _);
                    var weak = BattleResolver.ComputeSpellDamage(spell, SpellElement.Frost, false, new GameRng(seed), out var hitWeak);
                    var weakGuarded = BattleResolver.ComputeSpellDamage(spell, SpellElement.Frost, true, new GameRng(seed), out _);
                    Assert.That(hitWeak, Is.True);
                    Assert.That(normal, Is.EqualTo((int)System.Math.Min(int.MaxValue, expectedNormal)), $"Power {power}");
                    Assert.That(weak, Is.EqualTo((int)System.Math.Min(int.MaxValue, expectedWeak)), $"Power {power}");
                    Assert.That(weakGuarded, Is.EqualTo((int)System.Math.Min(int.MaxValue, System.Math.Max(1L, expectedWeak / 2))), $"Power {power}");
                }
            }
        }

        [Test]
        public void VictoryRewardsSaturateInsteadOfOverflowing()
        {
            var raw = DefaultGameData.CreateRaw();
            var slimeIndex = System.Array.FindIndex(raw.Enemies, e => e.Id == "soot_slime");
            raw.Enemies[slimeIndex].ExpReward = 1_500_000_000;
            raw.Enemies[slimeIndex].GoldReward = 1_500_000_000;
            var data = GameData.Validate(raw);
            var party = new Party(data);
            var battle = new BattleState(data, data.GetGroup("f1_slimes"));
            for (var i = 0; i < battle.Enemies.Count; i++)
            {
                battle.Enemies[i].SetVitals(1, battle.Enemies[i].Mp);
            }

            BattleResolver.ResolveTurn(
                data,
                battle,
                party,
                new Inventory(data),
                new[] { BattleCommand.Attack(0), BattleCommand.Attack(1), BattleCommand.Attack(0) },
                new GameRng(7),
                null);

            Assert.That(battle.Outcome, Is.EqualTo(BattleOutcome.Victory));
            Assert.That(battle.ExpEarnedEach, Is.EqualTo(int.MaxValue));
            Assert.That(battle.GoldEarned, Is.EqualTo(int.MaxValue));
            var maxExp = data.ExpTable.ExpForLevel(data.ExpTable.MaxLevel);
            for (var i = 0; i < party.Count; i++)
            {
                Assert.That(party[i].Exp, Is.EqualTo(maxExp));
                Assert.That(party[i].Level, Is.EqualTo(data.ExpTable.MaxLevel));
            }

            var world = World.CreateNew(data, 1);
            world.AddGold(battle.GoldEarned);
            Assert.That(world.Gold, Is.EqualTo(data.Rules.MaxGold));

            // 累積経験値の加算も桁あふれしない
            var member = new Party(data)[0];
            member.AddExp(0, maxExp - 1);
            member.AddExp(0, int.MaxValue);
            Assert.That(member.Exp, Is.EqualTo(maxExp));
        }

        [Test]
        public void MpShortageIsRejectedAtSelectionAndHandledCleanlyAtResolutionForAllyAndEnemy()
        {
            var data = DefaultGameData.Create();
            var party = new Party(data);
            var inv = new Inventory(data);
            var battle = new BattleState(data, data.GetGroup("f1_slimes"));

            // 選択時: MP が 1 足りなければ InsufficientMp、ちょうどなら成功
            var mage = party[2];
            var emberCost = data.GetSpell("ember").MpCost;
            mage.SetVitals(mage.Hp, emberCost - 1);
            Assert.That(
                BattleRules.ValidateCommand(data, battle, party, inv, 2, BattleCommand.CastSpell("ember", 0), null, out var errShort),
                Is.False);
            Assert.That(errShort, Is.EqualTo(CommandError.InsufficientMp));

            mage.SetVitals(mage.Hp, emberCost);
            Assert.That(
                BattleRules.ValidateCommand(data, battle, party, inv, 2, BattleCommand.CastSpell("ember", 0), null, out var errExact),
                Is.True);
            Assert.That(errExact, Is.EqualTo(CommandError.None));

            // 解決時: 味方と敵の両方で MP 不足なら消費せず InsufficientMp になる
            mage.SetVitals(mage.Hp, emberCost - 1);
            var acolyteBattle = new BattleState(data, data.GetGroup("f2_cult"));
            acolyteBattle.Enemies[0].SetVitals(acolyteBattle.Enemies[0].Hp, 0);

            // 敵 AI は MP 0 のとき呪文を選ばず通常攻撃へ落ちる
            var aiChoice = EnemyAi.ChooseAction(data, acolyteBattle, party, 0, new GameRng(7));
            Assert.That(aiChoice.Kind, Is.EqualTo(EnemyActionKind.Attack));

            // 解決時点で敵呪文が選ばれていた場合も MP 不足で不発になる
            var events = BattleResolver.ResolveTurn(
                data,
                acolyteBattle,
                party,
                inv,
                new[] { BattleCommand.Defend(), BattleCommand.Defend(), BattleCommand.CastSpell("ember", 0) },
                new GameRng(7),
                new EnemyDecision?[] { new EnemyDecision(EnemyActionKind.Spell, "ember", 0), null, null });

            var allyNoMp = false;
            var enemyNoMp = false;
            for (var i = 0; i < events.Count; i++)
            {
                if (events[i].Kind == BattleEventKind.InsufficientMp)
                {
                    if (events[i].ActorSide == BattleSide.Ally)
                    {
                        allyNoMp = true;
                    }
                    else
                    {
                        enemyNoMp = true;
                    }
                }
            }

            Assert.That(allyNoMp, Is.True);
            Assert.That(enemyNoMp, Is.True);
            Assert.That(mage.Mp, Is.EqualTo(emberCost - 1));
            Assert.That(acolyteBattle.Enemies[0].Mp, Is.EqualTo(0));
        }

        [Test]
        public void SubGroupRetargetingAndDeadHealTargetPreserveResourcesWhenTargetGone()
        {
            var data = DefaultGameData.Create();
            var party = new Party(data);
            var inv = new Inventory(data);
            inv.Add("herb", 1);

            // 1 種類 1 個の薬草を 2 人が同時に選ぶことはできない
            var battleCheck = new BattleState(data, data.GetGroup("f1_mixed"));
            var pending = new[] { BattleCommand.UseItem("herb", 0), BattleCommand.None, BattleCommand.None };
            Assert.That(
                BattleRules.ValidateCommand(data, battleCheck, party, inv, 1, BattleCommand.UseItem("herb", 0), pending, out var itemErr),
                Is.False);
            Assert.That(itemErr, Is.EqualTo(CommandError.OutOfItem));

            // グループ [soot_slime, cinder_bat, soot_slime] で slot 0 が既に倒れていれば slot 2 へ自動変更される
            var raw = DefaultGameData.CreateRaw();
            raw.EnemyGroups[1].EnemyIds = new[] { "soot_slime", "cinder_bat", "soot_slime" };
            var customData = GameData.Validate(raw);
            var customParty = new Party(customData);
            for (var i = 0; i < customParty.Count; i++)
            {
                customParty[i].AddExp(i, customData.ExpTable.ExpForLevel(6));
                customParty[i].FullRestore();
            }

            var battle = new BattleState(customData, customData.GetGroup("f1_mixed"));
            Assert.That(battle.Enemies[0].SubGroup, Is.EqualTo(battle.Enemies[2].SubGroup));
            Assert.That(battle.Enemies[1].SubGroup, Is.Not.EqualTo(battle.Enemies[0].SubGroup));

            // slot 0 を HP 1 にして戦士と魔法使いが両方 slot 0 を狙う -> 1 人目が slot 0 撃破、2 人目は slot 2 へ自動変更
            battle.Enemies[0].SetVitals(1, 0);
            battle.Enemies[2].SetVitals(1, 0);
            BattleResolver.ResolveTurn(
                customData,
                battle,
                customParty,
                new Inventory(customData),
                new[] { BattleCommand.Attack(0), BattleCommand.Defend(), BattleCommand.CastSpell("ember", 0) },
                new GameRng(11));

            Assert.That(battle.Enemies[0].IsAlive, Is.False);
            Assert.That(battle.Enemies[2].IsAlive, Is.False);
            Assert.That(battle.Enemies[1].IsAlive, Is.True);

            // 既に全滅したサブグループを狙った呪文は不発となり MP を消費しない
            var mpBefore = customParty[2].Mp;
            var events2 = BattleResolver.ResolveTurn(
                customData,
                battle,
                customParty,
                new Inventory(customData),
                new[] { BattleCommand.Defend(), BattleCommand.Defend(), BattleCommand.CastSpell("ember", 0) },
                new GameRng(12));

            Assert.That(customParty[2].Mp, Is.EqualTo(mpBefore));
            var sawNoEffect = false;
            for (var i = 0; i < events2.Count; i++)
            {
                if (events2[i].Kind == BattleEventKind.ActionNoEffect && events2[i].ActorIndex == 2)
                {
                    sawNoEffect = true;
                }
            }

            Assert.That(sawNoEffect, Is.True);

            // ターン途中で回復対象の仲間が倒れたら、回復呪文も道具も MP・個数を消費しない
            var wipeParty = new Party(customData);
            wipeParty[0].SetVitals(1, wipeParty[0].Mp);
            wipeParty[1].SetVitals(wipeParty[1].MaxHp, wipeParty[1].MaxMp);
            wipeParty[2].SetVitals(wipeParty[2].MaxHp, wipeParty[2].MaxMp);
            var bag = new Inventory(customData);
            bag.Add("herb", 1);

            // 素早い敵が先に slot 0 を倒し、その後で僧侶の heal(0) と魔法使いの herb(0) が回る
            var fastBattle = new BattleState(customData, customData.GetGroup("group_boss"));
            var priestMpBefore = wipeParty[1].Mp;
            BattleResolver.ResolveTurn(
                customData,
                fastBattle,
                wipeParty,
                bag,
                new[] { BattleCommand.Defend(), BattleCommand.CastSpell("heal", 0), BattleCommand.UseItem("herb", 0) },
                new GameRng(3),
                new EnemyDecision?[] { new EnemyDecision(EnemyActionKind.HeavyAttack, string.Empty, 0) });

            Assert.That(wipeParty[0].IsAlive, Is.False);
            Assert.That(wipeParty[1].Mp, Is.EqualTo(priestMpBefore));
            Assert.That(bag.Count("herb"), Is.EqualTo(1));
        }

        [Test]
        public void BattleEndsImmediatelyOnVictoryOrPartyWipeAndBossRejectsFlee()
        {
            var data = DefaultGameData.Create();
            var party = new Party(data);
            var inv = new Inventory(data);

            // ボス戦は逃走不可
            var bossBattle = new BattleState(data, data.GetGroup("group_boss"));
            Assert.That(
                BattleRules.ValidateCommand(data, bossBattle, party, inv, 0, BattleCommand.Flee(), null, out var fleeErr),
                Is.False);
            Assert.That(fleeErr, Is.EqualTo(CommandError.CannotFleeBoss));

            // 全滅発生後は残りの敵行動が続かない
            for (var i = 0; i < party.Count; i++)
            {
                party[i].SetVitals(1, party[i].Mp);
            }

            var multiEnemyBattle = new BattleState(data, data.GetGroup("f1_bats"));
            multiEnemyBattle.Enemies[0].SetVitals(multiEnemyBattle.Enemies[0].Hp, 8);
            var wipeEvents = BattleResolver.ResolveTurn(
                data,
                multiEnemyBattle,
                party,
                inv,
                new[] { BattleCommand.Defend(), BattleCommand.Defend(), BattleCommand.Defend() },
                new GameRng(99),
                new EnemyDecision?[]
                {
                    new EnemyDecision(EnemyActionKind.Spell, "flame_wave", -1),
                    new EnemyDecision(EnemyActionKind.Attack, string.Empty, 0),
                });

            Assert.That(multiEnemyBattle.Outcome, Is.EqualTo(BattleOutcome.PartyWiped));
            Assert.That(wipeEvents[wipeEvents.Count - 1].Kind, Is.EqualTo(BattleEventKind.PartyWiped));
        }

        [Test]
        public void SnapshotReplayMatchesResolvedStateAcrossRandomizedBattles()
        {
            var data = DefaultGameData.Create();
            for (var seed = 1; seed <= 50; seed++)
            {
                var rng = new GameRng(seed * 7919);
                var party = new Party(data);
                var levelBoost = rng.Range(0, 6);
                if (levelBoost > 0)
                {
                    var exp = data.ExpTable.ExpForLevel(1 + levelBoost);
                    for (var m = 0; m < party.Count; m++)
                    {
                        party[m].AddExp(m, exp);
                        party[m].FullRestore();
                    }
                }

                var inv = new Inventory(data);
                inv.Add("herb", 4);
                inv.Add("ether_dew", 2);

                var group = data.EnemyGroups[rng.Next(data.EnemyGroups.Count)];
                var battle = new BattleState(data, group);

                var safety = 0;
                while (battle.Outcome == BattleOutcome.Ongoing && safety < 40)
                {
                    safety++;
                    var cmds = BuildRandomValidCommands(data, battle, party, inv, rng);
                    var snapBefore = BattleSnapshot.Capture(battle, party, inv);
                    var events = BattleResolver.ResolveTurn(data, battle, party, inv, cmds, rng);
                    for (var e = 0; e < events.Count; e++)
                    {
                        snapBefore.Apply(events[e]);
                    }

                    var snapAfter = BattleSnapshot.Capture(battle, party, inv);
                    Assert.That(snapBefore.Matches(snapAfter), Is.True, $"Snapshot mismatch on seed={seed}, turn={battle.Turn}");
                    Assert.That(snapBefore.Hash(), Is.EqualTo(snapAfter.Hash()));
                }
            }
        }

        private static BattleCommand[] BuildRandomValidCommands(GameData data, BattleState battle, Party party, Inventory inv, GameRng rng)
        {
            var cmds = new BattleCommand[party.Count];
            if (!battle.IsBossBattle && rng.Next(12) == 0)
            {
                for (var i = 0; i < party.Count; i++)
                {
                    cmds[i] = party[i].IsAlive ? BattleCommand.Flee() : BattleCommand.None;
                }

                return cmds;
            }

            var livingEnemies = new List<int>();
            for (var i = 0; i < battle.Enemies.Count; i++)
            {
                if (battle.Enemies[i].IsAlive)
                {
                    livingEnemies.Add(i);
                }
            }

            var livingAllies = new List<int>();
            var deadAllies = new List<int>();
            for (var i = 0; i < party.Count; i++)
            {
                if (party[i].IsAlive)
                {
                    livingAllies.Add(i);
                }
                else
                {
                    deadAllies.Add(i);
                }
            }

            for (var i = 0; i < party.Count; i++)
            {
                var actor = party[i];
                if (!actor.IsAlive)
                {
                    cmds[i] = BattleCommand.None;
                    continue;
                }

                var roll = rng.Next(5);
                if (roll == 0)
                {
                    cmds[i] = BattleCommand.Defend();
                    continue;
                }

                if (roll == 1 || roll == 2)
                {
                    var spells = actor.LearnedSpells();
                    if (spells.Count > 0)
                    {
                        var sp = spells[rng.Next(spells.Count)];
                        var target = sp.Kind switch
                        {
                            SpellKind.SingleAttack => livingEnemies[rng.Next(livingEnemies.Count)],
                            SpellKind.GroupAttack => livingEnemies[rng.Next(livingEnemies.Count)],
                            SpellKind.SingleHeal => livingAllies[rng.Next(livingAllies.Count)],
                            SpellKind.Revive => deadAllies.Count > 0 ? deadAllies[0] : -1,
                            _ => 0,
                        };
                        var candidate = BattleCommand.CastSpell(sp.Id, target);
                        if (BattleRules.ValidateCommand(data, battle, party, inv, i, candidate, cmds, out _))
                        {
                            cmds[i] = candidate;
                            continue;
                        }
                    }
                }

                if (roll == 3)
                {
                    var usable = inv.UsableItems();
                    if (usable.Count > 0)
                    {
                        var entry = usable[rng.Next(usable.Count)];
                        var candidate = BattleCommand.UseItem(entry.ProductId, livingAllies[rng.Next(livingAllies.Count)]);
                        if (BattleRules.ValidateCommand(data, battle, party, inv, i, candidate, cmds, out _))
                        {
                            cmds[i] = candidate;
                            continue;
                        }
                    }
                }

                cmds[i] = BattleCommand.Attack(livingEnemies[rng.Next(livingEnemies.Count)]);
            }

            return cmds;
        }
    }
}
