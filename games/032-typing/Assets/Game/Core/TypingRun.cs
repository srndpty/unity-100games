using System;
using System.Collections.Generic;
using IntGeometry;

namespace Game.Core
{
    public enum RunStatus
    {
        Playing,
        Cleared,
        Failed,
    }

    /// <summary>1 打の結果（試合から見たもの）。</summary>
    public enum TypeOutcome
    {
        /// <summary>試合が終わっているので何もしない。</summary>
        Ignored,

        /// <summary>誤字。進行せず、連続正解が 0 に戻る。</summary>
        Miss,

        /// <summary>狙いの敵の単語が 1 打進んだ。</summary>
        Correct,

        /// <summary>狙いの敵の単語を打ち終えて倒した。</summary>
        Defeated,
    }

    /// <summary>画面に反映する出来事。</summary>
    [Flags]
    public enum RunEvents
    {
        None = 0,
        Correct = 1,
        Miss = 1 << 1,
        Locked = 1 << 2,
        Released = 1 << 3,
        Defeated = 1 << 4,
        Hit = 1 << 5,
        Spawned = 1 << 6,
        Cleared = 1 << 7,
        Failed = 1 << 8,
    }

    /// <summary>迫ってくる敵。単語 1 つと、その打鍵の進み具合を持つ。</summary>
    public sealed class Enemy
    {
        /// <summary>出現した瞬間の奥行き。手前の線が 0。</summary>
        public const int Far = 10000;

        internal Enemy(int id, WordEntry word, int lane, int age, int approachTicks)
        {
            Id = id;
            Word = word;
            Lane = lane;
            Age = age;
            ApproachTicks = approachTicks;
            Progress = new RomajiMatcher(word.Reading);
        }

        private Enemy(Enemy source)
        {
            Id = source.Id;
            Word = source.Word;
            Lane = source.Lane;
            Age = source.Age;
            ApproachTicks = source.ApproachTicks;
            Progress = source.Progress.Clone();
        }

        /// <summary>出現順の番号（試合の中で一意）。</summary>
        public int Id { get; }

        public WordEntry Word { get; }

        /// <summary>画面の横の列。</summary>
        public int Lane { get; }

        /// <summary>出現してからの Tick。</summary>
        public int Age { get; internal set; }

        public int ApproachTicks { get; }

        /// <summary>打鍵の照合（読み取り専用）。進めるのは <see cref="TypingRun.Type"/>・<see cref="TypingRun.Backspace"/> だけ。</summary>
        public MatcherView Matcher => Progress.View;

        internal RomajiMatcher Progress { get; }

        /// <summary>奥行き（<see cref="Far"/> から 0 へ減る）。</summary>
        public int Distance => (int)(Far - ((long)Far * Math.Min(Age, ApproachTicks) / ApproachTicks));

        internal Enemy Clone() => new(this);
    }

    /// <summary>
    /// 試合全体。打鍵（<see cref="Type"/>・<see cref="Backspace"/>）は Tick の間に反映し、<see cref="Tick"/> で敵が進む。
    /// <para>狙い：狙いがないとき、打った 1 文字を受け付ける敵のうち最も手前（出現からの Tick が多い）、同じなら出現順の早い敵を狙う。
    /// 狙いがある間は、打鍵はその敵だけに効く。</para>
    /// <para>同じ Tick の順序：打鍵の反映 → 敵の前進 → 被弾 → 出現。規定数の撃破はその打鍵の時点でクリアになる。</para>
    /// </summary>
    public sealed class TypingRun
    {
        private readonly VigilConfig _config;
        private readonly WordBook _book;
        private readonly LevelConfig _level;
        private readonly List<Enemy> _enemies = new();
        private readonly List<WordEntry> _deck = new();
        private readonly SeededRandom _deckRandom;
        private readonly SeededRandom _spawnRandom;
        private int _spawnTimer;
        private int _nextId;
        private RunEvents _events;

        /// <param name="config">設定（検証して複製する）。</param>
        /// <param name="book">単語帳。</param>
        /// <param name="difficulty">難度。</param>
        /// <param name="seed">試合のシード。単語の山と出現（間隔・列）で系列を分ける。</param>
        public TypingRun(VigilConfig config, WordBook book, Difficulty difficulty, int seed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            _book = book ?? throw new ArgumentNullException(nameof(book));
            config.Validate();
            config.CheckWords(book, difficulty);
            _config = config.Copy();
            Difficulty = difficulty;
            _level = _config.Level(difficulty);
            Seed = seed;
            _deckRandom = new SeededRandom(unchecked(seed * 2));
            _spawnRandom = new SeededRandom(unchecked((seed * 2) + 1));
            Hp = _config.Hp;
            _spawnTimer = _level.FirstSpawnTicks;
        }

