using System;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class FieldAndTownTests
    {
        [Test]
        public void MovementChestsLockedDoorNpcConditionalDialogueAndSafeZoneWorkAsSpecified()
        {
            var data = DefaultGameData.Create();
            var world = World.CreateNew(data, matchSeed: 55);

            // 町（安全地帯）では何百歩歩いてもエンカウントしない
            world.Explorer.WarpTo("town", 6, 7, Direction.Right);
            for (var step = 0; step < 80; step++)
            {
                var dir = (step % 2 == 0) ? Direction.Right : Direction.Left;
                for (var t = 0; t < data.Rules.StepTicks; t++)
                {
                    var res = world.Explorer.Tick(data, world.Flags, t == 0 ? dir : (Direction?)null, out _, out _);
                    Assert.That(res, Is.Not.EqualTo(FieldStepResult.EncounterTriggered));
                }
            }

            // 長老の会話は鍵フラグの前後で切り替わる
            world.Explorer.WarpTo("town", 6, 7, Direction.Down);
            var beforeKeyTalk = world.Interact();
            Assert.That(beforeKeyTalk.Kind, Is.EqualTo(FieldInteractionKind.Dialogue));

            // B3F の施錠扉（9, 5）は鍵なしでは通行不可・解錠不可
            world.Explorer.WarpTo("dungeon_f3", 9, 6, Direction.Up);
            Assert.That(world.Explorer.RequestMove(data, world.Flags, Direction.Up), Is.EqualTo(FieldStepResult.Blocked));
            var blockedDoor = world.Interact();
            Assert.That(blockedDoor.Kind, Is.EqualTo(FieldInteractionKind.LockedDoorBlocked));

            // B2F の鍵宝箱を開けると鍵フラグが立ち、2 回目は空になり、扉が開いて歩けるようになる
            world.Explorer.WarpTo("dungeon_f2", 14, 3, Direction.Up);
            var chestOpen = world.Interact();
            Assert.That(chestOpen.Kind, Is.EqualTo(FieldInteractionKind.OpenedChest));
            Assert.That(world.Flags.Get("flag_iron_key"), Is.True);
            Assert.That(world.Interact().Kind, Is.EqualTo(FieldInteractionKind.ChestAlreadyOpen));

            world.Explorer.WarpTo("town", 6, 7, Direction.Down);
            var afterKeyTalk = world.Interact();
            Assert.That(string.Equals(beforeKeyTalk.Message, afterKeyTalk.Message, StringComparison.Ordinal), Is.False);

            world.Explorer.WarpTo("dungeon_f3", 9, 6, Direction.Up);
            Assert.That(world.Interact().Kind, Is.EqualTo(FieldInteractionKind.UnlockedDoor));
            Assert.That(world.Explorer.RequestMove(data, world.Flags, Direction.Up), Is.EqualTo(FieldStepResult.Moved));

            // 持ち物が 99 個ある宝箱は開かず、フラグも消費しない
            world.Inventory.Add("ether_dew", 98); // 初期 1 + 98 = 99
            world.Explorer.WarpTo("dungeon_f3", 3, 4, Direction.Up);
            Assert.That(world.Interact().Kind, Is.EqualTo(FieldInteractionKind.ChestBagFull));
            Assert.That(world.Flags.Get("chest_f3_dew"), Is.False);
        }

        [Test]
        public void InnShopChurchBoundariesAndWipeoutPenaltyRespectAllRules()
        {
            var data = DefaultGameData.Create();
            var world = World.CreateNew(data, matchSeed: 101);
            var shop = data.GetShop("town_shop");

            // 宿屋: 生存者のみ全回復し、戦闘不能者はそのまま。1G 不足で泊まれない
            world.Party[0].TakeDamage(world.Party[0].MaxHp); // 戦闘不能
            world.Party[1].TakeDamage(10);
            world.Party[1].SpendMp(8);

            world.TrySpendGold(world.Gold - (data.Rules.InnCost - 1));
            Assert.That(TownServices.RestAtInn(world, out var innErr), Is.False);
            Assert.That(innErr, Is.EqualTo(TownActionError.InsufficientGold));

            world.AddGold(1);
            Assert.That(TownServices.RestAtInn(world, out _), Is.True);
            Assert.That(world.Gold, Is.EqualTo(0));
            Assert.That(world.Party[0].IsAlive, Is.False);
            Assert.That(world.Party[1].Hp, Is.EqualTo(world.Party[1].MaxHp));
            Assert.That(world.Party[1].Mp, Is.EqualTo(world.Party[1].MaxMp));

            // 教会蘇生: 費用 1G 不足で失敗、ちょうどで蘇生成功、生存者への蘇生は失敗
            var reviveCost = data.Rules.ReviveCostPerLevel * world.Party[0].Level;
            world.AddGold(reviveCost - 1);
            Assert.That(TownServices.ReviveAtChurch(world, 0, out _, out var revShortErr), Is.False);
            Assert.That(revShortErr, Is.EqualTo(TownActionError.InsufficientGold));

            world.AddGold(1);
            Assert.That(TownServices.ReviveAtChurch(world, 0, out var paid, out _), Is.True);
            Assert.That(paid, Is.EqualTo(reviveCost));
            Assert.That(world.Party[0].IsAlive, Is.True);
            Assert.That(TownServices.ReviveAtChurch(world, 0, out _, out var aliveErr), Is.False);
            Assert.That(aliveErr, Is.EqualTo(TownActionError.TargetAlreadyAlive));

            // 店: 即装備購入で旧装備が袋に戻り、装備中の品は外してから半額で売却できる
            var ironPrice = data.ProductPrice("iron_blade");
            world.AddGold(ironPrice - 1);
            Assert.That(TownServices.BuyProduct(world, shop, "iron_blade", 0, out var buyShort), Is.False);
            Assert.That(buyShort, Is.EqualTo(TownActionError.InsufficientGold));

            world.AddGold(1);
            Assert.That(TownServices.BuyProduct(world, shop, "iron_blade", 0, out _), Is.True);
            Assert.That(world.Party[0].WeaponId, Is.EqualTo("iron_blade"));
            Assert.That(world.Inventory.Count("bronze_sword"), Is.EqualTo(1));

            // 袋に無い iron_blade（装備中）を売却すると、自動で外れて買値の半額が入る
            Assert.That(TownServices.SellProduct(world, "iron_blade", out var soldGold, out _), Is.True);
            Assert.That(soldGold, Is.EqualTo(ironPrice / 2));
            Assert.That(world.Party[0].WeaponId, Is.EqualTo(string.Empty));

            // 全滅ペナルティ: 所持金半減・教会正面へワープ・全員全回復・経験値/所持品/フラグは維持
            world.Flags.Set("flag_iron_key", true);
            world.Party[0].AddExp(0, 80);
            var expBefore = world.Party[0].Exp;
            var herbsBefore = world.Inventory.Count("herb");
            world.TrySpendGold(world.Gold);
            world.AddGold(55);

            var wipeBattle = new BattleState(data, data.GetGroup("f1_bats"));
            wipeBattle.Enemies[0].SetVitals(wipeBattle.Enemies[0].Hp, 10);
            for (var i = 0; i < world.Party.Count; i++)
            {
                world.Party[i].SetVitals(1, 0);
            }

            BattleResolver.ResolveTurn(
                data,
                wipeBattle,
                world.Party,
                world.Inventory,
                new[] { BattleCommand.Defend(), BattleCommand.Defend(), BattleCommand.Defend() },
                new RandomSeries(5),
                new EnemyDecision?[] { new EnemyDecision(EnemyActionKind.Spell, "flame_wave", -1), null });
            Assert.That(wipeBattle.Outcome, Is.EqualTo(BattleOutcome.PartyWiped));
            world.Explorer.WarpTo("dungeon_f2", 6, 5, Direction.Down);
            world.ApplyBattleOutcome(wipeBattle);

            Assert.That(world.Gold, Is.EqualTo(27));
            Assert.That(world.Explorer.MapId, Is.EqualTo("town"));
            Assert.That(world.Explorer.X, Is.EqualTo(data.Rules.ChurchRespawnX));
            Assert.That(world.Explorer.Y, Is.EqualTo(data.Rules.ChurchRespawnY));
            Assert.That(world.Party.LivingCount, Is.EqualTo(3));
            Assert.That(world.Party[0].Exp, Is.EqualTo(expBefore));
            Assert.That(world.Inventory.Count("herb"), Is.EqualTo(herbsBefore));
            Assert.That(world.Flags.Get("flag_iron_key"), Is.True);
        }
    }
}
