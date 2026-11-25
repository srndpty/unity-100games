using System;
using System.Collections.Generic;

namespace Game.Core
{
    public enum BattleEventKind
    {
        TurnStarted = 0,
        DefendStance = 1,
        ActionDeclared = 2,
        InsufficientMp = 3,
        ActionNoEffect = 4,
        MpSpent = 5,
        ItemConsumed = 6,
        DamageDealt = 7,
        HpHealed = 8,
        MpRestored = 9,
        UnitRevived = 10,
        UnitFallen = 11,
        FleeSucceeded = 12,
        FleeFailed = 13,
        Victory = 14,
        ExpAwarded = 15,
        LevelUp = 16,
        PartyWiped = 17,
    }

    /// <summary>戦闘解決が返す不変の演出用記録。状態そのものは変えない。</summary>
    public sealed class BattleEvent
    {
        public BattleEvent(
            BattleEventKind kind,
            BattleSide actorSide = BattleSide.Ally,
            int actorIndex = -1,
            BattleSide targetSide = BattleSide.Ally,
            int targetIndex = -1,
            int amount = 0,
            int secondaryAmount = 0,
            string actionId = "",
            bool hitWeakness = false,
            bool wasGuarded = false,
            LevelUpGain? levelUp = null,
            string message = "")
        {
            Kind = kind;
            ActorSide = actorSide;
            ActorIndex = actorIndex;
            TargetSide = targetSide;
            TargetIndex = targetIndex;
            Amount = amount;
            SecondaryAmount = secondaryAmount;
            ActionId = actionId ?? string.Empty;
            HitWeakness = hitWeakness;
            WasGuarded = wasGuarded;
            LevelUp = levelUp;
            Message = message ?? string.Empty;
        }

        public BattleEventKind Kind { get; }
        public BattleSide ActorSide { get; }
        public int ActorIndex { get; }
        public BattleSide TargetSide { get; }
        public int TargetIndex { get; }
        public int Amount { get; }
        public int SecondaryAmount { get; }
        public string ActionId { get; }
        public bool HitWeakness { get; }
        public bool WasGuarded { get; }
        public LevelUpGain? LevelUp { get; }
        public string Message { get; }
    }

    public readonly struct EnemyDecision
    {
        public EnemyDecision(EnemyActionKind kind, string spellId, int targetIndex)
        {
            Kind = kind;
            SpellId = spellId ?? string.Empty;
            TargetIndex = targetIndex;
        }

        public EnemyActionKind Kind { get; }
        public string SpellId { get; }
        public int TargetIndex { get; }
    }

    public static class EnemyAi
    {
        public static EnemyDecision ChooseAction(GameData data, BattleState battle, Party party, int enemyIndex, GameRng rng)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (battle == null)
            {
                throw new ArgumentNullException(nameof(battle));
            }

            if (party == null)
            {
                throw new ArgumentNullException(nameof(party));
            }

            if (rng == null)
            {
                throw new ArgumentNullException(nameof(rng));
            }

            var enemy = battle.Enemies[enemyIndex];
            var usePhase2 = enemy.Info.IsBoss && enemy.Info.Phase2Actions.Count > 0 && (enemy.Hp * 2) <= enemy.Info.MaxHp;
            var table = usePhase2 ? enemy.Info.Phase2Actions : enemy.Info.Actions;

            var usable = new List<EnemyActionInfo>(table.Count);
            var totalWeight = 0;
            for (var i = 0; i < table.Count; i++)
            {
                var entry = table[i];
                if (entry.Kind == EnemyActionKind.Spell && !BattleRules.CanEnemyCast(data, enemy, entry.SpellId, out _))
                {
                    continue;
                }

                usable.Add(entry);
                totalWeight += entry.Weight;
            }

            EnemyActionKind chosenKind = EnemyActionKind.Attack;
            var chosenSpellId = string.Empty;
            if (usable.Count > 0 && totalWeight > 0)
            {
                var roll = rng.Next(totalWeight);
                for (var i = 0; i < usable.Count; i++)
                {
                    roll -= usable[i].Weight;
                    if (roll < 0)
                    {
                        chosenKind = usable[i].Kind;
                        chosenSpellId = usable[i].SpellId;
                        break;
                    }
                }
            }

            if (chosenKind == EnemyActionKind.Spell)
            {
                var spell = data.GetSpell(chosenSpellId);
                if (spell.Kind == SpellKind.SingleHeal)
                {
                    var healTarget = PickEnemyHealTarget(battle, enemyIndex);
                    return new EnemyDecision(chosenKind, chosenSpellId, healTarget);
                }

                if (spell.Kind == SpellKind.AllHeal || spell.Kind == SpellKind.GroupAttack)
                {
                    return new EnemyDecision(chosenKind, chosenSpellId, -1);
                }
            }

            var targetAlly = PickLivingAlly(party, rng);
            return new EnemyDecision(chosenKind, chosenSpellId, targetAlly);
        }

