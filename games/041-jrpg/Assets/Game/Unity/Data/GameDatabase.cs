#nullable enable
using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity.Data
{
    public sealed class GameDatabase : ScriptableObject
    {
        public RuleConfig BaseRules = new();
        public ExpTableDef ExpTable = new();
        public ClassAsset[] Classes = Array.Empty<ClassAsset>();
        public SpellAsset[] Spells = Array.Empty<SpellAsset>();
        public ItemAsset[] Items = Array.Empty<ItemAsset>();
        public EquipAsset[] Equips = Array.Empty<EquipAsset>();
        public EnemyAsset[] Enemies = Array.Empty<EnemyAsset>();
        public EnemyGroupAsset[] EnemyGroups = Array.Empty<EnemyGroupAsset>();
        public EncounterAsset[] EncounterTables = Array.Empty<EncounterAsset>();
        public ShopAsset[] Shops = Array.Empty<ShopAsset>();
        public MapAsset[] Maps = Array.Empty<MapAsset>();

        public GameDataDef ToRawDef(EmberlockSettings? settings = null)
        {
            var rules = BaseRules == null ? new RuleConfig() : BaseRules.Copy();
            if (settings != null)
            {
                settings.Validate();
                rules.StepTicks = settings.StepTicks;
                rules.EncounterRateMultiplier = settings.EncounterRateMultiplier;
            }

            return new GameDataDef
            {
                Rules = rules,
                ExpTable = ExpTable == null ? new ExpTableDef() : ExpTable.Copy(),
                Classes = Classes == null ? Array.Empty<ClassDef>() : Array.ConvertAll(Classes, a => a == null ? new ClassDef() : a.Def.Copy()),
                Spells = Spells == null ? Array.Empty<SpellDef>() : Array.ConvertAll(Spells, a => a == null ? new SpellDef() : a.Def.Copy()),
                Items = Items == null ? Array.Empty<ItemDef>() : Array.ConvertAll(Items, a => a == null ? new ItemDef() : a.Def.Copy()),
                Equips = Equips == null ? Array.Empty<EquipDef>() : Array.ConvertAll(Equips, a => a == null ? new EquipDef() : a.Def.Copy()),
                Enemies = Enemies == null ? Array.Empty<EnemyDef>() : Array.ConvertAll(Enemies, a => a == null ? new EnemyDef() : a.Def.Copy()),
                EnemyGroups = EnemyGroups == null ? Array.Empty<EnemyGroupDef>() : Array.ConvertAll(EnemyGroups, a => a == null ? new EnemyGroupDef() : a.Def.Copy()),
                EncounterTables = EncounterTables == null ? Array.Empty<EncounterTableDef>() : Array.ConvertAll(EncounterTables, a => a == null ? new EncounterTableDef() : a.Def.Copy()),
                Shops = Shops == null ? Array.Empty<ShopDef>() : Array.ConvertAll(Shops, a => a == null ? new ShopDef() : a.Def.Copy()),
                Maps = Maps == null ? Array.Empty<MapDef>() : Array.ConvertAll(Maps, m => m == null ? new MapDef() : m.ToMapDef()),
            };
        }

        public GameData BuildValidated(EmberlockSettings? settings = null)
        {
            return GameData.Validate(ToRawDef(settings));
        }

        public Sprite? FindEnemySprite(string enemyId)
        {
            if (Enemies == null)
            {
                return null;
            }

            for (var i = 0; i < Enemies.Length; i++)
            {
                var asset = Enemies[i];
                if (asset != null && string.Equals(asset.Def.Id, enemyId, StringComparison.Ordinal))
                {
                    return asset.BattleSprite;
                }
            }

            return null;
        }
    }
}
