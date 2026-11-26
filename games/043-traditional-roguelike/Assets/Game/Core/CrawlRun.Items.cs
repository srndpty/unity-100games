using System.Collections.Generic;
using IntGeometry;

namespace Game.Core
{
    /// <summary>拾う・使う・投げる・置く。</summary>
    public sealed partial class CrawlRun
    {
        /// <summary>品の上に乗ったら自動で拾う。満杯なら拾わずに知らせる（移動は受け付ける）。</summary>
        private void StepOnItem()
        {
            var item = ItemAt(Player.Position);
            if (!item.HasValue) { return; }
            if (Player.Inventory.Count >= Config.InventoryCapacity)
            {
                Emit(EventKind.FullCannotPickUp, 0, -1, default, item.Value, Player.Position, 0);
                return;
            }

            PickUp();
        }

        private void PickUp()
        {
            var item = ItemAt(Player.Position)!.Value;
            SetItem(Player.Position, null);
            Player.Inventory.Add(item);
            Emit(EventKind.PickedUp, 0, -1, default, item, Player.Position, 0);
        }

        private void UseItem(int slot)
        {
            var p = Player;
            var item = p.Inventory[slot];
            p.Inventory.RemoveAt(slot);
            Emit(EventKind.Used, 0, -1, default, item, p.Position, 0);
            switch (item)
            {
                case ItemKind.HealPotion:
                    var healed = System.Math.Min(p.MaxHp - p.Hp, Config.HealAmount);
                    p.Hp += healed;
                    Emit(EventKind.Healed, 0, -1, default, item, p.Position, healed);
                    break;
                case ItemKind.PoisonPotion:
                    // 自分で飲んでも死なない（HP は 1 まで）。
                    var lost = System.Math.Min(p.Hp - 1, Config.PoisonSelfDamage);
                    p.Hp -= lost;
                    Emit(EventKind.Poisoned, 0, -1, default, item, p.Position, lost);
                    break;
                case ItemKind.SleepPotion:
                    p.SleepTurns = Config.SleepTurns;
                    Emit(EventKind.FellAsleep, 0, -1, default, item, p.Position, Config.SleepTurns);
                    break;
                case ItemKind.Bread:
                case ItemKind.BigBread:
                    var amount = item == ItemKind.Bread ? Config.BreadSatiety : Config.BigBreadSatiety;
                    var gained = System.Math.Min(Config.MaxSatiety - p.Satiety, amount);
                    p.Satiety += gained;
                    Emit(EventKind.Ate, 0, -1, default, item, p.Position, gained);
                    break;
                case ItemKind.MapScroll:
                    Known.RevealTerrain(Grid);
                    Emit(EventKind.MapRevealed, 0, -1, default, item, p.Position, 0);
                    break;
                case ItemKind.WarpScroll:
                    if (TryRandomFreeTile(_ => true, out var to))
                    {
                        p.Position = to;
                        Look();
                    }

                    Emit(EventKind.Warped, 0, -1, default, item, p.Position, 0);
                    break;
            }
        }

        /// <summary>
        /// 向いている方向へ最大 <see cref="CrawlConfig.ThrowRange"/> マス。最初の敵に当たれば効果を与えて消え、
        /// 壁（角抜けを含む）か射程で止まったら、そのマスの近くの空いた床に落ちる。
        /// </summary>
        private void ThrowItem(int slot)
        {
            var p = Player;
            var item = p.Inventory[slot];
            p.Inventory.RemoveAt(slot);
            Emit(EventKind.Thrown, 0, -1, default, item, p.Position, (int)p.Facing);
            var at = p.Position;
            for (int i = 0; i < Config.ThrowRange; i++)
            {
                if (!Grid.CanStep(at, p.Facing)) { break; }
                at += p.Facing.Offset();
                var target = MonsterAt(at);
                if (target != null)
                {
                    HitWithThrown(target, item);
                    return;
                }
            }

            PlaceItem(item, at, EventKind.ThrowLanded, 0);
        }

        private void HitWithThrown(Monster target, ItemKind item)
        {
            switch (item)
            {
                case ItemKind.HealPotion:
                    target.Hp = System.Math.Min(target.MaxHp, target.Hp + Config.HealAmount);
                    Emit(EventKind.ThrowHit, 0, target.Id, target.Kind, item, target.Position, 0);
                    return;
                case ItemKind.SleepPotion:
                    target.SleepTurns = Config.SleepTurns;
                    Emit(EventKind.ThrowHit, 0, target.Id, target.Kind, item, target.Position, 0);
                    Emit(EventKind.MonsterSlept, target.Id, -1, target.Kind, item, target.Position, Config.SleepTurns);
                    return;
            }

            var damage = item switch
            {
                ItemKind.Stone => Config.StoneDamage,
                ItemKind.PoisonPotion => Config.PoisonThrowDamage,
                _ => Config.ThrowBumpDamage,
            };
            if (target.Asleep)
            {
                target.Asleep = false;
                Emit(EventKind.MonsterWoke, target.Id, -1, target.Kind, default, target.Position, 0);
            }

            Emit(EventKind.ThrowHit, 0, target.Id, target.Kind, item, target.Position, damage);
            DamageMonster(target, damage);
        }

        /// <summary>
        /// 品を <paramref name="at"/> の近くの空いた床（品のないマス）に落とす。近い順（8 方向の幅優先、方向の順）に探し、
        /// 階の品が上限か置き場がなければ消える。
        /// </summary>
        private void PlaceItem(ItemKind item, Vec2i at, EventKind landed, int actor)
        {
            if (_itemCount < Config.FloorItemCap && TryFreeItemTile(at, out var spot))
            {
                SetItem(spot, item);
                Emit(landed, actor, -1, default, item, spot, 0);
                return;
            }

            Emit(EventKind.ItemLost, actor, -1, default, item, at, 0);
        }

        private bool TryFreeItemTile(Vec2i origin, out Vec2i result)
        {
            var seen = new HashSet<Vec2i> { origin };
            var queue = new Queue<Vec2i>();
            queue.Enqueue(origin);
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                if (Grid.IsWalkable(p) && ItemAt(p) == null)
                {
                    result = p;
                    return true;
                }

                for (int d = 0; d < Directions.Count; d++)
                {
                    var dir = (Direction)d;
                    if (!Grid.CanStep(p, dir)) { continue; }
                    var q = p + dir.Offset();
                    if (seen.Add(q)) { queue.Enqueue(q); }
                }
            }

            result = default;
            return false;
        }
    }
}
