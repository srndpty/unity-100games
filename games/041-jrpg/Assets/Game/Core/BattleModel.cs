using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using TileMotion;

namespace Game.Core
{
    public enum BattleSide
    {
        Ally = 0,
        Enemy = 1,
    }

    public enum BattleOutcome
    {
        Ongoing = 0,
        Victory = 1,
        PartyWiped = 2,
        Fled = 3,
    }

    public enum BattleCommandKind
    {
        None = 0,
        Attack = 1,
        Spell = 2,
        Item = 3,
        Defend = 4,
        Flee = 5,
    }

    public enum CommandError
    {
        None = 0,
        BattleAlreadyEnded = 1,
        ActorIndexOutOfRange = 2,
        ActorDead = 3,
        EmptyCommand = 4,
        UnknownSpell = 5,
        SpellNotLearned = 6,
        InsufficientMp = 7,
        SpellNotUsableInField = 8,
        UnknownItem = 9,
        OutOfItem = 10,
        InvalidTarget = 11,
        CannotFleeBoss = 12,
    }

    public static class CommandErrorText
    {
        public static string Describe(CommandError error) => error switch
        {
            CommandError.None => string.Empty,
            CommandError.BattleAlreadyEnded => "戦闘はすでに終了しています。",
            CommandError.ActorIndexOutOfRange => "行動者の番号が範囲外です。",
            CommandError.ActorDead => "戦闘不能の仲間は行動できません。",
            CommandError.EmptyCommand => "コマンドが選択されていません。",
            CommandError.UnknownSpell => "存在しない呪文です。",
            CommandError.SpellNotLearned => "その呪文はまだ覚えていません。",
            CommandError.InsufficientMp => "MPが足りない！",
            CommandError.SpellNotUsableInField => "移動中には使えない呪文です。",
            CommandError.UnknownItem => "存在しない道具です。",
            CommandError.OutOfItem => "その道具を持っていません。",
            CommandError.InvalidTarget => "その対象には使えません。",
            CommandError.CannotFleeBoss => "魔将からは逃げられない！",
            _ => "選択できません。",
        };
    }

