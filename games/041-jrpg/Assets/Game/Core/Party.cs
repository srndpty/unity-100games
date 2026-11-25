using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using TileMotion;

namespace Game.Core
{
    public sealed class LevelUpGain
    {
        public LevelUpGain(int memberIndex, int newLevel, int maxHpGain, int maxMpGain, int atkGain, int defGain, int agiGain, IReadOnlyList<string> learnedSpells)
        {
            MemberIndex = memberIndex;
            NewLevel = newLevel;
            MaxHpGain = maxHpGain;
            MaxMpGain = maxMpGain;
            AtkGain = atkGain;
            DefGain = defGain;
            AgiGain = agiGain;
            LearnedSpells = learnedSpells;
        }

        public int MemberIndex { get; }
        public int NewLevel { get; }
        public int MaxHpGain { get; }
        public int MaxMpGain { get; }
        public int AtkGain { get; }
        public int DefGain { get; }
        public int AgiGain { get; }
        public IReadOnlyList<string> LearnedSpells { get; }
    }

    public sealed class PartyMember
    {
        private readonly GameData _data;

        public PartyMember(GameData data, ClassInfo classInfo)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            Class = classInfo ?? throw new ArgumentNullException(nameof(classInfo));
            Level = 1;
            Exp = 0;
            WeaponId = classInfo.StartWeaponId;
            ArmorId = classInfo.StartArmorId;
            Hp = MaxHp;
            Mp = MaxMp;
        }

        private PartyMember(GameData data, ClassInfo classInfo, int level, int exp, int hp, int mp, string weaponId, string armorId)
        {
            _data = data;
            Class = classInfo;
            Level = level;
            Exp = exp;
            WeaponId = weaponId;
            ArmorId = armorId;
            Hp = hp;
            Mp = mp;
        }

        public ClassInfo Class { get; }
        public string Name => Class.HeroName;
        public int Level { get; private set; }
        public int Exp { get; private set; }
        public int Hp { get; private set; }
        public int Mp { get; private set; }
        public string WeaponId { get; private set; }
        public string ArmorId { get; private set; }

        public bool IsAlive => Hp > 0;
        public int MaxHp => Class.MaxHpAt(Level);
        public int MaxMp => Class.MaxMpAt(Level);

        public int Atk => Class.AtkAt(Level)
            + (WeaponId.Length > 0 && _data.TryGetEquip(WeaponId, out var w) ? w!.AtkBonus : 0)
            + (ArmorId.Length > 0 && _data.TryGetEquip(ArmorId, out var a) ? a!.AtkBonus : 0);

        public int Def => Class.DefAt(Level)
            + (WeaponId.Length > 0 && _data.TryGetEquip(WeaponId, out var w) ? w!.DefBonus : 0)
            + (ArmorId.Length > 0 && _data.TryGetEquip(ArmorId, out var a) ? a!.DefBonus : 0);

        public int Agi => Class.AgiAt(Level)
            + (WeaponId.Length > 0 && _data.TryGetEquip(WeaponId, out var w) ? w!.AgiBonus : 0)
            + (ArmorId.Length > 0 && _data.TryGetEquip(ArmorId, out var a) ? a!.AgiBonus : 0);

        public bool KnowsSpell(string spellId) => Class.KnowsSpellAt(spellId, Level);

        public IReadOnlyList<SpellInfo> LearnedSpells()
        {
            var list = new List<SpellInfo>();
            for (var i = 0; i < Class.Spells.Count; i++)
            {
                if (Class.Spells[i].Level <= Level)
                {
                    list.Add(_data.GetSpell(Class.Spells[i].SpellId));
                }
            }

            return list.AsReadOnly();
        }

        public void SetVitals(int hp, int mp)
        {
            Hp = Math.Clamp(hp, 0, MaxHp);
            Mp = Math.Clamp(mp, 0, MaxMp);
            if (Hp == 0)
            {
                // 戦闘不能時の MP はそのまま保持する
                Mp = Math.Clamp(mp, 0, MaxMp);
            }
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
            Hp = Math.Min(MaxHp, Hp + amount);
            return Hp - before;
        }

