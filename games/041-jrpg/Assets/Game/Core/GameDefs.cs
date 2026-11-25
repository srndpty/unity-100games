using System;

namespace Game.Core
{
    public enum Direction
    {
        Down = 0,
        Left = 1,
        Right = 2,
        Up = 3,
    }

    public static class DirectionOps
    {
        public static int DeltaX(this Direction direction) => direction switch
        {
            Direction.Left => -1,
            Direction.Right => 1,
            _ => 0,
        };

        public static int DeltaY(this Direction direction) => direction switch
        {
            Direction.Up => -1,
            Direction.Down => 1,
            _ => 0,
        };

        public static bool IsDefined(Direction direction) =>
            direction == Direction.Down ||
            direction == Direction.Left ||
            direction == Direction.Right ||
            direction == Direction.Up;
    }

    public enum SpellKind
    {
        SingleAttack = 0,
        GroupAttack = 1,
        SingleHeal = 2,
        AllHeal = 3,
        Revive = 4,
    }

    public enum SpellElement
    {
        None = 0,
        Fire = 1,
        Frost = 2,
        Light = 3,
    }

    public enum ItemKind
    {
        HealHp = 0,
        RestoreMp = 1,
    }

    public enum EquipSlot
    {
        Weapon = 0,
        Armor = 1,
    }

    public enum EnemyActionKind
    {
        Attack = 0,
        HeavyAttack = 1,
        Spell = 2,
    }

    public enum FacilityKind
    {
        None = 0,
        Inn = 1,
        Shop = 2,
        Church = 3,
    }

    public enum TileTerrain
    {
        Floor = 0,
        Grass = 1,
        Bridge = 2,
        Stairs = 3,
        Wall = 4,
        Water = 5,
        LockedDoor = 6,
        Chest = 7,
    }

    [Serializable]
    public sealed class LearnableSpellDef
    {
        public int Level = 1;
        public string SpellId = string.Empty;

        public LearnableSpellDef Copy() => new() { Level = Level, SpellId = SpellId ?? string.Empty };
    }

    [Serializable]
    public sealed class ClassDef
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public string HeroName = string.Empty;
        public int BaseHp = 30;
        public int BaseMp;
        public int BaseAtk = 10;
        public int BaseDef = 8;
        public int BaseAgi = 8;
        public int HpGain = 6;
        public int MpGain;
        public int AtkGain = 3;
        public int DefGain = 2;
        public int AgiGain = 2;
        public string StartWeaponId = string.Empty;
        public string StartArmorId = string.Empty;
        public LearnableSpellDef[] Spells = Array.Empty<LearnableSpellDef>();

