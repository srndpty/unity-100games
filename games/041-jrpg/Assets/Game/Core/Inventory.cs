using System;
using System.Collections.Generic;
using TileMotion;

namespace Game.Core
{
    public readonly struct InventoryEntry
    {
        public InventoryEntry(string productId, int count)
        {
            ProductId = productId;
            Count = count;
        }

        public string ProductId { get; }
        public int Count { get; }
    }

    /// <summary>未装備の道具・装備をまとめて管理する所持袋（各 ID の上限は MaxStack）。</summary>
    public sealed class Inventory
    {
        private readonly GameData _data;
        private readonly SortedDictionary<string, int> _counts;

        public Inventory(GameData data)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        }

        private Inventory(GameData data, SortedDictionary<string, int> counts)
        {
            _data = data;
            _counts = counts;
        }

        public int MaxStack => _data.Rules.MaxItemStack;

        public int Count(string productId)
        {
            if (string.IsNullOrEmpty(productId))
            {
                return 0;
            }

            return _counts.TryGetValue(productId, out var c) ? c : 0;
        }

        public int TotalOwned(Party party, string productId)
        {
            if (party == null)
            {
                throw new ArgumentNullException(nameof(party));
            }

            return Count(productId) + party.EquippedCount(productId);
        }

        public bool CanAdd(Party party, string productId, int amount = 1)
        {
            if (party == null)
            {
                throw new ArgumentNullException(nameof(party));
            }

            if (amount <= 0 || !_data.IsKnownProduct(productId))
            {
                return false;
            }

            return TotalOwned(party, productId) + amount <= MaxStack;
        }

        public bool Add(string productId, int amount = 1)
        {
            if (amount <= 0 || !_data.IsKnownProduct(productId))
            {
                return false;
            }

            var current = Count(productId);
            if (current + amount > MaxStack)
            {
                return false;
            }

            _counts[productId] = current + amount;
            return true;
        }

        public bool Remove(string productId, int amount = 1)
        {
            if (amount <= 0 || string.IsNullOrEmpty(productId))
            {
                return false;
            }

            var current = Count(productId);
            if (current < amount)
            {
                return false;
            }

            var next = current - amount;
            if (next == 0)
            {
                _counts.Remove(productId);
            }
            else
            {
                _counts[productId] = next;
            }

            return true;
        }

        public IReadOnlyList<InventoryEntry> AllEntries()
        {
            var list = new List<InventoryEntry>(_counts.Count);
            foreach (var kv in _counts)
            {
                list.Add(new InventoryEntry(kv.Key, kv.Value));
            }

            return list.AsReadOnly();
        }

        public IReadOnlyList<InventoryEntry> UsableItems()
        {
            var list = new List<InventoryEntry>();
            foreach (var kv in _counts)
            {
                if (_data.TryGetItem(kv.Key, out _))
                {
                    list.Add(new InventoryEntry(kv.Key, kv.Value));
                }
            }

            return list.AsReadOnly();
        }

        /// <summary>所持袋にある装備で、指定メンバーが着けられるものを返す。</summary>
        public IReadOnlyList<EquipInfo> EquippableInBag(PartyMember member, EquipSlot slot)
        {
            if (member == null)
            {
                throw new ArgumentNullException(nameof(member));
            }

            var list = new List<EquipInfo>();
            foreach (var kv in _counts)
            {
                if (_data.TryGetEquip(kv.Key, out var eq) && eq!.Slot == slot && member.CanEquip(eq))
                {
                    list.Add(eq);
                }
            }

            return list.AsReadOnly();
        }

        /// <summary>所持袋から装備を着け、元の装備があれば所持袋へ戻す。</summary>
        public bool EquipFromBag(PartyMember member, string equipId)
        {
            if (member == null)
            {
                throw new ArgumentNullException(nameof(member));
            }

            if (!_data.TryGetEquip(equipId, out var eq) || !member.CanEquip(eq!) || Count(equipId) <= 0)
            {
                return false;
            }

            var currentSlotId = eq!.Slot == EquipSlot.Weapon ? member.WeaponId : member.ArmorId;
            if (string.Equals(currentSlotId, equipId, StringComparison.Ordinal))
            {
                return true;
            }

            if (currentSlotId.Length > 0 && Count(currentSlotId) >= MaxStack)
            {
                return false;
            }

            Remove(equipId, 1);
            var previous = member.Equip(eq);
            if (previous.Length > 0)
            {
                Add(previous, 1);
            }

            return true;
        }

        public bool UnequipToBag(PartyMember member, EquipSlot slot)
        {
            if (member == null)
            {
                throw new ArgumentNullException(nameof(member));
            }

            var current = slot == EquipSlot.Weapon ? member.WeaponId : member.ArmorId;
            if (current.Length == 0)
            {
                return false;
            }

            if (Count(current) >= MaxStack)
            {
                return false;
            }

            member.Unequip(slot);
            Add(current, 1);
            return true;
        }

        public Inventory Clone() => new(_data, new SortedDictionary<string, int>(_counts, StringComparer.Ordinal));

        public void MixHash(ref StateHash hash)
        {
            hash.Mix(_counts.Count);
            foreach (var kv in _counts)
            {
                if (_data.TryGetItem(kv.Key, out var it))
                {
                    hash.Mix(1);
                    hash.Mix(it!.Index);
                }
                else
                {
                    hash.Mix(2);
                    hash.Mix(_data.GetEquip(kv.Key).Index);
                }

                hash.Mix(kv.Value);
            }
        }
    }
}
