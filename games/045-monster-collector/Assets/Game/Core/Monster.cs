using System;
using System.Collections.Generic;
using TileMotion;

namespace Game.Core
{
    /// <summary>覚えている技と残りの PP。</summary>
    public sealed class MoveSlot
    {
        internal MoveSlot(MoveDef move, int pp)
        {
            Move = move;
            Pp = pp;
        }

        public MoveDef Move { get; internal set; }

        public int Pp { get; internal set; }

        internal MoveSlot Clone() => new(Move, Pp);
    }

    /// <summary>
    /// モンスターの個体。能力は種族値とレベルから決まる（個体差なし）。
    /// 状態を変える操作は Core の内部（戦闘・手持ち・回復所）だけが持ち、外には読み取りだけを見せる。
    /// </summary>
    public sealed class Monster
    {
        private readonly List<MoveSlot> _moves;

        private Monster(SpeciesDef species, int level, int exp, int hp, List<MoveSlot> moves, StatusDef? status, int statusEndsTurn)
        {
            Species = species;
            Level = level;
            Exp = exp;
            Hp = hp;
            _moves = moves;
            Moves = _moves.AsReadOnly();
            Status = status;
            StatusEndsTurn = statusEndsTurn;
        }

        public SpeciesDef Species { get; }

        public int Level { get; private set; }

        public int Exp { get; private set; }

        public int Hp { get; internal set; }

        public IReadOnlyList<MoveSlot> Moves { get; }

        /// <summary>状態異常（なければ null）。戦闘が終わると治る。</summary>
        public StatusDef? Status { get; private set; }

        /// <summary>状態異常を解除するターン（このターンの開始で治る）。</summary>
        public int StatusEndsTurn { get; private set; }

        public int MaxHp => Formula.MaxHp(Species.BaseHp, Level);

        public int Attack => Formula.Stat(Species.BaseAttack, Level);

        public int Defense => Formula.Stat(Species.BaseDefense, Level);

        public int Speed => Formula.Stat(Species.BaseSpeed, Level);

        public bool IsFainted => Hp <= 0;

        public string Name => Species.Name;

        /// <summary>そのレベルまでに覚える技のうち後ろの 4 つを持ち、経験値はそのレベルの下限、HP は満タンで作る。</summary>
        public static Monster Create(SpeciesDef species, int level, Rules rules)
        {
            if (species == null) { throw new ArgumentNullException(nameof(species)); }
            if (rules == null) { throw new ArgumentNullException(nameof(rules)); }
            if (level < 1 || level > rules.LevelCap) { throw new ArgumentOutOfRangeException(nameof(level)); }
            var known = new List<MoveSlot>();
            foreach (var l in species.Learnset)
            {
                if (l.Level > level) { break; }
                if (known.Exists(m => ReferenceEquals(m.Move, l.Move))) { continue; }
                known.Add(new MoveSlot(l.Move, l.Move.Pp));
                if (known.Count > MonsterDataValidator.MaxMoves) { known.RemoveAt(0); }
            }

            return new Monster(species, level, Formula.ExpForLevel(rules, level), Formula.MaxHp(species.BaseHp, level), known, null, 0);
        }

        /// <summary>セーブから戻す。値の範囲は呼び出し側（SaveCodec）が確かめる。</summary>
        internal static Monster Restore(SpeciesDef species, int level, int exp, int hp, IEnumerable<MoveSlot> moves)
        {
            return new Monster(species, level, exp, hp, new List<MoveSlot>(moves), null, 0);
        }

        public Monster Clone()
        {
            var moves = new List<MoveSlot>(_moves.Count);
            foreach (var m in _moves) { moves.Add(m.Clone()); }
            return new Monster(Species, Level, Exp, Hp, moves, Status, StatusEndsTurn);
        }

        public bool Knows(MoveDef move)
        {
            foreach (var m in _moves)
            {
                if (ReferenceEquals(m.Move, move)) { return true; }
            }

            return false;
        }

        /// <summary>PP の残っている技があるか（なければ わるあがき を使う）。</summary>
        public bool HasUsableMove()
        {
            foreach (var m in _moves)
            {
                if (m.Pp > 0) { return true; }
            }

            return false;
        }

        internal void SetStatus(StatusDef status, int endsTurn)
        {
            Status = status;
            StatusEndsTurn = endsTurn;
        }

        internal void ClearStatus()
        {
            Status = null;
            StatusEndsTurn = 0;
        }

        /// <summary>HP・PP・状態異常を全て戻す（回復所・全滅の後）。</summary>
        internal void FullRestore()
        {
            Hp = MaxHp;
            foreach (var m in _moves) { m.Pp = m.Move.Pp; }
            ClearStatus();
        }

        internal void AddMove(MoveDef move)
        {
            if (_moves.Count >= MonsterDataValidator.MaxMoves) { throw new InvalidOperationException("技は 4 つまでです。"); }
            _moves.Add(new MoveSlot(move, move.Pp));
        }

        internal void ReplaceMove(int slot, MoveDef move)
        {
            _moves[slot] = new MoveSlot(move, move.Pp);
        }

        /// <summary>経験値を足してレベルを上げる。上がったレベルの一覧を返す（上限で止める）。</summary>
        internal List<int> GainExp(Rules rules, int amount)
        {
            var levels = new List<int>();
            var capExp = Formula.ExpForLevel(rules, rules.LevelCap);
            Exp = (int)Math.Min(capExp, (long)Exp + Math.Max(0, amount));
            while (Level < rules.LevelCap && Exp >= Formula.ExpForLevel(rules, Level + 1))
            {
                var oldMax = MaxHp;
                Level++;
                if (Hp > 0) { Hp += MaxHp - oldMax; }
                levels.Add(Level);
            }

            return levels;
        }

        public void MixHash(ref StateHash h)
        {
            h.Mix(Species.Index);
            h.Mix(Level);
            h.Mix(Exp);
            h.Mix(Hp);
            h.Mix(_moves.Count);
            foreach (var m in _moves)
            {
                h.Mix(m.Move.Index);
                h.Mix(m.Pp);
            }

            h.Mix(Status?.Index ?? -1);
            h.Mix(StatusEndsTurn);
        }
    }
}
