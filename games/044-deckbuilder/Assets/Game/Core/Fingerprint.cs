using System;
using TileMotion;

namespace Game.Core
{
    /// <summary>
    /// 設定とデータの指紋。状態のハッシュに混ぜ、規則の値やカードの中身が違えば同じ盤面でも別のハッシュにする（040）。
    /// 試合の始めに 1 回だけ計算する。
    /// </summary>
    public static class Fingerprint
    {
        public static uint Of(GameData data, DeckConfig c)
        {
            if (data == null) { throw new ArgumentNullException(nameof(data)); }
            if (c == null) { throw new ArgumentNullException(nameof(c)); }
            var h = StateHash.Start();
            foreach (var value in new[]
            {
                c.PlayerMaxHp, c.Energy, c.DrawPerTurn, c.HandLimit, c.MaxEnemies, c.MaxEffects, c.MaxAmount, c.WeakPercent, c.VulnerablePercent,
                c.RewardChoices, c.MinDeckSize, c.RestHealPercent, c.RestMaxHpGain,
            })
            {
                h.Mix(value);
            }

            foreach (var w in c.NormalRarityWeights) { h.Mix(w); }
            foreach (var w in c.HardRarityWeights) { h.Mix(w); }

            foreach (var card in data.Cards)
            {
                MixText(ref h, card.Id);
                h.Mix((int)card.Type);
                h.Mix((int)card.Rarity);
                h.Mix(card.Cost);
                h.Mix(card.Exhaust);
                foreach (var e in card.Effects) { MixEffect(ref h, e); }
            }

            foreach (var d in data.StarterDeck) { h.Mix(d.Index); }
            foreach (var enemy in data.Enemies)
            {
                MixText(ref h, enemy.Id);
                h.Mix(enemy.Hp);
                h.Mix((int)enemy.Order);
                h.Mix(enemy.HalfMove);
                foreach (var m in enemy.Moves)
                {
                    h.Mix(m.Weight);
                    h.Mix(m.Effects.Count);
                    foreach (var e in m.Effects) { MixEffect(ref h, e); }
                }
            }

            foreach (var enc in data.Encounters)
            {
                h.Mix((int)enc.Tier);
                foreach (var f in enc.Foes) { h.Mix(f.Index); }
            }

            return h.Value;
        }

        private static void MixEffect(ref StateHash h, EffectDef e)
        {
            h.Mix((int)e.Kind);
            h.Mix(e.Amount);
            h.Mix((int)e.Target);
            h.Mix((int)e.Status);
            MixText(ref h, e.Summon);
        }

        private static void MixText(ref StateHash h, string text)
        {
            h.Mix(text.Length);
            foreach (var ch in text) { h.Mix(ch); }
        }
    }
}