        public int RestoreMp(int amount)
        {
            if (amount <= 0 || !IsAlive)
            {
                return 0;
            }

            var before = Mp;
            Mp = Math.Min(MaxMp, Mp + amount);
            return Mp - before;
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

        public int Revive(int hpAmount)
        {
            if (IsAlive || hpAmount <= 0)
            {
                return 0;
            }

            Hp = Math.Clamp(hpAmount, 1, MaxHp);
            return Hp;
        }

        public void FullRestore()
        {
            Hp = MaxHp;
            Mp = MaxMp;
        }

        public void FullRestoreIfAlive()
        {
            if (IsAlive)
            {
                Hp = MaxHp;
                Mp = MaxMp;
            }
        }

        public IReadOnlyList<LevelUpGain> AddExp(int memberIndex, int amount)
        {
            if (amount <= 0 || !IsAlive)
            {
                return Array.Empty<LevelUpGain>();
            }

            var maxExp = _data.ExpTable.ExpForLevel(_data.ExpTable.MaxLevel);
            Exp = Math.Min(maxExp, Exp + amount);
            var targetLevel = _data.ExpTable.LevelForExp(Exp);
            if (targetLevel <= Level)
            {
                return Array.Empty<LevelUpGain>();
            }

            var gains = new List<LevelUpGain>(targetLevel - Level);
            while (Level < targetLevel)
            {
                Level++;
                var learned = new List<string>();
                for (var i = 0; i < Class.Spells.Count; i++)
                {
                    if (Class.Spells[i].Level == Level)
                    {
                        learned.Add(Class.Spells[i].SpellId);
                    }
                }

                // レベルアップ時は上昇値ぶん現在 HP・MP も増やす
                Hp = Math.Min(MaxHp, Hp + Class.HpGain);
                Mp = Math.Min(MaxMp, Mp + Class.MpGain);
                gains.Add(new LevelUpGain(memberIndex, Level, Class.HpGain, Class.MpGain, Class.AtkGain, Class.DefGain, Class.AgiGain, learned.AsReadOnly()));
            }

            return gains.AsReadOnly();
        }

        public bool CanEquip(EquipInfo equip) => equip != null && equip.CanEquip(Class.Id);

        /// <summary>指定した装備を着け、外れた旧装備 ID（なければ空文字）を返す。</summary>
        public string Equip(EquipInfo equip)
        {
            if (equip == null)
            {
                throw new ArgumentNullException(nameof(equip));
            }

            if (!CanEquip(equip))
            {
                throw new InvalidOperationException($"{Name} は {equip.Name} を装備できません。");
            }

            if (equip.Slot == EquipSlot.Weapon)
            {
                var prev = WeaponId;
                WeaponId = equip.Id;
                return prev;
            }
            else
            {
                var prev = ArmorId;
                ArmorId = equip.Id;
                return prev;
            }
        }

        public string Unequip(EquipSlot slot)
        {
            if (slot == EquipSlot.Weapon)
            {
                var prev = WeaponId;
                WeaponId = string.Empty;
                return prev;
            }
            else
            {
                var prev = ArmorId;
                ArmorId = string.Empty;
                return prev;
            }
        }

        internal static PartyMember Restore(GameData data, ClassInfo classInfo, int level, int exp, int hp, int mp, string weaponId, string armorId)
            => new(data, classInfo, level, exp, hp, mp, weaponId, armorId);

        public PartyMember Clone() => new(_data, Class, Level, Exp, Hp, Mp, WeaponId, ArmorId);

        public void MixHash(ref StateHash hash)
        {
            hash.Mix(Class.Index);
            hash.Mix(Level);
            hash.Mix(Exp);
            hash.Mix(Hp);
            hash.Mix(Mp);
            hash.Mix(WeaponId.Length > 0 ? _data.GetEquip(WeaponId).Index : -1);
            hash.Mix(ArmorId.Length > 0 ? _data.GetEquip(ArmorId).Index : -1);
        }
    }

    public sealed class Party
    {
        private readonly PartyMember[] _members;
        private readonly ReadOnlyCollection<PartyMember> _view;

        public Party(GameData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            _members = new PartyMember[data.Rules.PartyClassIds.Count];
            for (var i = 0; i < _members.Length; i++)
            {
                _members[i] = new PartyMember(data, data.GetClass(data.Rules.PartyClassIds[i]));
            }

            _view = Array.AsReadOnly(_members);
        }

        internal Party(PartyMember[] members)
        {
            _members = members;
            _view = Array.AsReadOnly(_members);
        }

        public IReadOnlyList<PartyMember> Members => _view;
        public int Count => _members.Length;
        public PartyMember this[int index] => _members[index];

        public bool IsWipedOut
        {
            get
            {
                for (var i = 0; i < _members.Length; i++)
                {
                    if (_members[i].IsAlive)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public int LivingCount
        {
            get
            {
                var c = 0;
                for (var i = 0; i < _members.Length; i++)
                {
                    if (_members[i].IsAlive)
                    {
                        c++;
                    }
                }

                return c;
            }
        }

        public int EquippedCount(string equipId)
        {
            if (string.IsNullOrEmpty(equipId))
            {
                return 0;
            }

            var count = 0;
            for (var i = 0; i < _members.Length; i++)
            {
                if (string.Equals(_members[i].WeaponId, equipId, StringComparison.Ordinal))
                {
                    count++;
                }

                if (string.Equals(_members[i].ArmorId, equipId, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        public Party Clone()
        {
            var copy = new PartyMember[_members.Length];
            for (var i = 0; i < _members.Length; i++)
            {
                copy[i] = _members[i].Clone();
            }

            return new Party(copy);
        }

        public void MixHash(ref StateHash hash)
        {
            hash.Mix(_members.Length);
            for (var i = 0; i < _members.Length; i++)
            {
                _members[i].MixHash(ref hash);
            }
        }
    }
}