        private TypingRun(TypingRun source)
        {
            _config = source._config;
            _book = source._book;
            _level = source._level;
            foreach (var enemy in source._enemies) { _enemies.Add(enemy.Clone()); }
            _deck.AddRange(source._deck);
            _deckRandom = source._deckRandom.Clone();
            _spawnRandom = source._spawnRandom.Clone();
            _spawnTimer = source._spawnTimer;
            _nextId = source._nextId;
            _events = source._events;
            Difficulty = source.Difficulty;
            Seed = source.Seed;
            Status = source.Status;
            Ticks = source.Ticks;
            Hp = source.Hp;
            Defeated = source.Defeated;
            CorrectKeys = source.CorrectKeys;
            Misses = source.Misses;
            Combo = source.Combo;
            MaxCombo = source.MaxCombo;
            Score = source.Score;
            Spawned = source.Spawned;
            LastDefeated = source.LastDefeated?.Clone();
            LastDefeatedTick = source.LastDefeatedTick;
            LastHitTick = source.LastHitTick;
            LastMissTick = source.LastMissTick;
            if (source.Target != null) { Target = _enemies.Find(e => e.Id == source.Target.Id); }
        }

        public Difficulty Difficulty { get; }

        public int Seed { get; }

        public RunStatus Status { get; private set; }

        /// <summary>進めた Tick の数。</summary>
        public long Ticks { get; private set; }

        public int Hp { get; private set; }

        public int MaxHp => _config.Hp;

        public int Goal => _level.Goal;

        public int Lanes => _config.Lanes;

        public int ApproachTicks => _level.ApproachTicks;

        public int Defeated { get; private set; }

        /// <summary>受け付けた打鍵の数。</summary>
        public int CorrectKeys { get; private set; }

        /// <summary>誤字の数。</summary>
        public int Misses { get; private set; }

        /// <summary>連続して受け付けた打鍵の数。誤字で 0 に戻る。</summary>
        public int Combo { get; private set; }

        public int MaxCombo { get; private set; }

        public long Score { get; private set; }

        /// <summary>出現させた敵の数。</summary>
        public int Spawned { get; private set; }

        /// <summary>画面にいる敵（出現順）。</summary>
        public IReadOnlyList<Enemy> Enemies => _enemies.AsReadOnly();

        /// <summary>狙っている敵。</summary>
        public Enemy? Target { get; private set; }

        // 以下は描画（演出）のためだけに残す。以後の動きに影響しないのでハッシュに入れない。

        /// <summary>最後に倒した敵（倒した瞬間の位置のまま）。</summary>
        public Enemy? LastDefeated { get; private set; }

        /// <summary>最後に倒した時点の <see cref="Ticks"/>（なければ -1）。</summary>
        public long LastDefeatedTick { get; private set; } = -1;

        /// <summary>最後に被弾した Tick（なければ -1）。</summary>
        public long LastHitTick { get; private set; } = -1;

        /// <summary>最後に誤字を打った時点の <see cref="Ticks"/>（なければ -1）。</summary>
        public long LastMissTick { get; private set; } = -1;

        /// <summary>正確さ（‰）。まだ 1 打もなければ 1000。</summary>
        public int AccuracyPermil => CorrectKeys + Misses == 0 ? 1000 : (int)(1000L * CorrectKeys / (CorrectKeys + Misses));

        /// <summary>1 打の得点の倍率：1 + 連続正解 / 20（上限 5）。</summary>
        public static int Multiplier(int combo) => Math.Min(5, 1 + (combo / 20));

        /// <summary>受け付けた打鍵の速さ（1 分あたり）。<paramref name="playedTicks"/> は遊んだ時間。</summary>
        public static int KeysPerMinute(int correctKeys, long playedTicks) =>
            playedTicks <= 0 ? 0 : (int)Math.Min(int.MaxValue, correctKeys * 3600L / playedTicks);

