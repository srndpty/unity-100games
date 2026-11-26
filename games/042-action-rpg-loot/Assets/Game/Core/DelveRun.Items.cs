using System;
using IntGeometry;
using TileMotion;

namespace Game.Core
{
    /// <summary>持ち物の操作の結果。</summary>
    public enum ItemResult
    {
        Done,
        InventoryFull,
        TooFar,
        NotFound,
        GroundFull,
        NotAllowed,
    }

    /// <summary>ドロップ・地面の品・持ち物と装備。</summary>
    public sealed partial class DelveRun
    {
        /// <summary>落とした品を並べる位置（敵の位置からのずれ、単位）。</summary>
        private static readonly Vec2i[] _scatter =
        {
            new(0, 0), new(128, 0), new(-128, 0), new(0, 128), new(0, -128), new(128, 128), new(-128, -128), new(128, -128), new(-128, 128),
        };

        /// <summary>敵が倒れたときに 1 回だけ呼ぶ。配置ごとの乱数系列から引くので、倒す順に依らない。</summary>
        private void DropLoot(Foe foe)
        {
            _record.DropRolls++;
            var rng = LootRng.For(Seed, FloorNumber, foe.Spawn.Index);
            var items = _generator.RollDrop(rng, FloorNumber, _loot.Drop(FoeInfo.DropId(foe.Kind)), out var potion);
            int slot = 0;
            foreach (var item in items)
            {
                _record.ItemsDropped++;
                _events |= item.Rarity switch
                {
                    Rarity.Rare => RunEvents.DropRare,
                    Rarity.Magic => RunEvents.DropMagic,
                    _ => RunEvents.DropNormal,
                };
                Place(foe.Position, slot++, item);
            }

            if (potion) { Place(foe.Position, slot, null); }
        }

        private void Place(Vec2i origin, int slot, Item? item)
        {
            if (_ground.Count >= _config.GroundCap)
            {
                _record.ItemsLost++;
                return;
            }

            var position = origin + _scatter[slot % _scatter.Length];
            if (Map.IsSolid(FloorMap.ColumnOf(position.X), FloorMap.RowOf(position.Y))) { position = origin; }
            _ground.Add(new GroundItem(_nextGroundId++, position, item));
        }

        /// <summary>回復薬は触れるだけで拾う（持てる数まで）。</summary>
        private void TouchPotions()
        {
            long range = (long)_config.PotionTouchRange * _config.PotionTouchRange;
            for (int i = _ground.Count - 1; i >= 0; i--)
            {
                var g = _ground[i];
                if (!g.IsPotion || _hero.Potions >= _config.MaxPotions) { continue; }
                if (Vec2i.DistanceSquared(g.Position, _hero.Position) >= range) { continue; }
                _ground.RemoveAt(i);
                _hero.Potions++;
                _events |= RunEvents.PickedUp;
            }
        }

        /// <summary>E：拾える距離の中で最も近い装備品を拾う。</summary>
        private void PickUpNearest()
        {
            long best = (long)_config.PickupRange * _config.PickupRange;
            GroundItem? target = null;
            foreach (var g in _ground)
            {
                if (g.IsPotion) { continue; }
                long d = Vec2i.DistanceSquared(g.Position, _hero.Position);
                if (d < best || (d == best && target == null))
                {
                    best = d;
                    target = g;
                }
            }

            if (target != null) { Take(target); }
        }

        /// <summary>名前札のクリック：番号の品を拾う。遠い・ない・倒れている・結果へ移った後は拾わない。</summary>
        public ItemResult PickUp(long groundId)
        {
            if (_hero.Dead || IsOver) { return ItemResult.NotAllowed; }
            foreach (var g in _ground)
            {
                if (g.Id != groundId) { continue; }
                long range = (long)_config.ClickPickupRange * _config.ClickPickupRange;
                if (Vec2i.DistanceSquared(g.Position, _hero.Position) > range) { return ItemResult.TooFar; }
                return Take(g);
            }

            return ItemResult.NotFound;
        }

        private ItemResult Take(GroundItem g)
        {
            if (g.IsPotion)
            {
                if (_hero.Potions >= _config.MaxPotions) { return ItemResult.InventoryFull; }
                _hero.Potions++;
            }
            else
            {
                if (_inventory.Count >= _config.InventorySize)
                {
                    _events |= RunEvents.InventoryFull;
                    return ItemResult.InventoryFull;
                }

                _inventory.Add(g.Item!);
                _record.ItemsPicked++;
                if (g.Item!.Rarity > _record.BestRarity) { _record.BestRarity = g.Item.Rarity; }
            }

            _ground.Remove(g);
            _events |= RunEvents.PickedUp;
            return ItemResult.Done;
        }

        /// <summary>持ち物の <paramref name="index"/> 番を装備する。前に付けていた品は同じ番号へ入る。</summary>
        public ItemResult Equip(int index)
        {
            if (index < 0 || index >= _inventory.Count) { return ItemResult.NotFound; }
            if (IsOver) { return ItemResult.NotAllowed; }
            var item = _inventory[index];
            var previous = _equipment.Equip(item);
            _inventory.RemoveAt(index);
            if (previous != null) { _inventory.Insert(index, previous); }
            Recompute();
            _events |= RunEvents.Equipped;
            return ItemResult.Done;
        }

        /// <summary>装備を外して持ち物の最後へ入れる。持ち物が満杯なら外さない。</summary>
        public ItemResult Unequip(Slot slot)
        {
            if (IsOver) { return ItemResult.NotAllowed; }
            if (_equipment[slot] == null) { return ItemResult.NotFound; }
            if (_inventory.Count >= _config.InventorySize) { return ItemResult.InventoryFull; }
            _inventory.Add(_equipment.Unequip(slot)!);
            Recompute();
            _events |= RunEvents.Equipped;
            return ItemResult.Done;
        }

