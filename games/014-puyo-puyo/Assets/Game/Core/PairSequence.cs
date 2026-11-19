using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>ペアの供給元。index 番目のペアは何度読んでも同じ。</summary>
    public interface IPairSource
    {
        Pair At(int index);
    }

    /// <summary>決まった順にペアを配る供給元。列の最後まで来たら先頭へ戻る。撮影とテストで使う。</summary>
    public sealed class FixedPairs : IPairSource
    {
        private readonly Pair[] _pairs;

        public FixedPairs(params Pair[] pairs)
        {
            if (pairs == null || pairs.Length == 0) { throw new ArgumentException("ペアがありません。", nameof(pairs)); }
            _pairs = (Pair[])pairs.Clone();
        }

        public Pair At(int index)
        {
            if (index < 0) { throw new ArgumentOutOfRangeException(nameof(index), index, "0 以上を指定してください。"); }
            return _pairs[index % _pairs.Length];
        }
    }

    /// <summary>シードから決まるペア列。両者が同じ列を順に使う。</summary>
    public sealed class PairSequence : IPairSource
    {
        private readonly SeededRandom _random;
        private readonly List<Pair> _pairs = new();

        public PairSequence(int seed) => _random = new SeededRandom(seed);

        public Pair At(int index)
        {
            if (index < 0) { throw new ArgumentOutOfRangeException(nameof(index), index, "0 以上を指定してください。"); }
            while (_pairs.Count <= index) { _pairs.Add(new Pair(NextColor(), NextColor())); }
            return _pairs[index];
        }

        private Cell NextColor() => (Cell)((int)Cell.Ember + _random.Next(CellRules.SporeColorCount));
    }
}
