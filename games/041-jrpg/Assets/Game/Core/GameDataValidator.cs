using System;
using System.Collections.Generic;
using TileMotion;

namespace Game.Core
{
    internal static class GameDataValidator
    {
        public static bool Build(GameDataDef raw, out GameData? result, out DataError? error)
        {
            result = null;

            if (!ValidateExpTable(raw.ExpTable, out var expInfo, out error))
            {
                return false;
            }

            if (!ValidateSpells(raw.Spells, out var spellInfos, out var spellIds, out error))
            {
                return false;
            }

            if (!ValidateItems(raw.Items, out var itemInfos, out var itemIds, out error))
            {
                return false;
            }

            if (!ValidateClassIdsOnly(raw.Classes, out var classIds, out error))
            {
                return false;
            }

            if (!ValidateEquips(raw.Equips, itemIds, classIds, out var equipInfos, out var equipById, out error))
            {
                return false;
            }

            if (!ValidateClasses(raw.Classes, expInfo!, spellIds, equipById, out var classInfos, out error))
            {
                return false;
            }

            if (!ValidateEnemies(raw.Enemies, spellInfos, spellIds, out var enemyInfos, out var enemyById, out error))
            {
                return false;
            }

            if (!ValidateGroups(raw.EnemyGroups, enemyById, out var groupInfos, out var groupById, out error))
            {
                return false;
            }

            if (!ValidateEncounterTables(raw.EncounterTables, raw.Rules, groupById, out var encounterInfos, out var encounterIds, out error))
            {
                return false;
            }

            if (!ValidateShops(raw.Shops, itemIds, equipById, out var shopInfos, out var shopIds, out error))
            {
                return false;
            }

            if (!ValidateMaps(raw.Maps, itemIds, equipById, encounterIds, shopIds, groupById, raw.Rules, out var maps, out var mapById, out var knownFlags, out error))
            {
                return false;
            }

            if (!ValidateRules(raw.Rules, classIds, itemIds, mapById, out var ruleInfo, out error))
            {
                return false;
            }

            var fingerprint = ComputeFingerprint(ruleInfo!, expInfo!, classInfos, spellInfos, itemInfos, equipInfos, enemyInfos, groupInfos, encounterInfos, shopInfos, maps, knownFlags);
            result = new GameData(ruleInfo!, expInfo!, classInfos, spellInfos, itemInfos, equipInfos, enemyInfos, groupInfos, encounterInfos, shopInfos, maps, knownFlags, fingerprint);
            error = null;
            return true;
        }

        private static bool ValidateExpTable(ExpTableDef exp, out ExpTableInfo? info, out DataError? error)
        {
            info = null;
            if (exp.MaxLevel < 2 || exp.MaxLevel > 50)
            {
                error = new DataError(DataErrorKind.OutOfRange, "ExpTable.MaxLevel", "最大レベルは 2〜50 で指定してください。");
                return false;
            }

            if (exp.RequiredExp == null || exp.RequiredExp.Length != exp.MaxLevel)
            {
                error = new DataError(DataErrorKind.OutOfRange, "ExpTable.RequiredExp", "経験値表の長さが MaxLevel と一致しません。");
                return false;
            }

            if (exp.RequiredExp[0] != 0)
            {
                error = new DataError(DataErrorKind.OutOfRange, "ExpTable.RequiredExp[0]", "レベル 1 の必要経験値は 0 である必要があります。");
                return false;
            }

            for (var i = 1; i < exp.RequiredExp.Length; i++)
            {
                if (exp.RequiredExp[i] <= exp.RequiredExp[i - 1])
                {
                    error = new DataError(DataErrorKind.NonMonotonicExp, $"ExpTable.RequiredExp[{i}]", "経験値表は狭義単調増加である必要があります。");
                    return false;
                }
            }

            info = new ExpTableInfo(exp.MaxLevel, exp.RequiredExp);
            error = null;
            return true;
        }

        private static bool ValidateSpells(SpellDef[] defs, out SpellInfo[] infos, out HashSet<string> ids, out DataError? error)
        {
            infos = Array.Empty<SpellInfo>();
            ids = new HashSet<string>(StringComparer.Ordinal);
            if (defs == null || defs.Length == 0)
            {
                error = new DataError(DataErrorKind.EmptyCollection, "Spells", "呪文の定義が空です。");
                return false;
            }

            var result = new SpellInfo[defs.Length];
            for (var i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                var loc = $"Spells[{i}]";
                if (!GameData.IsValidId(d.Id))
                {
                    error = new DataError(DataErrorKind.InvalidId, loc, $"不正な呪文 ID です: '{d.Id}'");
                    return false;
                }

                if (!ids.Add(d.Id))
                {
                    error = new DataError(DataErrorKind.DuplicateId, loc, $"呪文 ID が重複しています: '{d.Id}'");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(d.Name))
                {
                    error = new DataError(DataErrorKind.OutOfRange, loc, "呪文名が空です。");
                    return false;
                }

                if ((int)d.Kind < (int)SpellKind.SingleAttack || (int)d.Kind > (int)SpellKind.Revive)
                {
                    error = new DataError(DataErrorKind.OutOfRange, loc, "未定義の呪文種別です。");
                    return false;
                }

                if ((int)d.Element < (int)SpellElement.None || (int)d.Element > (int)SpellElement.Light)
                {
                    error = new DataError(DataErrorKind.OutOfRange, loc, "未定義の属性です。");
                    return false;
                }

                if (d.MpCost <= 0 || d.Power <= 0)
                {
                    error = new DataError(DataErrorKind.OutOfRange, loc, "呪文の消費 MP と威力は 1 以上である必要があります。");
                    return false;
                }

                if (d.Kind == SpellKind.Revive && d.Power > 100)
                {
                    error = new DataError(DataErrorKind.OutOfRange, loc, "蘇生呪文の回復割合は 1〜100 で指定してください。");
                    return false;
                }

                if ((d.Kind == SpellKind.SingleAttack || d.Kind == SpellKind.GroupAttack) && d.UsableInField)
                {
                    error = new DataError(DataErrorKind.OutOfRange, loc, "攻撃呪文を移動中使用可能にはできません。");
                    return false;
                }

                result[i] = new SpellInfo(i, d);
            }

            infos = result;
            error = null;
            return true;
        }

        private static bool ValidateItems(ItemDef[] defs, out ItemInfo[] infos, out HashSet<string> ids, out DataError? error)
        {
            infos = Array.Empty<ItemInfo>();
            ids = new HashSet<string>(StringComparer.Ordinal);
            if (defs == null || defs.Length == 0)
            {
                error = new DataError(DataErrorKind.EmptyCollection, "Items", "道具の定義が空です。");
                return false;
            }

            var result = new ItemInfo[defs.Length];
            for (var i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                var loc = $"Items[{i}]";
                if (!GameData.IsValidId(d.Id))
                {
                    error = new DataError(DataErrorKind.InvalidId, loc, $"不正な道具 ID です: '{d.Id}'");
                    return false;
                }

                if (!ids.Add(d.Id))
                {
                    error = new DataError(DataErrorKind.DuplicateId, loc, $"道具 ID が重複しています: '{d.Id}'");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(d.Name))
                {
                    error = new DataError(DataErrorKind.OutOfRange, loc, "道具名が空です。");
                    return false;
                }

                if ((int)d.Kind < (int)ItemKind.HealHp || (int)d.Kind > (int)ItemKind.RestoreMp)
                {
                    error = new DataError(DataErrorKind.OutOfRange, loc, "未定義の道具種別です。");
                    return false;
                }

                if (d.Power <= 0 || d.Price < 2)
                {
                    error = new DataError(DataErrorKind.OutOfRange, loc, "道具の効果量は 1 以上、価格は 2 以上である必要があります。");
                    return false;
                }

                result[i] = new ItemInfo(i, d);
            }

            infos = result;
            error = null;
            return true;
        }

