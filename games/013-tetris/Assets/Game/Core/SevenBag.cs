using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>ピースの供給元。</summary>
    public interface IPieceSource
    {
        /// <summary>index 個先のピースを、取り出さずに見る。0 が次のピース。</summary>
        PieceKind Peek(int index);

        /// <summary>次のピースを取り出す。</summary>
        PieceKind Take();
    }

    /// <summary>7種を1個ずつ入れた袋を混ぜ、順に配る。</summary>
    public sealed class SevenBag : IPieceSource
    {
        private readonly SeededRandom _random;
        private readonly List<PieceKind> _queue = new();

        public SevenBag(int seed) => _random = new SeededRandom(seed);

        public PieceKind Peek(int index)
        {
            if (index < 0) { throw new ArgumentOutOfRangeException(nameof(index), index, "0 以上を指定してください。"); }
            while (_queue.Count <= index) { Refill(); }
            return _queue[index];
        }

        public PieceKind Take()
        {
            var kind = Peek(0);
            _queue.RemoveAt(0);
            return kind;
        }

        private void Refill()
        {
            var bag = new PieceKind[PieceShapes.KindCount];
            for (var i = 0; i < bag.Length; i++) { bag[i] = (PieceKind)i; }
            for (var i = bag.Length - 1; i > 0; i--)
            {
                var j = _random.Next(i + 1);
                (bag[i], bag[j]) = (bag[j], bag[i]);
            }

            _queue.AddRange(bag);
        }
    }
}
