using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using TileMotion;

namespace Game.Core
{
    public sealed class AllySnapshot
    {
        public AllySnapshot(string name, string className, int level, int exp, int maxHp, int maxMp, int hp, int mp, bool isDefending)
        {
            Name = name;
            ClassName = className;
            Level = level;
            Exp = exp;
            MaxHp = maxHp;
            MaxMp = maxMp;
            Hp = hp;
            Mp = mp;
            IsDefending = isDefending;
        }

        public string Name { get; }
        public string ClassName { get; }
        public int Level { get; internal set; }
        public int Exp { get; internal set; }
        public int MaxHp { get; internal set; }
        public int MaxMp { get; internal set; }
        public int Hp { get; internal set; }
        public int Mp { get; internal set; }
        public bool IsDefending { get; internal set; }
        public bool IsAlive => Hp > 0;
    }

    public sealed class EnemySnapshot
    {
        public EnemySnapshot(int slot, string enemyId, string displayName, int subGroup, int maxHp, int maxMp, int hp, int mp)
        {
            Slot = slot;
            EnemyId = enemyId;
            DisplayName = displayName;
            SubGroup = subGroup;
            MaxHp = maxHp;
            MaxMp = maxMp;
            Hp = hp;
            Mp = mp;
        }

        public int Slot { get; }
        public string EnemyId { get; }
        public string DisplayName { get; }
        public int SubGroup { get; }
        public int MaxHp { get; }
        public int MaxMp { get; }
        public int Hp { get; internal set; }
        public int Mp { get; internal set; }
        public bool IsAlive => Hp > 0;
    }

    /// <summary>
    /// 戦闘演出を段階的に表示するための状態スナップショット。
    /// ターン解決前のスナップショットに出来事列を順番に適用すると、解決後の実状態と一致する。
    /// </summary>
    public sealed class BattleSnapshot
    {
        private readonly AllySnapshot[] _allies;
        private readonly ReadOnlyCollection<AllySnapshot> _alliesView;
        private readonly EnemySnapshot[] _enemies;
        private readonly ReadOnlyCollection<EnemySnapshot> _enemiesView;
        private readonly SortedDictionary<string, int> _bagCounts;

        private BattleSnapshot(
            AllySnapshot[] allies,
            EnemySnapshot[] enemies,
            SortedDictionary<string, int> bagCounts,
            int turn,
            BattleOutcome outcome,
            int expEach,
            int goldEarned)
        {
            _allies = allies;
            _alliesView = Array.AsReadOnly(_allies);
            _enemies = enemies;
            _enemiesView = Array.AsReadOnly(_enemies);
            _bagCounts = bagCounts;
            Turn = turn;
            Outcome = outcome;
            ExpEarnedEach = expEach;
            GoldEarned = goldEarned;
        }

        public IReadOnlyList<AllySnapshot> Allies => _alliesView;
        public IReadOnlyList<EnemySnapshot> Enemies => _enemiesView;
        public int Turn { get; private set; }
        public BattleOutcome Outcome { get; private set; }
        public int ExpEarnedEach { get; private set; }
        public int GoldEarned { get; private set; }

        public int ItemCount(string id) => _bagCounts.TryGetValue(id ?? string.Empty, out var c) ? c : 0;

        public static BattleSnapshot Capture(BattleState battle, Party party, Inventory inventory)
        {
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

            var allies = new AllySnapshot[party.Count];
            for (var i = 0; i < party.Count; i++)
            {
                var m = party[i];
                allies[i] = new AllySnapshot(
                    m.Name,
                    m.Class.Name,
                    m.Level,
                    m.Exp,
                    m.MaxHp,
                    m.MaxMp,
                    m.Hp,
                    m.Mp,
                    battle.AllyDefending[i]);
            }

            var enemies = new EnemySnapshot[battle.Enemies.Count];
            for (var i = 0; i < battle.Enemies.Count; i++)
            {
                var e = battle.Enemies[i];
                enemies[i] = new EnemySnapshot(
                    e.Slot,
                    e.Info.Id,
                    e.DisplayName,
                    e.SubGroup,
                    e.Info.MaxHp,
                    e.Info.MaxMp,
                    e.Hp,
                    e.Mp);
            }

            var bag = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var entries = inventory.AllEntries();
            for (var i = 0; i < entries.Count; i++)
            {
                bag[entries[i].ProductId] = entries[i].Count;
            }

            return new BattleSnapshot(allies, enemies, bag, battle.Turn, battle.Outcome, battle.ExpEarnedEach, battle.GoldEarned);
        }