        private static bool ValidateClassIdsOnly(ClassDef[] defs, out HashSet<string> ids, out DataError? error)
        {
            ids = new HashSet<string>(StringComparer.Ordinal);
            if (defs == null || defs.Length == 0)
            {
                error = new DataError(DataErrorKind.EmptyCollection, "Classes", "職業の定義が空です。");
                return false;
            }

            for (var i = 0; i < defs.Length; i++)
            {
                var id = defs[i].Id;
                var loc = $"Classes[{i}]";
                if (!GameData.IsValidId(id))
                {
                    error = new DataError(DataErrorKind.InvalidId, loc, $"不正な職業 ID です: '{id}'");
                    return false;
                }

                if (!ids.Add(id))
                {
                    error = new DataError(DataErrorKind.DuplicateId, loc, $"職業 ID が重複しています: '{id}'");
                    return false;
                }
            }

            error = null;
            return true;
        }

        private static bool ValidateEquips(
            EquipDef[] defs,
            HashSet<string> itemIds,
            HashSet<string> classIds,
            out EquipInfo[] infos,
            out Dictionary<string, EquipInfo> byId,
            out DataError? error)
        {
            infos = Array.Empty<EquipInfo>();
            byId = new Dictionary<string, EquipInfo>(StringComparer.Ordinal);
            if (defs == null || defs.Length == 0)
            {
                error = new DataError(DataErrorKind.EmptyCollection, "Equips", "装備の定義が空です。");
                return false;
            }

            var result = new EquipInfo[defs.Length];
            for (var i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                var loc = $"Equips[{i}]";
                if (!GameData.IsValidId(d.Id))
                {
                    error = new DataError(DataErrorKind.InvalidId, loc, $"不正な装備 ID です: '{d.Id}'");
                    return false;
                }

                if (itemIds.Contains(d.Id) || byId.ContainsKey(d.Id))
                {
                    error = new DataError(DataErrorKind.DuplicateId, loc, $"装備 ID が重複しています: '{d.Id}'");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(d.Name))
                {
                    error = new DataError(DataErrorKind.OutOfRange, loc, "装備名が空です。");
                    return false;
                }

                if (d.Slot != EquipSlot.Weapon && d.Slot != EquipSlot.Armor)
                {
                    error = new DataError(DataErrorKind.OutOfRange, loc, "未定義の装備部位です。");
                    return false;
                }

                if (d.AtkBonus < 0 || d.DefBonus < 0 || d.AgiBonus < 0 || (d.AtkBonus + d.DefBonus + d.AgiBonus) <= 0 || d.Price < 2)
                {
                    error = new DataError(DataErrorKind.OutOfRange, loc, "装備の補正値は非負かつ合計 1 以上、価格は 2 以上である必要があります。");
                    return false;
                }

                if (d.AllowedClassIds == null || d.AllowedClassIds.Length == 0)
                {
                    error = new DataError(DataErrorKind.NoAllowedClass, loc, $"装備 '{d.Id}' を装備できる職業が 1 つもありません。");
                    return false;
                }

                var allowedSet = new HashSet<string>(StringComparer.Ordinal);
                var allowedCopy = new string[d.AllowedClassIds.Length];
                for (var k = 0; k < d.AllowedClassIds.Length; k++)
                {
                    var cid = d.AllowedClassIds[k];
                    if (!classIds.Contains(cid))
                    {
                        error = new DataError(DataErrorKind.UnresolvedReference, $"{loc}.AllowedClassIds[{k}]", $"未知の職業 ID '{cid}' を参照しています。");
                        return false;
                    }

                    if (!allowedSet.Add(cid))
                    {
                        error = new DataError(DataErrorKind.DuplicateId, $"{loc}.AllowedClassIds[{k}]", $"装備可能職業 '{cid}' が重複しています。");
                        return false;
                    }

                    allowedCopy[k] = cid;
                }

                var info = new EquipInfo(i, d, allowedCopy);
                result[i] = info;
                byId[d.Id] = info;
            }

            infos = result;
            error = null;
            return true;
        }

        private static bool ValidateClasses(
            ClassDef[] defs,
            ExpTableInfo expTable,
            HashSet<string> spellIds,
            Dictionary<string, EquipInfo> equipById,
            out ClassInfo[] infos,
            out DataError? error)
        {
            infos = Array.Empty<ClassInfo>();
            var result = new ClassInfo[defs.Length];
            for (var i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                var loc = $"Classes[{i}]";
                if (string.IsNullOrWhiteSpace(d.Name) || string.IsNullOrWhiteSpace(d.HeroName))
                {
                    error = new DataError(DataErrorKind.OutOfRange, loc, "職業名・初期キャラクター名は必須です。");
                    return false;
                }

                if (d.BaseHp <= 0 || d.BaseMp < 0 || d.BaseAtk <= 0 || d.BaseDef < 0 || d.BaseAgi <= 0)
                {
                    error = new DataError(DataErrorKind.OutOfRange, loc, "初期能力値が範囲外です。");
                    return false;
                }

                if (d.HpGain <= 0 || d.MpGain < 0 || d.AtkGain < 0 || d.DefGain < 0 || d.AgiGain < 0)
                {
                    error = new DataError(DataErrorKind.OutOfRange, loc, "成長値が範囲外です。");
                    return false;
                }

                if (d.StartWeaponId.Length > 0)
                {
                    if (!equipById.TryGetValue(d.StartWeaponId, out var w))
                    {
                        error = new DataError(DataErrorKind.UnresolvedReference, $"{loc}.StartWeaponId", $"未知の初期武器 '{d.StartWeaponId}' です。");
                        return false;
                    }

                    if (w.Slot != EquipSlot.Weapon || !w.CanEquip(d.Id))
                    {
                        error = new DataError(DataErrorKind.OutOfRange, $"{loc}.StartWeaponId", $"初期武器 '{d.StartWeaponId}' をこの職業は装備できません。");
                        return false;
                    }
                }

                if (d.StartArmorId.Length > 0)
                {
                    if (!equipById.TryGetValue(d.StartArmorId, out var a))
                    {
                        error = new DataError(DataErrorKind.UnresolvedReference, $"{loc}.StartArmorId", $"未知の初期防具 '{d.StartArmorId}' です。");
                        return false;
                    }

                    if (a.Slot != EquipSlot.Armor || !a.CanEquip(d.Id))
                    {
                        error = new DataError(DataErrorKind.OutOfRange, $"{loc}.StartArmorId", $"初期防具 '{d.StartArmorId}' をこの職業は装備できません。");
                        return false;
                    }
                }

                var spellSet = new HashSet<string>(StringComparer.Ordinal);
                var spells = new LearnableSpellInfo[d.Spells.Length];
                for (var k = 0; k < d.Spells.Length; k++)
                {
                    var s = d.Spells[k];
                    if (s.Level < 1 || s.Level > expTable.MaxLevel)
                    {
                        error = new DataError(DataErrorKind.OutOfRange, $"{loc}.Spells[{k}]", $"習得レベル {s.Level} が範囲外です。");
                        return false;
                    }

                    if (!spellIds.Contains(s.SpellId))
                    {
                        error = new DataError(DataErrorKind.UnresolvedReference, $"{loc}.Spells[{k}]", $"未知の呪文 ID '{s.SpellId}' を参照しています。");
                        return false;
                    }

                    if (!spellSet.Add(s.SpellId))
                    {
                        error = new DataError(DataErrorKind.DuplicateId, $"{loc}.Spells[{k}]", $"習得呪文 '{s.SpellId}' が重複しています。");
                        return false;
                    }

                    spells[k] = new LearnableSpellInfo(s.Level, s.SpellId);
                }

                result[i] = new ClassInfo(i, d, spells);
            }

            infos = result;
            error = null;
            return true;
        }

