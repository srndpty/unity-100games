namespace Game.Core
{
    /// <summary>EMBERLOCK HOLLOW の既定ゲームデータ。テストと初期アセット生成の基準になる。</summary>
    public static class DefaultGameData
    {
        public static GameData Create() => GameData.Validate(CreateRaw());

        public static GameDataDef CreateRaw()
        {
            return new GameDataDef
            {
                Rules = new RuleConfig
                {
                    TickRate = 60,
                    StepTicks = 8,
                    StartGold = 60,
                    MaxGold = 99999,
                    MaxItemStack = 99,
                    InnCost = 15,
                    ReviveCostPerLevel = 20,
                    FleeBasePercent = 60,
                    FleeMinPercent = 25,
                    FleeMaxPercent = 95,
                    EncounterRateMultiplier = 1f,
                    PartyClassIds = new[] { "warrior", "priest", "mage" },
                    StartItems = new[]
                    {
                        new StartItemEntry { ItemId = "herb", Count = 3 },
                        new StartItemEntry { ItemId = "ether_dew", Count = 1 },
                    },
                    StartMapId = "town",
                    StartX = 8,
                    StartY = 7,
                    ChurchMapId = "town",
                    ChurchRespawnX = 14,
                    ChurchRespawnY = 5,
                    BossDefeatedFlag = "boss_defeated",
                },
                ExpTable = new ExpTableDef
                {
                    MaxLevel = 10,
                    RequiredExp = new[] { 0, 24, 65, 130, 230, 370, 560, 800, 1100, 1450 },
                },
                Classes = new[]
                {
                    new ClassDef
                    {
                        Id = "warrior",
                        Name = "戦士",
                        HeroName = "アレン",
                        BaseHp = 44,
                        BaseMp = 0,
                        BaseAtk = 17,
                        BaseDef = 12,
                        BaseAgi = 9,
                        HpGain = 9,
                        MpGain = 0,
                        AtkGain = 4,
                        DefGain = 3,
                        AgiGain = 2,
                        StartWeaponId = "bronze_sword",
                        StartArmorId = "leather_vest",
                        Spells = System.Array.Empty<LearnableSpellDef>(),
                    },
                    new ClassDef
                    {
                        Id = "priest",
                        Name = "僧侶",
                        HeroName = "リナ",
                        BaseHp = 34,
                        BaseMp = 20,
                        BaseAtk = 11,
                        BaseDef = 9,
                        BaseAgi = 11,
                        HpGain = 6,
                        MpGain = 5,
                        AtkGain = 3,
                        DefGain = 2,
                        AgiGain = 2,
                        StartWeaponId = "oak_staff",
                        StartArmorId = "linen_robe",
                        Spells = new[]
                        {
                            new LearnableSpellDef { Level = 1, SpellId = "heal" },
                            new LearnableSpellDef { Level = 2, SpellId = "holy_bolt" },
                            new LearnableSpellDef { Level = 4, SpellId = "aura_heal" },
                            new LearnableSpellDef { Level = 5, SpellId = "revive" },
                        },
                    },
                    new ClassDef
                    {
                        Id = "mage",
                        Name = "魔法使い",
                        HeroName = "セオ",
                        BaseHp = 28,
                        BaseMp = 24,
                        BaseAtk = 7,
                        BaseDef = 7,
                        BaseAgi = 13,
                        HpGain = 5,
                        MpGain = 6,
                        AtkGain = 2,
                        DefGain = 2,
                        AgiGain = 3,
                        StartWeaponId = "ash_wand",
                        StartArmorId = "linen_robe",
                        Spells = new[]
                        {
                            new LearnableSpellDef { Level = 1, SpellId = "ember" },
                            new LearnableSpellDef { Level = 2, SpellId = "frost_lance" },
                            new LearnableSpellDef { Level = 3, SpellId = "flame_wave" },
                            new LearnableSpellDef { Level = 5, SpellId = "glacier" },
                        },
                    },
                },
                Spells = new[]
                {
                    new SpellDef
                    {
                        Id = "heal",
                        Name = "ヒール",
                        Description = "味方1人のHPを回復する",
                        Kind = SpellKind.SingleHeal,
                        Element = SpellElement.Light,
                        MpCost = 4,
                        Power = 30,
                        UsableInField = true,
                    },
                    new SpellDef
                    {
                        Id = "aura_heal",
                        Name = "オーラ",
                        Description = "生存する味方全員のHPを回復する",
                        Kind = SpellKind.AllHeal,
                        Element = SpellElement.Light,
                        MpCost = 9,
                        Power = 26,
                        UsableInField = true,
                    },
                    new SpellDef
                    {
                        Id = "revive",
                        Name = "リザレクト",
                        Description = "倒れた味方1人を最大HPの半分で蘇生する",
                        Kind = SpellKind.Revive,
                        Element = SpellElement.Light,
                        MpCost = 10,
                        Power = 50,
                        UsableInField = true,
                    },
                    new SpellDef
                    {
                        Id = "holy_bolt",
                        Name = "セイント",
                        Description = "光の矢で敵1体を射抜く",
                        Kind = SpellKind.SingleAttack,
                        Element = SpellElement.Light,
                        MpCost = 5,
                        Power = 24,
                        UsableInField = false,
                    },
                    new SpellDef
                    {
                        Id = "ember",
                        Name = "エンバー",
                        Description = "火球で敵1体を焼く",
                        Kind = SpellKind.SingleAttack,
                        Element = SpellElement.Fire,
                        MpCost = 4,
                        Power = 22,
                        UsableInField = false,
                    },
                    new SpellDef
                    {
                        Id = "frost_lance",
                        Name = "フロスト",
                        Description = "氷槍で敵1体を貫く（炎の魔物に有効）",
                        Kind = SpellKind.SingleAttack,
                        Element = SpellElement.Frost,
                        MpCost = 6,
                        Power = 34,
                        UsableInField = false,
                    },
                    new SpellDef
                    {
                        Id = "flame_wave",
                        Name = "フレア波",
                        Description = "炎の波で敵1グループを薙ぎ払う",
                        Kind = SpellKind.GroupAttack,
                        Element = SpellElement.Fire,
                        MpCost = 7,
                        Power = 22,
                        UsableInField = false,
                    },
                    new SpellDef
                    {
                        Id = "glacier",
                        Name = "グレイシア",
                        Description = "極寒の氷柱で敵1体を砕く",
                        Kind = SpellKind.SingleAttack,
                        Element = SpellElement.Frost,
                        MpCost = 10,
                        Power = 54,
                        UsableInField = false,
                    },
                },
                Items = new[]
                {
                    new ItemDef
                    {
                        Id = "herb",
                        Name = "灯草",
                        Description = "味方1人のHPを32回復する",
                        Kind = ItemKind.HealHp,
                        Power = 32,
                        Price = 12,
                        UsableInField = true,
                    },
                    new ItemDef
                    {
                        Id = "hi_herb",
                        Name = "上灯草",
                        Description = "味方1人のHPを75回復する",
                        Kind = ItemKind.HealHp,
                        Power = 75,
                        Price = 36,
                        UsableInField = true,
                    },
                    new ItemDef
                    {
                        Id = "ether_dew",
                        Name = "星露",
                        Description = "味方1人のMPを18回復する",
                        Kind = ItemKind.RestoreMp,
                        Power = 18,
                        Price = 40,
                        UsableInField = true,
                    },
                },
                Equips = new[]
                {
                    new EquipDef
                    {
                        Id = "bronze_sword",
                        Name = "銅の剣",
                        Description = "扱いやすい戦士の剣（攻+5）",
                        Slot = EquipSlot.Weapon,
                        AtkBonus = 5,
                        Price = 30,
                        AllowedClassIds = new[] { "warrior" },
                    },
                    new EquipDef
                    {
                        Id = "oak_staff",
                        Name = "樫の杖",
                        Description = "祈祷用の木の杖（攻+3）",
                        Slot = EquipSlot.Weapon,
                        AtkBonus = 3,
                        Price = 20,
                        AllowedClassIds = new[] { "priest", "mage" },
                    },
                    new EquipDef
                    {
                        Id = "ash_wand",
                        Name = "灰のワンド",
                        Description = "魔力を導く短杖（攻+2 速+1）",
                        Slot = EquipSlot.Weapon,
                        AtkBonus = 2,
                        AgiBonus = 1,
                        Price = 20,
                        AllowedClassIds = new[] { "mage" },
                    },
                    new EquipDef
                    {
                        Id = "iron_blade",
                        Name = "鉄の長剣",
                        Description = "鍛えられた鋼の剣（攻+12）",
                        Slot = EquipSlot.Weapon,
                        AtkBonus = 12,
                        Price = 80,
                        AllowedClassIds = new[] { "warrior" },
                    },
                    new EquipDef
                    {
                        Id = "silver_mace",
                        Name = "銀の鎚",
                        Description = "聖銀を鍛えた戦鎚（攻+9）",
                        Slot = EquipSlot.Weapon,
                        AtkBonus = 9,
                        Price = 72,
                        AllowedClassIds = new[] { "priest" },
                    },
                    new EquipDef
                    {
                        Id = "rime_rod",
                        Name = "氷紋の杖",
                        Description = "冷気を帯びた宝杖（攻+6 速+3）",
                        Slot = EquipSlot.Weapon,
                        AtkBonus = 6,
                        AgiBonus = 3,
                        Price = 70,
                        AllowedClassIds = new[] { "mage" },
                    },
                    new EquipDef
                    {
                        Id = "ember_greatsword",
                        Name = "焔断ちの大剣",
                        Description = "坑道最奥に眠る剛剣（攻+22）",
                        Slot = EquipSlot.Weapon,
                        AtkBonus = 22,
                        Price = 200,
                        AllowedClassIds = new[] { "warrior" },
                    },
                    new EquipDef
                    {
                        Id = "leather_vest",
                        Name = "革の鎧",
                        Description = "丈夫な革の胸当て（守+4）",
                        Slot = EquipSlot.Armor,
                        DefBonus = 4,
                        Price = 24,
                        AllowedClassIds = new[] { "warrior", "priest" },
                    },
                    new EquipDef
                    {
                        Id = "linen_robe",
                        Name = "麻の衣",
                        Description = "旅装の軽い衣（守+3）",
                        Slot = EquipSlot.Armor,
                        DefBonus = 3,
                        Price = 18,
                        AllowedClassIds = new[] { "priest", "mage" },
                    },
                    new EquipDef
                    {
                        Id = "chain_mail",
                        Name = "鎖帷子",
                        Description = "鉄輪を編んだ鎧（守+11）",
                        Slot = EquipSlot.Armor,
                        DefBonus = 11,
                        Price = 76,
                        AllowedClassIds = new[] { "warrior", "priest" },
                    },
                    new EquipDef
                    {
                        Id = "silk_cloak",
                        Name = "星織りの外套",
                        Description = "身軽な魔道外套（守+8 速+2）",
                        Slot = EquipSlot.Armor,
                        DefBonus = 8,
                        AgiBonus = 2,
                        Price = 64,
                        AllowedClassIds = new[] { "priest", "mage" },
                    },
                    new EquipDef
                    {
                        Id = "ward_plate",
                        Name = "守護の胸甲",
                        Description = "重厚な古の板金鎧（守+18）",
                        Slot = EquipSlot.Armor,
                        DefBonus = 18,
                        Price = 180,
                        AllowedClassIds = new[] { "warrior", "priest" },
                    },
                },
                Enemies = new[]
                {
                    new EnemyDef
                    {
                        Id = "soot_slime",
                        Name = "煤スライム",
                        MaxHp = 18,
                        MaxMp = 0,
                        Atk = 13,
                        Def = 6,
                        Agi = 7,
                        ExpReward = 8,
                        GoldReward = 11,
                        WeakElement = SpellElement.Fire,
                        Actions = new[]
                        {
                            new EnemyActionEntry { Kind = EnemyActionKind.Attack, Weight = 100 },
                        },
                    },
                    new EnemyDef
                    {
                        Id = "cinder_bat",
                        Name = "灰羽コウモリ",
                        MaxHp = 22,
                        MaxMp = 8,
                        Atk = 15,
                        Def = 8,
                        Agi = 14,
                        ExpReward = 10,
                        GoldReward = 13,
                        WeakElement = SpellElement.Frost,
                        Actions = new[]
                        {
                            new EnemyActionEntry { Kind = EnemyActionKind.Attack, Weight = 70 },
                            new EnemyActionEntry { Kind = EnemyActionKind.Spell, SpellId = "ember", Weight = 30 },
                        },
                    },
                    new EnemyDef
                    {
                        Id = "ember_hound",
                        Name = "火群オオカミ",
                        MaxHp = 35,
                        MaxMp = 0,
                        Atk = 22,
                        Def = 12,
                        Agi = 13,
                        ExpReward = 17,
                        GoldReward = 22,
                        WeakElement = SpellElement.Frost,
                        Actions = new[]
                        {
                            new EnemyActionEntry { Kind = EnemyActionKind.Attack, Weight = 65 },
                            new EnemyActionEntry { Kind = EnemyActionKind.HeavyAttack, Weight = 35 },
                        },
                    },
                    new EnemyDef
                    {
                        Id = "hollow_acolyte",
                        Name = "虚ろの祈祷師",
                        MaxHp = 32,
                        MaxMp = 12,
                        Atk = 14,
                        Def = 10,
                        Agi = 11,
                        ExpReward = 20,
                        GoldReward = 26,
                        WeakElement = SpellElement.Light,
                        Actions = new[]
                        {
                            new EnemyActionEntry { Kind = EnemyActionKind.Attack, Weight = 40 },
                            new EnemyActionEntry { Kind = EnemyActionKind.Spell, SpellId = "ember", Weight = 40 },
                            new EnemyActionEntry { Kind = EnemyActionKind.Spell, SpellId = "heal", Weight = 20 },
                        },
                    },
                    new EnemyDef
                    {
                        Id = "iron_sentinel",
                        Name = "朽ちた衛兵",
                        MaxHp = 52,
                        MaxMp = 0,
                        Atk = 27,
                        Def = 22,
                        Agi = 9,
                        ExpReward = 30,
                        GoldReward = 36,
                        WeakElement = SpellElement.Fire,
                        Actions = new[]
                        {
                            new EnemyActionEntry { Kind = EnemyActionKind.Attack, Weight = 60 },
                            new EnemyActionEntry { Kind = EnemyActionKind.HeavyAttack, Weight = 40 },
                        },
                    },
                    new EnemyDef
                    {
                        Id = "boss_valug",
                        Name = "焔鎖の魔将ヴァルグ",
                        IsBoss = true,
                        MaxHp = 230,
                        MaxMp = 42,
                        Atk = 34,
                        Def = 20,
                        Agi = 15,
                        ExpReward = 120,
                        GoldReward = 200,
                        WeakElement = SpellElement.Frost,
                        Actions = new[]
                        {
                            new EnemyActionEntry { Kind = EnemyActionKind.Attack, Weight = 50 },
                            new EnemyActionEntry { Kind = EnemyActionKind.HeavyAttack, Weight = 25 },
                            new EnemyActionEntry { Kind = EnemyActionKind.Spell, SpellId = "ember", Weight = 25 },
                        },
                        Phase2Actions = new[]
                        {
                            new EnemyActionEntry { Kind = EnemyActionKind.HeavyAttack, Weight = 40 },
                            new EnemyActionEntry { Kind = EnemyActionKind.Spell, SpellId = "flame_wave", Weight = 40 },
                            new EnemyActionEntry { Kind = EnemyActionKind.Attack, Weight = 20 },
                        },
                    },
                },
                EnemyGroups = new[]
                {
                    new EnemyGroupDef { Id = "f1_slimes", EnemyIds = new[] { "soot_slime", "soot_slime" } },
                    new EnemyGroupDef { Id = "f1_mixed", EnemyIds = new[] { "soot_slime", "cinder_bat" } },
                    new EnemyGroupDef { Id = "f1_bats", EnemyIds = new[] { "cinder_bat", "cinder_bat" } },
                    new EnemyGroupDef { Id = "f2_hounds", EnemyIds = new[] { "ember_hound", "cinder_bat" } },
                    new EnemyGroupDef { Id = "f2_cult", EnemyIds = new[] { "hollow_acolyte", "soot_slime", "soot_slime" } },
                    new EnemyGroupDef { Id = "f3_guard", EnemyIds = new[] { "iron_sentinel", "hollow_acolyte" } },
                    new EnemyGroupDef { Id = "f3_pack", EnemyIds = new[] { "ember_hound", "ember_hound", "cinder_bat" } },
                    new EnemyGroupDef { Id = "group_boss", EnemyIds = new[] { "boss_valug" } },
                },
                EncounterTables = new[]
                {
                    new EncounterTableDef
                    {
                        Id = "enc_f1",
                        RatePercent = 14,
                        GraceSteps = 4,
                        Entries = new[]
                        {
                            new EncounterEntry { GroupId = "f1_slimes", Weight = 45 },
                            new EncounterEntry { GroupId = "f1_mixed", Weight = 35 },
                            new EncounterEntry { GroupId = "f1_bats", Weight = 20 },
                        },
                    },
                    new EncounterTableDef
                    {
                        Id = "enc_f2",
                        RatePercent = 16,
                        GraceSteps = 4,
                        Entries = new[]
                        {
                            new EncounterEntry { GroupId = "f2_hounds", Weight = 50 },
                            new EncounterEntry { GroupId = "f2_cult", Weight = 35 },
                            new EncounterEntry { GroupId = "f1_bats", Weight = 15 },
                        },
                    },
                    new EncounterTableDef
                    {
                        Id = "enc_f3",
                        RatePercent = 18,
                        GraceSteps = 4,
                        Entries = new[]
                        {
                            new EncounterEntry { GroupId = "f3_guard", Weight = 50 },
                            new EncounterEntry { GroupId = "f3_pack", Weight = 35 },
                            new EncounterEntry { GroupId = "f2_cult", Weight = 15 },
                        },
                    },
                },
                Shops = new[]
                {
                    new ShopDef
                    {
                        Id = "town_shop",
                        Name = "灯守りのよろず屋",
                        ProductIds = new[]
                        {
                            "herb",
                            "hi_herb",
                            "ether_dew",
                            "iron_blade",
                            "silver_mace",
                            "chain_mail",
                            "silk_cloak",
                        },
                    },
                },
                Maps = new[]
                {
                    BuildTownMap(),
                    BuildDungeonF1(),
                    BuildDungeonF2(),
                    BuildDungeonF3(),
                    BuildBossRoom(),
                },
            };
        }

        public static MapDef BuildTownMap() => new()
        {
            Id = "town",
            Name = "灯守りの町",
            IsSafeZone = true,
            EncounterTableId = string.Empty,
            GridText =
                "##################\n" +
                "########S#########\n" +
                "######,.,.,#######\n" +
                "#....#,,+,,#.....#\n" +
                "#....#,,+,,#.....#\n" +
                "##.###,,+,,###.###\n" +
                "#......,+,.......#\n" +
                "#,..++++++++++..,#\n" +
                "#................#\n" +
                "###.##......~~~~.#\n" +
                "#....#......~~~~.#\n" +
                "#....#..........,#\n" +
                "######..........,#\n" +
                "##################",
            Portals = new[]
            {
                new PortalDef { X = 8, Y = 1, TargetMapId = "dungeon_f1", TargetX = 2, TargetY = 10 },
            },
            Npcs = new[]
            {
                new NpcDef
                {
                    Id = "innkeeper",
                    Name = "宿屋の主人",
                    X = 2,
                    Y = 4,
                    Facility = FacilityKind.Inn,
                    Dialogue = "旅の疲れを癒していくかい？",
                },
                new NpcDef
                {
                    Id = "priest",
                    Name = "教会の神父",
                    X = 14,
                    Y = 4,
                    Facility = FacilityKind.Church,
                    Dialogue = "灯の加護があらんことを。",
                },
                new NpcDef
                {
                    Id = "merchant",
                    Name = "よろず屋",
                    X = 3,
                    Y = 11,
                    Facility = FacilityKind.Shop,
                    ShopId = "town_shop",
                    Dialogue = "旅の支度なら任せてくれ！",
                },
                new NpcDef
                {
                    Id = "guard",
                    Name = "門番",
                    X = 7,
                    Y = 2,
                    Facility = FacilityKind.None,
                    Dialogue = "北の階段は『焔鎖の坑道』へ続く。傷ついたら宿屋へ、倒れた仲間は教会へ連れていけ。",
                },
                new NpcDef
                {
                    Id = "elder",
                    Name = "長老",
                    X = 6,
                    Y = 8,
                    Facility = FacilityKind.None,
                    Dialogue = "地下三階の鉄格子は、地下二階の宝箱に眠る『鉄格子の鍵』で開くはずじゃ。",
                    ConditionFlag = "flag_iron_key",
                    ConditionalDialogue = "おお、『鉄格子の鍵』を見つけたか！ 地下三階の鉄格子を開けば祭壇へ進めるぞ。",
                },
                new NpcDef
                {
                    Id = "scholar",
                    Name = "学者",
                    X = 11,
                    Y = 8,
                    Facility = FacilityKind.None,
                    Dialogue = "坑道の奥にいる魔将ヴァルグは炎を纏う。氷の呪文『フロスト』や『グレイシア』が弱点だ。",
                    ConditionFlag = "boss_defeated",
                    ConditionalDialogue = "坑道の焔が鎮まったのだな…町を救ってくれて本当にありがとう！",
                },
            },
        };

        public static MapDef BuildDungeonF1() => new()
        {
            Id = "dungeon_f1",
            Name = "焔鎖の坑道 B1F",
            IsSafeZone = false,
            EncounterTableId = "enc_f1",
            GridText =
                "##################\n" +
                "#......#.........#\n" +
                "#..C...#......C..#\n" +
                "#......#.........#\n" +
                "#####.######.#####\n" +
                "#................#\n" +
                "#..~~~~....~~~~..#\n" +
                "#................#\n" +
                "####.########.####\n" +
                "#................#\n" +
                "#........#.....S.#\n" +
                "#.S......#.......#\n" +
                "##################",
            Portals = new[]
            {
                new PortalDef { X = 2, Y = 11, TargetMapId = "town", TargetX = 8, TargetY = 2 },
                new PortalDef { X = 15, Y = 10, TargetMapId = "dungeon_f2", TargetX = 2, TargetY = 2 },
            },
            Chests = new[]
            {
                new ChestDef
                {
                    X = 3,
                    Y = 2,
                    OpenedFlag = "chest_f1_gold",
                    RewardGold = 60,
                    RewardLabel = "60G",
                },
                new ChestDef
                {
                    X = 14,
                    Y = 2,
                    OpenedFlag = "chest_f1_rod",
                    RewardEquipId = "rime_rod",
                    RewardLabel = "氷紋の杖",
                },
            },
        };

        public static MapDef BuildDungeonF2() => new()
        {
            Id = "dungeon_f2",
            Name = "焔鎖の坑道 B2F",
            IsSafeZone = false,
            EncounterTableId = "enc_f2",
            GridText =
                "##################\n" +
                "#.S....#.........#\n" +
                "#......#......C..#\n" +
                "#......#.........#\n" +
                "#..#####....######\n" +
                "#................#\n" +
                "######..~~..######\n" +
                "#................#\n" +
                "######.#####.#####\n" +
                "#........#.......#\n" +
                "#..C.....#.....S.#\n" +
                "#........#.......#\n" +
                "##################",
            Portals = new[]
            {
                new PortalDef { X = 2, Y = 1, TargetMapId = "dungeon_f1", TargetX = 14, TargetY = 10 },
                new PortalDef { X = 15, Y = 10, TargetMapId = "dungeon_f3", TargetX = 2, TargetY = 10 },
            },
            Chests = new[]
            {
                new ChestDef
                {
                    X = 14,
                    Y = 2,
                    OpenedFlag = "chest_f2_key",
                    RewardFlag = "flag_iron_key",
                    RewardLabel = "鉄格子の鍵",
                },
                new ChestDef
                {
                    X = 3,
                    Y = 10,
                    OpenedFlag = "chest_f2_armor",
                    RewardEquipId = "ward_plate",
                    RewardLabel = "守護の胸甲",
                },
            },
        };

        public static MapDef BuildDungeonF3() => new()
        {
            Id = "dungeon_f3",
            Name = "焔鎖の坑道 B3F",
            IsSafeZone = false,
            EncounterTableId = "enc_f3",
            GridText =
                "##################\n" +
                "#########S########\n" +
                "#.......+++......#\n" +
                "#..C....+++...C..#\n" +
                "#.......+++......#\n" +
                "#########D########\n" +
                "#.......+++......#\n" +
                "#..~~~~.+++.~~~~.#\n" +
                "#................#\n" +
                "####.########.####\n" +
                "#................#\n" +
                "#.S..............#\n" +
                "##################",
            Portals = new[]
            {
                new PortalDef { X = 2, Y = 11, TargetMapId = "dungeon_f2", TargetX = 14, TargetY = 10 },
                new PortalDef { X = 9, Y = 1, TargetMapId = "boss_room", TargetX = 8, TargetY = 10 },
            },
            Doors = new[]
            {
                new LockedDoorDef
                {
                    X = 9,
                    Y = 5,
                    RequiredKeyFlag = "flag_iron_key",
                    OpenedFlag = "door_f3_main",
                    LockedMessage = "頑丈な鉄格子だ。『鉄格子の鍵』が必要だ。",
                    UnlockedMessage = "『鉄格子の鍵』で鉄格子を開けた！",
                },
            },
            Chests = new[]
            {
                new ChestDef
                {
                    X = 3,
                    Y = 3,
                    OpenedFlag = "chest_f3_dew",
                    RewardItemId = "ether_dew",
                    RewardLabel = "星露",
                },
                new ChestDef
                {
                    X = 14,
                    Y = 3,
                    OpenedFlag = "chest_f3_sword",
                    RewardEquipId = "ember_greatsword",
                    RewardLabel = "焔断ちの大剣",
                },
            },
        };

        public static MapDef BuildBossRoom() => new()
        {
            Id = "boss_room",
            Name = "焔鎖の祭壇",
            IsSafeZone = false,
            EncounterTableId = string.Empty,
            GridText =
                "#################\n" +
                "#####.......#####\n" +
                "####..+++++..####\n" +
                "####..+++++..####\n" +
                "####...+++...####\n" +
                "######..+..######\n" +
                "######..+..######\n" +
                "#####...+...#####\n" +
                "#####...+...#####\n" +
                "#####...+...#####\n" +
                "#####...+...#####\n" +
                "########S########\n" +
                "#################",
            Portals = new[]
            {
                new PortalDef { X = 8, Y = 11, TargetMapId = "dungeon_f3", TargetX = 9, TargetY = 2 },
            },
            Boss = new BossTriggerDef
            {
                Enabled = true,
                BossTileX = 8,
                BossTileY = 3,
                TriggerX = 8,
                TriggerY = 4,
                BossGroupId = "group_boss",
                DefeatedFlag = "boss_defeated",
                IntroMessage = "焔鎖の魔将ヴァルグが現れた！",
            },
        };
    }
}
