using System;
using TileMotion;

namespace Game.Core
{
    /// <summary>地面に落ちた回復アイテム。</summary>
    public readonly struct Item : IEquatable<Item>
    {
        public Item(long serial, int x, int z)
        {
            Serial = serial;
            X = x;
            Z = z;
        }

        /// <summary>落ちた順の番号（1 から）。0 は空き。</summary>
        public long Serial { get; }

        public int X { get; }

        public int Z { get; }

        public bool Active => Serial != 0;

        public bool Equals(Item other) => Serial == other.Serial && X == other.X && Z == other.Z;

        public override bool Equals(object? obj) => obj is Item other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Serial, X, Z);

        public static bool operator ==(Item left, Item right) => left.Equals(right);

        public static bool operator !=(Item left, Item right) => !left.Equals(right);
    }

    /// <summary>
    /// 回復アイテム。面のデータで food を付けた敵が倒れて消えると、その位置（プレイヤーが届く画面の中へ寄せる）に落ちる。
    /// 地上で立ち・歩きのプレイヤーの喰らい箱がアイテムの箱と 3 軸で重なると拾い、体力が HealAmount 戻る（最大まで）。
    /// 置き場が満杯なら落とさない（面のデータの検証で food の数は置き場以下に限る）。
    /// </summary>
    public sealed partial class Brawl
    {
        public const int ItemCapacity = 8;

        private readonly Item[] _items = new Item[ItemCapacity];
        private long _itemSerial;

        public int HealCount { get; private set; }

        public Item ItemAt(int index)
        {
            if (index < 0 || index >= ItemCapacity) { throw new ArgumentOutOfRangeException(nameof(index)); }
            return _items[index];
        }

        public int ItemsOnGround
        {
            get
            {
                int count = 0;
                for (int i = 0; i < ItemCapacity; i++)
                {
                    if (_items[i].Active) { count++; }
                }

                return count;
            }
        }

        /// <summary>アイテムの箱（地面に置いた低い箱）。</summary>
        public DepthBox ItemBox(Item item)
        {
            if (!item.Active) { throw new ArgumentException("空のアイテムです。", nameof(item)); }
            int half = _config.ItemWidth / 2;
            return new DepthBox(item.X - half, item.X + half, 0, 8 * Px.One, item.Z, _config.ItemDepth);
        }

        /// <summary>アイテムを落とす（敵が消えるときと、テスト・撮影の入口）。置き場が満杯なら false。</summary>
        public bool Drop(int x, int z)
        {
            for (int i = 0; i < ItemCapacity; i++)
            {
                if (_items[i].Active) { continue; }
                x = Math.Clamp(x, PlayerMinX, PlayerMaxX);
                z = Math.Clamp(z, _config.ZMin, _config.ZMax);
                _items[i] = new Item(++_itemSerial, x, z);
                return true;
            }

            return false;
        }

        private void PickUp()
        {
            var p = Player;
            if (p.Y != 0 || (p.State != ActorState.Idle && p.State != ActorState.Walk) || p.Defeated) { return; }
            var body = p.Body;
            for (int i = 0; i < ItemCapacity; i++)
            {
                if (!_items[i].Active || !ItemBox(_items[i]).Overlaps(body)) { continue; }
                _items[i] = default;
                p.Health = Math.Min(p.Def!.Health, p.Health + _config.HealAmount);
                HealCount++;
            }
        }

        private void MixItems(ref StateHash hash)
        {
            hash.Mix(_itemSerial);
            hash.Mix(HealCount);
            for (int i = 0; i < ItemCapacity; i++)
            {
                hash.Mix(_items[i].Serial);
                hash.Mix(_items[i].X);
                hash.Mix(_items[i].Z);
            }
        }
    }
}