    public readonly struct BattleCommand : IEquatable<BattleCommand>
    {
        public BattleCommand(BattleCommandKind kind, string actionId, int targetIndex)
        {
            Kind = kind;
            ActionId = actionId ?? string.Empty;
            TargetIndex = targetIndex;
        }

        public BattleCommandKind Kind { get; }
        public string ActionId { get; }
        public int TargetIndex { get; }

        public static BattleCommand None => new(BattleCommandKind.None, string.Empty, -1);
        public static BattleCommand Attack(int enemyIndex) => new(BattleCommandKind.Attack, string.Empty, enemyIndex);
        public static BattleCommand CastSpell(string spellId, int targetIndex) => new(BattleCommandKind.Spell, spellId, targetIndex);
        public static BattleCommand UseItem(string itemId, int allyIndex) => new(BattleCommandKind.Item, itemId, allyIndex);
        public static BattleCommand Defend() => new(BattleCommandKind.Defend, string.Empty, -1);
        public static BattleCommand Flee() => new(BattleCommandKind.Flee, string.Empty, -1);

        public bool Equals(BattleCommand other) =>
            Kind == other.Kind &&
            TargetIndex == other.TargetIndex &&
            string.Equals(ActionId, other.ActionId, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is BattleCommand other && Equals(other);

        public override int GetHashCode() => HashCode.Combine((int)Kind, ActionId, TargetIndex);

        public static bool operator ==(BattleCommand left, BattleCommand right) => left.Equals(right);
        public static bool operator !=(BattleCommand left, BattleCommand right) => !left.Equals(right);
    }

    public sealed class BattleEnemy
    {
        public BattleEnemy(int slot, EnemyInfo info, int subGroup, string suffix)
        {
            Slot = slot;
            Info = info ?? throw new ArgumentNullException(nameof(info));
            SubGroup = subGroup;
            Suffix = suffix ?? string.Empty;
            DisplayName = Suffix.Length > 0 ? info.Name + Suffix : info.Name;
            Hp = info.MaxHp;
            Mp = info.MaxMp;
        }

        private BattleEnemy(int slot, EnemyInfo info, int subGroup, string suffix, string displayName, int hp, int mp)
        {
            Slot = slot;
            Info = info;
            SubGroup = subGroup;
            Suffix = suffix;
            DisplayName = displayName;
            Hp = hp;
            Mp = mp;
        }

        public int Slot { get; }
        public EnemyInfo Info { get; }
        public int SubGroup { get; }
        public string Suffix { get; }
        public string DisplayName { get; }
        public int Hp { get; private set; }
        public int Mp { get; private set; }
        public bool IsAlive => Hp > 0;

        public void SetVitals(int hp, int mp)
        {
            Hp = Math.Clamp(hp, 0, Info.MaxHp);
            Mp = Math.Clamp(mp, 0, Info.MaxMp);
        }

        public int TakeDamage(int amount)
        {
            if (amount <= 0 || !IsAlive)
            {
                return 0;
            }

            var actual = Math.Min(Hp, amount);
            Hp -= actual;
            return actual;
        }

        public int HealHp(int amount)
        {
            if (amount <= 0 || !IsAlive)
            {
                return 0;
            }

            var before = Hp;
            Hp = Math.Min(Info.MaxHp, Hp + amount);
            return Hp - before;
        }

        public bool SpendMp(int cost)
        {
            if (cost < 0 || Mp < cost)
            {
                return false;
            }

            Mp -= cost;
            return true;
        }

        public BattleEnemy Clone() => new(Slot, Info, SubGroup, Suffix, DisplayName, Hp, Mp);

        public void MixHash(ref StateHash hash)
        {
            hash.Mix(Info.Index);
            hash.Mix(SubGroup);
            hash.Mix(Hp);
            hash.Mix(Mp);
        }
    }

    public sealed class BattleState
    {
        private readonly BattleEnemy[] _enemies;
        private readonly ReadOnlyCollection<BattleEnemy> _enemiesView;
        private readonly bool[] _allyDefending;
        private readonly ReadOnlyCollection<bool> _defendingView;

        public BattleState(GameData data, EnemyGroupInfo group)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            Group = group ?? throw new ArgumentNullException(nameof(group));
            var count = group.EnemyIds.Count;
            _enemies = new BattleEnemy[count];

            var speciesTotals = new Dictionary<string, int>(StringComparer.Ordinal);
            var subGroupById = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < count; i++)
            {
                var id = group.EnemyIds[i];
                speciesTotals[id] = speciesTotals.TryGetValue(id, out var c) ? c + 1 : 1;
                if (!subGroupById.ContainsKey(id))
                {
                    subGroupById[id] = subGroupById.Count;
                }
            }

            var speciesSeen = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < count; i++)
            {
                var id = group.EnemyIds[i];
                var seen = speciesSeen.TryGetValue(id, out var s) ? s : 0;
                speciesSeen[id] = seen + 1;
                var suffix = speciesTotals[id] > 1 ? ((char)('A' + seen)).ToString() : string.Empty;
                _enemies[i] = new BattleEnemy(i, data.GetEnemy(id), subGroupById[id], suffix);
            }