        public void Apply(BattleEvent ev)
        {
            if (ev == null)
            {
                throw new ArgumentNullException(nameof(ev));
            }

            switch (ev.Kind)
            {
                case BattleEventKind.TurnStarted:
                    Turn = ev.Amount;
                    for (var i = 0; i < _allies.Length; i++)
                    {
                        _allies[i].IsDefending = false;
                    }

                    break;

                case BattleEventKind.DefendStance:
                    _allies[ev.ActorIndex].IsDefending = true;
                    break;

                case BattleEventKind.MpSpent:
                    if (ev.TargetSide == BattleSide.Ally)
                    {
                        _allies[ev.TargetIndex].Mp = Math.Max(0, _allies[ev.TargetIndex].Mp - ev.Amount);
                    }
                    else
                    {
                        _enemies[ev.TargetIndex].Mp = Math.Max(0, _enemies[ev.TargetIndex].Mp - ev.Amount);
                    }

                    break;

                case BattleEventKind.ItemConsumed:
                    if (_bagCounts.TryGetValue(ev.ActionId, out var c))
                    {
                        var next = c - ev.Amount;
                        if (next <= 0)
                        {
                            _bagCounts.Remove(ev.ActionId);
                        }
                        else
                        {
                            _bagCounts[ev.ActionId] = next;
                        }
                    }

                    break;

                case BattleEventKind.DamageDealt:
                    if (ev.TargetSide == BattleSide.Ally)
                    {
                        _allies[ev.TargetIndex].Hp = Math.Max(0, _allies[ev.TargetIndex].Hp - ev.Amount);
                    }
                    else
                    {
                        _enemies[ev.TargetIndex].Hp = Math.Max(0, _enemies[ev.TargetIndex].Hp - ev.Amount);
                    }

                    break;

                case BattleEventKind.HpHealed:
                    if (ev.TargetSide == BattleSide.Ally)
                    {
                        var a = _allies[ev.TargetIndex];
                        a.Hp = Math.Min(a.MaxHp, a.Hp + ev.Amount);
                    }
                    else
                    {
                        var e = _enemies[ev.TargetIndex];
                        e.Hp = Math.Min(e.MaxHp, e.Hp + ev.Amount);
                    }

                    break;

                case BattleEventKind.MpRestored:
                    {
                        var a = _allies[ev.TargetIndex];
                        a.Mp = Math.Min(a.MaxMp, a.Mp + ev.Amount);
                        break;
                    }

                case BattleEventKind.UnitRevived:
                    _allies[ev.TargetIndex].Hp = Math.Min(_allies[ev.TargetIndex].MaxHp, ev.Amount);
                    break;

                case BattleEventKind.FleeSucceeded:
                    Outcome = BattleOutcome.Fled;
                    break;

                case BattleEventKind.Victory:
                    Outcome = BattleOutcome.Victory;
                    ExpEarnedEach = ev.Amount;
                    GoldEarned = ev.SecondaryAmount;
                    break;

                case BattleEventKind.ExpAwarded:
                    _allies[ev.TargetIndex].Exp += ev.Amount;
                    break;

                case BattleEventKind.LevelUp:
                    if (ev.LevelUp != null)
                    {
                        var g = ev.LevelUp;
                        var a = _allies[ev.TargetIndex];
                        a.Level = g.NewLevel;
                        a.MaxHp += g.MaxHpGain;
                        a.MaxMp += g.MaxMpGain;
                        a.Hp = Math.Min(a.MaxHp, a.Hp + g.MaxHpGain);
                        a.Mp = Math.Min(a.MaxMp, a.Mp + g.MaxMpGain);
                    }

                    break;

                case BattleEventKind.PartyWiped:
                    Outcome = BattleOutcome.PartyWiped;
                    break;
            }
        }

        public bool Matches(BattleSnapshot other)
        {
            if (other == null ||
                Turn != other.Turn ||
                Outcome != other.Outcome ||
                ExpEarnedEach != other.ExpEarnedEach ||
                GoldEarned != other.GoldEarned ||
                _allies.Length != other._allies.Length ||
                _enemies.Length != other._enemies.Length ||
                _bagCounts.Count != other._bagCounts.Count)
            {
                return false;
            }

            for (var i = 0; i < _allies.Length; i++)
            {
                var a = _allies[i];
                var b = other._allies[i];
                if (a.Level != b.Level ||
                    a.Exp != b.Exp ||
                    a.MaxHp != b.MaxHp ||
                    a.MaxMp != b.MaxMp ||
                    a.Hp != b.Hp ||
                    a.Mp != b.Mp ||
                    a.IsDefending != b.IsDefending)
                {
                    return false;
                }
            }

            for (var i = 0; i < _enemies.Length; i++)
            {
                var a = _enemies[i];
                var b = other._enemies[i];
                if (a.Hp != b.Hp || a.Mp != b.Mp)
                {
                    return false;
                }
            }

            foreach (var kv in _bagCounts)
            {
                if (!other._bagCounts.TryGetValue(kv.Key, out var val) || val != kv.Value)
                {
                    return false;
                }
            }

            return true;
        }

        public uint Hash()
        {
            var hash = StateHash.Start();
            hash.Mix(Turn);
            hash.Mix((int)Outcome);
            hash.Mix(ExpEarnedEach);
            hash.Mix(GoldEarned);
            for (var i = 0; i < _allies.Length; i++)
            {
                var a = _allies[i];
                hash.Mix(a.Level);
                hash.Mix(a.Exp);
                hash.Mix(a.MaxHp);
                hash.Mix(a.MaxMp);
                hash.Mix(a.Hp);
                hash.Mix(a.Mp);
                hash.Mix(a.IsDefending);
            }

            for (var i = 0; i < _enemies.Length; i++)
            {
                hash.Mix(_enemies[i].Hp);
                hash.Mix(_enemies[i].Mp);
            }

            hash.Mix(_bagCounts.Count);
            foreach (var kv in _bagCounts)
            {
                hash.Mix(kv.Key.Length);
                for (var c = 0; c < kv.Key.Length; c++)
                {
                    hash.Mix(kv.Key[c]);
                }

                hash.Mix(kv.Value);
            }

            return hash.Value;
        }
    }
}
