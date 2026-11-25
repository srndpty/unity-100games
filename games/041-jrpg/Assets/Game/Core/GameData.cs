using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using TileMotion;

namespace Game.Core
{
    public enum DataErrorKind
    {
        NullInput = 0,
        EmptyCollection = 1,
        InvalidId = 2,
        DuplicateId = 3,
        UnresolvedReference = 4,
        OutOfRange = 5,
        NonFiniteValue = 6,
        NonMonotonicExp = 7,
        NoAllowedClass = 8,
        InvalidMap = 9,
        EmptyChest = 10,
    }

    public sealed class DataError
    {
        public DataError(DataErrorKind kind, string location, string message)
        {
            Kind = kind;
            Location = location ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public DataErrorKind Kind { get; }

        public string Location { get; }

        public string Message { get; }

        public override string ToString() => $"{Kind} ({Location}): {Message}";
    }

    public sealed class GameDataException : Exception
    {
        public GameDataException(DataError error)
            : base(error?.ToString() ?? "Invalid game data.")
        {
            Error = error ?? new DataError(DataErrorKind.NullInput, string.Empty, "Invalid game data.");
        }

        public DataError Error { get; }
    }

    public sealed class LearnableSpellInfo
    {
        public LearnableSpellInfo(int level, string spellId)
        {
            Level = level;
            SpellId = spellId;
        }

        public int Level { get; }

        public string SpellId { get; }
    }

    public sealed class ClassInfo
    {
        private readonly ReadOnlyCollection<LearnableSpellInfo> _spells;

        public ClassInfo(int index, ClassDef def, LearnableSpellInfo[] spells)
        {
            Index = index;
            Id = def.Id;
            Name = def.Name;
            HeroName = def.HeroName;
            BaseHp = def.BaseHp;
            BaseMp = def.BaseMp;
            BaseAtk = def.BaseAtk;
            BaseDef = def.BaseDef;
            BaseAgi = def.BaseAgi;
            HpGain = def.HpGain;
            MpGain = def.MpGain;
            AtkGain = def.AtkGain;
            DefGain = def.DefGain;
            AgiGain = def.AgiGain;
            StartWeaponId = def.StartWeaponId;
            StartArmorId = def.StartArmorId;
            _spells = Array.AsReadOnly(spells);
        }

        public int Index { get; }
        public string Id { get; }
        public string Name { get; }
        public string HeroName { get; }
        public int BaseHp { get; }
        public int BaseMp { get; }
        public int BaseAtk { get; }
        public int BaseDef { get; }
        public int BaseAgi { get; }
        public int HpGain { get; }
        public int MpGain { get; }
        public int AtkGain { get; }
        public int DefGain { get; }
        public int AgiGain { get; }
        public string StartWeaponId { get; }
        public string StartArmorId { get; }
        public IReadOnlyList<LearnableSpellInfo> Spells => _spells;

        public int MaxHpAt(int level) => BaseHp + ((level - 1) * HpGain);
        public int MaxMpAt(int level) => BaseMp + ((level - 1) * MpGain);
        public int AtkAt(int level) => BaseAtk + ((level - 1) * AtkGain);
        public int DefAt(int level) => BaseDef + ((level - 1) * DefGain);
        public int AgiAt(int level) => BaseAgi + ((level - 1) * AgiGain);

