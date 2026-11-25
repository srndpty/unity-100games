using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    /// <summary>
    /// プレイヤーと同じボタン入力だけで町・坑道探索・育成・買物・魔将討伐まで完走する自動操縦役。
    /// 判断を現在の <see cref="QuestFlow"/> の状態だけから決めるため、試合をまたいで状態を持ち越さない。
    /// </summary>
    public static class Autopilot
    {
        public static void Step(QuestFlow flow)
        {
            if (flow == null)
            {
                throw new ArgumentNullException(nameof(flow));
            }

            IssueInput(flow);
            flow.StepTicks(1);
        }

        private static void IssueInput(QuestFlow flow)
        {
            if (flow.IsPaused)
            {
                flow.TapButton(InputButton.Pause);
                return;
            }

            switch (flow.Screen)
            {
                case QuestScreen.Title:
                    DriveTitle(flow);
                    break;

                case QuestScreen.HowTo:
                case QuestScreen.Dialogue:
                case QuestScreen.Ending:
                    flow.TapButton(InputButton.Confirm);
                    break;

                case QuestScreen.Battle:
                    DriveBattle(flow);
                    break;

                case QuestScreen.Inn:
                    DriveInn(flow);
                    break;

                case QuestScreen.Church:
                    DriveChurch(flow);
                    break;

                case QuestScreen.Shop:
                    DriveShop(flow);
                    break;

                case QuestScreen.CampMenu:
                    DriveCampMenu(flow);
                    break;

                case QuestScreen.Field:
                    DriveField(flow);
                    break;
            }
        }

        private static void DriveTitle(QuestFlow flow)
        {
            for (var i = 0; i < flow.TitleOptions.Count; i++)
            {
                if (flow.TitleOptions[i] == TitleOptionKind.NewGame)
                {
                    if (flow.TitleCursor != i)
                    {
                        flow.TapButton(InputButton.Down);
                    }
                    else
                    {
                        flow.TapButton(InputButton.Confirm);
                    }

                    return;
                }
            }

            flow.TapButton(InputButton.Confirm);
        }

        private static void DriveInn(QuestFlow flow)
        {
            var world = flow.World!;
            if (NeedsInnRest(world) && world.Gold >= flow.Data.Rules.InnCost)
            {
                if (flow.InnCursor != 0)
                {
                    flow.TapButton(InputButton.Up);
                }
                else
                {
                    flow.TapButton(InputButton.Confirm);
                }
            }
            else
            {
                flow.TapButton(InputButton.Cancel);
            }
        }

        private static void DriveChurch(QuestFlow flow)
        {
            var world = flow.World!;
            var fallen = flow.GetFallenMemberIndices();
            if (fallen.Count > 0)
            {
                var cost = flow.Data.Rules.ReviveCostPerLevel * world.Party[fallen[0]].Level;
                if (world.Gold >= cost)
                {
                    if (flow.ChurchMode == ChurchSubMode.Root)
                    {
                        if (flow.ChurchRootCursor != 1)
                        {
                            flow.TapButton(InputButton.Down);
                        }
                        else
                        {
                            flow.TapButton(InputButton.Confirm);
                        }
                    }
                    else
                    {
                        flow.TapButton(InputButton.Confirm);
                    }

                    return;
                }
            }

            flow.TapButton(InputButton.Cancel);
        }

        private static void DriveShop(QuestFlow flow)
        {
            var world = flow.World!;
            var shop = flow.ActiveShop;
            if (shop == null || !TryPickShopPurchase(world, shop, out var wantId, out var equipMember))
            {
                flow.TapButton(InputButton.Cancel);
                return;
            }

            switch (flow.ShopMode)
            {
                case ShopSubMode.Root:
                    if (flow.ShopRootCursor != 0)
                    {
                        flow.TapButton(InputButton.Up);
                    }
                    else
                    {
                        flow.TapButton(InputButton.Confirm);
                    }

                    break;

                case ShopSubMode.BuySelect:
                    {
                        var opts = flow.GetShopBuyOptions();
                        var wantIdx = FindOptionIndex(opts, wantId);
                        if (wantIdx < 0)
                        {
                            flow.TapButton(InputButton.Cancel);
                        }
                        else if (flow.ShopSubCursor != wantIdx)
                        {
                            flow.TapButton(InputButton.Down);
                        }
                        else
                        {
                            flow.TapButton(InputButton.Confirm);
                        }

                        break;
                    }

                case ShopSubMode.BuyEquipTarget:
                    {
                        var targets = flow.GetShopEquipTargetOptions();
                        var targetKey = equipMember.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        var wantIdx = FindOptionIndex(targets, targetKey);
                        if (wantIdx < 0)
                        {
                            wantIdx = 0;
                        }

                        if (flow.ShopTargetCursor != wantIdx)
                        {
                            flow.TapButton(InputButton.Down);
                        }
                        else
                        {
                            flow.TapButton(InputButton.Confirm);
                        }

                        break;
                    }

                default:
                    flow.TapButton(InputButton.Cancel);
                    break;
            }
        }

        private static void DriveCampMenu(QuestFlow flow)
        {
            var world = flow.World!;
            if (TryFindBetterBagEquip(world, out var eqMember, out var eqSlot, out var eqId))
            {
                switch (flow.CampMode)
                {
                    case CampSubMode.Root:
                        MoveOrConfirm(flow, flow.CampRootCursor, 3);
                        return;
                    case CampSubMode.EquipMember:
                        MoveOrConfirm(flow, flow.CampMemberCursor, eqMember);
                        return;
                    case CampSubMode.EquipSlot:
                        if (flow.CampMemberCursor != eqMember)
                        {
                            flow.TapButton(InputButton.Cancel);
                        }
                        else if (flow.SelectedEquipSlot != eqSlot)
                        {
                            flow.TapButton(InputButton.Down);
                        }
                        else
                        {
                            flow.TapButton(InputButton.Confirm);
                        }

                        return;
                    case CampSubMode.EquipCandidate:
                        {
                            if (flow.CampMemberCursor != eqMember || flow.SelectedEquipSlot != eqSlot)
                            {
                                flow.TapButton(InputButton.Cancel);
                                return;
                            }

                            var opts = flow.GetEquipCandidateOptions();
                            var idx = FindOptionIndex(opts, eqId);
                            if (idx < 0)
                            {
                                flow.TapButton(InputButton.Cancel);
                            }
                            else
                            {
                                MoveOrConfirm(flow, flow.CampSubCursor, idx);
                            }

                            return;
                        }

                    default:
                        flow.TapButton(InputButton.Cancel);
                        return;
                }
            }

            if (TryPickFieldCare(world, out var useSpell, out var casterIdx, out var actionId, out var targetIdx))
            {
                if (useSpell)
                {
                    switch (flow.CampMode)
                    {
                        case CampSubMode.Root:
                            MoveOrConfirm(flow, flow.CampRootCursor, 1);
                            return;
                        case CampSubMode.SpellCaster:
                            MoveOrConfirm(flow, flow.CampMemberCursor, casterIdx);
                            return;
                        case CampSubMode.SpellSelect:
                            {
                                if (flow.CampMemberCursor != casterIdx)
                                {
                                    flow.TapButton(InputButton.Cancel);
                                    return;
                                }

                                var opts = flow.GetCampSpellOptions();
                                var idx = FindOptionIndex(opts, actionId);
                                if (idx < 0 || !opts[idx].Enabled)
                                {
                                    flow.TapButton(InputButton.Cancel);
                                }
                                else
                                {
                                    MoveOrConfirm(flow, flow.CampSubCursor, idx);
                                }

                                return;
                            }

                        case CampSubMode.SpellTarget:
                            MoveOrConfirm(flow, flow.CampTargetCursor, targetIdx);
                            return;
                        default:
                            flow.TapButton(InputButton.Cancel);
                            return;
                    }
                }

                switch (flow.CampMode)
                {
                    case CampSubMode.Root:
                        MoveOrConfirm(flow, flow.CampRootCursor, 2);
                        return;
                    case CampSubMode.ItemSelect:
                        {
                            var opts = flow.GetCampItemOptions();
                            var idx = FindOptionIndex(opts, actionId);
                            if (idx < 0 || !opts[idx].Enabled)
                            {
                                flow.TapButton(InputButton.Cancel);
                            }
                            else
                            {
                                MoveOrConfirm(flow, flow.CampSubCursor, idx);
                            }

                            return;
                        }

                    case CampSubMode.ItemTarget:
                        MoveOrConfirm(flow, flow.CampTargetCursor, targetIdx);
                        return;
                    default:
                        flow.TapButton(InputButton.Cancel);
                        return;
                }
            }

            flow.TapButton(InputButton.Cancel);
        }

        private static void DriveBattle(QuestFlow flow)
        {
            var presenter = flow.ActiveBattle;
            var world = flow.World;
            if (presenter == null || world == null)
            {
                return;
            }

            if (presenter.Step == BattleInputStep.Animating || presenter.Step == BattleInputStep.Finished)
            {
                flow.TapButton(InputButton.Confirm);
                return;
            }

            var desired = ChooseBattleCommand(world, presenter.Battle, presenter.PendingCommands, presenter.CurrentActorIndex);
            switch (presenter.Step)
            {
                case BattleInputStep.RootCommand:
                    {
                        var wantRoot = desired.Kind switch
                        {
                            BattleCommandKind.Attack => 0,
                            BattleCommandKind.Spell => 1,
                            BattleCommandKind.Item => 2,
                            BattleCommandKind.Defend => 3,
                            BattleCommandKind.Flee => 4,
                            _ => 0,
                        };
                        MoveOrConfirm(flow, presenter.RootCursor, wantRoot);
                        break;
                    }

                case BattleInputStep.SpellSelect:
                    {
                        var opts = presenter.GetCurrentSpellOptions();
                        var idx = FindOptionIndex(opts, desired.ActionId);
                        if (idx < 0 || !opts[idx].Enabled)
                        {
                            flow.TapButton(InputButton.Cancel);
                        }
                        else
                        {
                            MoveOrConfirm(flow, presenter.SubCursor, idx);
                        }

                        break;
                    }

                case BattleInputStep.ItemSelect:
                    {
                        var opts = presenter.GetCurrentItemOptions();
                        var idx = FindOptionIndex(opts, desired.ActionId);
                        if (idx < 0 || !opts[idx].Enabled)
                        {
                            flow.TapButton(InputButton.Cancel);
                        }
                        else
                        {
                            MoveOrConfirm(flow, presenter.SubCursor, idx);
                        }

                        break;
                    }

                case BattleInputStep.EnemyTarget:
                case BattleInputStep.AllyTarget:
                    MoveOrConfirm(flow, presenter.TargetCursor, desired.TargetIndex);
                    break;
            }
        }

        private static BattleCommand ChooseBattleCommand(
            World world,
            BattleState battle,
            IReadOnlyList<BattleCommand> pending,
            int actorIdx)
        {
            var data = world.Data;
            var party = world.Party;
            var actor = party[actorIdx];

            // 1. 蘇生優先（僧侶がリザレクトを使える場合）
            var deadAlly = FindDeadAlly(party);
            if (deadAlly >= 0 && BattleRules.CanCast(data, actor, "revive", inBattle: true, out _))
            {
                return BattleCommand.CastSpell("revive", deadAlly);
            }

            // 2. 回復優先
            var injuredCount = CountInjuredAllies(party, thresholdPercent: 68);
            var criticalAlly = LowestHpRatioLivingAlly(party, out var lowestPercent);
            var dangerThreshold = battle.IsBossBattle ? 65 : 52;

            if (injuredCount >= 2 && BattleRules.CanCast(data, actor, "aura_heal", inBattle: true, out _))
            {
                return BattleCommand.CastSpell("aura_heal", 0);
            }

            if (lowestPercent <= dangerThreshold)
            {
                if (BattleRules.CanCast(data, actor, "heal", inBattle: true, out _))
                {
                    return BattleCommand.CastSpell("heal", criticalAlly);
                }

                if (!AlreadyHealingAlly(pending, actorIdx, criticalAlly))
                {
                    if (CanSelectItem(data, battle, party, world.Inventory, pending, actorIdx, "hi_herb", criticalAlly))
                    {
                        return BattleCommand.UseItem("hi_herb", criticalAlly);
                    }

                    if (CanSelectItem(data, battle, party, world.Inventory, pending, actorIdx, "herb", criticalAlly))
                    {
                        return BattleCommand.UseItem("herb", criticalAlly);
                    }
                }
            }

            // 3. ボス戦で魔法使い・僧侶の MP が枯渇したら星露で補給
            if (battle.IsBossBattle && actor.MaxMp > 0 && actor.Mp < 6)
            {
                if (CanSelectItem(data, battle, party, world.Inventory, pending, actorIdx, "ether_dew", actorIdx))
                {
                    return BattleCommand.UseItem("ether_dew", actorIdx);
                }
            }

            var targetEnemy = PickBestEnemyTarget(battle);
            var targetInfo = battle.Enemies[targetEnemy].Info;

            // 4. 魔法使いの攻撃呪文選択
            if (string.Equals(actor.Class.Id, "mage", StringComparison.Ordinal))
            {
                if (battle.IsBossBattle)
                {
                    if (BattleRules.CanCast(data, actor, "glacier", inBattle: true, out _))
                    {
                        return BattleCommand.CastSpell("glacier", targetEnemy);
                    }

                    if (BattleRules.CanCast(data, actor, "frost_lance", inBattle: true, out _))
                    {
                        return BattleCommand.CastSpell("frost_lance", targetEnemy);
                    }

                    if (BattleRules.CanCast(data, actor, "ember", inBattle: true, out _))
                    {
                        return BattleCommand.CastSpell("ember", targetEnemy);
                    }
                }
                else
                {
                    var groupSize = CountLivingInSubGroup(battle, battle.Enemies[targetEnemy].SubGroup);
                    if (groupSize >= 2 && actor.Mp >= 12 && BattleRules.CanCast(data, actor, "flame_wave", inBattle: true, out _))
                    {
                        return BattleCommand.CastSpell("flame_wave", targetEnemy);
                    }

                    if (targetInfo.WeakElement == SpellElement.Frost && actor.Mp >= 10 &&
                        BattleRules.CanCast(data, actor, "frost_lance", inBattle: true, out _))
                    {
                        return BattleCommand.CastSpell("frost_lance", targetEnemy);
                    }

                    if ((targetInfo.WeakElement == SpellElement.Fire || targetInfo.Def >= 16) && actor.Mp >= 8 &&
                        BattleRules.CanCast(data, actor, "ember", inBattle: true, out _))
                    {
                        return BattleCommand.CastSpell("ember", targetEnemy);
                    }
                }
            }

            // 5. 僧侶の光属性攻撃（回復余裕時）
            if (string.Equals(actor.Class.Id, "priest", StringComparison.Ordinal) &&
                (battle.IsBossBattle || targetInfo.WeakElement == SpellElement.Light) &&
                actor.Mp >= 15 &&
                BattleRules.CanCast(data, actor, "holy_bolt", inBattle: true, out _))
            {
                return BattleCommand.CastSpell("holy_bolt", targetEnemy);
            }

            return BattleCommand.Attack(targetEnemy);
        }

        private static void DriveField(QuestFlow flow)
        {
            var world = flow.World!;
            if (world.Explorer.IsMoving)
            {
                return;
            }

            // 袋により良い装備がある、または移動中ケアが必要ならキャンプメニューを開く
            if (TryFindBetterBagEquip(world, out _, out _, out _) ||
                (!world.CurrentMap.IsSafeZone && TryPickFieldCare(world, out _, out _, out _, out _)))
            {
                flow.TapButton(InputButton.Menu);
                return;
            }

            ChooseFieldObjective(world, out var destMapId, out var destX, out var destY, out var interactDir);
            if (string.Equals(world.Explorer.MapId, destMapId, StringComparison.Ordinal) &&
                world.Explorer.X == destX &&
                world.Explorer.Y == destY)
            {
                if (interactDir.HasValue)
                {
                    if (world.Explorer.Facing != interactDir.Value)
                    {
                        flow.TapButton(DirectionToButton(interactDir.Value));
                    }
                    else
                    {
                        flow.TapButton(InputButton.Confirm);
                    }
                }

                return;
            }

            var nextDir = FindNextStepDirection(world, destMapId, destX, destY);
            if (nextDir.HasValue)
            {
                flow.TapButton(DirectionToButton(nextDir.Value));
            }
        }

        private static void ChooseFieldObjective(
            World world,
            out string mapId,
            out int goalX,
            out int goalY,
            out Direction? interactFacing)
        {
            interactFacing = null;
            var data = world.Data;
            var inTown = string.Equals(world.Explorer.MapId, "town", StringComparison.Ordinal);

            // 町にいる間の整備（蘇生 -> 宿屋 -> 買物）
            if (inTown)
            {
                var deadIdx = FindDeadAlly(world.Party);
                if (deadIdx >= 0)
                {
                    var cost = data.Rules.ReviveCostPerLevel * world.Party[deadIdx].Level;
                    if (world.Gold >= cost)
                    {
                        mapId = "town";
                        goalX = 14;
                        goalY = 5;
                        interactFacing = Direction.Up;
                        return;
                    }
                }

                if (NeedsInnRest(world) && world.Gold >= data.Rules.InnCost)
                {
                    mapId = "town";
                    goalX = 2;
                    goalY = 5;
                    interactFacing = Direction.Up;
                    return;
                }

                if (TryPickShopPurchase(world, data.GetShop("town_shop"), out _, out _))
                {
                    mapId = "town";
                    goalX = 3;
                    goalY = 10;
                    interactFacing = Direction.Down;
                    return;
                }
            }

            // ダンジョン中で危険またはボス戦前の最終補給が必要なら町へ帰還
            if (!inTown && ShouldReturnToTown(world))
            {
                mapId = "town";
                goalX = 8;
                goalY = 4;
                return;
            }

            // 1. B1F 宝箱回収
            if (TryFindUnopenedChestGoal(world, "dungeon_f1", out goalX, out goalY, out interactFacing))
            {
                mapId = "dungeon_f1";
                return;
            }

            // 2. Lv3 まで B1F で育成
            if (MinLivingLevel(world.Party) < 3)
            {
                mapId = "dungeon_f1";
                goalX = (world.Explorer.X == 5 && world.Explorer.Y == 5) ? 6 : 5;
                goalY = 5;
                return;
            }

            // 3. B2F 宝箱回収（鉄格子の鍵 + 守護の胸甲）
            if (TryFindUnopenedChestGoal(world, "dungeon_f2", out goalX, out goalY, out interactFacing))
            {
                mapId = "dungeon_f2";
                return;
            }

            // 4. Lv5 まで B2F で育成（リザレクト・グレイシア習得）
            if (MinLivingLevel(world.Party) < 5)
            {
                mapId = "dungeon_f2";
                goalX = (world.Explorer.X == 6 && world.Explorer.Y == 5) ? 7 : 6;
                goalY = 5;
                return;
            }

            // 5. B3F の鉄格子を解錠
            if (!world.Flags.Get("door_f3_main") && world.Flags.Get("flag_iron_key"))
            {
                mapId = "dungeon_f3";
                goalX = 9;
                goalY = 6;
                interactFacing = Direction.Up;
                return;
            }

            // 6. B3F 宝箱回収（焔断ちの大剣 + 星露）
            if (TryFindUnopenedChestGoal(world, "dungeon_f3", out goalX, out goalY, out interactFacing))
            {
                mapId = "dungeon_f3";
                return;
            }

            // 7. Lv6 まで B3F で育成
            if (MinLivingLevel(world.Party) < 6)
            {
                mapId = "dungeon_f3";
                goalX = (world.Explorer.X == 8 && world.Explorer.Y == 8) ? 9 : 8;
                goalY = 8;
                return;
            }

            // 8. 魔将ヴァルグ戦へ突入
            mapId = "boss_room";
            goalX = 8;
            goalY = 4;
        }

        private static bool ShouldReturnToTown(World world)
        {
            var dead = FindDeadAlly(world.Party);
            if (dead >= 0 && !BattleRules.CanCast(world.Data, world.Party[1], "revive", inBattle: false, out _))
            {
                return world.Gold >= world.Data.Rules.ReviveCostPerLevel * world.Party[dead].Level;
            }

            LowestHpRatioLivingAlly(world.Party, out var minHpPct);
            var noFieldHeal = !BattleRules.CanCast(world.Data, world.Party[1], "heal", inBattle: false, out _) &&
                              world.Inventory.Count("herb") == 0 &&
                              world.Inventory.Count("hi_herb") == 0;
            if (minHpPct <= 55 && noFieldHeal && world.Gold >= world.Data.Rules.InnCost)
            {
                return true;
            }

            // 初期装備のままお金が貯まったら一度町で鉄装備を買う
            if (world.Gold >= 180 &&
                string.Equals(world.Party[0].WeaponId, "bronze_sword", StringComparison.Ordinal) &&
                !world.Flags.Get("chest_f3_sword"))
            {
                return true;
            }

            // ボス突入条件が揃った時点で MP と星露が両方尽きていれば一度町で万全にする
            if (MinLivingLevel(world.Party) >= 6 &&
                world.Flags.Get("chest_f3_sword") &&
                !string.Equals(world.Explorer.MapId, "boss_room", StringComparison.Ordinal))
            {
                var mage = world.Party[2];
                var priest = world.Party[1];
                if (world.Inventory.Count("ether_dew") == 0 &&
                    (priest.Mp < 10 || mage.Mp < 10) &&
                    world.Gold >= world.Data.Rules.InnCost)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindUnopenedChestGoal(
            World world,
            string mapId,
            out int standX,
            out int standY,
            out Direction? facing)
        {
            standX = 0;
            standY = 0;
            facing = null;
            var map = world.Data.GetMap(mapId);
            Direction[] dirs = { Direction.Up, Direction.Down, Direction.Left, Direction.Right };

            for (var i = 0; i < map.Chests.Count; i++)
            {
                var c = map.Chests[i];
                if (world.Flags.Get(c.OpenedFlag))
                {
                    continue;
                }

                for (var d = 0; d < dirs.Length; d++)
                {
                    DirectionMath.Delta(dirs[d], out var dx, out var dy);
                    var sx = c.X - dx;
                    var sy = c.Y - dy;
                    if (map.IsWalkable(sx, sy, world.Flags) && map.PortalAt(sx, sy) == null)
                    {
                        standX = sx;
                        standY = sy;
                        facing = dirs[d];
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryFindBetterBagEquip(World world, out int memberIdx, out EquipSlot slot, out string equipId)
        {
            memberIdx = 0;
            slot = EquipSlot.Weapon;
            equipId = string.Empty;

            for (var i = 0; i < world.Party.Count; i++)
            {
                var m = world.Party[i];
                foreach (var s in new[] { EquipSlot.Weapon, EquipSlot.Armor })
                {
                    var curId = s == EquipSlot.Weapon ? m.WeaponId : m.ArmorId;
                    var curScore = curId.Length > 0 ? ScoreEquip(world.Data.GetEquip(curId)) : 0;
                    var candidates = world.Inventory.EquippableInBag(m, s);
                    for (var c = 0; c < candidates.Count; c++)
                    {
                        if (ScoreEquip(candidates[c]) > curScore)
                        {
                            memberIdx = i;
                            slot = s;
                            equipId = candidates[c].Id;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static int ScoreEquip(EquipInfo eq) => (eq.AtkBonus * 10) + (eq.DefBonus * 10) + eq.AgiBonus;

        private static bool TryPickFieldCare(
            World world,
            out bool useSpell,
            out int casterIdx,
            out string actionId,
            out int targetIdx)
        {
            useSpell = false;
            casterIdx = 1;
            actionId = string.Empty;
            targetIdx = 0;

            var dead = FindDeadAlly(world.Party);
            if (dead >= 0 && BattleRules.CanCast(world.Data, world.Party[1], "revive", inBattle: false, out _))
            {
                useSpell = true;
                casterIdx = 1;
                actionId = "revive";
                targetIdx = dead;
                return true;
            }

            var target = LowestHpRatioLivingAlly(world.Party, out var minPct);
            if (minPct <= 65)
            {
                if (BattleRules.CanCast(world.Data, world.Party[1], "heal", inBattle: false, out _))
                {
                    useSpell = true;
                    casterIdx = 1;
                    actionId = "heal";
                    targetIdx = target;
                    return true;
                }

                if (world.Inventory.Count("herb") > 0)
                {
                    useSpell = false;
                    actionId = "herb";
                    targetIdx = target;
                    return true;
                }

                if (world.Inventory.Count("hi_herb") > 0 && minPct <= 50)
                {
                    useSpell = false;
                    actionId = "hi_herb";
                    targetIdx = target;
                    return true;
                }
            }

            return false;
        }

        private static bool TryPickShopPurchase(World world, ShopInfo shop, out string productId, out int equipMemberIndex)
        {
            productId = string.Empty;
            equipMemberIndex = -1;
            var reserve = world.Data.Rules.InnCost * 2;

            // 1. 戦士の剣（大剣を未入手なら鉄の長剣）
            if (!world.Flags.Get("chest_f3_sword") &&
                ScoreCurrentEquip(world, 0, EquipSlot.Weapon) < ScoreEquip(world.Data.GetEquip("iron_blade")) &&
                CanAffordWithReserve(world, shop, "iron_blade", reserve))
            {
                productId = "iron_blade";
                equipMemberIndex = 0;
                return true;
            }

            // 2. 戦士の鎖帷子（守護の胸甲を未入手なら）
            if (!world.Flags.Get("chest_f2_armor") &&
                ScoreCurrentEquip(world, 0, EquipSlot.Armor) < ScoreEquip(world.Data.GetEquip("chain_mail")) &&
                CanAffordWithReserve(world, shop, "chain_mail", reserve))
            {
                productId = "chain_mail";
                equipMemberIndex = 0;
                return true;
            }

            // 3. 魔法使い・僧侶の星織りの外套
            for (var m = 2; m >= 1; m--)
            {
                if (ScoreCurrentEquip(world, m, EquipSlot.Armor) < ScoreEquip(world.Data.GetEquip("silk_cloak")) &&
                    CanAffordWithReserve(world, shop, "silk_cloak", reserve))
                {
                    productId = "silk_cloak";
                    equipMemberIndex = m;
                    return true;
                }
            }

            // 4. 僧侶の銀の鎚
            if (ScoreCurrentEquip(world, 1, EquipSlot.Weapon) < ScoreEquip(world.Data.GetEquip("silver_mace")) &&
                CanAffordWithReserve(world, shop, "silver_mace", reserve))
            {
                productId = "silver_mace";
                equipMemberIndex = 1;
                return true;
            }

            // 5. 消耗品の補充
            if (world.Inventory.Count("herb") < 5 && CanAffordWithReserve(world, shop, "herb", reserve))
            {
                productId = "herb";
                return true;
            }

            if (MinLivingLevel(world.Party) >= 4 && world.Inventory.Count("hi_herb") < 4 && CanAffordWithReserve(world, shop, "hi_herb", reserve))
            {
                productId = "hi_herb";
                return true;
            }

            if (MinLivingLevel(world.Party) >= 5 && world.Inventory.Count("ether_dew") < 3 && CanAffordWithReserve(world, shop, "ether_dew", reserve))
            {
                productId = "ether_dew";
                return true;
            }

            return false;
        }

        private static int ScoreCurrentEquip(World world, int memberIdx, EquipSlot slot)
        {
            var m = world.Party[memberIdx];
            var id = slot == EquipSlot.Weapon ? m.WeaponId : m.ArmorId;
            return id.Length > 0 ? ScoreEquip(world.Data.GetEquip(id)) : 0;
        }

        private static bool CanAffordWithReserve(World world, ShopInfo shop, string id, int reserve)
        {
            if (!shop.Sells(id) || !world.Inventory.CanAdd(world.Party, id, 1))
            {
                return false;
            }

            return world.Gold >= world.Data.ProductPrice(id) + reserve;
        }

        private static Direction? FindNextStepDirection(World world, string targetMapId, int goalX, int goalY)
        {
            var currentMap = world.CurrentMap;
            var localGoalX = goalX;
            var localGoalY = goalY;

            if (!string.Equals(currentMap.Id, targetMapId, StringComparison.Ordinal))
            {
                var portal = FindPortalTowardMap(world.Data, currentMap.Id, targetMapId);
                if (portal == null)
                {
                    return null;
                }

                localGoalX = portal.X;
                localGoalY = portal.Y;
            }

            return BfsFirstDirection(currentMap, world.Flags, world.Explorer.X, world.Explorer.Y, localGoalX, localGoalY);
        }

        private static PortalInfo? FindPortalTowardMap(GameData data, string startMapId, string targetMapId)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal) { startMapId };
            var queue = new Queue<(string MapId, PortalInfo FirstPortal)>();
            var startMap = data.GetMap(startMapId);
            for (var i = 0; i < startMap.Portals.Count; i++)
            {
                var p = startMap.Portals[i];
                if (string.Equals(p.TargetMapId, targetMapId, StringComparison.Ordinal))
                {
                    return p;
                }

                if (visited.Add(p.TargetMapId))
                {
                    queue.Enqueue((p.TargetMapId, p));
                }
            }

            while (queue.Count > 0)
            {
                var (mapId, first) = queue.Dequeue();
                var map = data.GetMap(mapId);
                for (var i = 0; i < map.Portals.Count; i++)
                {
                    var p = map.Portals[i];
                    if (string.Equals(p.TargetMapId, targetMapId, StringComparison.Ordinal))
                    {
                        return first;
                    }

                    if (visited.Add(p.TargetMapId))
                    {
                        queue.Enqueue((p.TargetMapId, first));
                    }
                }
            }

            return null;
        }

        private static Direction? BfsFirstDirection(FieldMap map, FlagSet flags, int startX, int startY, int goalX, int goalY)
        {
            if (startX == goalX && startY == goalY)
            {
                return null;
            }

            Direction[] dirs = { Direction.Up, Direction.Right, Direction.Down, Direction.Left };
            var visited = new bool[map.Width, map.Height];
            visited[startX, startY] = true;
            var queue = new Queue<(int X, int Y, Direction FirstDir)>();

            for (var d = 0; d < dirs.Length; d++)
            {
                DirectionMath.Delta(dirs[d], out var dx, out var dy);
                var nx = startX + dx;
                var ny = startY + dy;
                if (!CanStepInPath(map, flags, nx, ny, goalX, goalY) || visited[nx, ny])
                {
                    continue;
                }

                if (nx == goalX && ny == goalY)
                {
                    return dirs[d];
                }

                visited[nx, ny] = true;
                queue.Enqueue((nx, ny, dirs[d]));
            }

            while (queue.Count > 0)
            {
                var (x, y, firstDir) = queue.Dequeue();
                for (var d = 0; d < dirs.Length; d++)
                {
                    DirectionMath.Delta(dirs[d], out var dx, out var dy);
                    var nx = x + dx;
                    var ny = y + dy;
                    if (!CanStepInPath(map, flags, nx, ny, goalX, goalY) || visited[nx, ny])
                    {
                        continue;
                    }

                    if (nx == goalX && ny == goalY)
                    {
                        return firstDir;
                    }

                    visited[nx, ny] = true;
                    queue.Enqueue((nx, ny, firstDir));
                }
            }

            return null;
        }

        private static bool CanStepInPath(FieldMap map, FlagSet flags, int x, int y, int goalX, int goalY)
        {
            if (!map.IsWalkable(x, y, flags))
            {
                return false;
            }

            if (x == goalX && y == goalY)
            {
                return true;
            }

            // 目的地以外の階段やボス発火マスは不用意に踏まない
            if (map.PortalAt(x, y) != null)
            {
                return false;
            }

            if (map.Boss.Enabled && !flags.Get(map.Boss.DefeatedFlag) && x == map.Boss.TriggerX && y == map.Boss.TriggerY)
            {
                return false;
            }

            return true;
        }

        private static bool NeedsInnRest(World world)
        {
            for (var i = 0; i < world.Party.Count; i++)
            {
                var m = world.Party[i];
                if (m.IsAlive && (m.Hp < m.MaxHp || m.Mp < m.MaxMp))
                {
                    return true;
                }
            }

            return false;
        }

        private static int MinLivingLevel(Party party)
        {
            var min = int.MaxValue;
            for (var i = 0; i < party.Count; i++)
            {
                if (party[i].IsAlive && party[i].Level < min)
                {
                    min = party[i].Level;
                }
            }

            return min == int.MaxValue ? 1 : min;
        }

        private static int FindDeadAlly(Party party)
        {
            for (var i = 0; i < party.Count; i++)
            {
                if (!party[i].IsAlive)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int CountInjuredAllies(Party party, int thresholdPercent)
        {
            var count = 0;
            for (var i = 0; i < party.Count; i++)
            {
                var m = party[i];
                if (m.IsAlive && (m.Hp * 100) <= (m.MaxHp * thresholdPercent))
                {
                    count++;
                }
            }

            return count;
        }

        private static int LowestHpRatioLivingAlly(Party party, out int lowestPercent)
        {
            var best = 0;
            lowestPercent = 100;
            for (var i = 0; i < party.Count; i++)
            {
                var m = party[i];
                if (!m.IsAlive)
                {
                    continue;
                }

                var pct = (m.Hp * 100) / Math.Max(1, m.MaxHp);
                if (pct < lowestPercent)
                {
                    lowestPercent = pct;
                    best = i;
                }
            }

            return best;
        }

        private static bool AlreadyHealingAlly(IReadOnlyList<BattleCommand> pending, int upTo, int allyIndex)
        {
            for (var i = 0; i < upTo; i++)
            {
                if ((pending[i].Kind == BattleCommandKind.Item || pending[i].Kind == BattleCommandKind.Spell) &&
                    pending[i].TargetIndex == allyIndex)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CanSelectItem(
            GameData data,
            BattleState battle,
            Party party,
            Inventory inv,
            IReadOnlyList<BattleCommand> pending,
            int actorIdx,
            string itemId,
            int targetAlly)
        {
            return BattleRules.ValidateCommand(
                data,
                battle,
                party,
                inv,
                actorIdx,
                BattleCommand.UseItem(itemId, targetAlly),
                pending,
                out _);
        }

        private static int PickBestEnemyTarget(BattleState battle)
        {
            var best = 0;
            var bestHp = int.MaxValue;
            for (var i = 0; i < battle.Enemies.Count; i++)
            {
                var e = battle.Enemies[i];
                if (e.IsAlive && e.Hp < bestHp)
                {
                    bestHp = e.Hp;
                    best = i;
                }
            }

            return best;
        }

        private static int CountLivingInSubGroup(BattleState battle, int subGroup)
        {
            var c = 0;
            for (var i = 0; i < battle.Enemies.Count; i++)
            {
                if (battle.Enemies[i].IsAlive && battle.Enemies[i].SubGroup == subGroup)
                {
                    c++;
                }
            }

            return c;
        }

        private static int FindOptionIndex(IReadOnlyList<SelectableOption> opts, string id)
        {
            for (var i = 0; i < opts.Count; i++)
            {
                if (string.Equals(opts[i].Id, id, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static void MoveOrConfirm(QuestFlow flow, int currentCursor, int targetCursor)
        {
            if (currentCursor != targetCursor)
            {
                flow.TapButton(InputButton.Down);
            }
            else
            {
                flow.TapButton(InputButton.Confirm);
            }
        }

        private static InputButton DirectionToButton(Direction dir) => dir switch
        {
            Direction.Up => InputButton.Up,
            Direction.Right => InputButton.Right,
            Direction.Down => InputButton.Down,
            _ => InputButton.Left,
        };
    }
}
