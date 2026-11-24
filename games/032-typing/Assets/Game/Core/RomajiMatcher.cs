using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Core
{
    /// <summary>1 打の結果。</summary>
    public enum KeyResult
    {
        /// <summary>どの綴りでも続かない（誤字）。状態は変わらない。</summary>
        Miss,

        /// <summary>受け付けた。</summary>
        Correct,

        /// <summary>受け付けて、読みを最後まで打ち終えた。</summary>
        Completed,
    }

    /// <summary>
    /// 読み 1 つに対する打鍵の照合。打った文字列と矛盾しない道筋（区切りの開始位置・区切り・その中の打った長さ）を全て持ち、
    /// どの道筋でも続かない文字は誤字として受け付けない。道筋の集合は打った文字列だけで決まる。
    /// </summary>
    public sealed class RomajiMatcher
    {
        private readonly List<Path> _paths = new();
        private readonly List<Path> _next = new();
        private readonly StringBuilder _typed = new();

        public RomajiMatcher(Reading reading)
        {
            Reading = reading ?? throw new ArgumentNullException(nameof(reading));
            View = new MatcherView(this);
            Reset();
        }

        public Reading Reading { get; }

        /// <summary>読み取り専用の窓口（試合の外へはこれだけを渡す）。</summary>
        public MatcherView View { get; }

        /// <summary>照合の状態が変わるたびに増える番号（受け付けた打鍵と <see cref="Reset"/>）。描画が書き換えの要否を決めるのに使う。</summary>
        public int Revision { get; private set; }

        /// <summary>これまでに受け付けた文字列。</summary>
        public string Typed => _typed.ToString();

        public int TypedLength => _typed.Length;

        public bool IsComplete { get; private set; }

        /// <summary>
        /// 残りの案内：先頭の道筋の区切りの残りと、それ以降の既定の綴り。
        /// 打った綴りに合わせて変わる（例：「し」を <c>s</c> と打った後に <c>i</c> を打てば以降は <c>si</c> の続き）。
        /// </summary>
        public string Guide
        {
            get
            {
                if (IsComplete) { return string.Empty; }
                var path = _paths[0];
                var chunk = Reading.ChunkAt(path.Start, path.Chunk);
                return chunk.Spelling.Substring(path.Offset) + Reading.DefaultFrom(path.Start + chunk.Length);
            }
        }

        /// <summary>読みのうち打ち終えたかなの数（先頭の道筋の区切りの開始位置。終われば読みの長さ）。</summary>
        public int KanaDone => IsComplete ? Reading.Kana.Length : _paths[0].Start;

        /// <summary><paramref name="key"/> を受け付けるか（状態は変えない）。</summary>
        public bool Accepts(char key)
        {
            if (IsComplete) { return false; }
            foreach (var path in _paths)
            {
                if (Reading.ChunkAt(path.Start, path.Chunk).Spelling[path.Offset] == key) { return true; }
            }

            return false;
        }

        /// <summary>1 打を照合する。誤字なら状態を変えない。打ち終えた後は全て誤字。</summary>
        public KeyResult Type(char key)
        {
            if (IsComplete) { return KeyResult.Miss; }
            _next.Clear();
            var completed = false;
            foreach (var path in _paths)
            {
                var chunk = Reading.ChunkAt(path.Start, path.Chunk);
                if (chunk.Spelling[path.Offset] != key) { continue; }
                if (path.Offset + 1 < chunk.Spelling.Length)
                {
                    AddNext(new Path(path.Start, path.Chunk, path.Offset + 1));
                    continue;
                }

                var end = path.Start + chunk.Length;
                if (end == Reading.Kana.Length) { completed = true; }
                else { Expand(end); }
            }

            if (!completed && _next.Count == 0) { return KeyResult.Miss; }
            Revision++;
            _typed.Append(key);
            _paths.Clear();
            if (completed)
            {
                IsComplete = true;
                return KeyResult.Completed;
            }

            _paths.AddRange(_next);
            return KeyResult.Correct;
        }

        /// <summary>打つ前に戻す。</summary>
        public void Reset()
        {
            Revision++;
            _typed.Clear();
            IsComplete = false;
            _paths.Clear();
            _next.Clear();
            Expand(0);
            _paths.AddRange(_next);
        }

        /// <summary>同じ読み・同じ打った文字列の独立した照合器。</summary>
        public RomajiMatcher Clone()
        {
            var clone = new RomajiMatcher(Reading);
            clone._paths.Clear();
            clone._paths.AddRange(_paths);
            clone._typed.Append(_typed);
            clone.IsComplete = IsComplete;
            clone.Revision = Revision;
            return clone;
        }

        private void Expand(int position)
        {
            for (var i = 0; i < Reading.ChunkCount(position); i++) { AddNext(new Path(position, i, 0)); }
        }

        private void AddNext(Path path)
        {
            if (!_next.Contains(path)) { _next.Add(path); }
        }

        private readonly struct Path : IEquatable<Path>
        {
            public Path(int start, int chunk, int offset)
            {
                Start = start;
                Chunk = chunk;
                Offset = offset;
            }

            public int Start { get; }

            public int Chunk { get; }

            public int Offset { get; }

            public bool Equals(Path other) => Start == other.Start && Chunk == other.Chunk && Offset == other.Offset;

            public override bool Equals(object? obj) => obj is Path other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(Start, Chunk, Offset);
        }
    }

    /// <summary>
    /// 照合器の読み取り専用の窓口。試合（<see cref="TypingRun"/>）の外から敵の照合を進めたり戻したりできないよう、
    /// 状態を変える操作を持たない別の型にしている（照合器へキャストして書き換えることもできない）。
    /// </summary>
    public sealed class MatcherView
    {
        private readonly RomajiMatcher _matcher;

        internal MatcherView(RomajiMatcher matcher) => _matcher = matcher;

        public Reading Reading => _matcher.Reading;

        public string Typed => _matcher.Typed;

        public int TypedLength => _matcher.TypedLength;

        public bool IsComplete => _matcher.IsComplete;

        public string Guide => _matcher.Guide;

        public int KanaDone => _matcher.KanaDone;

        /// <inheritdoc cref="RomajiMatcher.Revision"/>
        public int Revision => _matcher.Revision;

        public bool Accepts(char key) => _matcher.Accepts(key);
    }
}
