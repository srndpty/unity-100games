using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// 統一ターンスケジューラ。自機も敵も「次に動ける時刻」を持ち、最も早い者が 1 行動して、行動の重さを自分の時刻に足す。
    /// 同じ時刻なら番号の小さい方が先（自機は 0、敵は出現の順に 1 から）。速い敵（重さ 50）は自機の 1 行動の間に 2 回動く。
    /// </summary>
    public sealed class TurnScheduler
    {
        public const int PlayerId = 0;

        private readonly List<Entry> _entries = new();

        /// <summary>最後に行動を始めた者の時刻。</summary>
        public long Now { get; private set; }

        public int Count => _entries.Count;

        public void Add(int id, long time)
        {
            if (Find(id) >= 0) { throw new InvalidOperationException($"行動者 {id} はすでにいます。"); }
            if (time < Now) { throw new ArgumentOutOfRangeException(nameof(time), "過去の時刻には入れられません。"); }
            _entries.Add(new Entry(id, time));
        }

        public void Remove(int id)
        {
            var i = Find(id);
            if (i >= 0) { _entries.RemoveAt(i); }
        }

        public bool Contains(int id) => Find(id) >= 0;

        public long TimeOf(int id)
        {
            var i = Find(id);
            if (i < 0) { throw new KeyNotFoundException($"行動者 {id} はいません。"); }
            return _entries[i].Time;
        }

        /// <summary>次に動く者（最も早い時刻、同じなら番号の小さい方）。時刻を <see cref="Now"/> に進める。</summary>
        public int Next()
        {
            if (_entries.Count == 0) { throw new InvalidOperationException("行動者がいません。"); }
            var best = 0;
            for (int i = 1; i < _entries.Count; i++)
            {
                var e = _entries[i];
                var b = _entries[best];
                if (e.Time < b.Time || (e.Time == b.Time && e.Id < b.Id)) { best = i; }
            }

            Now = _entries[best].Time;
            return _entries[best].Id;
        }

        /// <summary>行動を終えた者の時刻に重さを足す。</summary>
        public void Spend(int id, int cost)
        {
            if (cost <= 0) { throw new ArgumentOutOfRangeException(nameof(cost)); }
            var i = Find(id);
            if (i < 0) { throw new KeyNotFoundException($"行動者 {id} はいません。"); }
            _entries[i] = new Entry(id, _entries[i].Time + cost);
        }

        /// <summary>全員を取り除き、時刻だけを残す（階の移動）。</summary>
        public void Clear() => _entries.Clear();

        public TurnScheduler Clone()
        {
            var copy = new TurnScheduler { Now = Now };
            copy._entries.AddRange(_entries);
            return copy;
        }

        internal void Mix(ref TileMotion.StateHash hash)
        {
            hash.Mix(Now);
            hash.Mix(_entries.Count);
            foreach (var e in _entries)
            {
                hash.Mix(e.Id);
                hash.Mix(e.Time);
            }
        }

        private int Find(int id)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Id == id) { return i; }
            }

            return -1;
        }

        private readonly struct Entry
        {
            public Entry(int id, long time)
            {
                Id = id;
                Time = time;
            }

            public int Id { get; }

            public long Time { get; }
        }
    }
}