            _enemiesView = Array.AsReadOnly(_enemies);
            _allyDefending = new bool[data.Rules.PartyClassIds.Count];
            _defendingView = Array.AsReadOnly(_allyDefending);
            Turn = 0;
            Outcome = BattleOutcome.Ongoing;
        }

        private BattleState(EnemyGroupInfo group, BattleEnemy[] enemies, bool[] defending, int turn, BattleOutcome outcome, int expEach, int goldEarned)
        {
            Group = group;
            _enemies = enemies;
            _enemiesView = Array.AsReadOnly(_enemies);
            _allyDefending = defending;
            _defendingView = Array.AsReadOnly(_allyDefending);
            Turn = turn;
            Outcome = outcome;
            ExpEarnedEach = expEach;
            GoldEarned = goldEarned;
        }

        public EnemyGroupInfo Group { get; }
        public IReadOnlyList<BattleEnemy> Enemies => _enemiesView;
        public IReadOnlyList<bool> AllyDefending => _defendingView;
        public int Turn { get; private set; }
        public BattleOutcome Outcome { get; private set; }
        public int ExpEarnedEach { get; private set; }
        public int GoldEarned { get; private set; }
        public bool IsBossBattle => Group.IsBossGroup;

        public bool AllEnemiesDead
        {
            get
            {
                for (var i = 0; i < _enemies.Length; i++)
                {
                    if (_enemies[i].IsAlive)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>
        /// 選んだ敵が生きていればその番号、既に倒れていれば同じグループの次の生存者の番号を返す。
        /// そのグループが全滅していれば -1 を返す。
        /// </summary>
        public int ResolveSubGroupTarget(int targetIndex)
        {
            if (targetIndex < 0 || targetIndex >= _enemies.Length)
            {
                return -1;
            }

            if (_enemies[targetIndex].IsAlive)
            {
                return targetIndex;
            }

            var subGroup = _enemies[targetIndex].SubGroup;
            for (var step = 1; step < _enemies.Length; step++)
            {
                var idx = (targetIndex + step) % _enemies.Length;
                if (_enemies[idx].SubGroup == subGroup && _enemies[idx].IsAlive)
                {
                    return idx;
                }
            }

            return -1;
        }

        internal void BeginTurn()
        {
            Turn++;
            for (var i = 0; i < _allyDefending.Length; i++)
            {
                _allyDefending[i] = false;
            }
        }

        internal void SetAllyDefending(int index, bool defending) => _allyDefending[index] = defending;
        internal void MarkFled() => Outcome = BattleOutcome.Fled;
        internal void MarkPartyWiped() => Outcome = BattleOutcome.PartyWiped;

        internal void MarkVictory(int expEach, int gold)
        {
            Outcome = BattleOutcome.Victory;
            ExpEarnedEach = expEach;
            GoldEarned = gold;
        }

        public BattleState Clone()
        {
            var enemies = new BattleEnemy[_enemies.Length];
            for (var i = 0; i < _enemies.Length; i++)
            {
                enemies[i] = _enemies[i].Clone();
            }

            return new BattleState(Group, enemies, (bool[])_allyDefending.Clone(), Turn, Outcome, ExpEarnedEach, GoldEarned);
        }

        public uint Hash(Party party, Inventory inventory, GameData data)
        {
            if (party == null)
            {
                throw new ArgumentNullException(nameof(party));
            }

            if (inventory == null)
            {
                throw new ArgumentNullException(nameof(inventory));
            }

            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            var hash = StateHash.Start();
            hash.Mix(data.Fingerprint);
            MixHash(ref hash);
            party.MixHash(ref hash);
            inventory.MixHash(ref hash);
            return hash.Value;
        }

        public void MixHash(ref StateHash hash)
        {
            hash.Mix(Group.Index);
            hash.Mix(Turn);
            hash.Mix((int)Outcome);
            hash.Mix(ExpEarnedEach);
            hash.Mix(GoldEarned);
            for (var i = 0; i < _allyDefending.Length; i++)
            {
                hash.Mix(_allyDefending[i]);
            }

            hash.Mix(_enemies.Length);
            for (var i = 0; i < _enemies.Length; i++)
            {
                _enemies[i].MixHash(ref hash);
            }
        }
    }

    public static class BattleRules
    {
        public static bool CanCast(GameData data, PartyMember caster, string spellId, bool inBattle, out CommandError error)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (caster == null)
            {
                throw new ArgumentNullException(nameof(caster));
            }

            if (!caster.IsAlive)
            {
                error = CommandError.ActorDead;
                return false;
            }

            if (!data.TryGetSpell(spellId, out var spell))
            {
                error = CommandError.UnknownSpell;
                return false;
            }

            if (!caster.KnowsSpell(spellId))
            {
                error = CommandError.SpellNotLearned;
                return false;
            }

            if (!inBattle && !spell!.UsableInField)
            {
                error = CommandError.SpellNotUsableInField;
                return false;
            }

            if (caster.Mp < spell!.MpCost)
            {
                error = CommandError.InsufficientMp;
                return false;
            }

            error = CommandError.None;
            return true;
        }

        public static bool CanEnemyCast(GameData data, BattleEnemy enemy, string spellId, out CommandError error)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (enemy == null)
            {
                throw new ArgumentNullException(nameof(enemy));
            }

            if (!enemy.IsAlive)
            {
                error = CommandError.ActorDead;
                return false;
            }

            if (!data.TryGetSpell(spellId, out var spell))
            {
                error = CommandError.UnknownSpell;
                return false;
            }

            if (enemy.Mp < spell!.MpCost)
            {
                error = CommandError.InsufficientMp;
                return false;
            }

            error = CommandError.None;
            return true;
        }

        public static bool ValidateCommand(
            GameData data,
            BattleState battle,
            Party party,
            Inventory inventory,
            int actorIndex,
            BattleCommand command,
            IReadOnlyList<BattleCommand>? pendingCommands,
            out CommandError error)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (battle == null)
            {
                throw new ArgumentNullException(nameof(battle));
            }

            if (party == null)
            {
                throw new ArgumentNullException(nameof(party));
            }

            if (inventory == null)
            {
                throw new ArgumentNullException(nameof(inventory));
            }

            if (battle.Outcome != BattleOutcome.Ongoing)
            {
                error = CommandError.BattleAlreadyEnded;
                return false;
            }

            if (actorIndex < 0 || actorIndex >= party.Count)
            {
                error = CommandError.ActorIndexOutOfRange;
                return false;
            }

            var actor = party[actorIndex];
            if (!actor.IsAlive)
            {
                if (command.Kind == BattleCommandKind.None)
                {
                    error = CommandError.None;
                    return true;
                }

                error = CommandError.ActorDead;
                return false;
            }

            switch (command.Kind)
            {
                case BattleCommandKind.None:
                    error = CommandError.EmptyCommand;
                    return false;

                case BattleCommandKind.Defend:
                    error = CommandError.None;
                    return true;

                case BattleCommandKind.Flee:
                    if (battle.IsBossBattle)
                    {
                        error = CommandError.CannotFleeBoss;
                        return false;
                    }

                    error = CommandError.None;
                    return true;

                case BattleCommandKind.Attack:
                    if (command.TargetIndex < 0 || command.TargetIndex >= battle.Enemies.Count || !battle.Enemies[command.TargetIndex].IsAlive)
                    {
                        error = CommandError.InvalidTarget;
                        return false;
                    }

                    error = CommandError.None;
                    return true;

                case BattleCommandKind.Spell:
                    if (!CanCast(data, actor, command.ActionId, inBattle: true, out error))
                    {
                        return false;
                    }

                    var spell = data.GetSpell(command.ActionId);
                    switch (spell.Kind)
                    {
                        case SpellKind.SingleAttack:
                        case SpellKind.GroupAttack:
                            if (command.TargetIndex < 0 || command.TargetIndex >= battle.Enemies.Count || !battle.Enemies[command.TargetIndex].IsAlive)
                            {
                                error = CommandError.InvalidTarget;
                                return false;
                            }

                            break;

                        case SpellKind.SingleHeal:
                            if (command.TargetIndex < 0 || command.TargetIndex >= party.Count || !party[command.TargetIndex].IsAlive)
                            {
                                error = CommandError.InvalidTarget;
                                return false;
                            }

                            break;

                        case SpellKind.Revive:
                            if (command.TargetIndex < 0 || command.TargetIndex >= party.Count || party[command.TargetIndex].IsAlive)
                            {
                                error = CommandError.InvalidTarget;
                                return false;
                            }

                            break;

                        case SpellKind.AllHeal:
                            break;
                    }

                    error = CommandError.None;
                    return true;

                case BattleCommandKind.Item:
                    if (!data.TryGetItem(command.ActionId, out _))
                    {
                        error = CommandError.UnknownItem;
                        return false;
                    }

                    var reserved = 0;
                    if (pendingCommands != null)
                    {
                        for (var i = 0; i < pendingCommands.Count; i++)
                        {
                            if (i != actorIndex &&
                                pendingCommands[i].Kind == BattleCommandKind.Item &&
                                string.Equals(pendingCommands[i].ActionId, command.ActionId, StringComparison.Ordinal))
                            {
                                reserved++;
                            }
                        }
                    }

                    if (reserved + 1 > inventory.Count(command.ActionId))
                    {
                        error = CommandError.OutOfItem;
                        return false;
                    }

                    if (command.TargetIndex < 0 || command.TargetIndex >= party.Count || !party[command.TargetIndex].IsAlive)
                    {
                        error = CommandError.InvalidTarget;
                        return false;
                    }

                    error = CommandError.None;
                    return true;

                default:
                    error = CommandError.EmptyCommand;
                    return false;
            }
        }
    }
}
