using System;

namespace Game.Core
{
    public enum TownActionError
    {
        None = 0,
        InsufficientGold = 1,
        InvalidTarget = 2,
        TargetAlreadyAlive = 3,
        UnknownProduct = 4,
        NotSoldHere = 5,
        OwnershipCapReached = 6,
        CannotEquipClass = 7,
        NothingToSell = 8,
        NotAtChurch = 9,
    }

    public static class TownErrorText
    {
        public static string Describe(TownActionError error) => error switch
        {
            TownActionError.None => string.Empty,
            TownActionError.InsufficientGold => "ゴールドが足りません。",
            TownActionError.InvalidTarget => "対象が正しくありません。",
            TownActionError.TargetAlreadyAlive => "その仲間は倒れていません。",
            TownActionError.UnknownProduct => "存在しない品物です。",
            TownActionError.NotSoldHere => "この店では扱っていません。",
            TownActionError.OwnershipCapReached => "これ以上は持てません。",
            TownActionError.CannotEquipClass => "その職業では装備できません。",
            TownActionError.NothingToSell => "売れる品を持っていません。",
            TownActionError.NotAtChurch => "セーブは教会でのみ行えます。",
            _ => "実行できません。",
        };
    }

    public static class TownServices
    {
        /// <summary>宿屋に泊まり、生存している仲間の HP・MP を全回復する（戦闘不能はそのまま）。</summary>
        public static bool RestAtInn(World world, out TownActionError error)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            var cost = world.Data.Rules.InnCost;
            if (!world.TrySpendGold(cost))
            {
                error = TownActionError.InsufficientGold;
                return false;
            }

            for (var i = 0; i < world.Party.Count; i++)
            {
                world.Party[i].FullRestoreIfAlive();
            }

            error = TownActionError.None;
            return true;
        }

        /// <summary>
        /// 店で道具または装備を 1 つ買う。<paramref name="equipMemberIndex"/> に 0 以上を渡すと購入と同時に装備し、旧装備を袋へ戻す。
        /// </summary>
        public static bool BuyProduct(
            World world,
            ShopInfo shop,
            string productId,
            int equipMemberIndex,
            out TownActionError error)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (shop == null)
            {
                throw new ArgumentNullException(nameof(shop));
            }

            if (!world.Data.IsKnownProduct(productId))
            {
                error = TownActionError.UnknownProduct;
                return false;
            }

            if (!shop.Sells(productId))
            {
                error = TownActionError.NotSoldHere;
                return false;
            }

            var price = world.Data.ProductPrice(productId);
            if (world.Gold < price)
            {
                error = TownActionError.InsufficientGold;
                return false;
            }

            if (!world.Inventory.CanAdd(world.Party, productId, 1))
            {
                error = TownActionError.OwnershipCapReached;
                return false;
            }

            if (equipMemberIndex >= 0)
            {
                if (equipMemberIndex >= world.Party.Count || !world.Data.TryGetEquip(productId, out var equip))
                {
                    error = TownActionError.InvalidTarget;
                    return false;
                }

                var member = world.Party[equipMemberIndex];
                if (!member.CanEquip(equip!))
                {
                    error = TownActionError.CannotEquipClass;
                    return false;
                }

                var oldId = equip!.Slot == EquipSlot.Weapon ? member.WeaponId : member.ArmorId;
                if (oldId.Length > 0 && world.Inventory.Count(oldId) >= world.Inventory.MaxStack)
                {
                    error = TownActionError.OwnershipCapReached;
                    return false;
                }

                world.TrySpendGold(price);
                var replaced = member.Equip(equip);
                if (replaced.Length > 0)
                {
                    world.Inventory.Add(replaced, 1);
                }

                error = TownActionError.None;
                return true;
            }

            world.TrySpendGold(price);
            world.Inventory.Add(productId, 1);
            error = TownActionError.None;
            return true;
        }

        /// <summary>所持袋または装備中の品を 1 つ買値の半額（端数切り捨て）で売却する。装備中しか無ければ外してから売る。</summary>
        public static bool SellProduct(World world, string productId, out int goldGained, out TownActionError error)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            goldGained = 0;
            if (!world.Data.IsKnownProduct(productId))
            {
                error = TownActionError.UnknownProduct;
                return false;
            }

            if (world.Inventory.Count(productId) > 0)
            {
                world.Inventory.Remove(productId, 1);
                goldGained = world.Data.ProductSellPrice(productId);
                world.AddGold(goldGained);
                error = TownActionError.None;
                return true;
            }

            if (world.Data.TryGetEquip(productId, out var equip))
            {
                for (var i = 0; i < world.Party.Count; i++)
                {
                    var m = world.Party[i];
                    var equippedId = equip!.Slot == EquipSlot.Weapon ? m.WeaponId : m.ArmorId;
                    if (string.Equals(equippedId, productId, StringComparison.Ordinal))
                    {
                        m.Unequip(equip.Slot);
                        goldGained = world.Data.ProductSellPrice(productId);
                        world.AddGold(goldGained);
                        error = TownActionError.None;
                        return true;
                    }
                }
            }

            error = TownActionError.NothingToSell;
            return false;
        }

        /// <summary>指定メンバーの装備欄から直接外して売却する。</summary>
        public static bool SellEquipped(World world, int memberIndex, EquipSlot slot, out int goldGained, out TownActionError error)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            goldGained = 0;
            if (memberIndex < 0 || memberIndex >= world.Party.Count)
            {
                error = TownActionError.InvalidTarget;
                return false;
            }

            var member = world.Party[memberIndex];
            var equippedId = slot == EquipSlot.Weapon ? member.WeaponId : member.ArmorId;
            if (equippedId.Length == 0)
            {
                error = TownActionError.NothingToSell;
                return false;
            }

            member.Unequip(slot);
            goldGained = world.Data.ProductSellPrice(equippedId);
            world.AddGold(goldGained);
            error = TownActionError.None;
            return true;
        }

        /// <summary>教会で戦闘不能の仲間 1 人を蘇生する（HP 全快、MP は保持）。</summary>
        public static bool ReviveAtChurch(World world, int memberIndex, out int costPaid, out TownActionError error)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            costPaid = 0;
            if (memberIndex < 0 || memberIndex >= world.Party.Count)
            {
                error = TownActionError.InvalidTarget;
                return false;
            }

            var member = world.Party[memberIndex];
            if (member.IsAlive)
            {
                error = TownActionError.TargetAlreadyAlive;
                return false;
            }

            var cost = world.Data.Rules.ReviveCostPerLevel * member.Level;
            if (!world.TrySpendGold(cost))
            {
                error = TownActionError.InsufficientGold;
                return false;
            }

            member.Revive(member.MaxHp);
            costPaid = cost;
            error = TownActionError.None;
            return true;
        }
    }
}
