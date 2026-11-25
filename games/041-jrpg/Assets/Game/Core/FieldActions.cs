using System;

namespace Game.Core
{
    public static class FieldActions
    {
        public static bool UseSpellInField(
            World world,
            int casterIndex,
            string spellId,
            int targetIndex,
            out string message,
            out CommandError error)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            message = string.Empty;
            if (casterIndex < 0 || casterIndex >= world.Party.Count)
            {
                error = CommandError.ActorIndexOutOfRange;
                message = CommandErrorText.Describe(error);
                return false;
            }

            var caster = world.Party[casterIndex];
            if (!BattleRules.CanCast(world.Data, caster, spellId, inBattle: false, out error))
            {
                message = CommandErrorText.Describe(error);
                return false;
            }

            var spell = world.Data.GetSpell(spellId);
            switch (spell.Kind)
            {
                case SpellKind.SingleHeal:
                    if (targetIndex < 0 || targetIndex >= world.Party.Count || !world.Party[targetIndex].IsAlive)
                    {
                        error = CommandError.InvalidTarget;
                        message = CommandErrorText.Describe(error);
                        return false;
                    }

                    caster.SpendMp(spell.MpCost);
                    var healed = world.Party[targetIndex].HealHp(spell.Power);
                    message = $"{caster.Name}は {spell.Name} を唱えた！ {world.Party[targetIndex].Name}のHPが {healed} 回復した！";
                    error = CommandError.None;
                    return true;

                case SpellKind.AllHeal:
                    caster.SpendMp(spell.MpCost);
                    for (var i = 0; i < world.Party.Count; i++)
                    {
                        if (world.Party[i].IsAlive)
                        {
                            world.Party[i].HealHp(spell.Power);
                        }
                    }

                    message = $"{caster.Name}は {spell.Name} を唱えた！ 仲間たちの傷が癒えた！";
                    error = CommandError.None;
                    return true;

                case SpellKind.Revive:
                    if (targetIndex < 0 || targetIndex >= world.Party.Count || world.Party[targetIndex].IsAlive)
                    {
                        error = CommandError.InvalidTarget;
                        message = CommandErrorText.Describe(error);
                        return false;
                    }

                    caster.SpendMp(spell.MpCost);
                    var target = world.Party[targetIndex];
                    var hpAmount = Math.Max(1, (target.MaxHp * spell.Power) / 100);
                    target.Revive(hpAmount);
                    message = $"{caster.Name}は {spell.Name} を唱えた！ {target.Name}が息を吹き返した！";
                    error = CommandError.None;
                    return true;

                default:
                    error = CommandError.SpellNotUsableInField;
                    message = CommandErrorText.Describe(error);
                    return false;
            }
        }

        public static bool UseItemInField(
            World world,
            string itemId,
            int targetIndex,
            out string message,
            out CommandError error)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            message = string.Empty;
            if (!world.Data.TryGetItem(itemId, out var item))
            {
                error = CommandError.UnknownItem;
                message = CommandErrorText.Describe(error);
                return false;
            }

            if (!item!.UsableInField)
            {
                error = CommandError.SpellNotUsableInField;
                message = CommandErrorText.Describe(error);
                return false;
            }

            if (world.Inventory.Count(itemId) <= 0)
            {
                error = CommandError.OutOfItem;
                message = CommandErrorText.Describe(error);
                return false;
            }

            if (targetIndex < 0 || targetIndex >= world.Party.Count || !world.Party[targetIndex].IsAlive)
            {
                error = CommandError.InvalidTarget;
                message = CommandErrorText.Describe(error);
                return false;
            }

            world.Inventory.Remove(itemId, 1);
            var target = world.Party[targetIndex];
            if (item.Kind == ItemKind.HealHp)
            {
                var healed = target.HealHp(item.Power);
                message = $"{target.Name}に {item.Name} を使った！ HPが {healed} 回復した！";
            }
            else
            {
                var restored = target.RestoreMp(item.Power);
                message = $"{target.Name}に {item.Name} を使った！ MPが {restored} 回復した！";
            }

            error = CommandError.None;
            return true;
        }
    }
}