        public ClassDef Copy()
        {
            var spells = Spells == null ? Array.Empty<LearnableSpellDef>() : Array.ConvertAll(Spells, s => s == null ? new LearnableSpellDef() : s.Copy());
            return new ClassDef
            {
                Id = Id ?? string.Empty,
                Name = Name ?? string.Empty,
                HeroName = HeroName ?? string.Empty,
                BaseHp = BaseHp,
                BaseMp = BaseMp,
                BaseAtk = BaseAtk,
                BaseDef = BaseDef,
                BaseAgi = BaseAgi,
                HpGain = HpGain,
                MpGain = MpGain,
                AtkGain = AtkGain,
                DefGain = DefGain,
                AgiGain = AgiGain,
                StartWeaponId = StartWeaponId ?? string.Empty,
                StartArmorId = StartArmorId ?? string.Empty,
                Spells = spells,
            };
        }
    }

    [Serializable]
    public sealed class SpellDef
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public string Description = string.Empty;
        public SpellKind Kind;
        public SpellElement Element;
        public int MpCost = 4;
        public int Power = 20;
        public bool UsableInField;

        public SpellDef Copy() => new()
        {
            Id = Id ?? string.Empty,
            Name = Name ?? string.Empty,
            Description = Description ?? string.Empty,
            Kind = Kind,
            Element = Element,
            MpCost = MpCost,
            Power = Power,
            UsableInField = UsableInField,
        };
    }

    [Serializable]
    public sealed class ItemDef
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public string Description = string.Empty;
        public ItemKind Kind;
        public int Power = 25;
        public int Price = 10;
        public bool UsableInField = true;

        public ItemDef Copy() => new()
        {
            Id = Id ?? string.Empty,
            Name = Name ?? string.Empty,
            Description = Description ?? string.Empty,
            Kind = Kind,
            Power = Power,
            Price = Price,
            UsableInField = UsableInField,
        };
    }

    [Serializable]
    public sealed class EquipDef
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public string Description = string.Empty;
        public EquipSlot Slot;
        public int AtkBonus;
        public int DefBonus;
        public int AgiBonus;
        public int Price = 20;
        public string[] AllowedClassIds = Array.Empty<string>();

        public EquipDef Copy() => new()
        {
            Id = Id ?? string.Empty,
            Name = Name ?? string.Empty,
            Description = Description ?? string.Empty,
            Slot = Slot,
            AtkBonus = AtkBonus,
            DefBonus = DefBonus,
            AgiBonus = AgiBonus,
            Price = Price,
            AllowedClassIds = AllowedClassIds == null ? Array.Empty<string>() : Array.ConvertAll(AllowedClassIds, c => c ?? string.Empty),
        };
    }

    [Serializable]
    public sealed class ExpTableDef
    {
        public int MaxLevel = 10;
        public int[] RequiredExp = Array.Empty<int>();

        public ExpTableDef Copy() => new()
        {
            MaxLevel = MaxLevel,
            RequiredExp = RequiredExp == null ? Array.Empty<int>() : (int[])RequiredExp.Clone(),
        };
    }

    [Serializable]
    public sealed class EnemyActionEntry
    {
        public EnemyActionKind Kind;
        public string SpellId = string.Empty;
        public int Weight = 10;

        public EnemyActionEntry Copy() => new()
        {
            Kind = Kind,
            SpellId = SpellId ?? string.Empty,
            Weight = Weight,
        };
    }

    [Serializable]
    public sealed class EnemyDef
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public bool IsBoss;
        public int MaxHp = 20;
        public int MaxMp;
        public int Atk = 10;
        public int Def = 5;
        public int Agi = 8;
        public int ExpReward = 5;
        public int GoldReward = 8;
        public SpellElement WeakElement;
        public EnemyActionEntry[] Actions = Array.Empty<EnemyActionEntry>();
        public EnemyActionEntry[] Phase2Actions = Array.Empty<EnemyActionEntry>();

        public EnemyDef Copy() => new()
        {
            Id = Id ?? string.Empty,
            Name = Name ?? string.Empty,
            IsBoss = IsBoss,
            MaxHp = MaxHp,
            MaxMp = MaxMp,
            Atk = Atk,
            Def = Def,
            Agi = Agi,
            ExpReward = ExpReward,
            GoldReward = GoldReward,
            WeakElement = WeakElement,
            Actions = Actions == null ? Array.Empty<EnemyActionEntry>() : Array.ConvertAll(Actions, a => a == null ? new EnemyActionEntry() : a.Copy()),
            Phase2Actions = Phase2Actions == null ? Array.Empty<EnemyActionEntry>() : Array.ConvertAll(Phase2Actions, a => a == null ? new EnemyActionEntry() : a.Copy()),
        };
    }

    [Serializable]
    public sealed class EnemyGroupDef
    {
        public string Id = string.Empty;
        public string[] EnemyIds = Array.Empty<string>();

        public EnemyGroupDef Copy() => new()
        {
            Id = Id ?? string.Empty,
            EnemyIds = EnemyIds == null ? Array.Empty<string>() : Array.ConvertAll(EnemyIds, e => e ?? string.Empty),
        };
    }

    [Serializable]
    public sealed class EncounterEntry
    {
        public string GroupId = string.Empty;
        public int Weight = 10;

        public EncounterEntry Copy() => new()
        {
            GroupId = GroupId ?? string.Empty,
            Weight = Weight,
        };
    }

    [Serializable]
    public sealed class EncounterTableDef
    {
        public string Id = string.Empty;
        public int RatePercent = 15;
        public int GraceSteps = 4;
        public EncounterEntry[] Entries = Array.Empty<EncounterEntry>();

        public EncounterTableDef Copy() => new()
        {
            Id = Id ?? string.Empty,
            RatePercent = RatePercent,
            GraceSteps = GraceSteps,
            Entries = Entries == null ? Array.Empty<EncounterEntry>() : Array.ConvertAll(Entries, e => e == null ? new EncounterEntry() : e.Copy()),
        };
    }

    [Serializable]
    public sealed class ShopDef
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public string[] ProductIds = Array.Empty<string>();

        public ShopDef Copy() => new()
        {
            Id = Id ?? string.Empty,
            Name = Name ?? string.Empty,
            ProductIds = ProductIds == null ? Array.Empty<string>() : Array.ConvertAll(ProductIds, p => p ?? string.Empty),
        };
    }

    [Serializable]
    public sealed class PortalDef
    {
        public int X;
        public int Y;
        public string TargetMapId = string.Empty;
        public int TargetX;
        public int TargetY;

        public PortalDef Copy() => new()
        {
            X = X,
            Y = Y,
            TargetMapId = TargetMapId ?? string.Empty,
            TargetX = TargetX,
            TargetY = TargetY,
        };
    }

    [Serializable]
    public sealed class ChestDef
    {
        public int X;
        public int Y;
        public string OpenedFlag = string.Empty;
        public string RewardItemId = string.Empty;
        public string RewardEquipId = string.Empty;
        public int RewardGold;
        public string RewardFlag = string.Empty;
        public string RewardLabel = string.Empty;

        public ChestDef Copy() => new()
        {
            X = X,
            Y = Y,
            OpenedFlag = OpenedFlag ?? string.Empty,
            RewardItemId = RewardItemId ?? string.Empty,
            RewardEquipId = RewardEquipId ?? string.Empty,
            RewardGold = RewardGold,
            RewardFlag = RewardFlag ?? string.Empty,
            RewardLabel = RewardLabel ?? string.Empty,
        };
    }

    [Serializable]
    public sealed class LockedDoorDef
    {
        public int X;
        public int Y;
        public string RequiredKeyFlag = string.Empty;
        public string OpenedFlag = string.Empty;
        public string LockedMessage = string.Empty;
        public string UnlockedMessage = string.Empty;

        public LockedDoorDef Copy() => new()
        {
            X = X,
            Y = Y,
            RequiredKeyFlag = RequiredKeyFlag ?? string.Empty,
            OpenedFlag = OpenedFlag ?? string.Empty,
            LockedMessage = LockedMessage ?? string.Empty,
            UnlockedMessage = UnlockedMessage ?? string.Empty,
        };
    }

    [Serializable]
    public sealed class NpcDef
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public int X;
        public int Y;
        public FacilityKind Facility;
        public string ShopId = string.Empty;
        public string Dialogue = string.Empty;
        public string ConditionFlag = string.Empty;
        public string ConditionalDialogue = string.Empty;

        public NpcDef Copy() => new()
        {
            Id = Id ?? string.Empty,
            Name = Name ?? string.Empty,
            X = X,
            Y = Y,
            Facility = Facility,
            ShopId = ShopId ?? string.Empty,
            Dialogue = Dialogue ?? string.Empty,
            ConditionFlag = ConditionFlag ?? string.Empty,
            ConditionalDialogue = ConditionalDialogue ?? string.Empty,
        };
    }

    [Serializable]
    public sealed class BossTriggerDef
    {
        public bool Enabled;
        public int TriggerX;
        public int TriggerY;
        public int BossTileX;
        public int BossTileY;
        public string BossGroupId = string.Empty;
        public string DefeatedFlag = string.Empty;
        public string IntroMessage = string.Empty;

        public BossTriggerDef Copy() => new()
        {
            Enabled = Enabled,
            TriggerX = TriggerX,
            TriggerY = TriggerY,
            BossTileX = BossTileX,
            BossTileY = BossTileY,
            BossGroupId = BossGroupId ?? string.Empty,
            DefeatedFlag = DefeatedFlag ?? string.Empty,
            IntroMessage = IntroMessage ?? string.Empty,
        };
    }

    [Serializable]
    public sealed class MapDef
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public bool IsSafeZone;
        public string EncounterTableId = string.Empty;
        public string GridText = string.Empty;
        public PortalDef[] Portals = Array.Empty<PortalDef>();
        public ChestDef[] Chests = Array.Empty<ChestDef>();
        public LockedDoorDef[] Doors = Array.Empty<LockedDoorDef>();
        public NpcDef[] Npcs = Array.Empty<NpcDef>();
        public BossTriggerDef Boss = new();

        public MapDef Copy() => new()
        {
            Id = Id ?? string.Empty,
            Name = Name ?? string.Empty,
            IsSafeZone = IsSafeZone,
            EncounterTableId = EncounterTableId ?? string.Empty,
            GridText = GridText ?? string.Empty,
            Portals = Portals == null ? Array.Empty<PortalDef>() : Array.ConvertAll(Portals, p => p == null ? new PortalDef() : p.Copy()),
            Chests = Chests == null ? Array.Empty<ChestDef>() : Array.ConvertAll(Chests, c => c == null ? new ChestDef() : c.Copy()),
            Doors = Doors == null ? Array.Empty<LockedDoorDef>() : Array.ConvertAll(Doors, d => d == null ? new LockedDoorDef() : d.Copy()),
            Npcs = Npcs == null ? Array.Empty<NpcDef>() : Array.ConvertAll(Npcs, n => n == null ? new NpcDef() : n.Copy()),
            Boss = Boss == null ? new BossTriggerDef() : Boss.Copy(),
        };
    }

    [Serializable]
    public sealed class StartItemEntry
    {
        public string ItemId = string.Empty;
        public int Count = 1;

        public StartItemEntry Copy() => new() { ItemId = ItemId ?? string.Empty, Count = Count };
    }

    [Serializable]
    public sealed class RuleConfig
    {
        public int TickRate = 60;
        public int StepTicks = 8;
        public int StartGold = 60;
        public int MaxGold = 99999;
        public int MaxItemStack = 99;
        public int InnCost = 15;
        public int ReviveCostPerLevel = 20;
        public int FleeBasePercent = 60;
        public int FleeMinPercent = 25;
        public int FleeMaxPercent = 95;
        public float EncounterRateMultiplier = 1f;
        public string[] PartyClassIds = new[] { "warrior", "priest", "mage" };
        public StartItemEntry[] StartItems = Array.Empty<StartItemEntry>();
        public string StartMapId = "town";
        public int StartX = 9;
        public int StartY = 9;
        public string ChurchMapId = "town";
        public int ChurchRespawnX = 14;
        public int ChurchRespawnY = 6;
        public string BossDefeatedFlag = "boss_defeated";

        public RuleConfig Copy() => new()
        {
            TickRate = TickRate,
            StepTicks = StepTicks,
            StartGold = StartGold,
            MaxGold = MaxGold,
            MaxItemStack = MaxItemStack,
            InnCost = InnCost,
            ReviveCostPerLevel = ReviveCostPerLevel,
            FleeBasePercent = FleeBasePercent,
            FleeMinPercent = FleeMinPercent,
            FleeMaxPercent = FleeMaxPercent,
            EncounterRateMultiplier = EncounterRateMultiplier,
            PartyClassIds = PartyClassIds == null ? Array.Empty<string>() : Array.ConvertAll(PartyClassIds, c => c ?? string.Empty),
            StartItems = StartItems == null ? Array.Empty<StartItemEntry>() : Array.ConvertAll(StartItems, s => s == null ? new StartItemEntry() : s.Copy()),
            StartMapId = StartMapId ?? string.Empty,
            StartX = StartX,
            StartY = StartY,
            ChurchMapId = ChurchMapId ?? string.Empty,
            ChurchRespawnX = ChurchRespawnX,
            ChurchRespawnY = ChurchRespawnY,
            BossDefeatedFlag = BossDefeatedFlag ?? string.Empty,
        };
    }

    [Serializable]
    public sealed class GameDataDef
    {
        public RuleConfig Rules = new();
        public ExpTableDef ExpTable = new();
        public ClassDef[] Classes = Array.Empty<ClassDef>();
        public SpellDef[] Spells = Array.Empty<SpellDef>();
        public ItemDef[] Items = Array.Empty<ItemDef>();
        public EquipDef[] Equips = Array.Empty<EquipDef>();
        public EnemyDef[] Enemies = Array.Empty<EnemyDef>();
        public EnemyGroupDef[] EnemyGroups = Array.Empty<EnemyGroupDef>();
        public EncounterTableDef[] EncounterTables = Array.Empty<EncounterTableDef>();
        public ShopDef[] Shops = Array.Empty<ShopDef>();
        public MapDef[] Maps = Array.Empty<MapDef>();

        public GameDataDef Copy() => new()
        {
            Rules = Rules == null ? new RuleConfig() : Rules.Copy(),
            ExpTable = ExpTable == null ? new ExpTableDef() : ExpTable.Copy(),
            Classes = Classes == null ? Array.Empty<ClassDef>() : Array.ConvertAll(Classes, c => c == null ? new ClassDef() : c.Copy()),
            Spells = Spells == null ? Array.Empty<SpellDef>() : Array.ConvertAll(Spells, s => s == null ? new SpellDef() : s.Copy()),
            Items = Items == null ? Array.Empty<ItemDef>() : Array.ConvertAll(Items, i => i == null ? new ItemDef() : i.Copy()),
            Equips = Equips == null ? Array.Empty<EquipDef>() : Array.ConvertAll(Equips, e => e == null ? new EquipDef() : e.Copy()),
            Enemies = Enemies == null ? Array.Empty<EnemyDef>() : Array.ConvertAll(Enemies, e => e == null ? new EnemyDef() : e.Copy()),
            EnemyGroups = EnemyGroups == null ? Array.Empty<EnemyGroupDef>() : Array.ConvertAll(EnemyGroups, g => g == null ? new EnemyGroupDef() : g.Copy()),
            EncounterTables = EncounterTables == null ? Array.Empty<EncounterTableDef>() : Array.ConvertAll(EncounterTables, t => t == null ? new EncounterTableDef() : t.Copy()),
            Shops = Shops == null ? Array.Empty<ShopDef>() : Array.ConvertAll(Shops, s => s == null ? new ShopDef() : s.Copy()),
            Maps = Maps == null ? Array.Empty<MapDef>() : Array.ConvertAll(Maps, m => m == null ? new MapDef() : m.Copy()),
        };
    }
}