        /// <summary>持ち物の品を足元へ捨てる。地面が満杯なら捨てない（品を消さない）。</summary>
        public ItemResult Discard(int index)
        {
            if (index < 0 || index >= _inventory.Count) { return ItemResult.NotFound; }
            if (IsOver || _hero.Dead) { return ItemResult.NotAllowed; }
            if (_ground.Count >= _config.GroundCap) { return ItemResult.GroundFull; }
            var item = _inventory[index];
            _inventory.RemoveAt(index);
            _ground.Add(new GroundItem(_nextGroundId++, _hero.Position, item));
            return ItemResult.Done;
        }

        /// <summary>持ち物の品を装備したら能力値がどう変わるか（画面の比較）。</summary>
        public System.Collections.Generic.List<StatDiff> Compare(Item candidate) => ItemText.Compare(_loot.Hero, _equipment, candidate);

        /// <summary>装備が変わったら能力値を一式から計算し直す。HP は新しい最大を超えない。</summary>
        private void Recompute()
        {
            _stats = StatSheet.Compute(_loot.Hero, _equipment);
            if (_hero.Life > _stats.Life) { _hero.Life = _stats.Life; }
        }

        /// <summary>テスト用：地面に品を置く（null なら回復薬）。</summary>
        internal GroundItem AddGroundForTest(Vec2i position, Item? item)
        {
            var g = new GroundItem(_nextGroundId++, position, item);
            _ground.Add(g);
            return g;
        }

        /// <summary>テスト用：飛び道具を置く。</summary>
        internal void AddProjectileForTest(Projectile projectile) => _projectiles.Add(projectile);

        /// <summary>テスト用：持ち物に品を入れる。</summary>
        internal void AddInventoryForTest(Item item) => _inventory.Add(item);

        /// <summary>テスト用：持ち物を通さずに装備する（能力値は計算し直す）。</summary>
        internal void EquipForTest(Item item)
        {
            _equipment.Equip(item);
            Recompute();
        }

        /// <summary>テスト用：能力値を直接置く（式だけを確かめる）。</summary>
        internal void SetStatsForTest(StatBlock stats) => _stats = stats;

        /// <summary>状態のハッシュ。規則（設定・戦利品・地図）の指紋と、以後の動きを決める状態をすべて混ぜる。</summary>
        public uint Hash()
        {
            var hash = StateHash.Start();
            hash.Mix(_rulesFingerprint);
            hash.Mix(Seed);
            hash.Mix(Tick);
            hash.Mix(FloorNumber);
            hash.Mix((int)Status);
            hash.Mix(VictoryTimer);
            hash.Mix(_combat.State);
            hash.Mix(_nextGroundId);
            var h = _hero;
            foreach (var v in new long[]
            {
                h.Position.X, h.Position.Y, h.Facing.X, h.Facing.Y, h.Life, h.DeathTimer, h.Invuln, h.AttackCooldown, h.SwingHitTick,
                h.SwingDirection.X, h.SwingDirection.Y, h.SkillCooldown, h.SpinHitTick, h.PotionCooldown, h.Potions,
            })
            {
                hash.Mix(v);
            }

            hash.Mix(h.Dead);
            foreach (var f in _foes)
            {
                foreach (var v in new long[]
                {
                    f.Spawn.Index, f.Position.X, f.Position.Y, f.Life, (int)f.State, f.Timer, f.Direction.X, f.Direction.Y,
                    f.FanTimer, f.ChargeTimer,
                })
                {
                    hash.Mix(v);
                }

                hash.Mix(f.Aggro);
                hash.Mix(f.ChargeHit);
                hash.Mix(f.Dead);
                hash.Mix(f.Dropped);
            }

            hash.Mix(_projectiles.Count);
            foreach (var p in _projectiles)
            {
                hash.Mix(p.Position.X);
                hash.Mix(p.Position.Y);
                hash.Mix(p.Velocity.X);
                hash.Mix(p.Velocity.Y);
                hash.Mix(p.Radius);
                hash.Mix(p.Damage);
                hash.Mix((int)p.Element);
            }

            hash.Mix(_ground.Count);
            foreach (var g in _ground)
            {
                hash.Mix(g.Id);
                hash.Mix(g.Position.X);
                hash.Mix(g.Position.Y);
                MixItem(ref hash, g.Item);
            }

            hash.Mix(_inventory.Count);
            foreach (var item in _inventory) { MixItem(ref hash, item); }
            for (int s = 0; s < SlotInfo.Count; s++) { MixItem(ref hash, _equipment[(Slot)s]); }
            foreach (var v in new[] { _record.Kills, _record.Deaths, _record.ItemsPicked, _record.PotionsUsed, _record.ItemsDropped, _record.DropRolls, _record.ItemsLost })
            {
                hash.Mix(v);
            }

            hash.Mix((int)_record.BestRarity);
            return hash.Value;
        }

        private static void MixItem(ref StateHash hash, Item? item)
        {
            if (item == null)
            {
                hash.Mix(-1);
                return;
            }

            hash.Mix(item.Base.Index);
            hash.Mix((int)item.Rarity);
            hash.Mix(item.Affixes.Count);
            foreach (var a in item.Affixes)
            {
                hash.Mix(a.Affix.Index);
                hash.Mix(a.Tier);
                foreach (var v in a.Values) { hash.Mix(v); }
            }
        }
    }
}