        /// <summary>1 文字を打つ。受け付けない文字（<see cref="RomajiTable.Keys"/> の外）は例外。</summary>
        public TypeOutcome Type(char key)
        {
            if (!RomajiTable.IsKey(key)) { throw new ArgumentOutOfRangeException(nameof(key), key, "受け付けない文字です。"); }
            if (Status != RunStatus.Playing) { return TypeOutcome.Ignored; }

            var target = Target ?? Choose(key);
            if (target == null) { return Miss(); }
            var result = target.Progress.Type(key);
            if (result == KeyResult.Miss) { return Miss(); }
            if (Target == null)
            {
                Target = target;
                _events |= RunEvents.Locked;
            }

            CorrectKeys++;
            Combo++;
            MaxCombo = Math.Max(MaxCombo, Combo);
            Score += 10L * Multiplier(Combo);
            _events |= RunEvents.Correct;
            if (result == KeyResult.Correct) { return TypeOutcome.Correct; }

            Score += 50L * target.Word.Reading.Kana.Length;
            Defeated++;
            _enemies.Remove(target);
            LastDefeated = target;
            LastDefeatedTick = Ticks;
            Target = null;
            _events |= RunEvents.Defeated | RunEvents.Released;
            if (Defeated >= _level.Goal)
            {
                Status = RunStatus.Cleared;
                _events |= RunEvents.Cleared;
            }

            return TypeOutcome.Defeated;
        }

        /// <summary>狙いを外し、その敵の進み具合を最初に戻す。狙いがなければ何もしない。誤字・連続正解には影響しない。</summary>
        public bool Backspace()
        {
            if (Status != RunStatus.Playing || Target == null) { return false; }
            Target.Progress.Reset();
            Target = null;
            _events |= RunEvents.Released;
            return true;
        }

        /// <summary>1 Tick 進める：敵の前進 → 手前の線に着いた敵の被弾 → 出現。</summary>
        public void Tick()
        {
            if (Status != RunStatus.Playing) { return; }
            Ticks++;
            for (var i = 0; i < _enemies.Count; i++)
            {
                var enemy = _enemies[i];
                enemy.Age++;
                if (enemy.Age < enemy.ApproachTicks) { continue; }
                _enemies.RemoveAt(i);
                i--;
                if (Target == enemy)
                {
                    Target = null;
                    _events |= RunEvents.Released;
                }

                Hp--;
                LastHitTick = Ticks;
                _events |= RunEvents.Hit;
                if (Hp <= 0)
                {
                    Status = RunStatus.Failed;
                    _events |= RunEvents.Failed;
                    return;
                }
            }

            if (_spawnTimer > 0) { _spawnTimer--; }
            if (_spawnTimer > 0 || _enemies.Count >= _level.MaxEnemies || Defeated + _enemies.Count >= _level.Goal) { return; }
            Spawn();
            _spawnTimer = _spawnRandom.Range(_level.SpawnMinTicks, _level.SpawnMaxTicks);
        }

        /// <summary>
        /// 敵を直接置く（テスト・撮影の場面づくり用）。単語の山と乱数は使わない。
        /// </summary>
        public Enemy Place(WordEntry word, int lane, int age = 0)
        {
            if (word == null) { throw new ArgumentNullException(nameof(word)); }
            if (lane < 0 || lane >= _config.Lanes) { throw new ArgumentOutOfRangeException(nameof(lane)); }
            if (age < 0 || age >= _level.ApproachTicks) { throw new ArgumentOutOfRangeException(nameof(age)); }

            // 描画は列ごとに 1 体なので、同じ列に 2 体は置かない（同時出現数の上限は撮影のために適用しない）。
            if (_enemies.Exists(e => e.Lane == lane)) { throw new ArgumentException($"列 {lane} には既に敵がいます。", nameof(lane)); }
            var enemy = new Enemy(_nextId++, word, lane, age, _level.ApproachTicks);
            _enemies.Add(enemy);
            Spawned++;
            return enemy;
        }

        /// <summary>前回の呼び出し以降に起きた出来事を取り出す。</summary>
        public RunEvents TakeEvents()
        {
            var events = _events;
            _events = RunEvents.None;
            return events;
        }

        /// <summary>独立した複製（単語帳・設定は共有する。どちらも変更しない）。</summary>
        public TypingRun Clone() => new(this);

