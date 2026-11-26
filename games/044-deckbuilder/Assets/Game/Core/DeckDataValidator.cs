using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>データの規則上の検証。誤りを理由付きで全て返す（空なら有効）。</summary>
    public static class DeckDataValidator
    {
        /// <summary>報酬の抽選に入らない（初期デッキだけの）希少度以外を Common・Uncommon・Rare の順の番号にする。</summary>
        public static int RewardSlot(Rarity rarity) => rarity switch
        {
            Rarity.Common => 0,
            Rarity.Uncommon => 1,
            Rarity.Rare => 2,
            _ => -1,
        };

        public static IReadOnlyList<string> Validate(GameData data, DeckConfig config)
        {
            if (data == null) { throw new ArgumentNullException(nameof(data)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            var errors = new List<string>(config.Validate());
            if (errors.Count > 0) { return errors; }

            if (data.Cards.Count != DeckConfig.CardCount) { errors.Add($"カードは {DeckConfig.CardCount} 種（{data.Cards.Count}）"); }
            Unique("カード", data.Cards, c => c.Id, errors);
            Unique("敵", data.Enemies, e => e.Id, errors);
            Unique("遭遇", data.Encounters, e => e.Id, errors);

            foreach (var card in data.Cards) { CheckCard(card, config, errors); }
            foreach (var enemy in data.Enemies) { CheckEnemy(enemy, data, config, errors); }
            CheckEncounters(data, config, errors);

            if (data.StarterDeck.Count < config.MinDeckSize || data.StarterDeck.Count > DeckConfig.MaxStarterDeck)
            {
                errors.Add($"初期デッキは {config.MinDeckSize}〜{DeckConfig.MaxStarterDeck} 枚（{data.StarterDeck.Count}）");
            }

            Pool("NormalRarityWeights", data, config.NormalRarityWeights, config.RewardChoices, errors);
            Pool("HardRarityWeights", data, config.HardRarityWeights, config.RewardChoices, errors);
            return errors;
        }

        private static void Unique<T>(string what, IReadOnlyList<T> items, Func<T, string> id, List<string> errors)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                if (!seen.Add(id(item))) { errors.Add($"{what} {id(item)} が重複"); }
            }
        }

        private static void CheckCard(CardDef card, DeckConfig config, List<string> errors)
        {
            var name = $"カード {card.Id}";
            if (card.Cost < 0 || card.Cost > config.Energy) { errors.Add($"{name}：コストは 0〜{config.Energy}（{card.Cost}）"); }
            if (card.Effects.Count == 0 || card.Effects.Count > config.MaxEffects)
            {
                errors.Add($"{name}：効果は 1〜{config.MaxEffects} 個（{card.Effects.Count}）");
            }

            foreach (var e in card.Effects)
            {
                var allowed = e.Kind switch
                {
                    EffectKind.Damage or EffectKind.DamageByBlock => e.Target is TargetMode.Chosen or TargetMode.AllEnemies or TargetMode.RandomEnemy,
                    EffectKind.Block or EffectKind.DoubleBlock => e.Target == TargetMode.Self,
                    EffectKind.Draw or EffectKind.GainEnergy or EffectKind.ExhaustRandom => e.Target == TargetMode.None,
                    EffectKind.Status => e.Target is TargetMode.Chosen or TargetMode.AllEnemies or TargetMode.RandomEnemy or TargetMode.Self,
                    _ => false,
                };
                if (!allowed) { errors.Add($"{name}：{e.Kind} に対象 {e.Target} は使えません"); }
                Amount(name, e, config, errors);
            }
        }

        private static void Amount(string name, EffectDef e, DeckConfig config, List<string> errors)
        {
            var needsAmount = e.Kind is not (EffectKind.DamageByBlock or EffectKind.DoubleBlock or EffectKind.Summon);
            if (needsAmount && (e.Amount < 1 || e.Amount > config.MaxAmount))
            {
                errors.Add($"{name}：{e.Kind} の量は 1〜{config.MaxAmount}（{e.Amount}）");
            }
        }

        private static void CheckEnemy(EnemyDef enemy, GameData data, DeckConfig config, List<string> errors)
        {
            var name = $"敵 {enemy.Id}";
            if (enemy.Hp < 1 || enemy.Hp > 999) { errors.Add($"{name}：HP は 1〜999（{enemy.Hp}）"); }
            var regular = 0;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var move in enemy.Moves)
            {
                if (!ids.Add(move.Id)) { errors.Add($"{name}：行動 {move.Id} が重複"); }
                if (move.Index != enemy.HalfMove)
                {
                    regular++;
                    if (enemy.Order == MoveOrder.Random && (move.Weight < 1 || move.Weight > 10000))
                    {
                        errors.Add($"{name}：行動 {move.Id} の重みは 1〜10000（{move.Weight}）");
                    }
                }

                if (move.Effects.Count == 0 || move.Effects.Count > config.MaxEffects)
                {
                    errors.Add($"{name}：行動 {move.Id} の効果は 1〜{config.MaxEffects} 個（{move.Effects.Count}）");
                }

                foreach (var e in move.Effects)
                {
                    var allowed = e.Kind switch
                    {
                        EffectKind.Damage or EffectKind.DamageByBlock => e.Target == TargetMode.Player,
                        EffectKind.Block or EffectKind.DoubleBlock => e.Target == TargetMode.Self,
                        EffectKind.Status => e.Target is TargetMode.Self or TargetMode.Player,
                        EffectKind.Summon => e.Target == TargetMode.None,
                        _ => false,
                    };
                    if (!allowed) { errors.Add($"{name}：行動 {move.Id} の {e.Kind} に対象 {e.Target} は使えません"); }
                    if (e.Kind == EffectKind.Summon && !data.TryGetEnemy(e.Summon, out _)) { errors.Add($"{name}：呼ぶ敵 {e.Summon} がありません"); }
                    Amount($"{name}：行動 {move.Id}", e, config, errors);
                }
            }

            var need = enemy.Order == MoveOrder.Random ? 2 : 1;
            if (regular < need) { errors.Add($"{name}：普段の行動が {need} つ以上必要（{regular}）"); }
        }

        private static void CheckEncounters(GameData data, DeckConfig config, List<string> errors)
        {
            var counts = new int[5];
            foreach (var enc in data.Encounters)
            {
                counts[(int)enc.Tier]++;
                if (enc.Foes.Count < 1 || enc.Foes.Count > config.MaxEnemies)
                {
                    errors.Add($"遭遇 {enc.Id}：敵は 1〜{config.MaxEnemies} 体（{enc.Foes.Count}）");
                }
            }

            // ルート：戦 1 は Easy と Normal の 2 択、戦 2 は Normal と Hard の 2 択（戦 1 と別の Normal）、戦 3 は Boss。
            if (counts[(int)EncounterTier.Easy] < 1) { errors.Add("Easy の遭遇が必要"); }
            if (counts[(int)EncounterTier.Normal] < 2) { errors.Add("Normal の遭遇が 2 つ以上必要"); }
            if (counts[(int)EncounterTier.Hard] < 1) { errors.Add("Hard の遭遇が必要"); }
            if (counts[(int)EncounterTier.Boss] < 1) { errors.Add("Boss の遭遇が必要"); }
        }

        private static void Pool(string name, GameData data, int[] weights, int choices, List<string> errors)
        {
            var pool = 0;
            foreach (var card in data.Cards)
            {
                var slot = RewardSlot(card.Rarity);
                if (slot >= 0 && weights[slot] > 0) { pool++; }
            }

            if (pool < choices) { errors.Add($"{name}：報酬に出うるカードが {choices} 種未満（{pool}）"); }
        }
    }
}