        private static bool ValidateEnemies(
            EnemyDef[] defs,
            SpellInfo[] spellInfos,
            HashSet<string> spellIds,
            out EnemyInfo[] infos,
            out Dictionary<string, EnemyInfo> byId,
            out DataError? error)
        {
            infos = Array.Empty<EnemyInfo>();
            byId = new Dictionary<string, EnemyInfo>(StringComparer.Ordinal);
            if (defs == null || defs.Length == 0)
            {
                error = new DataError(DataErrorKind.EmptyCollection, "Enemies", "敵の定義が空です。");
                return false;
            }

            var spellMap = new Dictionary<string, SpellInfo>(spellInfos.Length, StringComparer.Ordinal);
            for (var i = 0; i < spellInfos.Length; i++)
            {
                spellMap[spellInfos[i].Id] = spellInfos[i];
            }

            var result = new EnemyInfo[defs.Length];
            for (var i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                var loc = $"Enemies[{i}]";
                if (!GameData.IsValidId(d.Id))
                {
                    error = new DataError(DataErrorKind.InvalidId, loc, $"不正な敵 ID です: '{d.Id}'");
                    return false;
                }

                if (byId.ContainsKey(d.Id))
                {
                    error = new DataError(DataErrorKind.DuplicateId, loc, $"敵 ID が重複しています: '{d.Id}'");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(d.Name))
                {
                    error = new DataError(DataErrorKind.OutOfRange, loc, "敵の名前が空です。");
                    return false;
                }

                if (d.MaxHp <= 0 || d.MaxMp < 0 || d.Atk <= 0 || d.Def < 0 || d.Agi <= 0 || d.ExpReward < 0 || d.GoldReward < 0)
                {
                    error = new DataError(DataErrorKind.OutOfRange, loc, "敵の能力値・報酬が範囲外です。");
                    return false;
                }

                if ((int)d.WeakElement < (int)SpellElement.None || (int)d.WeakElement > (int)SpellElement.Light)
                {
                    error = new DataError(DataErrorKind.OutOfRange, loc, "未定義の弱点属性です。");
                    return false;
                }

                if (d.Actions == null || d.Actions.Length == 0)
                {
                    error = new DataError(DataErrorKind.EmptyCollection, $"{loc}.Actions", "敵の行動表が空です。");
                    return false;
                }

                if (!ValidateEnemyActionList(d.Actions, $"{loc}.Actions", spellIds, spellMap, out var actions, out error))
                {
                    return false;
                }

                if (d.IsBoss && (d.Phase2Actions == null || d.Phase2Actions.Length == 0))
                {
                    error = new DataError(DataErrorKind.EmptyCollection, $"{loc}.Phase2Actions", "ボスの後半行動表が空です。");
                    return false;
                }

                var p2Raw = d.Phase2Actions ?? Array.Empty<EnemyActionEntry>();
                var phase2 = Array.Empty<EnemyActionInfo>();
                if (p2Raw.Length > 0 && !ValidateEnemyActionList(p2Raw, $"{loc}.Phase2Actions", spellIds, spellMap, out phase2, out error))
                {
                    return false;
                }

                var info = new EnemyInfo(i, d, actions, phase2);
                result[i] = info;
                byId[d.Id] = info;
            }

            infos = result;
            error = null;
            return true;
        }

        private static bool ValidateEnemyActionList(
            EnemyActionEntry[] entries,
            string loc,
            HashSet<string> spellIds,
            Dictionary<string, SpellInfo> spellMap,
            out EnemyActionInfo[] infos,
            out DataError? error)
        {
            infos = new EnemyActionInfo[entries.Length];
            for (var i = 0; i < entries.Length; i++)
            {
                var e = entries[i];
                if (e.Weight <= 0)
                {
                    error = new DataError(DataErrorKind.OutOfRange, $"{loc}[{i}]", "行動の重みは 1 以上である必要があります。");
                    return false;
                }

                if ((int)e.Kind < (int)EnemyActionKind.Attack || (int)e.Kind > (int)EnemyActionKind.Spell)
                {
                    error = new DataError(DataErrorKind.OutOfRange, $"{loc}[{i}]", "未定義の敵行動種別です。");
                    return false;
                }

                if (e.Kind == EnemyActionKind.Spell)
                {
                    if (!spellIds.Contains(e.SpellId))
                    {
                        error = new DataError(DataErrorKind.UnresolvedReference, $"{loc}[{i}].SpellId", $"未知の呪文 ID '{e.SpellId}' を参照しています。");
                        return false;
                    }

                    if (spellMap[e.SpellId].Kind == SpellKind.Revive)
                    {
                        error = new DataError(DataErrorKind.OutOfRange, $"{loc}[{i}].SpellId", "敵の行動表に蘇生呪文は指定できません。");
                        return false;
                    }
                }
                else if (e.SpellId.Length > 0)
                {
                    error = new DataError(DataErrorKind.OutOfRange, $"{loc}[{i}].SpellId", "呪文以外の行動に SpellId は指定できません。");
                    return false;
                }

                infos[i] = new EnemyActionInfo(e.Kind, e.SpellId, e.Weight);
            }

            error = null;
            return true;
        }

