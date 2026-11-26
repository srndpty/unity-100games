using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>攻撃の量の計算。順序は固定：基礎 ＋ 筋力 → 脱力 → 弱体 → 0 未満は 0。</summary>
    public static class DamageCalc
    {
        /// <summary>計算の上限（筋力が積み上がっても int に収める）。</summary>
        public const int Max = 999999;

        public static int Attack(int baseAmount, Combatant attacker, Combatant defender, DeckConfig config)
        {
            if (attacker == null) { throw new ArgumentNullException(nameof(attacker)); }
            if (defender == null) { throw new ArgumentNullException(nameof(defender)); }
            return Attack(baseAmount, attacker.Status(StatusKind.Strength), attacker.Status(StatusKind.Weak) > 0, defender.Status(StatusKind.Vulnerable) > 0, config);
        }

        /// <summary>状態の値から直接計算する（画面の見積もり用。式は同じ）。</summary>
        public static int Attack(int baseAmount, int strength, bool weak, bool vulnerable, DeckConfig config)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            long v = (long)baseAmount + strength;
            if (weak) { v = v * config.WeakPercent / 100; }
            if (vulnerable) { v = v * config.VulnerablePercent / 100; }
            return (int)Math.Max(0, Math.Min(Max, v));
        }
    }

    public sealed partial class Battle
    {
        private Combatant Find(int id) => id == Combatant.PlayerId ? _player : EnemyById(id) ?? throw new InvalidOperationException($"番号 {id} の者がいません。");

        /// <summary>1 コマンドの実行。全体・無作為の対象はここ（実行の時点）で決める。</summary>
        private void Execute(Command command)
        {
            var effect = command.Effect;
            var source = Find(command.Source);
            switch (effect.Kind)
            {
                case EffectKind.Damage:
                case EffectKind.DamageByBlock:
                    var baseAmount = effect.Kind == EffectKind.Damage ? effect.Amount : source.Block;
                    foreach (var target in Targets(command))
                    {
                        ApplyDamage(source, target, DamageCalc.Attack(baseAmount, source, target, _config));
                    }

                    break;
                case EffectKind.Block:
                    GainBlock(source, effect.Amount);
                    break;
                case EffectKind.DoubleBlock:
                    GainBlock(source, source.Block);
                    break;
                case EffectKind.Draw:
                    Draw(effect.Amount);
                    break;
                case EffectKind.GainEnergy:
                    var before = Energy;
                    Energy = Math.Min(99, Energy + effect.Amount);
                    Emit(EventKind.EnergyGained, source.Id, -1, Energy - before, 0);
                    break;
                case EffectKind.ExhaustRandom:
                    for (int i = 0; i < effect.Amount && _hand.Count > 0; i++)
                    {
                        var index = _effectRng.Next(_hand.Count);
                        var card = _hand[index];
                        _hand.RemoveAt(index);
                        _exhaust.Add(card);
                        Emit(EventKind.Exhausted, -1, -1, 0, card.Id);
                    }

                    break;
                case EffectKind.Status:
                    foreach (var target in Targets(command))
                    {
                        target.AddStatus(effect.Status, effect.Amount);
                        Emit(EventKind.StatusApplied, source.Id, target.Id, effect.Amount, (int)effect.Status);
                    }

                    break;
                case EffectKind.Summon:
                    Summon(source, effect.Summon);
                    break;
                default:
                    throw new InvalidOperationException($"効果 {effect.Kind} は実行できません。");
            }
        }

        private List<Combatant> Targets(Command command)
        {
            var list = new List<Combatant>(_enemies.Count);
            switch (command.Effect.Target)
            {
                case TargetMode.Self:
                    list.Add(Find(command.Source));
                    break;
                case TargetMode.Player:
                    list.Add(_player);
                    break;
                case TargetMode.Chosen:
                    var chosen = EnemyById(command.Chosen);
                    if (chosen != null && chosen.Alive) { list.Add(chosen); }
                    break;
                case TargetMode.AllEnemies:
                    foreach (var e in _enemies)
                    {
                        if (e.Alive) { list.Add(e); }
                    }

                    break;
                case TargetMode.RandomEnemy:
                    var alive = new List<Enemy>(_enemies.Count);
                    foreach (var e in _enemies)
                    {
                        if (e.Alive) { alive.Add(e); }
                    }

                    if (alive.Count > 0) { list.Add(alive[_effectRng.Next(alive.Count)]); }
                    break;
                default:
                    throw new InvalidOperationException($"対象 {command.Effect.Target} は使えません。");
            }

            if (list.Count == 0) { Emit(EventKind.Fizzled, command.Source, command.Chosen, 0, (int)command.Effect.Kind); }
            return list;
        }

        private void ApplyDamage(Combatant source, Combatant target, int amount)
        {
            var blocked = Math.Min(target.Block, amount);
            target.Block -= blocked;
            var loss = Math.Min(target.Hp, amount - blocked);
            target.Hp -= loss;
            Emit(EventKind.Damaged, source.Id, target.Id, loss, blocked);
            if (!target.Alive) { Emit(EventKind.Died, -1, target.Id, 0, 0); }
        }

        private void GainBlock(Combatant who, int amount)
        {
            var before = who.Block;
            who.Block = (int)Math.Min(DamageCalc.Max, (long)who.Block + amount);
            Emit(EventKind.BlockGained, who.Id, who.Id, who.Block - before, 0);
        }

        /// <summary>持ち主のターンの初め：毒の量だけ HP を失い（ブロックを無視）、毒が 1 減る。</summary>
        private void PoisonTick(Combatant who)
        {
            var poison = who.Status(StatusKind.Poison);
            if (poison <= 0) { return; }
            var loss = Math.Min(who.Hp, poison);
            who.Hp -= loss;
            who.SetStatus(StatusKind.Poison, poison - 1);
            Emit(EventKind.PoisonTick, -1, who.Id, loss, 0);
            if (!who.Alive) { Emit(EventKind.Died, -1, who.Id, 0, 0); }
        }

        /// <summary>引く。山札が空なら捨て札を混ぜて山札にする（解決中・廃棄のカードは混ぜない）。手札が満杯なら引かない。</summary>
        private void Draw(int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (_hand.Count >= _config.HandLimit)
                {
                    Emit(EventKind.HandFull, -1, -1, 0, 0);
                    return;
                }

                if (_draw.Count == 0)
                {
                    if (_discard.Count == 0) { return; }
                    _draw.AddRange(_discard);
                    _discard.Clear();
                    ShuffleInPlace(_draw);
                    Emit(EventKind.Shuffled, -1, -1, _draw.Count, 0);
                }

                var card = _draw[_draw.Count - 1];
                _draw.RemoveAt(_draw.Count - 1);
                _hand.Add(card);
                Emit(EventKind.Drawn, -1, -1, 0, card.Id);
            }
        }

        private void Summon(Combatant source, string enemyId)
        {
            var alive = 0;
            foreach (var e in _enemies)
            {
                if (e.Alive) { alive++; }
            }

            if (alive >= _config.MaxEnemies)
            {
                Emit(EventKind.Fizzled, source.Id, -1, 0, (int)EffectKind.Summon);
                return;
            }

            var enemy = new Enemy(_nextEnemyId++, _data.Enemy(enemyId));
            _enemies.Add(enemy);
            Emit(EventKind.Summoned, source.Id, enemy.Id, 0, enemy.Def.Index);
        }

        /// <summary>次の行動を決める。HP 半分以下で一度だけの行動が先、ほかは巡回か重みの抽選（直前と同じ行動は選ばない）。</summary>
        private MoveDef ChooseMove(Enemy enemy)
        {
            var def = enemy.Def;
            if (def.HalfMove >= 0 && !enemy.HalfUsed && (long)enemy.Hp * 2 <= enemy.MaxHp)
            {
                enemy.HalfUsed = true;
                return def.Moves[def.HalfMove];
            }

            var regular = new List<MoveDef>(def.Moves.Count);
            foreach (var m in def.Moves)
            {
                if (m.Index != def.HalfMove) { regular.Add(m); }
            }

            if (def.Order == MoveOrder.Cycle)
            {
                var move = regular[enemy.CycleIndex % regular.Count];
                enemy.CycleIndex = (enemy.CycleIndex + 1) % regular.Count;
                return move;
            }

            if (regular.Count > 1) { regular.RemoveAll(m => m.Index == enemy.LastMove); }
            var weights = new int[regular.Count];
            for (int i = 0; i < weights.Length; i++) { weights[i] = regular[i].Weight; }
            return regular[_aiRng.Weighted(weights)];
        }
    }
}
