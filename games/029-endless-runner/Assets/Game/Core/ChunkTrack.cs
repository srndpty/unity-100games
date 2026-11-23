using System;
using IntGeometry;
using TileMotion;

namespace Game.Core
{
    /// <summary>自機がいる区間（助走またはチャンク 1 つ）。</summary>
    public readonly struct Segment
    {
        public Segment(long start, long end, int speed)
        {
            Start = start;
            End = end;
            Speed = speed;
        }

        public long Start { get; }

        public long End { get; }

        /// <summary>この区間の中の Tick の前進量。</summary>
        public int Speed { get; }
    }

    /// <summary>
    /// 無限に続くチャンクの列。自機の前方 <see cref="RunConfig.LookAheadMeters"/> までを並べ、
    /// 後方 <see cref="RunConfig.BehindMeters"/> を過ぎたものを回収する。生きているチャンクは固定容量のリングに入れる。
    /// <para>
    /// 選び方は専用の乱数系列と入口の距離（＝それまでのチャンクの長さの和）だけで決まり、自機の操作に依らない。
    /// 入口の距離で解禁された難度以下のチャンクから、重みで選ぶ。選べるものが 2 つ以上あれば直前と同じものは選ばない。
    /// </para>
    /// </summary>
    public sealed class ChunkTrack
    {
        private readonly RunRules _rules;
        private SeededRandom _random;
        private int[] _ids;
        private long[] _starts;
        private int[] _speeds;
        private int _head;
        private long _nextStart;
        private int _lastId = -1;

        public ChunkTrack(RunRules rules, int seed)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _random = new SeededRandom(seed);
            var capacity = rules.Settings.MaxActiveChunks;
            _ids = new int[capacity];
            _starts = new long[capacity];
            _speeds = new int[capacity];
            _nextStart = (long)rules.Settings.RunwayMeters * Units.Tile;
        }

        public int Count { get; private set; }

        public int Capacity => _ids.Length;

        /// <summary>これまでに並べた数。</summary>
        public long Spawned { get; private set; }

        /// <summary>これまでに回収した数。</summary>
        public long Recycled { get; private set; }

        /// <summary>助走の終わり（最初のチャンクの入口）。</summary>
        public long RunwayEnd => (long)_rules.Settings.RunwayMeters * Units.Tile;

        /// <summary>次に並べるチャンクの入口。</summary>
        public long NextStart => _nextStart;

        /// <summary>生きているチャンクの <paramref name="index"/> 番目（0 が最も手前）のライブラリの番号。</summary>
        public int IdAt(int index) => _ids[Slot(index)];

        public long StartAt(int index) => _starts[Slot(index)];

        public int SpeedAt(int index) => _speeds[Slot(index)];

        public ChunkDef ChunkAt(int index) => _rules.Chunks[IdAt(index)];

        /// <summary>何番目に並べたチャンクか（0 から数える。回収しても番号は変わらない）。</summary>
        public long SerialAt(int index)
        {
            if (index < 0 || index >= Count) { throw new ArgumentOutOfRangeException(nameof(index)); }
            return Recycled + index;
        }

        /// <summary>自機が z にいるときの区間。z は回収されていない範囲（助走か生きているチャンク）にあること。</summary>
        public Segment SegmentAt(long z)
        {
            var config = _rules.Settings;
            if (z < RunwayEnd) { return new Segment(0, RunwayEnd, Difficulty.Speed(config, 0)); }
            for (var i = Count - 1; i >= 0; i--)
            {
                var start = StartAt(i);
                if (start <= z) { return new Segment(start, start + ChunkAt(i).Length(config), SpeedAt(i)); }
            }

            throw new InvalidOperationException($"z = {z} のチャンクは並べていないか、回収済みです。");
        }

        /// <summary>自機が z に進んだ後：後方を回収し、前方を並べる。</summary>
        public void Update(long z)
        {
            var config = _rules.Settings;
            var behind = z - ((long)config.BehindMeters * Units.Tile);
            while (Count > 0 && StartAt(0) + ChunkAt(0).Length(config) <= behind)
            {
                _head = (_head + 1) % Capacity;
                Count--;
                Recycled++;
            }

            var ahead = z + ((long)config.LookAheadMeters * Units.Tile);
            while (_nextStart < ahead) { Spawn(); }
        }

        public ChunkTrack Clone()
        {
            var copy = (ChunkTrack)MemberwiseClone();
            copy._random = _random.Clone();
            copy._ids = (int[])_ids.Clone();
            copy._starts = (long[])_starts.Clone();
            copy._speeds = (int[])_speeds.Clone();
            return copy;
        }

        public void MixInto(ref StateHash hash)
        {
            hash.Mix(_random.State);
            hash.Mix(_nextStart);
            hash.Mix(_lastId);
            hash.Mix(Spawned);
            hash.Mix(Recycled);
            hash.Mix(Count);
            for (var i = 0; i < Count; i++)
            {
                hash.Mix(IdAt(i));
                hash.Mix(StartAt(i));
                hash.Mix(SpeedAt(i));
            }
        }

        private int Slot(int index)
        {
            if (index < 0 || index >= Count) { throw new ArgumentOutOfRangeException(nameof(index), index, $"生きているチャンクは {Count} 個です。"); }
            return (_head + index) % Capacity;
        }

        private void Spawn()
        {
            // 容量は設定の検証（RunRules.ActiveChunkBound）で足りることを保証している。超えたら止める（fail-closed）。
            if (Count >= Capacity) { throw new InvalidOperationException($"生きているチャンクが上限 {Capacity} を超えます。"); }
            var config = _rules.Settings;
            var id = Choose(Difficulty.Tier(config, _nextStart));
            var slot = (_head + Count) % Capacity;
            _ids[slot] = id;
            _starts[slot] = _nextStart;
            _speeds[slot] = Difficulty.Speed(config, _nextStart);
            Count++;
            Spawned++;
            _lastId = id;
            _nextStart += _rules.Chunks[id].Length(config);
        }

        private int Choose(int tier)
        {
            var chunks = _rules.Chunks;
            var eligible = 0;
            foreach (var chunk in chunks)
            {
                if (chunk.Tier <= tier) { eligible++; }
            }

            var total = 0;
            foreach (var chunk in chunks)
            {
                if (Allowed(chunk, tier, eligible)) { total += chunk.Weight; }
            }

            var pick = _random.Next(total);
            foreach (var chunk in chunks)
            {
                if (!Allowed(chunk, tier, eligible)) { continue; }
                if (pick < chunk.Weight) { return chunk.Id; }
                pick -= chunk.Weight;
            }

            throw new InvalidOperationException("チャンクを選べませんでした。");
        }

        private bool Allowed(ChunkDef chunk, int tier, int eligible) => chunk.Tier <= tier && (eligible < 2 || chunk.Id != _lastId);
    }
}