        private static int PickLivingAlly(Party party, GameRng rng)
        {
            var living = new List<int>(party.Count);
            for (var i = 0; i < party.Count; i++)
            {
                if (party[i].IsAlive)
                {
                    living.Add(i);
                }
            }

            if (living.Count == 0)
            {
                return -1;
            }

            return living[rng.Next(living.Count)];
        }

        private static int PickEnemyHealTarget(BattleState battle, int selfIndex)
        {
            var best = selfIndex;
            var bestRatioPermille = 2000;
            for (var i = 0; i < battle.Enemies.Count; i++)
            {
                var e = battle.Enemies[i];
                if (!e.IsAlive)
                {
                    continue;
                }

                var ratio = (e.Hp * 1000) / Math.Max(1, e.Info.MaxHp);
                if (ratio < bestRatioPermille)
                {
                    bestRatioPermille = ratio;
                    best = i;
                }
            }

            return best;
        }
    }

    /// <summary>
    /// 戦闘ターンの純粋な解決器。状態をターン終了時点の値へ一度に進め、演出用の出来事列を返す。
    /// </summary>
    public static class BattleResolver
    {
        public const int PhysicalPercentMin = 88;
        public const int PhysicalPercentMax = 112;
        public const int SpellPercentMin = 90;
        public const int SpellPercentMax = 110;

        public static int ComputePhysicalDamage(int atk, int def, bool isTargetDefending, GameRng rng)
        {
            if (rng == null)
            {
                throw new ArgumentNullException(nameof(rng));
            }

            var baseDamage = Math.Max(0L, ((long)atk / 2) - (def / 4));
            var damage = Math.Max(1L, baseDamage * rng.Range(PhysicalPercentMin, PhysicalPercentMax) / 100);
            return FinishDamage(damage, isTargetDefending);
        }

        public static int ComputeSpellDamage(SpellInfo spell, SpellElement targetWeakness, bool isTargetDefending, GameRng rng, out bool hitWeakness)
        {
            if (spell == null)
            {
                throw new ArgumentNullException(nameof(spell));
            }

            if (rng == null)
            {
                throw new ArgumentNullException(nameof(rng));
            }

            var damage = Math.Max(1L, (long)spell.Power * rng.Range(SpellPercentMin, SpellPercentMax) / 100);
            hitWeakness = spell.Element != SpellElement.None && spell.Element == targetWeakness;
            if (hitWeakness)
            {
                damage = damage * 3 / 2;
            }

            return FinishDamage(damage, isTargetDefending);
        }

        /// <summary>防御で半減し（最低 1）、int に収める。</summary>
        private static int FinishDamage(long damage, bool isTargetDefending)
        {
            if (isTargetDefending)
            {
                damage = Math.Max(1L, damage / 2);
            }

            return (int)Math.Min(int.MaxValue, damage);
        }

        public static int ComputeFleePercent(GameData data, BattleState battle, Party party)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (battle == null)
            {
                throw new ArgumentNullException(nameof(battle));
            }

            if (party == null)
            {
                throw new ArgumentNullException(nameof(party));
            }

            var partyAgiSum = 0;
            var partyAlive = 0;
            for (var i = 0; i < party.Count; i++)
            {
                if (party[i].IsAlive)
                {
                    partyAgiSum += party[i].Agi;
                    partyAlive++;
                }
            }

            var enemyAgiSum = 0;
            var enemyAlive = 0;
            for (var i = 0; i < battle.Enemies.Count; i++)
            {
                if (battle.Enemies[i].IsAlive)
                {
                    enemyAgiSum += battle.Enemies[i].Info.Agi;
                    enemyAlive++;
                }
            }

            var partyAvg = partyAlive > 0 ? Math.Max(1, partyAgiSum / partyAlive) : 1;
            var enemyAvg = enemyAlive > 0 ? Math.Max(1, enemyAgiSum / enemyAlive) : 1;
            var raw = (partyAvg * data.Rules.FleeBasePercent) / enemyAvg;
            return Math.Clamp(raw, data.Rules.FleeMinPercent, data.Rules.FleeMaxPercent);
        }

        public static IReadOnlyList<BattleEvent> ResolveTurn(
            GameData data,
            BattleState battle,
            Party party,
            Inventory inventory,
            IReadOnlyList<BattleCommand> commands,
            GameRng rng,
            IReadOnlyList<EnemyDecision?>? enemyOverrides = null)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (battle == null)
            {
                throw new ArgumentNullException(nameof(battle));
            }

            if (party == null)
            {
                throw new ArgumentNullException(nameof(party));
            }

            if (inventory == null)
            {
                throw new ArgumentNullException(nameof(inventory));
            }

            if (commands == null)
            {
                throw new ArgumentNullException(nameof(commands));
            }

            if (rng == null)
            {
                throw new ArgumentNullException(nameof(rng));
            }

            if (battle.Outcome != BattleOutcome.Ongoing)
            {
                throw new InvalidOperationException(CommandErrorText.Describe(CommandError.BattleAlreadyEnded));
            }

            if (commands.Count != party.Count)
            {
                throw new ArgumentException("コマンド数がパーティ人数と一致しません。", nameof(commands));
            }

            var hasFlee = false;
            for (var i = 0; i < commands.Count; i++)
            {
                if (party[i].IsAlive && commands[i].Kind == BattleCommandKind.Flee)
                {
                    if (battle.IsBossBattle)
                    {
                        throw new InvalidOperationException(CommandErrorText.Describe(CommandError.CannotFleeBoss));
                    }

                    hasFlee = true;
                }
            }

            if (!hasFlee)
            {
                for (var i = 0; i < commands.Count; i++)
                {
                    var cmd = commands[i];
                    if (!party[i].IsAlive && cmd.Kind != BattleCommandKind.None)
                    {
                        throw new InvalidOperationException(CommandErrorText.Describe(CommandError.ActorDead));
                    }

                    if (party[i].IsAlive && cmd.Kind == BattleCommandKind.None)
                    {
                        throw new InvalidOperationException(CommandErrorText.Describe(CommandError.EmptyCommand));
                    }
                }
            }

            var events = new List<BattleEvent>(16);
            battle.BeginTurn();
            events.Add(new BattleEvent(BattleEventKind.TurnStarted, amount: battle.Turn, message: $"ターン {battle.Turn}"));

            if (hasFlee)
            {
                var chance = ComputeFleePercent(data, battle, party);
                if (rng.Next(100) < chance)
                {
                    battle.MarkFled();
                    events.Add(new BattleEvent(BattleEventKind.FleeSucceeded, message: "うまく逃げ切った！"));
                    return events.AsReadOnly();
                }

                events.Add(new BattleEvent(BattleEventKind.FleeFailed, message: "回り込まれて逃げられない！"));
                var enemyOrder = RollTurnOrder(party, battle, commands, includeAllies: false, rng);
                ExecuteScheduled(data, battle, party, inventory, commands, enemyOrder, rng, enemyOverrides, events);
                return events.AsReadOnly();
            }

            // ぼうぎょはターン先頭で効く
            for (var i = 0; i < party.Count; i++)
            {
                if (party[i].IsAlive && commands[i].Kind == BattleCommandKind.Defend)
                {
                    battle.SetAllyDefending(i, true);
                    events.Add(new BattleEvent(
                        BattleEventKind.DefendStance,
                        actorSide: BattleSide.Ally,
                        actorIndex: i,
                        message: $"{party[i].Name}は身を固めている！"));
                }
            }

            var schedule = RollTurnOrder(party, battle, commands, includeAllies: true, rng);
            ExecuteScheduled(data, battle, party, inventory, commands, schedule, rng, enemyOverrides, events);
            return events.AsReadOnly();
        }

        private static List<(BattleSide Side, int Index, int Score)> RollTurnOrder(
            Party party,
            BattleState battle,
            IReadOnlyList<BattleCommand> commands,
            bool includeAllies,
            GameRng rng)
        {
            var list = new List<(BattleSide Side, int Index, int Score)>(party.Count + battle.Enemies.Count);
            if (includeAllies)
            {
                for (var i = 0; i < party.Count; i++)
                {
                    var cmd = commands[i];
                    if (!party[i].IsAlive || cmd.Kind == BattleCommandKind.None || cmd.Kind == BattleCommandKind.Defend)
                    {
                        continue;
                    }

                    var agi = party[i].Agi;
                    var jitter = agi / 4;
                    var score = agi + (jitter > 0 ? rng.Range(0, jitter) : 0);
                    list.Add((BattleSide.Ally, i, score));
                }
            }

            for (var i = 0; i < battle.Enemies.Count; i++)
            {
                if (!battle.Enemies[i].IsAlive)
                {
                    continue;
                }

                var agi = battle.Enemies[i].Info.Agi;
                var jitter = agi / 4;
                var score = agi + (jitter > 0 ? rng.Range(0, jitter) : 0);
                list.Add((BattleSide.Enemy, i, score));
            }

            list.Sort((a, b) =>
            {
                var c = b.Score.CompareTo(a.Score);
                if (c != 0)
                {
                    return c;
                }

                c = ((int)a.Side).CompareTo((int)b.Side);
                return c != 0 ? c : a.Index.CompareTo(b.Index);
            });

            return list;
        }

        private static void ExecuteScheduled(
            GameData data,
            BattleState battle,
            Party party,
            Inventory inventory,
            IReadOnlyList<BattleCommand> commands,
            List<(BattleSide Side, int Index, int Score)> schedule,
            GameRng rng,
            IReadOnlyList<EnemyDecision?>? enemyOverrides,
            List<BattleEvent> events)
        {
            for (var i = 0; i < schedule.Count; i++)
            {
                if (battle.Outcome != BattleOutcome.Ongoing)
                {
                    break;
                }

                var entry = schedule[i];
                if (entry.Side == BattleSide.Ally)
                {
                    if (!party[entry.Index].IsAlive)
                    {
                        continue;
                    }

                    ExecuteAllyAction(data, battle, party, inventory, entry.Index, commands[entry.Index], rng, events);
                }
                else
                {
                    if (!battle.Enemies[entry.Index].IsAlive)
                    {
                        continue;
                    }

                    var decision = enemyOverrides != null && entry.Index < enemyOverrides.Count && enemyOverrides[entry.Index].HasValue
                        ? enemyOverrides[entry.Index]!.Value
                        : EnemyAi.ChooseAction(data, battle, party, entry.Index, rng);
                    ExecuteEnemyAction(data, battle, party, entry.Index, decision, rng, events);
                }
            }
        }

        private static void ExecuteAllyAction(
            GameData data,
            BattleState battle,
            Party party,
            Inventory inventory,
            int actorIndex,
            BattleCommand cmd,
            GameRng rng,
            List<BattleEvent> events)
        {
            var actor = party[actorIndex];
            switch (cmd.Kind)
            {
                case BattleCommandKind.Attack:
                    {
                        events.Add(new BattleEvent(
                            BattleEventKind.ActionDeclared,
                            BattleSide.Ally,
                            actorIndex,
                            message: $"{actor.Name}の攻撃！"));
                        var target = battle.ResolveSubGroupTarget(cmd.TargetIndex);
                        if (target < 0)
                        {
                            events.Add(new BattleEvent(
                                BattleEventKind.ActionNoEffect,
                                BattleSide.Ally,
                                actorIndex,
                                message: "しかし相手はすでにいなかった！"));
                            return;
                        }

                        var enemy = battle.Enemies[target];
                        var dmg = ComputePhysicalDamage(actor.Atk, enemy.Info.Def, false, rng);
                        var actual = enemy.TakeDamage(dmg);
                        events.Add(new BattleEvent(
                            BattleEventKind.DamageDealt,
                            BattleSide.Ally,
                            actorIndex,
                            BattleSide.Enemy,
                            target,
                            actual,
                            message: $"{enemy.DisplayName}に {actual} のダメージ！"));
                        if (!enemy.IsAlive)
                        {
                            events.Add(new BattleEvent(
                                BattleEventKind.UnitFallen,
                                BattleSide.Ally,
                                actorIndex,
                                BattleSide.Enemy,
                                target,
                                message: $"{enemy.DisplayName}を倒した！"));
                            if (battle.AllEnemiesDead)
                            {
                                AwardVictory(battle, party, events);
                            }
                        }

                        break;
                    }

                case BattleCommandKind.Spell:
                    {
                        if (!data.TryGetSpell(cmd.ActionId, out var spell) || !actor.KnowsSpell(cmd.ActionId))
                        {
                            events.Add(new BattleEvent(
                                BattleEventKind.ActionNoEffect,
                                BattleSide.Ally,
                                actorIndex,
                                actionId: cmd.ActionId,
                                message: $"{actor.Name}は呪文を唱えられなかった！"));
                            return;
                        }

                        events.Add(new BattleEvent(
                            BattleEventKind.ActionDeclared,
                            BattleSide.Ally,
                            actorIndex,
                            actionId: spell!.Id,
                            message: $"{actor.Name}は {spell.Name} を唱えた！"));

                        if (actor.Mp < spell.MpCost)
                        {
                            events.Add(new BattleEvent(
                                BattleEventKind.InsufficientMp,
                                BattleSide.Ally,
                                actorIndex,
                                actionId: spell.Id,
                                message: "しかしMPが足りない！"));
                            return;
                        }

                        switch (spell.Kind)
                        {
                            case SpellKind.SingleAttack:
                                {
                                    var target = battle.ResolveSubGroupTarget(cmd.TargetIndex);
                                    if (target < 0)
                                    {
                                        events.Add(new BattleEvent(
                                            BattleEventKind.ActionNoEffect,
                                            BattleSide.Ally,
                                            actorIndex,
                                            actionId: spell.Id,
                                            message: "しかし対象はいなかった！"));
                                        return;
                                    }

                                    actor.SpendMp(spell.MpCost);
                                    events.Add(new BattleEvent(BattleEventKind.MpSpent, BattleSide.Ally, actorIndex, BattleSide.Ally, actorIndex, spell.MpCost, actionId: spell.Id));

                                    var enemy = battle.Enemies[target];
                                    var dmg = ComputeSpellDamage(spell, enemy.Info.WeakElement, false, rng, out var weak);
                                    var actual = enemy.TakeDamage(dmg);
                                    var suffix = weak ? "（弱点！）" : string.Empty;
                                    events.Add(new BattleEvent(
                                        BattleEventKind.DamageDealt,
                                        BattleSide.Ally,
                                        actorIndex,
                                        BattleSide.Enemy,
                                        target,
                                        actual,
                                        actionId: spell.Id,
                                        hitWeakness: weak,
                                        message: $"{enemy.DisplayName}に {actual} のダメージ！{suffix}"));
                                    if (!enemy.IsAlive)
                                    {
                                        events.Add(new BattleEvent(
                                            BattleEventKind.UnitFallen,
                                            BattleSide.Ally,
                                            actorIndex,
                                            BattleSide.Enemy,
                                            target,
                                            message: $"{enemy.DisplayName}を倒した！"));
                                        if (battle.AllEnemiesDead)
                                        {
                                            AwardVictory(battle, party, events);
                                        }
                                    }

                                    break;
                                }

                            case SpellKind.GroupAttack:
                                {
                                    if (cmd.TargetIndex < 0 || cmd.TargetIndex >= battle.Enemies.Count)
                                    {
                                        events.Add(new BattleEvent(BattleEventKind.ActionNoEffect, BattleSide.Ally, actorIndex, actionId: spell.Id, message: "しかし対象はいなかった！"));
                                        return;
                                    }

                                    var subGroup = battle.Enemies[cmd.TargetIndex].SubGroup;
                                    var targets = new List<int>(battle.Enemies.Count);
                                    for (var k = 0; k < battle.Enemies.Count; k++)
                                    {
                                        if (battle.Enemies[k].SubGroup == subGroup && battle.Enemies[k].IsAlive)
                                        {
                                            targets.Add(k);
                                        }
                                    }

                                    if (targets.Count == 0)
                                    {
                                        events.Add(new BattleEvent(
                                            BattleEventKind.ActionNoEffect,
                                            BattleSide.Ally,
                                            actorIndex,
                                            actionId: spell.Id,
                                            message: "しかし対象はいなかった！"));
                                        return;
                                    }

                                    actor.SpendMp(spell.MpCost);
                                    events.Add(new BattleEvent(BattleEventKind.MpSpent, BattleSide.Ally, actorIndex, BattleSide.Ally, actorIndex, spell.MpCost, actionId: spell.Id));

                                    for (var k = 0; k < targets.Count; k++)
                                    {
                                        var idx = targets[k];
                                        var enemy = battle.Enemies[idx];
                                        var dmg = ComputeSpellDamage(spell, enemy.Info.WeakElement, false, rng, out var weak);
                                        var actual = enemy.TakeDamage(dmg);
                                        var suffix = weak ? "（弱点！）" : string.Empty;
                                        events.Add(new BattleEvent(
                                            BattleEventKind.DamageDealt,
                                            BattleSide.Ally,
                                            actorIndex,
                                            BattleSide.Enemy,
                                            idx,
                                            actual,
                                            actionId: spell.Id,
                                            hitWeakness: weak,
                                            message: $"{enemy.DisplayName}に {actual} のダメージ！{suffix}"));
                                        if (!enemy.IsAlive)
                                        {
                                            events.Add(new BattleEvent(
                                                BattleEventKind.UnitFallen,
                                                BattleSide.Ally,
                                                actorIndex,
                                                BattleSide.Enemy,
                                                idx,
                                                message: $"{enemy.DisplayName}を倒した！"));
                                            if (battle.AllEnemiesDead)
                                            {
                                                AwardVictory(battle, party, events);
                                                return;
                                            }
                                        }
                                    }

                                    break;
                                }

                            case SpellKind.SingleHeal:
                                {
                                    if (cmd.TargetIndex < 0 || cmd.TargetIndex >= party.Count || !party[cmd.TargetIndex].IsAlive)
                                    {
                                        events.Add(new BattleEvent(
                                            BattleEventKind.ActionNoEffect,
                                            BattleSide.Ally,
                                            actorIndex,
                                            BattleSide.Ally,
                                            cmd.TargetIndex,
                                            actionId: spell.Id,
                                            message: "しかし効果がなかった！"));
                                        return;
                                    }

                                    actor.SpendMp(spell.MpCost);
                                    events.Add(new BattleEvent(BattleEventKind.MpSpent, BattleSide.Ally, actorIndex, BattleSide.Ally, actorIndex, spell.MpCost, actionId: spell.Id));
                                    var target = party[cmd.TargetIndex];
                                    var healed = target.HealHp(spell.Power);
                                    events.Add(new BattleEvent(
                                        BattleEventKind.HpHealed,
                                        BattleSide.Ally,
                                        actorIndex,
                                        BattleSide.Ally,
                                        cmd.TargetIndex,
                                        healed,
                                        actionId: spell.Id,
                                        message: $"{target.Name}のHPが {healed} 回復した！"));
                                    break;
                                }

                            case SpellKind.AllHeal:
                                {
                                    actor.SpendMp(spell.MpCost);
                                    events.Add(new BattleEvent(BattleEventKind.MpSpent, BattleSide.Ally, actorIndex, BattleSide.Ally, actorIndex, spell.MpCost, actionId: spell.Id));
                                    for (var k = 0; k < party.Count; k++)
                                    {
                                        if (!party[k].IsAlive)
                                        {
                                            continue;
                                        }

                                        var healed = party[k].HealHp(spell.Power);
                                        events.Add(new BattleEvent(
                                            BattleEventKind.HpHealed,
                                            BattleSide.Ally,
                                            actorIndex,
                                            BattleSide.Ally,
                                            k,
                                            healed,
                                            actionId: spell.Id,
                                            message: $"{party[k].Name}のHPが {healed} 回復した！"));
                                    }

                                    break;
                                }

                            case SpellKind.Revive:
                                {
                                    if (cmd.TargetIndex < 0 || cmd.TargetIndex >= party.Count || party[cmd.TargetIndex].IsAlive)
                                    {
                                        events.Add(new BattleEvent(
                                            BattleEventKind.ActionNoEffect,
                                            BattleSide.Ally,
                                            actorIndex,
                                            BattleSide.Ally,
                                            cmd.TargetIndex,
                                            actionId: spell.Id,
                                            message: "しかし効果がなかった！"));
                                        return;
                                    }

                                    actor.SpendMp(spell.MpCost);
                                    events.Add(new BattleEvent(BattleEventKind.MpSpent, BattleSide.Ally, actorIndex, BattleSide.Ally, actorIndex, spell.MpCost, actionId: spell.Id));
                                    var target = party[cmd.TargetIndex];
                                    var hpAmount = Math.Max(1, (target.MaxHp * spell.Power) / 100);
                                    var revivedHp = target.Revive(hpAmount);
                                    events.Add(new BattleEvent(
                                        BattleEventKind.UnitRevived,
                                        BattleSide.Ally,
                                        actorIndex,
                                        BattleSide.Ally,
                                        cmd.TargetIndex,
                                        revivedHp,
                                        actionId: spell.Id,
                                        message: $"{target.Name}が息を吹き返した！"));
                                    break;
                                }
                        }

                        break;
                    }

                case BattleCommandKind.Item:
                    {
                        if (!data.TryGetItem(cmd.ActionId, out var item))
                        {
                            events.Add(new BattleEvent(BattleEventKind.ActionNoEffect, BattleSide.Ally, actorIndex, message: "道具が見つからない！"));
                            return;
                        }

                        events.Add(new BattleEvent(
                            BattleEventKind.ActionDeclared,
                            BattleSide.Ally,
                            actorIndex,
                            actionId: item!.Id,
                            message: $"{actor.Name}は {item.Name} を使った！"));

                        if (inventory.Count(item.Id) <= 0)
                        {
                            events.Add(new BattleEvent(
                                BattleEventKind.ActionNoEffect,
                                BattleSide.Ally,
                                actorIndex,
                                actionId: item.Id,
                                message: $"しかし {item.Name} は残っていなかった！"));
                            return;
                        }

                        if (cmd.TargetIndex < 0 || cmd.TargetIndex >= party.Count || !party[cmd.TargetIndex].IsAlive)
                        {
                            events.Add(new BattleEvent(
                                BattleEventKind.ActionNoEffect,
                                BattleSide.Ally,
                                actorIndex,
                                BattleSide.Ally,
                                cmd.TargetIndex,
                                actionId: item.Id,
                                message: "しかし効果がなかった！"));
                            return;
                        }

                        inventory.Remove(item.Id, 1);
                        events.Add(new BattleEvent(BattleEventKind.ItemConsumed, BattleSide.Ally, actorIndex, amount: 1, actionId: item.Id));
                        var target = party[cmd.TargetIndex];
                        if (item.Kind == ItemKind.HealHp)
                        {
                            var healed = target.HealHp(item.Power);
                            events.Add(new BattleEvent(
                                BattleEventKind.HpHealed,
                                BattleSide.Ally,
                                actorIndex,
                                BattleSide.Ally,
                                cmd.TargetIndex,
                                healed,
                                actionId: item.Id,
                                message: $"{target.Name}のHPが {healed} 回復した！"));
                        }
                        else
                        {
                            var restored = target.RestoreMp(item.Power);
                            events.Add(new BattleEvent(
                                BattleEventKind.MpRestored,
                                BattleSide.Ally,
                                actorIndex,
                                BattleSide.Ally,
                                cmd.TargetIndex,
                                restored,
                                actionId: item.Id,
                                message: $"{target.Name}のMPが {restored} 回復した！"));
                        }

                        break;
                    }
            }
        }

        private static void ExecuteEnemyAction(
            GameData data,
            BattleState battle,
            Party party,
            int enemyIndex,
            EnemyDecision decision,
            GameRng rng,
            List<BattleEvent> events)
        {
            var enemy = battle.Enemies[enemyIndex];
            if (decision.Kind == EnemyActionKind.Spell)
            {
                if (!data.TryGetSpell(decision.SpellId, out var spell))
                {
                    return;
                }

                events.Add(new BattleEvent(
                    BattleEventKind.ActionDeclared,
                    BattleSide.Enemy,
                    enemyIndex,
                    actionId: spell!.Id,
                    message: $"{enemy.DisplayName}は {spell.Name} を唱えた！"));

                if (enemy.Mp < spell.MpCost)
                {
                    events.Add(new BattleEvent(
                        BattleEventKind.InsufficientMp,
                        BattleSide.Enemy,
                        enemyIndex,
                        actionId: spell.Id,
                        message: "しかしMPが足りない！"));
                    return;
                }

                if (spell.Kind == SpellKind.SingleHeal)
                {
                    var targetIdx = decision.TargetIndex >= 0 && decision.TargetIndex < battle.Enemies.Count && battle.Enemies[decision.TargetIndex].IsAlive
                        ? decision.TargetIndex
                        : enemyIndex;
                    enemy.SpendMp(spell.MpCost);
                    events.Add(new BattleEvent(BattleEventKind.MpSpent, BattleSide.Enemy, enemyIndex, BattleSide.Enemy, enemyIndex, spell.MpCost, actionId: spell.Id));
                    var healed = battle.Enemies[targetIdx].HealHp(spell.Power);
                    events.Add(new BattleEvent(
                        BattleEventKind.HpHealed,
                        BattleSide.Enemy,
                        enemyIndex,
                        BattleSide.Enemy,
                        targetIdx,
                        healed,
                        actionId: spell.Id,
                        message: $"{battle.Enemies[targetIdx].DisplayName}の傷が {healed} 癒えた！"));
                    return;
                }

                if (spell.Kind == SpellKind.AllHeal)
                {
                    enemy.SpendMp(spell.MpCost);
                    events.Add(new BattleEvent(BattleEventKind.MpSpent, BattleSide.Enemy, enemyIndex, BattleSide.Enemy, enemyIndex, spell.MpCost, actionId: spell.Id));
                    for (var k = 0; k < battle.Enemies.Count; k++)
                    {
                        if (!battle.Enemies[k].IsAlive)
                        {
                            continue;
                        }

                        var healed = battle.Enemies[k].HealHp(spell.Power);
                        events.Add(new BattleEvent(
                            BattleEventKind.HpHealed,
                            BattleSide.Enemy,
                            enemyIndex,
                            BattleSide.Enemy,
                            k,
                            healed,
                            actionId: spell.Id,
                            message: $"{battle.Enemies[k].DisplayName}の傷が {healed} 癒えた！"));
                    }

                    return;
                }

                if (spell.Kind == SpellKind.GroupAttack)
                {
                    enemy.SpendMp(spell.MpCost);
                    events.Add(new BattleEvent(BattleEventKind.MpSpent, BattleSide.Enemy, enemyIndex, BattleSide.Enemy, enemyIndex, spell.MpCost, actionId: spell.Id));
                    for (var k = 0; k < party.Count; k++)
                    {
                        if (!party[k].IsAlive)
                        {
                            continue;
                        }

                        var guarded = battle.AllyDefending[k];
                        var dmg = ComputeSpellDamage(spell, SpellElement.None, guarded, rng, out _);
                        var actual = party[k].TakeDamage(dmg);
                        events.Add(new BattleEvent(
                            BattleEventKind.DamageDealt,
                            BattleSide.Enemy,
                            enemyIndex,
                            BattleSide.Ally,
                            k,
                            actual,
                            actionId: spell.Id,
                            wasGuarded: guarded,
                            message: $"{party[k].Name}は {actual} のダメージを受けた！"));
                        if (!party[k].IsAlive)
                        {
                            events.Add(new BattleEvent(
                                BattleEventKind.UnitFallen,
                                BattleSide.Enemy,
                                enemyIndex,
                                BattleSide.Ally,
                                k,
                                message: $"{party[k].Name}は倒れた…"));
                            if (party.IsWipedOut)
                            {
                                battle.MarkPartyWiped();
                                events.Add(new BattleEvent(BattleEventKind.PartyWiped, message: "パーティは全滅してしまった…"));
                                return;
                            }
                        }
                    }

                    return;
                }

                // SingleAttack
                var allyTarget = ResolveLivingAllyTarget(party, decision.TargetIndex, rng);
                if (allyTarget < 0)
                {
                    return;
                }

                enemy.SpendMp(spell.MpCost);
                events.Add(new BattleEvent(BattleEventKind.MpSpent, BattleSide.Enemy, enemyIndex, BattleSide.Enemy, enemyIndex, spell.MpCost, actionId: spell.Id));
                var allyGuarded = battle.AllyDefending[allyTarget];
                var singleDmg = ComputeSpellDamage(spell, SpellElement.None, allyGuarded, rng, out _);
                var singleActual = party[allyTarget].TakeDamage(singleDmg);
                events.Add(new BattleEvent(
                    BattleEventKind.DamageDealt,
                    BattleSide.Enemy,
                    enemyIndex,
                    BattleSide.Ally,
                    allyTarget,
                    singleActual,
                    actionId: spell.Id,
                    wasGuarded: allyGuarded,
                    message: $"{party[allyTarget].Name}は {singleActual} のダメージを受けた！"));
                if (!party[allyTarget].IsAlive)
                {
                    events.Add(new BattleEvent(
                        BattleEventKind.UnitFallen,
                        BattleSide.Enemy,
                        enemyIndex,
                        BattleSide.Ally,
                        allyTarget,
                        message: $"{party[allyTarget].Name}は倒れた…"));
                    if (party.IsWipedOut)
                    {
                        battle.MarkPartyWiped();
                        events.Add(new BattleEvent(BattleEventKind.PartyWiped, message: "パーティは全滅してしまった…"));
                    }
                }

                return;
            }

            // 通常攻撃 / 強攻撃
            var targetAlly = ResolveLivingAllyTarget(party, decision.TargetIndex, rng);
            if (targetAlly < 0)
            {
                return;
            }

            var heavy = decision.Kind == EnemyActionKind.HeavyAttack;
            events.Add(new BattleEvent(
                BattleEventKind.ActionDeclared,
                BattleSide.Enemy,
                enemyIndex,
                message: heavy ? $"{enemy.DisplayName}の痛烈な一撃！" : $"{enemy.DisplayName}の攻撃！"));
            var atkPower = heavy ? (enemy.Info.Atk * 3) / 2 : enemy.Info.Atk;
            var isGuarded = battle.AllyDefending[targetAlly];
            var physDmg = ComputePhysicalDamage(atkPower, party[targetAlly].Def, isGuarded, rng);
            var dealt = party[targetAlly].TakeDamage(physDmg);
            events.Add(new BattleEvent(
                BattleEventKind.DamageDealt,
                BattleSide.Enemy,
                enemyIndex,
                BattleSide.Ally,
                targetAlly,
                dealt,
                wasGuarded: isGuarded,
                message: $"{party[targetAlly].Name}は {dealt} のダメージを受けた！"));
            if (!party[targetAlly].IsAlive)
            {
                events.Add(new BattleEvent(
                    BattleEventKind.UnitFallen,
                    BattleSide.Enemy,
                    enemyIndex,
                    BattleSide.Ally,
                    targetAlly,
                    message: $"{party[targetAlly].Name}は倒れた…"));
                if (party.IsWipedOut)
                {
                    battle.MarkPartyWiped();
                    events.Add(new BattleEvent(BattleEventKind.PartyWiped, message: "パーティは全滅してしまった…"));
                }
            }
        }

        private static int ResolveLivingAllyTarget(Party party, int preferred, GameRng rng)
        {
            if (preferred >= 0 && preferred < party.Count && party[preferred].IsAlive)
            {
                return preferred;
            }

            var living = new List<int>(party.Count);
            for (var i = 0; i < party.Count; i++)
            {
                if (party[i].IsAlive)
                {
                    living.Add(i);
                }
            }

            return living.Count == 0 ? -1 : living[rng.Next(living.Count)];
        }

        private static void AwardVictory(BattleState battle, Party party, List<BattleEvent> events)
        {
            // 1 体ずつは int に収まっても合計は収まらないことがあるので、long で足して飽和させる。
            // 経験値はレベル上限、ゴールドは所持上限でさらに飽和する。
            var expSum = 0L;
            var goldSum = 0L;
            for (var i = 0; i < battle.Enemies.Count; i++)
            {
                expSum += battle.Enemies[i].Info.ExpReward;
                goldSum += battle.Enemies[i].Info.GoldReward;
            }

            var exp = (int)Math.Min(int.MaxValue, expSum);
            var gold = (int)Math.Min(int.MaxValue, goldSum);

            battle.MarkVictory(exp, gold);
            events.Add(new BattleEvent(
                BattleEventKind.Victory,
                amount: exp,
                secondaryAmount: gold,
                message: $"魔物を打ち払った！ 経験値 {exp}、{gold}G を得た！"));

            for (var i = 0; i < party.Count; i++)
            {
                if (!party[i].IsAlive || exp <= 0)
                {
                    continue;
                }

                var beforeExp = party[i].Exp;
                var gains = party[i].AddExp(i, exp);
                var deltaExp = party[i].Exp - beforeExp;
                events.Add(new BattleEvent(
                    BattleEventKind.ExpAwarded,
                    BattleSide.Ally,
                    i,
                    BattleSide.Ally,
                    i,
                    deltaExp));
                for (var g = 0; g < gains.Count; g++)
                {
                    var gain = gains[g];
                    var learnedText = gain.LearnedSpells.Count > 0 ? $" 新たな呪文を覚えた！" : string.Empty;
                    events.Add(new BattleEvent(
                        BattleEventKind.LevelUp,
                        BattleSide.Ally,
                        i,
                        BattleSide.Ally,
                        i,
                        gain.NewLevel,
                        levelUp: gain,
                        message: $"{party[i].Name}は レベル {gain.NewLevel} に上がった！{learnedText}"));
                }
            }
        }
    }
}