        private static bool ValidateGroups(
            EnemyGroupDef[] defs,
            Dictionary<string, EnemyInfo> enemyById,
            out EnemyGroupInfo[] infos,
            out Dictionary<string, EnemyGroupInfo> byId,
            out DataError? error)
        {
            infos = Array.Empty<EnemyGroupInfo>();
            byId = new Dictionary<string, EnemyGroupInfo>(StringComparer.Ordinal);
            if (defs == null || defs.Length == 0)
            {
                error = new DataError(DataErrorKind.EmptyCollection, "EnemyGroups", "敵グループの定義が空です。");
                return false;
            }

            var result = new EnemyGroupInfo[defs.Length];
            for (var i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                var loc = $"EnemyGroups[{i}]";
                if (!GameData.IsValidId(d.Id))
                {
                    error = new DataError(DataErrorKind.InvalidId, loc, $"不正な敵グループ ID です: '{d.Id}'");
                    return false;
                }

                if (byId.ContainsKey(d.Id))
                {
                    error = new DataError(DataErrorKind.DuplicateId, loc, $"敵グループ ID が重複しています: '{d.Id}'");
                    return false;
                }

                if (d.EnemyIds == null || d.EnemyIds.Length == 0 || d.EnemyIds.Length > 4)
                {
                    error = new DataError(DataErrorKind.OutOfRange, $"{loc}.EnemyIds", "1 グループの敵の数は 1〜4 体で指定してください。");
                    return false;
                }

                var isBoss = false;
                var copy = new string[d.EnemyIds.Length];
                for (var k = 0; k < d.EnemyIds.Length; k++)
                {
                    var eid = d.EnemyIds[k];
                    if (!enemyById.TryGetValue(eid, out var enemy))
                    {
                        error = new DataError(DataErrorKind.UnresolvedReference, $"{loc}.EnemyIds[{k}]", $"未知の敵 ID '{eid}' を参照しています。");
                        return false;
                    }

                    isBoss |= enemy.IsBoss;
                    copy[k] = eid;
                }

                var info = new EnemyGroupInfo(i, d.Id, copy, isBoss);
                result[i] = info;
                byId[d.Id] = info;
            }

            infos = result;
            error = null;
            return true;
        }

        private static bool ValidateEncounterTables(
            EncounterTableDef[] defs,
            RuleConfig rules,
            Dictionary<string, EnemyGroupInfo> groupById,
            out EncounterTableInfo[] infos,
            out HashSet<string> ids,
            out DataError? error)
        {
            infos = Array.Empty<EncounterTableInfo>();
            ids = new HashSet<string>(StringComparer.Ordinal);
            if (!float.IsFinite(rules.EncounterRateMultiplier) || rules.EncounterRateMultiplier < 0.1f || rules.EncounterRateMultiplier > 5f)
            {
                error = new DataError(
                    !float.IsFinite(rules.EncounterRateMultiplier) ? DataErrorKind.NonFiniteValue : DataErrorKind.OutOfRange,
                    "Rules.EncounterRateMultiplier",
                    "遭遇率の倍率は有限な 0.1〜5.0 の値で指定してください。");
                return false;
            }

            if (defs == null || defs.Length == 0)
            {
                error = new DataError(DataErrorKind.EmptyCollection, "EncounterTables", "遭遇表の定義が空です。");
                return false;
            }

            var result = new EncounterTableInfo[defs.Length];
            for (var i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                var loc = $"EncounterTables[{i}]";
                if (!GameData.IsValidId(d.Id))
                {
                    error = new DataError(DataErrorKind.InvalidId, loc, $"不正な遭遇表 ID です: '{d.Id}'");
                    return false;
                }

                if (!ids.Add(d.Id))
                {
                    error = new DataError(DataErrorKind.DuplicateId, loc, $"遭遇表 ID が重複しています: '{d.Id}'");
                    return false;
                }

                if (d.RatePercent < 1 || d.RatePercent > 100 || d.GraceSteps < 0 || d.GraceSteps > 50)
                {
                    error = new DataError(DataErrorKind.OutOfRange, loc, "遭遇確率（1〜100）または猶予歩数（0〜50）が範囲外です。");
                    return false;
                }

                if (d.Entries == null || d.Entries.Length == 0)
                {
                    error = new DataError(DataErrorKind.EmptyCollection, $"{loc}.Entries", "遭遇表の候補が空です。");
                    return false;
                }

                var entries = new EncounterEntryInfo[d.Entries.Length];
                for (var k = 0; k < d.Entries.Length; k++)
                {
                    var e = d.Entries[k];
                    if (e.Weight <= 0)
                    {
                        error = new DataError(DataErrorKind.OutOfRange, $"{loc}.Entries[{k}]", "遭遇候補の重みは 1 以上である必要があります。");
                        return false;
                    }

                    if (!groupById.TryGetValue(e.GroupId, out var group))
                    {
                        error = new DataError(DataErrorKind.UnresolvedReference, $"{loc}.Entries[{k}].GroupId", $"未知の敵グループ ID '{e.GroupId}' を参照しています。");
                        return false;
                    }

                    if (group.IsBossGroup)
                    {
                        error = new DataError(DataErrorKind.OutOfRange, $"{loc}.Entries[{k}].GroupId", "通常遭遇表にボスグループは入れられません。");
                        return false;
                    }

                    entries[k] = new EncounterEntryInfo(e.GroupId, e.Weight);
                }

                var scaled = (int)((d.RatePercent * rules.EncounterRateMultiplier) + 0.5f);
                var effective = Math.Clamp(scaled, 1, 100);
                result[i] = new EncounterTableInfo(i, d.Id, d.RatePercent, effective, d.GraceSteps, entries);
            }

            infos = result;
            error = null;
            return true;
        }

        private static bool ValidateShops(
            ShopDef[] defs,
            HashSet<string> itemIds,
            Dictionary<string, EquipInfo> equipById,
            out ShopInfo[] infos,
            out HashSet<string> ids,
            out DataError? error)
        {
            infos = Array.Empty<ShopInfo>();
            ids = new HashSet<string>(StringComparer.Ordinal);
            if (defs == null || defs.Length == 0)
            {
                error = new DataError(DataErrorKind.EmptyCollection, "Shops", "店の定義が空です。");
                return false;
            }

            var result = new ShopInfo[defs.Length];
            for (var i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                var loc = $"Shops[{i}]";
                if (!GameData.IsValidId(d.Id))
                {
                    error = new DataError(DataErrorKind.InvalidId, loc, $"不正な店 ID です: '{d.Id}'");
                    return false;
                }

                if (!ids.Add(d.Id))
                {
                    error = new DataError(DataErrorKind.DuplicateId, loc, $"店 ID が重複しています: '{d.Id}'");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(d.Name))
                {
                    error = new DataError(DataErrorKind.OutOfRange, loc, "店の名前が空です。");
                    return false;
                }

                if (d.ProductIds == null || d.ProductIds.Length == 0)
                {
                    error = new DataError(DataErrorKind.EmptyCollection, $"{loc}.ProductIds", "店の品揃えが空です。");
                    return false;
                }

                var seen = new HashSet<string>(StringComparer.Ordinal);
                var products = new string[d.ProductIds.Length];
                for (var k = 0; k < d.ProductIds.Length; k++)
                {
                    var pid = d.ProductIds[k];
                    if (!itemIds.Contains(pid) && !equipById.ContainsKey(pid))
                    {
                        error = new DataError(DataErrorKind.UnresolvedReference, $"{loc}.ProductIds[{k}]", $"未知の商品 ID '{pid}' を参照しています。");
                        return false;
                    }

                    if (!seen.Add(pid))
                    {
                        error = new DataError(DataErrorKind.DuplicateId, $"{loc}.ProductIds[{k}]", $"店の商品 '{pid}' が重複しています。");
                        return false;
                    }

                    products[k] = pid;
                }

                result[i] = new ShopInfo(i, d.Id, d.Name, products);
            }

            infos = result;
            error = null;
            return true;
        }

