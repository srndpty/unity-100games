using System;
using IntGeometry;
using TileMotion;

namespace Game.Core
{
    public enum FieldInteractionKind
    {
        None = 0,
        Dialogue = 1,
        OpenInn = 2,
        OpenShop = 3,
        OpenChurch = 4,
        OpenedChest = 5,
        ChestAlreadyOpen = 6,
        ChestBagFull = 7,
        UnlockedDoor = 8,
        LockedDoorBlocked = 9,
        BossTriggered = 10,
    }

    public readonly struct FieldInteraction
    {
        public FieldInteraction(
            FieldInteractionKind kind,
            string speaker = "",
            string message = "",
            string targetId = "")
        {
            Kind = kind;
            Speaker = speaker ?? string.Empty;
            Message = message ?? string.Empty;
            TargetId = targetId ?? string.Empty;
        }

        public FieldInteractionKind Kind { get; }
        public string Speaker { get; }
        public string Message { get; }
        public string TargetId { get; }
    }

    /// <summary>ゲーム世界全体の状態（パーティ・所持袋・フラグ・位置・所持金・統計）を束ねる。</summary>
    public sealed class World
    {
        private World(
            GameData data,
            int matchSeed,
            Party party,
            Inventory inventory,
            FlagSet flags,
            Explorer explorer,
            int gold,
            int battleCount,
            long playTicks)
        {
            Data = data;
            MatchSeed = matchSeed;
            Party = party;
            Inventory = inventory;
            Flags = flags;
            Explorer = explorer;
            Gold = gold;
            BattleCount = battleCount;
            PlayTicks = playTicks;
        }

        public GameData Data { get; }
        public int MatchSeed { get; }
        public Party Party { get; }
        public Inventory Inventory { get; }
        public FlagSet Flags { get; }
        public Explorer Explorer { get; }
        public int Gold { get; private set; }
        public int BattleCount { get; private set; }
        public long PlayTicks { get; private set; }

        public FieldMap CurrentMap => Data.GetMap(Explorer.MapId);
        public bool IsCleared => Flags.Get(Data.Rules.BossDefeatedFlag);

        public static World CreateNew(GameData data, int matchSeed)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            var party = new Party(data);
            var inventory = new Inventory(data);
            for (var i = 0; i < data.Rules.StartItems.Count; i++)
            {
                var entry = data.Rules.StartItems[i];
                inventory.Add(entry.ItemId, entry.Count);
            }

            var flags = new FlagSet(data);
            var explorer = new Explorer(data, matchSeed);
            return new World(data, matchSeed, party, inventory, flags, explorer, data.Rules.StartGold, battleCount: 0, playTicks: 0L);
        }

        internal static World Restore(
            GameData data,
            int matchSeed,
            Party party,
            Inventory inventory,
            FlagSet flags,
            Explorer explorer,
            int gold,
            int battleCount,
            long playTicks)
        {
            return new World(data, matchSeed, party, inventory, flags, explorer, gold, battleCount, playTicks);
        }

        public void AdvancePlayTicks(long ticks = 1)
        {
            if (ticks > 0)
            {
                PlayTicks += ticks;
            }
        }

        public void AddGold(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            var sum = (long)Gold + amount;
            Gold = (int)Math.Min(Data.Rules.MaxGold, sum);
        }

        public bool TrySpendGold(int amount)
        {
            if (amount < 0 || Gold < amount)
            {
                return false;
            }

            Gold -= amount;
            return true;
        }

        /// <summary>次の戦闘専用の決定的な乱数系列を返す。</summary>
        public RandomSeries CreateBattleRng() => RandomSeries.Derive(MatchSeed, BattleCount, 0);

        /// <summary>現在、教会の神父と向かい合っていて移動中でないかを返す（セーブ許可条件）。</summary>
        public bool CanSaveAtChurch()
        {
            if (Explorer.IsMoving)
            {
                return false;
            }

            Explorer.FrontTile(out var fx, out var fy);
            var npc = CurrentMap.NpcAt(fx, fy);
            return npc != null && npc.Facility == FacilityKind.Church;
        }