        /// <summary>以後の動きを決める状態のハッシュ（FNV-1a）。</summary>
        public uint Hash()
        {
            var h = new Fnv();

            // 規則（難度と設定）の指紋：同じ状態でも規則が違えば以後が分かれる。
            h.Add((int)Difficulty);
            h.Add(_config.Hp);
            h.Add(_config.Lanes);
            h.Add(_level.MinTier);
            h.Add(_level.MaxTier);
            h.Add(_level.MaxEnemies);
            h.Add(_level.ApproachTicks);
            h.Add(_level.Goal);
            h.Add(_level.FirstSpawnTicks);
            h.Add(_level.SpawnMinTicks);
            h.Add(_level.SpawnMaxTicks);
            h.Add(_book.Count);
            h.Add((int)Status);
            h.Add(Ticks);
            h.Add(Hp);
            h.Add(Defeated);
            h.Add(CorrectKeys);
            h.Add(Misses);
            h.Add(Combo);
            h.Add(MaxCombo);
            h.Add(Score);
            h.Add(Spawned);
            h.Add(_nextId);
            h.Add(_spawnTimer);
            h.Add(_deckRandom.State);
            h.Add(_spawnRandom.State);
            h.Add(_deck.Count);
            foreach (var word in _deck) { h.Add(word.Index); }
            h.Add(_enemies.Count);
            foreach (var enemy in _enemies)
            {
                h.Add(enemy.Id);
                h.Add(enemy.Word.Index);
                h.Add(enemy.Lane);
                h.Add(enemy.Age);
                h.Add(enemy.Matcher.Typed);
            }

            h.Add(Target?.Id ?? -1);
            return h.Value;
        }

        private TypeOutcome Miss()
        {
            Misses++;
            Combo = 0;
            LastMissTick = Ticks;
            _events |= RunEvents.Miss;
            return TypeOutcome.Miss;
        }

        private Enemy? Choose(char key)
        {
            Enemy? best = null;
            foreach (var enemy in _enemies)
            {
                if (!enemy.Matcher.Accepts(key)) { continue; }

                // 出現順に見るので、同じ Age なら先に見た（早い）敵が残る。
                if (best == null || enemy.Age > best.Age) { best = enemy; }
            }

            return best;
        }

        private void Spawn()
        {
            var word = Draw();
            var free = new List<int>();
            for (var lane = 0; lane < _config.Lanes; lane++)
            {
                if (!_enemies.Exists(e => e.Lane == lane)) { free.Add(lane); }
            }

            var chosen = free[_spawnRandom.Next(free.Count)];
            _enemies.Add(new Enemy(_nextId++, word, chosen, 0, _level.ApproachTicks));
            Spawned++;
            _events |= RunEvents.Spawned;
        }

        /// <summary>
        /// 山から 1 語を引く。画面にいない語のうち、最初の 1 打が画面の敵と重ならない語を山の上から優先する。
        /// 画面にいない語が山になければ、山を作り直す（段の語を全て切り混ぜる）。
        /// </summary>
        private WordEntry Draw()
        {
            var index = Pick();
            if (index < 0)
            {
                Refill();
                index = Pick();
            }

            var word = _deck[index];
            _deck.RemoveAt(index);
            return word;
        }

        private int Pick()
        {
            var fallback = -1;
            for (var i = 0; i < _deck.Count; i++)
            {
                var word = _deck[i];
                if (_enemies.Exists(e => e.Word == word)) { continue; }
                if (fallback < 0) { fallback = i; }
                if (!_enemies.Exists(e => SharesFirstKey(e.Word.Reading, word.Reading))) { return i; }
            }

            return fallback;
        }

        private static bool SharesFirstKey(Reading a, Reading b)
        {
            foreach (var key in a.FirstKeys)
            {
                if (b.FirstKeys.Contains(key)) { return true; }
            }

            return false;
        }

        private void Refill()
        {
            _deck.Clear();
            foreach (var entry in _book.Entries)
            {
                if (entry.Tier >= _level.MinTier && entry.Tier <= _level.MaxTier) { _deck.Add(entry); }
            }

            for (var i = _deck.Count - 1; i > 0; i--)
            {
                var j = _deckRandom.Next(i + 1);
                (_deck[i], _deck[j]) = (_deck[j], _deck[i]);
            }
        }

        private struct Fnv
        {
            private uint _value;
            private bool _started;

            public uint Value => _started ? _value : 2166136261u;

            public void Add(int value) => Add(unchecked((uint)value));

            public void Add(long value)
            {
                Add(unchecked((uint)value));
                Add(unchecked((uint)(value >> 32)));
            }

            public void Add(string value)
            {
                Add(value.Length);
                foreach (var c in value) { Add((int)c); }
            }

            public void Add(uint value)
            {
                if (!_started)
                {
                    _value = 2166136261u;
                    _started = true;
                }

                for (var i = 0; i < 4; i++)
                {
                    _value ^= (value >> (i * 8)) & 0xFF;
                    _value = unchecked(_value * 16777619u);
                }
            }
        }
    }
}