        private static bool ValidateMaps(
            MapDef[] defs,
            HashSet<string> itemIds,
            Dictionary<string, EquipInfo> equipById,
            HashSet<string> encounterIds,
            HashSet<string> shopIds,
            Dictionary<string, EnemyGroupInfo> groupById,
            RuleConfig rules,
            out FieldMap[] maps,
            out Dictionary<string, FieldMap> mapById,
            out string[] knownFlags,
            out DataError? error)
        {
            maps = Array.Empty<FieldMap>();
            mapById = new Dictionary<string, FieldMap>(StringComparer.Ordinal);
            knownFlags = Array.Empty<string>();

            if (defs == null || defs.Length == 0)
            {
                error = new DataError(DataErrorKind.EmptyCollection, "Maps", "地図の定義が空です。");
                return false;
            }

            if (!GameData.IsValidId(rules.BossDefeatedFlag))
            {
                error = new DataError(DataErrorKind.InvalidId, "Rules.BossDefeatedFlag", "ボス撃破フラグ ID が不正です。");
                return false;
            }

            var flagSet = new SortedSet<string>(StringComparer.Ordinal) { rules.BossDefeatedFlag };
            var uniqueObjectFlags = new HashSet<string>(StringComparer.Ordinal);
            var rewardFlags = new HashSet<string>(StringComparer.Ordinal) { rules.BossDefeatedFlag };

            var built = new FieldMap[defs.Length];
            for (var i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                var loc = $"Maps[{i}]";
                if (!GameData.IsValidId(d.Id))
                {
                    error = new DataError(DataErrorKind.InvalidId, loc, $"不正な地図 ID です: '{d.Id}'");
                    return false;
                }

                if (mapById.ContainsKey(d.Id))
                {
                    error = new DataError(DataErrorKind.DuplicateId, loc, $"地図 ID が重複しています: '{d.Id}'");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(d.Name))
                {
                    error = new DataError(DataErrorKind.OutOfRange, loc, "地図名が空です。");
                    return false;
                }

                if (d.IsSafeZone && d.EncounterTableId.Length > 0)
                {
                    error = new DataError(DataErrorKind.InvalidMap, loc, "安全地帯の地図に遭遇表は設定できません。");
                    return false;
                }

                if (d.EncounterTableId.Length > 0 && !encounterIds.Contains(d.EncounterTableId))
                {
                    error = new DataError(DataErrorKind.UnresolvedReference, $"{loc}.EncounterTableId", $"未知の遭遇表 ID '{d.EncounterTableId}' を参照しています。");
                    return false;
                }

                if (!ParseGrid(d.GridText, loc, out var tiles, out var chestCells, out var doorCells, out error))
                {
                    return false;
                }

                var width = tiles.GetLength(0);
                var height = tiles.GetLength(1);

                var chests = new ChestInfo[d.Chests.Length];
                var seenChests = new HashSet<(int X, int Y)>();
                for (var k = 0; k < d.Chests.Length; k++)
                {
                    var c = d.Chests[k];
                    var cloc = $"{loc}.Chests[{k}]";
                    if (c.X < 0 || c.X >= width || c.Y < 0 || c.Y >= height || tiles[c.X, c.Y] != TileTerrain.Chest)
                    {
                        error = new DataError(DataErrorKind.InvalidMap, cloc, $"宝箱 ({c.X},{c.Y}) が地図上の 'C' タイルに置かれていません。");
                        return false;
                    }

                    if (!seenChests.Add((c.X, c.Y)))
                    {
                        error = new DataError(DataErrorKind.DuplicateId, cloc, $"同じ座標 ({c.X},{c.Y}) に複数の宝箱があります。");
                        return false;
                    }

                    if (!GameData.IsValidId(c.OpenedFlag))
                    {
                        error = new DataError(DataErrorKind.InvalidId, $"{cloc}.OpenedFlag", $"不正な宝箱フラグ ID です: '{c.OpenedFlag}'");
                        return false;
                    }

                    if (!uniqueObjectFlags.Add(c.OpenedFlag))
                    {
                        error = new DataError(DataErrorKind.DuplicateId, $"{cloc}.OpenedFlag", $"宝箱フラグ '{c.OpenedFlag}' が重複しています。");
                        return false;
                    }

                    flagSet.Add(c.OpenedFlag);

                    if (c.RewardGold < 0 || c.RewardGold > rules.MaxGold)
                    {
                        error = new DataError(DataErrorKind.OutOfRange, $"{cloc}.RewardGold", "宝箱の金額が範囲外です。");
                        return false;
                    }

                    if (c.RewardItemId.Length > 0 && !itemIds.Contains(c.RewardItemId))
                    {
                        error = new DataError(DataErrorKind.UnresolvedReference, $"{cloc}.RewardItemId", $"未知の道具 ID '{c.RewardItemId}' を参照しています。");
                        return false;
                    }

                    if (c.RewardEquipId.Length > 0 && !equipById.ContainsKey(c.RewardEquipId))
                    {
                        error = new DataError(DataErrorKind.UnresolvedReference, $"{cloc}.RewardEquipId", $"未知の装備 ID '{c.RewardEquipId}' を参照しています。");
                        return false;
                    }

                    if (c.RewardFlag.Length > 0)
                    {
                        if (!GameData.IsValidId(c.RewardFlag))
                        {
                            error = new DataError(DataErrorKind.InvalidId, $"{cloc}.RewardFlag", $"不正な報酬フラグ ID です: '{c.RewardFlag}'");
                            return false;
                        }

                        flagSet.Add(c.RewardFlag);
                        rewardFlags.Add(c.RewardFlag);
                    }

                    var hasReward = c.RewardItemId.Length > 0 || c.RewardEquipId.Length > 0 || c.RewardGold > 0 || c.RewardFlag.Length > 0;
                    if (!hasReward)
                    {
                        error = new DataError(DataErrorKind.EmptyChest, cloc, "宝箱の中身がありません。");
                        return false;
                    }

                    chests[k] = new ChestInfo(c);
                }

                if (seenChests.Count != chestCells)
                {
                    error = new DataError(DataErrorKind.InvalidMap, loc, "地図上の 'C' タイルの数と Chests 定義の数が一致しません。");
                    return false;
                }

                var doors = new LockedDoorInfo[d.Doors.Length];
                var seenDoors = new HashSet<(int X, int Y)>();
                for (var k = 0; k < d.Doors.Length; k++)
                {
                    var door = d.Doors[k];
                    var dloc = $"{loc}.Doors[{k}]";
                    if (door.X < 0 || door.X >= width || door.Y < 0 || door.Y >= height || tiles[door.X, door.Y] != TileTerrain.LockedDoor)
                    {
                        error = new DataError(DataErrorKind.InvalidMap, dloc, $"鍵扉 ({door.X},{door.Y}) が地図上の 'D' タイルに置かれていません。");
                        return false;
                    }

                    if (!seenDoors.Add((door.X, door.Y)))
                    {
                        error = new DataError(DataErrorKind.DuplicateId, dloc, $"同じ座標 ({door.X},{door.Y}) に複数の鍵扉があります。");
                        return false;
                    }

                    if (!GameData.IsValidId(door.RequiredKeyFlag) || !GameData.IsValidId(door.OpenedFlag) ||
                        string.Equals(door.RequiredKeyFlag, door.OpenedFlag, StringComparison.Ordinal))
                    {
                        error = new DataError(DataErrorKind.InvalidId, dloc, "鍵扉のフラグ ID が不正です。");
                        return false;
                    }

                    if (!uniqueObjectFlags.Add(door.OpenedFlag))
                    {
                        error = new DataError(DataErrorKind.DuplicateId, $"{dloc}.OpenedFlag", $"扉開閉フラグ '{door.OpenedFlag}' が重複しています。");
                        return false;
                    }

                    flagSet.Add(door.RequiredKeyFlag);
                    flagSet.Add(door.OpenedFlag);
                    doors[k] = new LockedDoorInfo(door);
                }

                if (seenDoors.Count != doorCells)
                {
                    error = new DataError(DataErrorKind.InvalidMap, loc, "地図上の 'D' タイルの数と Doors 定義の数が一致しません。");
                    return false;
                }

                var portals = new PortalInfo[d.Portals.Length];
                var seenPortals = new HashSet<(int X, int Y)>();
                for (var k = 0; k < d.Portals.Length; k++)
                {
                    var p = d.Portals[k];
                    var ploc = $"{loc}.Portals[{k}]";
                    if (p.X < 0 || p.X >= width || p.Y < 0 || p.Y >= height || !FieldMap.IsBaseTerrainWalkable(tiles[p.X, p.Y]))
                    {
                        error = new DataError(DataErrorKind.InvalidMap, ploc, $"扉・階段 ({p.X},{p.Y}) が歩行可能タイル上にありません。");
                        return false;
                    }

                    if (!seenPortals.Add((p.X, p.Y)))
                    {
                        error = new DataError(DataErrorKind.DuplicateId, ploc, $"同じ座標 ({p.X},{p.Y}) に複数の移動先があります。");
                        return false;
                    }

                    portals[k] = new PortalInfo(p);
                }

                var npcs = new NpcInfo[d.Npcs.Length];
                var npcIds = new HashSet<string>(StringComparer.Ordinal);
                var seenNpcs = new HashSet<(int X, int Y)>();
                for (var k = 0; k < d.Npcs.Length; k++)
                {
                    var n = d.Npcs[k];
                    var nloc = $"{loc}.Npcs[{k}]";
                    if (!GameData.IsValidId(n.Id) || !npcIds.Add(n.Id))
                    {
                        error = new DataError(DataErrorKind.InvalidId, nloc, $"不正または重複した NPC ID です: '{n.Id}'");
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(n.Name))
                    {
                        error = new DataError(DataErrorKind.OutOfRange, nloc, "NPC 名が空です。");
                        return false;
                    }

                    if (n.X < 0 || n.X >= width || n.Y < 0 || n.Y >= height || !FieldMap.IsBaseTerrainWalkable(tiles[n.X, n.Y]) || seenPortals.Contains((n.X, n.Y)) || !seenNpcs.Add((n.X, n.Y)))
                    {
                        error = new DataError(DataErrorKind.InvalidMap, nloc, $"NPC ({n.X},{n.Y}) の配置が不正です。");
                        return false;
                    }

                    if (n.Facility == FacilityKind.Shop && !shopIds.Contains(n.ShopId))
                    {
                        error = new DataError(DataErrorKind.UnresolvedReference, $"{nloc}.ShopId", $"未知の店 ID '{n.ShopId}' を参照しています。");
                        return false;
                    }

                    if (n.ConditionFlag.Length > 0)
                    {
                        if (!GameData.IsValidId(n.ConditionFlag))
                        {
                            error = new DataError(DataErrorKind.InvalidId, $"{nloc}.ConditionFlag", $"不正な条件フラグ ID です: '{n.ConditionFlag}'");
                            return false;
                        }

                        flagSet.Add(n.ConditionFlag);
                    }

                    npcs[k] = new NpcInfo(n);
                }

                var boss = new BossTriggerInfo(d.Boss);
                if (boss.Enabled)
                {
                    if (!groupById.TryGetValue(boss.BossGroupId, out var bgroup) || !bgroup.IsBossGroup)
                    {
                        error = new DataError(DataErrorKind.UnresolvedReference, $"{loc}.Boss.BossGroupId", $"有効なボスグループ '{boss.BossGroupId}' が見つかりません。");
                        return false;
                    }

                    if (!GameData.IsValidId(boss.DefeatedFlag))
                    {
                        error = new DataError(DataErrorKind.InvalidId, $"{loc}.Boss.DefeatedFlag", "ボス撃破フラグ ID が不正です。");
                        return false;
                    }

                    if (boss.TriggerX < 0 || boss.TriggerX >= width || boss.TriggerY < 0 || boss.TriggerY >= height || !FieldMap.IsBaseTerrainWalkable(tiles[boss.TriggerX, boss.TriggerY]) ||
                        boss.BossTileX < 0 || boss.BossTileX >= width || boss.BossTileY < 0 || boss.BossTileY >= height ||
                        (boss.TriggerX == boss.BossTileX && boss.TriggerY == boss.BossTileY))
                    {
                        error = new DataError(DataErrorKind.InvalidMap, $"{loc}.Boss", "ボスイベントの座標が不正です。");
                        return false;
                    }

                    flagSet.Add(boss.DefeatedFlag);
                }

                var map = new FieldMap(i, d.Id, d.Name, d.IsSafeZone, d.EncounterTableId, tiles, portals, chests, doors, npcs, boss);
                built[i] = map;
                mapById[d.Id] = map;
            }

            // 全地図の扉の行き先と鍵の入手手段を照合する
            for (var i = 0; i < built.Length; i++)
            {
                var m = built[i];
                for (var k = 0; k < m.Portals.Count; k++)
                {
                    var p = m.Portals[k];
                    if (!mapById.TryGetValue(p.TargetMapId, out var dest))
                    {
                        error = new DataError(DataErrorKind.UnresolvedReference, $"Maps[{i}].Portals[{k}].TargetMapId", $"扉の行き先の地図 '{p.TargetMapId}' が存在しません。");
                        return false;
                    }

                    if (!dest.IsWalkable(p.TargetX, p.TargetY, null))
                    {
                        error = new DataError(DataErrorKind.InvalidMap, $"Maps[{i}].Portals[{k}]", $"扉の行き先 {p.TargetMapId}({p.TargetX},{p.TargetY}) が通行不能です。");
                        return false;
                    }
                }

                for (var k = 0; k < m.Doors.Count; k++)
                {
                    var door = m.Doors[k];
                    if (!rewardFlags.Contains(door.RequiredKeyFlag))
                    {
                        error = new DataError(DataErrorKind.UnresolvedReference, $"Maps[{i}].Doors[{k}].RequiredKeyFlag", $"鍵フラグ '{door.RequiredKeyFlag}' を入手できる宝箱が存在しません。");
                        return false;
                    }
                }
            }

            var flagArray = new string[flagSet.Count];
            flagSet.CopyTo(flagArray);
            maps = built;
            knownFlags = flagArray;
            error = null;
            return true;
        }