        /// <summary>向いている 1 マス先を調べる・話す。</summary>
        public FieldInteraction Interact()
        {
            if (Explorer.IsMoving)
            {
                return new FieldInteraction(FieldInteractionKind.None);
            }

            var map = CurrentMap;
            Explorer.FrontTile(out var fx, out var fy);

            var npc = map.NpcAt(fx, fy);
            if (npc != null)
            {
                var speech = npc.Speak(Flags);
                return npc.Facility switch
                {
                    FacilityKind.Inn => new FieldInteraction(FieldInteractionKind.OpenInn, npc.Name, speech, npc.Id),
                    FacilityKind.Shop => new FieldInteraction(FieldInteractionKind.OpenShop, npc.Name, speech, npc.ShopId),
                    FacilityKind.Church => new FieldInteraction(FieldInteractionKind.OpenChurch, npc.Name, speech, npc.Id),
                    _ => new FieldInteraction(FieldInteractionKind.Dialogue, npc.Name, speech, npc.Id),
                };
            }

            var chest = map.ChestAt(fx, fy);
            if (chest != null)
            {
                if (Flags.Get(chest.OpenedFlag))
                {
                    return new FieldInteraction(FieldInteractionKind.ChestAlreadyOpen, message: "宝箱はからっぽだ。");
                }

                if (chest.RewardItemId.Length > 0 && !Inventory.CanAdd(Party, chest.RewardItemId, 1))
                {
                    return new FieldInteraction(FieldInteractionKind.ChestBagFull, message: "持ち物がいっぱいで受け取れない！");
                }

                if (chest.RewardEquipId.Length > 0 && !Inventory.CanAdd(Party, chest.RewardEquipId, 1))
                {
                    return new FieldInteraction(FieldInteractionKind.ChestBagFull, message: "持ち物がいっぱいで受け取れない！");
                }

                if (chest.RewardItemId.Length > 0)
                {
                    Inventory.Add(chest.RewardItemId, 1);
                }

                if (chest.RewardEquipId.Length > 0)
                {
                    Inventory.Add(chest.RewardEquipId, 1);
                }

                if (chest.RewardGold > 0)
                {
                    AddGold(chest.RewardGold);
                }

                if (chest.RewardFlag.Length > 0)
                {
                    Flags.Set(chest.RewardFlag, true);
                }

                Flags.Set(chest.OpenedFlag, true);
                return new FieldInteraction(
                    FieldInteractionKind.OpenedChest,
                    message: $"宝箱を開けた！ 『{chest.RewardLabel}』を手に入れた！",
                    targetId: chest.OpenedFlag);
            }

            var door = map.DoorAt(fx, fy);
            if (door != null && !Flags.Get(door.OpenedFlag))
            {
                if (Flags.Get(door.RequiredKeyFlag))
                {
                    Flags.Set(door.OpenedFlag, true);
                    return new FieldInteraction(FieldInteractionKind.UnlockedDoor, message: door.UnlockedMessage, targetId: door.OpenedFlag);
                }

                return new FieldInteraction(FieldInteractionKind.LockedDoorBlocked, message: door.LockedMessage, targetId: door.OpenedFlag);
            }

            if (map.Boss.Enabled &&
                !Flags.Get(map.Boss.DefeatedFlag) &&
                fx == map.Boss.BossTileX &&
                fy == map.Boss.BossTileY)
            {
                Explorer.ResetEncounterSteps();
                return new FieldInteraction(FieldInteractionKind.BossTriggered, message: map.Boss.IntroMessage, targetId: map.Boss.BossGroupId);
            }

            return new FieldInteraction(FieldInteractionKind.None);
        }

        /// <summary>戦闘終了時の結果を世界へ反映する。</summary>
        public void ApplyBattleOutcome(BattleState battle)
        {
            if (battle == null)
            {
                throw new ArgumentNullException(nameof(battle));
            }

            if (battle.Outcome == BattleOutcome.Ongoing)
            {
                throw new InvalidOperationException("進行中の戦闘結果は適用できません。");
            }

            BattleCount++;
            switch (battle.Outcome)
            {
                case BattleOutcome.Victory:
                    AddGold(battle.GoldEarned);
                    Explorer.ResetEncounterSteps();
                    if (battle.IsBossBattle)
                    {
                        Flags.Set(Data.Rules.BossDefeatedFlag, true);
                    }

                    break;

                case BattleOutcome.Fled:
                    Explorer.ResetEncounterSteps();
                    break;

                case BattleOutcome.PartyWiped:
                    // 全滅ペナルティ: 所持金半減・教会へ戻って全員 HP/MP 全回復・装備/道具/経験値/フラグは維持
                    Gold /= 2;
                    for (var i = 0; i < Party.Count; i++)
                    {
                        Party[i].FullRestore();
                    }

                    Explorer.WarpTo(
                        Data.Rules.ChurchMapId,
                        Data.Rules.ChurchRespawnX,
                        Data.Rules.ChurchRespawnY,
                        Direction.Up);
                    break;
            }
        }

        public World Clone() => new(
            Data,
            MatchSeed,
            Party.Clone(),
            Inventory.Clone(),
            Flags.Clone(),
            Explorer.Clone(),
            Gold,
            BattleCount,
            PlayTicks);

        public uint Hash()
        {
            var hash = StateHash.Start();
            hash.Mix(Data.Fingerprint);
            hash.Mix(MatchSeed);
            hash.Mix(Gold);
            hash.Mix(BattleCount);
            hash.Mix((int)(PlayTicks & 0xFFFFFFFFL));
            hash.Mix((int)((PlayTicks >> 32) & 0xFFFFFFFFL));
            Party.MixHash(ref hash);
            Inventory.MixHash(ref hash);
            Flags.MixHash(ref hash);
            Explorer.MixHash(Data, ref hash);
            return hash.Value;
        }
    }
}
