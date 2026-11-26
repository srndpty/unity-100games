using System;
using TileMotion;

namespace Game.Core
{
    /// <summary>
    /// 設定の指紋。状態のハッシュに混ぜ、規則の値が違えば同じ盤面でも別のハッシュにする（040）。試合の始めに 1 回だけ計算する。
    /// </summary>
    public static class Fingerprint
    {
        public static uint Of(CrawlConfig c)
        {
            if (c == null) { throw new ArgumentNullException(nameof(c)); }
            var hash = StateHash.Start();
            foreach (var value in new[]
            {
                c.Width, c.Height, c.MinRooms, c.MaxRooms, c.MinExtraCorridors, c.MaxExtraCorridors, c.GenerationAttempts, c.ViewRadius,
                c.PlayerHp, c.PlayerAttack, c.PlayerDefense, c.LevelHp, c.LevelAttack, c.LevelDefenseEvery, c.RegenEvery,
                c.MaxSatiety, c.HungerEvery, c.StarveDamage, c.HungryWarning, c.HitPercent, c.DamageMinPercent, c.DamageMaxPercent,
                c.InventoryCapacity, c.FloorItemCap, c.ThrowRange, c.MonstersBase, c.MonstersPerFloor, c.ItemsBase, c.ItemsPerFloor,
                c.HealAmount, c.PoisonSelfDamage, c.PoisonThrowDamage, c.StoneDamage, c.ThrowBumpDamage, c.SleepTurns,
                c.BreadSatiety, c.BigBreadSatiety,
            })
            {
                hash.Mix(value);
            }

            MixArray(ref hash, c.LevelExp);
            foreach (var m in c.Monsters)
            {
                foreach (var value in new[] { (int)m.Kind, m.Hp, m.Attack, m.Defense, m.ActionCost, m.Exp, m.Range, m.ArrowDamage })
                {
                    hash.Mix(value);
                }

                MixArray(ref hash, m.FloorWeights);
            }

            foreach (var item in c.Items)
            {
                hash.Mix((int)item.Kind);
                MixArray(ref hash, item.FloorWeights);
            }

            return hash.Value;
        }

        private static void MixArray(ref StateHash hash, int[] values)
        {
            hash.Mix(values.Length);
            foreach (var v in values) { hash.Mix(v); }
        }
    }
}