        private static bool ParseGrid(
            string text,
            string loc,
            out TileTerrain[,] tiles,
            out int chestCells,
            out int doorCells,
            out DataError? error)
        {
            tiles = new TileTerrain[0, 0];
            chestCells = 0;
            doorCells = 0;

            if (string.IsNullOrWhiteSpace(text))
            {
                error = new DataError(DataErrorKind.InvalidMap, $"{loc}.GridText", "地図の文字列が空です。");
                return false;
            }

            var rawLines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var lines = new List<string>();
            for (var i = 0; i < rawLines.Length; i++)
            {
                var line = rawLines[i].Trim();
                if (line.Length > 0)
                {
                    lines.Add(line);
                }
            }

            if (lines.Count < 5 || lines.Count > 64)
            {
                error = new DataError(DataErrorKind.InvalidMap, $"{loc}.GridText", "地図の高さは 5〜64 行で指定してください。");
                return false;
            }

            var width = lines[0].Length;
            if (width < 5 || width > 64)
            {
                error = new DataError(DataErrorKind.InvalidMap, $"{loc}.GridText", "地図の幅は 5〜64 列で指定してください。");
                return false;
            }

            var grid = new TileTerrain[width, lines.Count];
            for (var y = 0; y < lines.Count; y++)
            {
                var row = lines[y];
                if (row.Length != width)
                {
                    error = new DataError(DataErrorKind.InvalidMap, $"{loc}.GridText", $"行 {y} の幅が一致しません。");
                    return false;
                }

                for (var x = 0; x < width; x++)
                {
                    var ch = row[x];
                    TileTerrain t;
                    switch (ch)
                    {
                        case '.': t = TileTerrain.Floor; break;
                        case ',': t = TileTerrain.Grass; break;
                        case '+': t = TileTerrain.Bridge; break;
                        case 'S': t = TileTerrain.Stairs; break;
                        case '#': t = TileTerrain.Wall; break;
                        case '~': t = TileTerrain.Water; break;
                        case 'D':
                            t = TileTerrain.LockedDoor;
                            doorCells++;
                            break;
                        case 'C':
                            t = TileTerrain.Chest;
                            chestCells++;
                            break;
                        default:
                            error = new DataError(DataErrorKind.InvalidMap, $"{loc}.GridText", $"未知の地図文字 '{ch}' が含まれています。");
                            return false;
                    }

                    grid[x, y] = t;
                }
            }

            tiles = grid;
            error = null;
            return true;
        }

