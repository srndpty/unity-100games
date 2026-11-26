using System;
using System.Collections.Generic;
using TileMotion;

namespace Game.Core
{
    /// <summary>捕まえた個体の行き先。</summary>
    public enum RosterPlace
    {
        Party = 1,
        Box = 2,

        /// <summary>手持ちもボックスも満杯（戦闘では捕獲玉を投げられないので起きない）。</summary>
        Rejected = 3,
    }

    public enum RosterError
    {
        None = 0,
        NoSuchSlot = 1,
        PartyFull = 2,
        BoxFull = 3,

        /// <summary>手持ちを 0 匹、または戦える個体のいない手持ちにはできない。</summary>
        LastHealthy = 4,
    }

    /// <summary>
    /// 手持ち（上限 <see cref="Rules.PartyMax"/>）とボックス（上限 <see cref="Rules.BoxMax"/>）。
    /// 個体の出し入れはこのクラスのメソッドだけが行い、どの操作の後も上限と「手持ちに戦える個体が 1 匹以上」を保つ。
    /// ボックスへ預けた個体は全快する。
    /// </summary>
    public sealed class Roster
    {
        private readonly List<Monster> _party;
        private readonly List<Monster> _box;

        public Roster(int partyMax, int boxMax)
        {
            if (partyMax < 1) { throw new ArgumentOutOfRangeException(nameof(partyMax)); }
            if (boxMax < 0) { throw new ArgumentOutOfRangeException(nameof(boxMax)); }
            PartyMax = partyMax;
            BoxMax = boxMax;
            _party = new List<Monster>();
            _box = new List<Monster>();
            Party = _party.AsReadOnly();
            Box = _box.AsReadOnly();
        }

        public int PartyMax { get; }

        public int BoxMax { get; }

        public IReadOnlyList<Monster> Party { get; }

        public IReadOnlyList<Monster> Box { get; }

        /// <summary>捕まえた個体を置く場所があるか。</summary>
        public bool HasRoom => _party.Count < PartyMax || _box.Count < BoxMax;

        public bool HasHealthy => _party.Exists(m => !m.IsFainted);

        public Roster Clone()
        {
            var copy = new Roster(PartyMax, BoxMax);
            foreach (var m in _party) { copy._party.Add(m.Clone()); }
            foreach (var m in _box) { copy._box.Add(m.Clone()); }
            return copy;
        }

        /// <summary>新しい個体を受け取る：手持ちに空きがあれば手持ち、なければボックス、どちらも満杯なら受け取らない。</summary>
        public RosterPlace Receive(Monster monster)
        {
            if (monster == null) { throw new ArgumentNullException(nameof(monster)); }
            if (_party.Count < PartyMax)
            {
                _party.Add(monster);
                return RosterPlace.Party;
            }

            if (_box.Count < BoxMax)
            {
                monster.FullRestore();
                _box.Add(monster);
                return RosterPlace.Box;
            }

            return RosterPlace.Rejected;
        }

        /// <summary>手持ちの 1 匹をボックスへ預ける（全快する）。</summary>
        public RosterError Deposit(int partyIndex)
        {
            if (partyIndex < 0 || partyIndex >= _party.Count) { return RosterError.NoSuchSlot; }
            if (_box.Count >= BoxMax) { return RosterError.BoxFull; }
            if (!HealthyRemainsWithout(partyIndex)) { return RosterError.LastHealthy; }
            var m = _party[partyIndex];
            _party.RemoveAt(partyIndex);
            m.FullRestore();
            _box.Add(m);
            return RosterError.None;
        }

        /// <summary>ボックスの 1 匹を手持ちの最後へ引き出す。</summary>
        public RosterError Withdraw(int boxIndex)
        {
            if (boxIndex < 0 || boxIndex >= _box.Count) { return RosterError.NoSuchSlot; }
            if (_party.Count >= PartyMax) { return RosterError.PartyFull; }
            var m = _box[boxIndex];
            _box.RemoveAt(boxIndex);
            _party.Add(m);
            return RosterError.None;
        }

        /// <summary>手持ちの 2 匹の並びを入れ替える（先頭が最初に場に出る）。</summary>
        public RosterError SwapParty(int a, int b)
        {
            if (a < 0 || b < 0 || a >= _party.Count || b >= _party.Count) { return RosterError.NoSuchSlot; }
            (_party[a], _party[b]) = (_party[b], _party[a]);
            return RosterError.None;
        }

        /// <summary>戦闘の後に手持ちを書き戻す（同じ並び・同じ数）。</summary>
        internal void ReplaceParty(IReadOnlyList<Monster> party)
        {
            if (party.Count != _party.Count) { throw new ArgumentException("手持ちの数が違います。", nameof(party)); }
            for (int i = 0; i < party.Count; i++)
            {
                if (!ReferenceEquals(party[i].Species, _party[i].Species)) { throw new ArgumentException("手持ちの並びが違います。", nameof(party)); }
                _party[i] = party[i].Clone();
            }
        }

        internal void RestoreAll()
        {
            foreach (var m in _party) { m.FullRestore(); }
        }

        /// <summary>セーブから戻すときだけ使う（上限は呼び出し側が確かめる）。</summary>
        internal void LoadUnchecked(IEnumerable<Monster> party, IEnumerable<Monster> box)
        {
            _party.Clear();
            _box.Clear();
            _party.AddRange(party);
            _box.AddRange(box);
        }

        public void MixHash(ref StateHash h)
        {
            h.Mix(_party.Count);
            foreach (var m in _party) { m.MixHash(ref h); }
            h.Mix(_box.Count);
            foreach (var m in _box) { m.MixHash(ref h); }
        }

        private bool HealthyRemainsWithout(int index)
        {
            for (int i = 0; i < _party.Count; i++)
            {
                if (i != index && !_party[i].IsFainted) { return true; }
            }

            return false;
        }
    }
}