        public bool KnowsSpellAt(string spellId, int level)
        {
            for (var i = 0; i < _spells.Count; i++)
            {
                if (_spells[i].Level <= level && string.Equals(_spells[i].SpellId, spellId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public sealed class SpellInfo
    {
        public SpellInfo(int index, SpellDef def)
        {
            Index = index;
            Id = def.Id;
            Name = def.Name;
            Description = def.Description;
            Kind = def.Kind;
            Element = def.Element;
            MpCost = def.MpCost;
            Power = def.Power;
            UsableInField = def.UsableInField;
        }

        public int Index { get; }
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public SpellKind Kind { get; }
        public SpellElement Element { get; }
        public int MpCost { get; }
        public int Power { get; }
        public bool UsableInField { get; }

        public bool TargetsAlly =>
            Kind == SpellKind.SingleHeal ||
            Kind == SpellKind.AllHeal ||
            Kind == SpellKind.Revive;
    }

    public sealed class ItemInfo
    {
        public ItemInfo(int index, ItemDef def)
        {
            Index = index;
            Id = def.Id;
            Name = def.Name;
            Description = def.Description;
            Kind = def.Kind;
            Power = def.Power;
            Price = def.Price;
            SellPrice = def.Price / 2;
            UsableInField = def.UsableInField;
        }

        public int Index { get; }
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public ItemKind Kind { get; }
        public int Power { get; }
        public int Price { get; }
        public int SellPrice { get; }
        public bool UsableInField { get; }
    }

    public sealed class EquipInfo
    {
        private readonly ReadOnlyCollection<string> _allowedClassIds;

        public EquipInfo(int index, EquipDef def, string[] allowedClassIds)
        {
            Index = index;
            Id = def.Id;
            Name = def.Name;
            Description = def.Description;
            Slot = def.Slot;
            AtkBonus = def.AtkBonus;
            DefBonus = def.DefBonus;
            AgiBonus = def.AgiBonus;
            Price = def.Price;
            SellPrice = def.Price / 2;
            _allowedClassIds = Array.AsReadOnly(allowedClassIds);
        }

        public int Index { get; }
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public EquipSlot Slot { get; }
        public int AtkBonus { get; }
        public int DefBonus { get; }
        public int AgiBonus { get; }
        public int Price { get; }
        public int SellPrice { get; }
        public IReadOnlyList<string> AllowedClassIds => _allowedClassIds;

        public bool CanEquip(string classId)
        {
            for (var i = 0; i < _allowedClassIds.Count; i++)
            {
                if (string.Equals(_allowedClassIds[i], classId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public sealed class EnemyActionInfo
    {
        public EnemyActionInfo(EnemyActionKind kind, string spellId, int weight)
        {
            Kind = kind;
            SpellId = spellId;
            Weight = weight;
        }

        public EnemyActionKind Kind { get; }
        public string SpellId { get; }
        public int Weight { get; }
    }

    public sealed class EnemyInfo
    {
        private readonly ReadOnlyCollection<EnemyActionInfo> _actions;
        private readonly ReadOnlyCollection<EnemyActionInfo> _phase2Actions;

        public EnemyInfo(int index, EnemyDef def, EnemyActionInfo[] actions, EnemyActionInfo[] phase2Actions)
        {
            Index = index;
            Id = def.Id;
            Name = def.Name;
            IsBoss = def.IsBoss;
            MaxHp = def.MaxHp;
            MaxMp = def.MaxMp;
            Atk = def.Atk;
            Def = def.Def;
            Agi = def.Agi;
            ExpReward = def.ExpReward;
            GoldReward = def.GoldReward;
            WeakElement = def.WeakElement;
            _actions = Array.AsReadOnly(actions);
            _phase2Actions = Array.AsReadOnly(phase2Actions);
        }

        public int Index { get; }
        public string Id { get; }
        public string Name { get; }
        public bool IsBoss { get; }
        public int MaxHp { get; }
        public int MaxMp { get; }
        public int Atk { get; }
        public int Def { get; }
        public int Agi { get; }
        public int ExpReward { get; }
        public int GoldReward { get; }
        public SpellElement WeakElement { get; }
        public IReadOnlyList<EnemyActionInfo> Actions => _actions;
        public IReadOnlyList<EnemyActionInfo> Phase2Actions => _phase2Actions;
    }

    public sealed class EnemyGroupInfo
    {
        private readonly ReadOnlyCollection<string> _enemyIds;

        public EnemyGroupInfo(int index, string id, string[] enemyIds, bool isBossGroup)
        {
            Index = index;
            Id = id;
            _enemyIds = Array.AsReadOnly(enemyIds);
            IsBossGroup = isBossGroup;
        }

        public int Index { get; }
        public string Id { get; }
        public IReadOnlyList<string> EnemyIds => _enemyIds;
        public bool IsBossGroup { get; }
    }

    public sealed class EncounterEntryInfo
    {
        public EncounterEntryInfo(string groupId, int weight)
        {
            GroupId = groupId;
            Weight = weight;
        }

        public string GroupId { get; }
        public int Weight { get; }
    }

    public sealed class EncounterTableInfo
    {
        private readonly ReadOnlyCollection<EncounterEntryInfo> _entries;

        public EncounterTableInfo(int index, string id, int ratePercent, int effectiveRatePercent, int graceSteps, EncounterEntryInfo[] entries)
        {
            Index = index;
            Id = id;
            RatePercent = ratePercent;
            EffectiveRatePercent = effectiveRatePercent;
            GraceSteps = graceSteps;
            _entries = Array.AsReadOnly(entries);
            var sum = 0;
            for (var i = 0; i < entries.Length; i++)
            {
                sum += entries[i].Weight;
            }

            TotalWeight = sum;
        }

        public int Index { get; }
        public string Id { get; }
        public int RatePercent { get; }
        public int EffectiveRatePercent { get; }
        public int GraceSteps { get; }
        public int TotalWeight { get; }
        public IReadOnlyList<EncounterEntryInfo> Entries => _entries;

        public string RollGroupId(GameRng rng)
        {
            if (rng == null)
            {
                throw new ArgumentNullException(nameof(rng));
            }

            var pick = rng.Next(TotalWeight);
            for (var i = 0; i < _entries.Count; i++)
            {
                pick -= _entries[i].Weight;
                if (pick < 0)
                {
                    return _entries[i].GroupId;
                }
            }

            return _entries[_entries.Count - 1].GroupId;
        }
    }

    public sealed class ShopInfo
    {
        private readonly ReadOnlyCollection<string> _productIds;

        public ShopInfo(int index, string id, string name, string[] productIds)
        {
            Index = index;
            Id = id;
            Name = name;
            _productIds = Array.AsReadOnly(productIds);
        }

        public int Index { get; }
        public string Id { get; }
        public string Name { get; }
        public IReadOnlyList<string> ProductIds => _productIds;

        public bool Sells(string productId)
        {
            for (var i = 0; i < _productIds.Count; i++)
            {
                if (string.Equals(_productIds[i], productId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public sealed class PortalInfo
    {
        public PortalInfo(PortalDef def)
        {
            X = def.X;
            Y = def.Y;
            TargetMapId = def.TargetMapId;
            TargetX = def.TargetX;
            TargetY = def.TargetY;
        }

        public int X { get; }
        public int Y { get; }
        public string TargetMapId { get; }
        public int TargetX { get; }
        public int TargetY { get; }
    }

    public sealed class ChestInfo
    {
        public ChestInfo(ChestDef def)
        {
            X = def.X;
            Y = def.Y;
            OpenedFlag = def.OpenedFlag;
            RewardItemId = def.RewardItemId;
            RewardEquipId = def.RewardEquipId;
            RewardGold = def.RewardGold;
            RewardFlag = def.RewardFlag;
            RewardLabel = def.RewardLabel;
        }

        public int X { get; }
        public int Y { get; }
        public string OpenedFlag { get; }
        public string RewardItemId { get; }
        public string RewardEquipId { get; }
        public int RewardGold { get; }
        public string RewardFlag { get; }
        public string RewardLabel { get; }
    }

    public sealed class LockedDoorInfo
    {
        public LockedDoorInfo(LockedDoorDef def)
        {
            X = def.X;
            Y = def.Y;
            RequiredKeyFlag = def.RequiredKeyFlag;
            OpenedFlag = def.OpenedFlag;
            LockedMessage = string.IsNullOrEmpty(def.LockedMessage) ? "鍵がかかっている。" : def.LockedMessage;
            UnlockedMessage = string.IsNullOrEmpty(def.UnlockedMessage) ? "鍵を使って扉を開けた！" : def.UnlockedMessage;
        }

        public int X { get; }
        public int Y { get; }
        public string RequiredKeyFlag { get; }
        public string OpenedFlag { get; }
        public string LockedMessage { get; }
        public string UnlockedMessage { get; }
    }

    public sealed class NpcInfo
    {
        public NpcInfo(NpcDef def)
        {
            Id = def.Id;
            Name = def.Name;
            X = def.X;
            Y = def.Y;
            Facility = def.Facility;
            ShopId = def.ShopId;
            Dialogue = def.Dialogue;
            ConditionFlag = def.ConditionFlag;
            ConditionalDialogue = def.ConditionalDialogue;
        }

        public string Id { get; }
        public string Name { get; }
        public int X { get; }
        public int Y { get; }
        public FacilityKind Facility { get; }
        public string ShopId { get; }
        public string Dialogue { get; }
        public string ConditionFlag { get; }
        public string ConditionalDialogue { get; }

        public string Speak(FlagSet flags)
        {
            if (flags == null)
            {
                throw new ArgumentNullException(nameof(flags));
            }

            if (ConditionFlag.Length > 0 && flags.Get(ConditionFlag) && ConditionalDialogue.Length > 0)
            {
                return ConditionalDialogue;
            }

            return Dialogue;
        }
    }

    public sealed class BossTriggerInfo
    {
        public BossTriggerInfo(BossTriggerDef def)
        {
            Enabled = def.Enabled;
            TriggerX = def.TriggerX;
            TriggerY = def.TriggerY;
            BossTileX = def.BossTileX;
            BossTileY = def.BossTileY;
            BossGroupId = def.BossGroupId;
            DefeatedFlag = def.DefeatedFlag;
            IntroMessage = string.IsNullOrEmpty(def.IntroMessage) ? "魔将が立ちはだかった！" : def.IntroMessage;
        }

        public bool Enabled { get; }
        public int TriggerX { get; }
        public int TriggerY { get; }
        public int BossTileX { get; }
        public int BossTileY { get; }
        public string BossGroupId { get; }
        public string DefeatedFlag { get; }
        public string IntroMessage { get; }
    }

    /// <summary>検証済みの 1 枚のフィールド地図。</summary>
    public sealed class FieldMap
    {
        private readonly TileTerrain[,] _tiles;
        private readonly ReadOnlyCollection<PortalInfo> _portals;
        private readonly ReadOnlyCollection<ChestInfo> _chests;
        private readonly ReadOnlyCollection<LockedDoorInfo> _doors;
        private readonly ReadOnlyCollection<NpcInfo> _npcs;

        internal FieldMap(
            int index,
            string id,
            string name,
            bool isSafeZone,
            string encounterTableId,
            TileTerrain[,] tiles,
            PortalInfo[] portals,
            ChestInfo[] chests,
            LockedDoorInfo[] doors,
            NpcInfo[] npcs,
            BossTriggerInfo boss)
        {
            Index = index;
            Id = id;
            Name = name;
            IsSafeZone = isSafeZone;
            EncounterTableId = encounterTableId;
            Width = tiles.GetLength(0);
            Height = tiles.GetLength(1);
            _tiles = tiles;
            _portals = Array.AsReadOnly(portals);
            _chests = Array.AsReadOnly(chests);
            _doors = Array.AsReadOnly(doors);
            _npcs = Array.AsReadOnly(npcs);
            Boss = boss;
        }

        public int Index { get; }
        public string Id { get; }
        public string Name { get; }
        public bool IsSafeZone { get; }
        public string EncounterTableId { get; }
        public int Width { get; }
        public int Height { get; }
        public IReadOnlyList<PortalInfo> Portals => _portals;
        public IReadOnlyList<ChestInfo> Chests => _chests;
        public IReadOnlyList<LockedDoorInfo> Doors => _doors;
        public IReadOnlyList<NpcInfo> Npcs => _npcs;
        public BossTriggerInfo Boss { get; }

        public bool Contains(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

        public TileTerrain TerrainAt(int x, int y)
        {
            if (!Contains(x, y))
            {
                return TileTerrain.Wall;
            }

            return _tiles[x, y];
        }

        public static bool IsBaseTerrainWalkable(TileTerrain terrain) =>
            terrain == TileTerrain.Floor ||
            terrain == TileTerrain.Grass ||
            terrain == TileTerrain.Bridge ||
            terrain == TileTerrain.Stairs;

        public PortalInfo? PortalAt(int x, int y)
        {
            for (var i = 0; i < _portals.Count; i++)
            {
                if (_portals[i].X == x && _portals[i].Y == y)
                {
                    return _portals[i];
                }
            }

            return null;
        }

        public ChestInfo? ChestAt(int x, int y)
        {
            for (var i = 0; i < _chests.Count; i++)
            {
                if (_chests[i].X == x && _chests[i].Y == y)
                {
                    return _chests[i];
                }
            }

            return null;
        }

        public LockedDoorInfo? DoorAt(int x, int y)
        {
            for (var i = 0; i < _doors.Count; i++)
            {
                if (_doors[i].X == x && _doors[i].Y == y)
                {
                    return _doors[i];
                }
            }

            return null;
        }

        public NpcInfo? NpcAt(int x, int y)
        {
            for (var i = 0; i < _npcs.Count; i++)
            {
                if (_npcs[i].X == x && _npcs[i].Y == y)
                {
                    return _npcs[i];
                }
            }

            return null;
        }

        public bool IsWalkable(int x, int y, FlagSet? flags)
        {
            if (!Contains(x, y))
            {
                return false;
            }

            if (NpcAt(x, y) != null || ChestAt(x, y) != null)
            {
                return false;
            }

            if (Boss.Enabled && Boss.BossTileX == x && Boss.BossTileY == y && (flags == null || !flags.Get(Boss.DefeatedFlag)))
            {
                return false;
            }

            var terrain = _tiles[x, y];
            if (terrain == TileTerrain.LockedDoor)
            {
                var door = DoorAt(x, y);
                return door != null && flags != null && flags.Get(door.OpenedFlag);
            }

            return IsBaseTerrainWalkable(terrain);
        }
    }

    public sealed class RuleInfo
    {
        private readonly ReadOnlyCollection<string> _partyClassIds;
        private readonly ReadOnlyCollection<StartItemEntry> _startItems;

        public RuleInfo(RuleConfig cfg)
        {
            TickRate = cfg.TickRate;
            StepTicks = cfg.StepTicks;
            StartGold = cfg.StartGold;
            MaxGold = cfg.MaxGold;
            MaxItemStack = cfg.MaxItemStack;
            InnCost = cfg.InnCost;
            ReviveCostPerLevel = cfg.ReviveCostPerLevel;
            FleeBasePercent = cfg.FleeBasePercent;
            FleeMinPercent = cfg.FleeMinPercent;
            FleeMaxPercent = cfg.FleeMaxPercent;
            EncounterRateMultiplier = cfg.EncounterRateMultiplier;
            _partyClassIds = Array.AsReadOnly((string[])cfg.PartyClassIds.Clone());
            _startItems = Array.AsReadOnly(Array.ConvertAll(cfg.StartItems, s => s.Copy()));
            StartMapId = cfg.StartMapId;
            StartX = cfg.StartX;
            StartY = cfg.StartY;
            ChurchMapId = cfg.ChurchMapId;
            ChurchRespawnX = cfg.ChurchRespawnX;
            ChurchRespawnY = cfg.ChurchRespawnY;
            BossDefeatedFlag = cfg.BossDefeatedFlag;
        }

        public int TickRate { get; }
        public int StepTicks { get; }
        public int StartGold { get; }
        public int MaxGold { get; }
        public int MaxItemStack { get; }
        public int InnCost { get; }
        public int ReviveCostPerLevel { get; }
        public int FleeBasePercent { get; }
        public int FleeMinPercent { get; }
        public int FleeMaxPercent { get; }
        public float EncounterRateMultiplier { get; }
        public IReadOnlyList<string> PartyClassIds => _partyClassIds;
        public IReadOnlyList<StartItemEntry> StartItems => _startItems;
        public string StartMapId { get; }
        public int StartX { get; }
        public int StartY { get; }
        public string ChurchMapId { get; }
        public int ChurchRespawnX { get; }
        public int ChurchRespawnY { get; }
        public string BossDefeatedFlag { get; }
    }

    public sealed class ExpTableInfo
    {
        private readonly ReadOnlyCollection<int> _requiredExp;

        public ExpTableInfo(int maxLevel, int[] requiredExp)
        {
            MaxLevel = maxLevel;
            _requiredExp = Array.AsReadOnly((int[])requiredExp.Clone());
        }

        public int MaxLevel { get; }
        public IReadOnlyList<int> RequiredExp => _requiredExp;

        public int ExpForLevel(int level)
        {
            if (level < 1 || level > MaxLevel)
            {
                throw new ArgumentOutOfRangeException(nameof(level));
            }

            return _requiredExp[level - 1];
        }

        public int LevelForExp(int exp)
        {
            var lv = 1;
            for (var i = 1; i < _requiredExp.Count; i++)
            {
                if (exp >= _requiredExp[i])
                {
                    lv = i + 1;
                }
                else
                {
                    break;
                }
            }

            return lv;
        }
    }

    /// <summary>全体検証を終えた不変のゲームデータ一式。ID の索引とデータの指紋を持つ。</summary>
    public sealed class GameData
    {
        private readonly ReadOnlyCollection<ClassInfo> _classes;
        private readonly ReadOnlyCollection<SpellInfo> _spells;
        private readonly ReadOnlyCollection<ItemInfo> _items;
        private readonly ReadOnlyCollection<EquipInfo> _equips;
        private readonly ReadOnlyCollection<EnemyInfo> _enemies;
        private readonly ReadOnlyCollection<EnemyGroupInfo> _groups;
        private readonly ReadOnlyCollection<EncounterTableInfo> _encounterTables;
        private readonly ReadOnlyCollection<ShopInfo> _shops;
        private readonly ReadOnlyCollection<FieldMap> _maps;
        private readonly ReadOnlyCollection<string> _knownFlags;

        private readonly Dictionary<string, ClassInfo> _classById;
        private readonly Dictionary<string, SpellInfo> _spellById;
        private readonly Dictionary<string, ItemInfo> _itemById;
        private readonly Dictionary<string, EquipInfo> _equipById;
        private readonly Dictionary<string, EnemyInfo> _enemyById;
        private readonly Dictionary<string, EnemyGroupInfo> _groupById;
        private readonly Dictionary<string, EncounterTableInfo> _encounterById;
        private readonly Dictionary<string, ShopInfo> _shopById;
        private readonly Dictionary<string, FieldMap> _mapById;
        private readonly Dictionary<string, int> _flagById;

        internal GameData(
            RuleInfo rules,
            ExpTableInfo expTable,
            ClassInfo[] classes,
            SpellInfo[] spells,
            ItemInfo[] items,
            EquipInfo[] equips,
            EnemyInfo[] enemies,
            EnemyGroupInfo[] groups,
            EncounterTableInfo[] encounterTables,
            ShopInfo[] shops,
            FieldMap[] maps,
            string[] knownFlags,
            uint fingerprint)
        {
            Rules = rules;
            ExpTable = expTable;
            _classes = Array.AsReadOnly(classes);
            _spells = Array.AsReadOnly(spells);
            _items = Array.AsReadOnly(items);
            _equips = Array.AsReadOnly(equips);
            _enemies = Array.AsReadOnly(enemies);
            _groups = Array.AsReadOnly(groups);
            _encounterTables = Array.AsReadOnly(encounterTables);
            _shops = Array.AsReadOnly(shops);
            _maps = Array.AsReadOnly(maps);
            _knownFlags = Array.AsReadOnly(knownFlags);
            Fingerprint = fingerprint;

            _classById = BuildIndex(classes, c => c.Id);
            _spellById = BuildIndex(spells, s => s.Id);
            _itemById = BuildIndex(items, i => i.Id);
            _equipById = BuildIndex(equips, e => e.Id);
            _enemyById = BuildIndex(enemies, e => e.Id);
            _groupById = BuildIndex(groups, g => g.Id);
            _encounterById = BuildIndex(encounterTables, t => t.Id);
            _shopById = BuildIndex(shops, s => s.Id);
            _mapById = BuildIndex(maps, m => m.Id);
            _flagById = new Dictionary<string, int>(knownFlags.Length, StringComparer.Ordinal);
            for (var i = 0; i < knownFlags.Length; i++)
            {
                _flagById[knownFlags[i]] = i;
            }
        }

        public RuleInfo Rules { get; }
        public ExpTableInfo ExpTable { get; }
        public IReadOnlyList<ClassInfo> Classes => _classes;
        public IReadOnlyList<SpellInfo> Spells => _spells;
        public IReadOnlyList<ItemInfo> Items => _items;
        public IReadOnlyList<EquipInfo> Equips => _equips;
        public IReadOnlyList<EnemyInfo> Enemies => _enemies;
        public IReadOnlyList<EnemyGroupInfo> EnemyGroups => _groups;
        public IReadOnlyList<EncounterTableInfo> EncounterTables => _encounterTables;
        public IReadOnlyList<ShopInfo> Shops => _shops;
        public IReadOnlyList<FieldMap> Maps => _maps;
        public IReadOnlyList<string> KnownFlags => _knownFlags;
        public uint Fingerprint { get; }

        public bool TryGetClass(string id, out ClassInfo? value) => _classById.TryGetValue(id ?? string.Empty, out value);
        public bool TryGetSpell(string id, out SpellInfo? value) => _spellById.TryGetValue(id ?? string.Empty, out value);
        public bool TryGetItem(string id, out ItemInfo? value) => _itemById.TryGetValue(id ?? string.Empty, out value);
        public bool TryGetEquip(string id, out EquipInfo? value) => _equipById.TryGetValue(id ?? string.Empty, out value);
        public bool TryGetEnemy(string id, out EnemyInfo? value) => _enemyById.TryGetValue(id ?? string.Empty, out value);
        public bool TryGetGroup(string id, out EnemyGroupInfo? value) => _groupById.TryGetValue(id ?? string.Empty, out value);
        public bool TryGetEncounterTable(string id, out EncounterTableInfo? value) => _encounterById.TryGetValue(id ?? string.Empty, out value);
        public bool TryGetShop(string id, out ShopInfo? value) => _shopById.TryGetValue(id ?? string.Empty, out value);
        public bool TryGetMap(string id, out FieldMap? value) => _mapById.TryGetValue(id ?? string.Empty, out value);
        public bool TryGetFlagIndex(string id, out int index) => _flagById.TryGetValue(id ?? string.Empty, out index);

        public ClassInfo GetClass(string id) => _classById[id];
        public SpellInfo GetSpell(string id) => _spellById[id];
        public ItemInfo GetItem(string id) => _itemById[id];
        public EquipInfo GetEquip(string id) => _equipById[id];
        public EnemyInfo GetEnemy(string id) => _enemyById[id];
        public EnemyGroupInfo GetGroup(string id) => _groupById[id];
        public EncounterTableInfo GetEncounterTable(string id) => _encounterById[id];
        public ShopInfo GetShop(string id) => _shopById[id];
        public FieldMap GetMap(string id) => _mapById[id];

        public bool IsKnownProduct(string id) => _itemById.ContainsKey(id ?? string.Empty) || _equipById.ContainsKey(id ?? string.Empty);

        public int ProductPrice(string id)
        {
            if (_itemById.TryGetValue(id, out var item))
            {
                return item.Price;
            }

            if (_equipById.TryGetValue(id, out var equip))
            {
                return equip.Price;
            }

            throw new KeyNotFoundException("Unknown product: " + id);
        }

        public int ProductSellPrice(string id) => ProductPrice(id) / 2;

        public string ProductName(string id)
        {
            if (_itemById.TryGetValue(id, out var item))
            {
                return item.Name;
            }

            if (_equipById.TryGetValue(id, out var equip))
            {
                return equip.Name;
            }

            return id;
        }

        public static GameData Validate(GameDataDef raw)
        {
            if (!TryValidate(raw, out var data, out var error))
            {
                throw new GameDataException(error!);
            }

            return data!;
        }

        public static bool TryValidate(GameDataDef? raw, out GameData? data, out DataError? error)
        {
            data = null;
            if (raw == null)
            {
                error = new DataError(DataErrorKind.NullInput, "GameDataDef", "ゲームデータが null です。");
                return false;
            }

            var copy = raw.Copy();
            return GameDataValidator.Build(copy, out data, out error);
        }

        public static bool IsValidId(string? id)
        {
            if (string.IsNullOrEmpty(id) || id!.Length > 64)
            {
                return false;
            }

            for (var i = 0; i < id.Length; i++)
            {
                var ch = id[i];
                var ok = (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '_';
                if (!ok)
                {
                    return false;
                }
            }

            return true;
        }

        private static Dictionary<string, T> BuildIndex<T>(T[] items, Func<T, string> keySelector)
        {
            var dict = new Dictionary<string, T>(items.Length, StringComparer.Ordinal);
            for (var i = 0; i < items.Length; i++)
            {
                dict[keySelector(items[i])] = items[i];
            }

            return dict;
        }
    }
}