        private static bool ValidateRules(
            RuleConfig rules,
            HashSet<string> classIds,
            HashSet<string> itemIds,
            Dictionary<string, FieldMap> mapById,
            out RuleInfo? info,
            out DataError? error)
        {
            info = null;
            if (rules.TickRate < 10 || rules.TickRate > 240 ||
                rules.StepTicks < 1 || rules.StepTicks > 60 ||
                rules.MaxGold < 100 || rules.MaxGold > 9999999 ||
                rules.StartGold < 0 || rules.StartGold > rules.MaxGold ||
                rules.MaxItemStack < 1 || rules.MaxItemStack > 99 ||
                rules.InnCost < 1 || rules.InnCost > rules.MaxGold ||
                rules.ReviveCostPerLevel < 1 || rules.ReviveCostPerLevel > rules.MaxGold ||
                rules.FleeMinPercent < 0 || rules.FleeMaxPercent > 100 || rules.FleeMinPercent > rules.FleeMaxPercent ||
                rules.FleeBasePercent < 1 || rules.FleeBasePercent > 200)
            {
                error = new DataError(DataErrorKind.OutOfRange, "Rules", "ルール設定の数値が許容範囲外です。");
                return false;
            }

            if (rules.PartyClassIds == null || rules.PartyClassIds.Length != 3)
            {
                error = new DataError(DataErrorKind.OutOfRange, "Rules.PartyClassIds", "パーティは 3 人固定で指定してください。");
                return false;
            }

            for (var i = 0; i < rules.PartyClassIds.Length; i++)
            {
                if (!classIds.Contains(rules.PartyClassIds[i]))
                {
                    error = new DataError(DataErrorKind.UnresolvedReference, $"Rules.PartyClassIds[{i}]", $"未知の職業 ID '{rules.PartyClassIds[i]}' です。");
                    return false;
                }
            }

            if (rules.StartItems != null)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < rules.StartItems.Length; i++)
                {
                    var s = rules.StartItems[i];
                    if (!itemIds.Contains(s.ItemId))
                    {
                        error = new DataError(DataErrorKind.UnresolvedReference, $"Rules.StartItems[{i}]", $"未知の初期道具 '{s.ItemId}' です。");
                        return false;
                    }

                    if (!seen.Add(s.ItemId) || s.Count < 1 || s.Count > rules.MaxItemStack)
                    {
                        error = new DataError(DataErrorKind.OutOfRange, $"Rules.StartItems[{i}]", "初期道具の個数または重複が不正です。");
                        return false;
                    }
                }
            }

            if (!mapById.TryGetValue(rules.StartMapId, out var startMap) || !startMap.IsWalkable(rules.StartX, rules.StartY, null))
            {
                error = new DataError(DataErrorKind.InvalidMap, "Rules.StartMapId", "開始位置が有効な歩行可能タイルではありません。");
                return false;
            }

            if (!mapById.TryGetValue(rules.ChurchMapId, out var churchMap) || !churchMap.IsWalkable(rules.ChurchRespawnX, rules.ChurchRespawnY, null))
            {
                error = new DataError(DataErrorKind.InvalidMap, "Rules.ChurchMapId", "教会の復帰位置が有効な歩行可能タイルではありません。");
                return false;
            }

            var hasChurch = false;
            for (var i = 0; i < churchMap.Npcs.Count; i++)
            {
                if (churchMap.Npcs[i].Facility == FacilityKind.Church)
                {
                    hasChurch = true;
                    break;
                }
            }

            if (!hasChurch)
            {
                error = new DataError(DataErrorKind.InvalidMap, "Rules.ChurchMapId", "復帰先の地図に教会がありません。");
                return false;
            }

            info = new RuleInfo(rules);
            error = null;
            return true;
        }

        private static uint ComputeFingerprint(
            RuleInfo rules,
            ExpTableInfo exp,
            ClassInfo[] classes,
            SpellInfo[] spells,
            ItemInfo[] items,
            EquipInfo[] equips,
            EnemyInfo[] enemies,
            EnemyGroupInfo[] groups,
            EncounterTableInfo[] encounters,
            ShopInfo[] shops,
            FieldMap[] maps,
            string[] knownFlags)
        {
            var h = StateHash.Start();
            h.Mix(rules.TickRate);
            h.Mix(rules.StepTicks);
            h.Mix(rules.StartGold);
            h.Mix(rules.MaxGold);
            h.Mix(rules.MaxItemStack);
            h.Mix(rules.InnCost);
            h.Mix(rules.ReviveCostPerLevel);
            h.Mix(rules.FleeBasePercent);
            h.Mix(rules.FleeMinPercent);
            h.Mix(rules.FleeMaxPercent);
            h.Mix(BitConverter.SingleToInt32Bits(rules.EncounterRateMultiplier));
            MixText(ref h, rules.StartMapId);
            h.Mix(rules.StartX);
            h.Mix(rules.StartY);
            MixText(ref h, rules.ChurchMapId);
            h.Mix(rules.ChurchRespawnX);
            h.Mix(rules.ChurchRespawnY);
            MixText(ref h, rules.BossDefeatedFlag);
            for (var i = 0; i < rules.PartyClassIds.Count; i++)
            {
                MixText(ref h, rules.PartyClassIds[i]);
            }

            for (var i = 0; i < rules.StartItems.Count; i++)
            {
                MixText(ref h, rules.StartItems[i].ItemId);
                h.Mix(rules.StartItems[i].Count);
            }

            h.Mix(exp.MaxLevel);
            for (var i = 0; i < exp.RequiredExp.Count; i++)
            {
                h.Mix(exp.RequiredExp[i]);
            }

            for (var i = 0; i < classes.Length; i++)
            {
                var c = classes[i];
                MixText(ref h, c.Id);
                h.Mix(c.BaseHp);
                h.Mix(c.BaseMp);
                h.Mix(c.BaseAtk);
                h.Mix(c.BaseDef);
                h.Mix(c.BaseAgi);
                h.Mix(c.HpGain);
                h.Mix(c.MpGain);
                h.Mix(c.AtkGain);
                h.Mix(c.DefGain);
                h.Mix(c.AgiGain);
                MixText(ref h, c.StartWeaponId);
                MixText(ref h, c.StartArmorId);
                for (var k = 0; k < c.Spells.Count; k++)
                {
                    h.Mix(c.Spells[k].Level);
                    MixText(ref h, c.Spells[k].SpellId);
                }
            }

            for (var i = 0; i < spells.Length; i++)
            {
                var s = spells[i];
                MixText(ref h, s.Id);
                h.Mix((int)s.Kind);
                h.Mix((int)s.Element);
                h.Mix(s.MpCost);
                h.Mix(s.Power);
                h.Mix(s.UsableInField);
            }

            for (var i = 0; i < items.Length; i++)
            {
                var it = items[i];
                MixText(ref h, it.Id);
                h.Mix((int)it.Kind);
                h.Mix(it.Power);
                h.Mix(it.Price);
                h.Mix(it.UsableInField);
            }

            for (var i = 0; i < equips.Length; i++)
            {
                var eq = equips[i];
                MixText(ref h, eq.Id);
                h.Mix((int)eq.Slot);
                h.Mix(eq.AtkBonus);
                h.Mix(eq.DefBonus);
                h.Mix(eq.AgiBonus);
                h.Mix(eq.Price);
                for (var k = 0; k < eq.AllowedClassIds.Count; k++)
                {
                    MixText(ref h, eq.AllowedClassIds[k]);
                }
            }

            for (var i = 0; i < enemies.Length; i++)
            {
                var e = enemies[i];
                MixText(ref h, e.Id);
                h.Mix(e.IsBoss);
                h.Mix(e.MaxHp);
                h.Mix(e.MaxMp);
                h.Mix(e.Atk);
                h.Mix(e.Def);
                h.Mix(e.Agi);
                h.Mix(e.ExpReward);
                h.Mix(e.GoldReward);
                h.Mix((int)e.WeakElement);
                for (var k = 0; k < e.Actions.Count; k++)
                {
                    h.Mix((int)e.Actions[k].Kind);
                    MixText(ref h, e.Actions[k].SpellId);
                    h.Mix(e.Actions[k].Weight);
                }

                for (var k = 0; k < e.Phase2Actions.Count; k++)
                {
                    h.Mix((int)e.Phase2Actions[k].Kind);
                    MixText(ref h, e.Phase2Actions[k].SpellId);
                    h.Mix(e.Phase2Actions[k].Weight);
                }
            }

            for (var i = 0; i < groups.Length; i++)
            {
                MixText(ref h, groups[i].Id);
                for (var k = 0; k < groups[i].EnemyIds.Count; k++)
                {
                    MixText(ref h, groups[i].EnemyIds[k]);
                }
            }

            for (var i = 0; i < encounters.Length; i++)
            {
                var t = encounters[i];
                MixText(ref h, t.Id);
                h.Mix(t.RatePercent);
                h.Mix(t.EffectiveRatePercent);
                h.Mix(t.GraceSteps);
                for (var k = 0; k < t.Entries.Count; k++)
                {
                    MixText(ref h, t.Entries[k].GroupId);
                    h.Mix(t.Entries[k].Weight);
                }
            }

            for (var i = 0; i < shops.Length; i++)
            {
                MixText(ref h, shops[i].Id);
                for (var k = 0; k < shops[i].ProductIds.Count; k++)
                {
                    MixText(ref h, shops[i].ProductIds[k]);
                }
            }

            for (var i = 0; i < maps.Length; i++)
            {
                var m = maps[i];
                MixText(ref h, m.Id);
                h.Mix(m.IsSafeZone);
                MixText(ref h, m.EncounterTableId);
                h.Mix(m.Width);
                h.Mix(m.Height);
                for (var y = 0; y < m.Height; y++)
                {
                    for (var x = 0; x < m.Width; x++)
                    {
                        h.Mix((int)m.TerrainAt(x, y));
                    }
                }

                for (var k = 0; k < m.Portals.Count; k++)
                {
                    var p = m.Portals[k];
                    h.Mix(p.X);
                    h.Mix(p.Y);
                    MixText(ref h, p.TargetMapId);
                    h.Mix(p.TargetX);
                    h.Mix(p.TargetY);
                }

                for (var k = 0; k < m.Chests.Count; k++)
                {
                    var c = m.Chests[k];
                    h.Mix(c.X);
                    h.Mix(c.Y);
                    MixText(ref h, c.OpenedFlag);
                    MixText(ref h, c.RewardItemId);
                    MixText(ref h, c.RewardEquipId);
                    h.Mix(c.RewardGold);
                    MixText(ref h, c.RewardFlag);
                }

                for (var k = 0; k < m.Doors.Count; k++)
                {
                    var d = m.Doors[k];
                    h.Mix(d.X);
                    h.Mix(d.Y);
                    MixText(ref h, d.RequiredKeyFlag);
                    MixText(ref h, d.OpenedFlag);
                }

                for (var k = 0; k < m.Npcs.Count; k++)
                {
                    var n = m.Npcs[k];
                    MixText(ref h, n.Id);
                    h.Mix(n.X);
                    h.Mix(n.Y);
                    h.Mix((int)n.Facility);
                    MixText(ref h, n.ShopId);
                    MixText(ref h, n.ConditionFlag);
                }

                h.Mix(m.Boss.Enabled);
                if (m.Boss.Enabled)
                {
                    h.Mix(m.Boss.TriggerX);
                    h.Mix(m.Boss.TriggerY);
                    h.Mix(m.Boss.BossTileX);
                    h.Mix(m.Boss.BossTileY);
                    MixText(ref h, m.Boss.BossGroupId);
                    MixText(ref h, m.Boss.DefeatedFlag);
                }
            }

            for (var i = 0; i < knownFlags.Length; i++)
            {
                MixText(ref h, knownFlags[i]);
            }

            return h.Value;
        }

        private static void MixText(ref StateHash hash, string text)
        {
            hash.Mix(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                hash.Mix(text[i]);
            }
        }
    }
}
