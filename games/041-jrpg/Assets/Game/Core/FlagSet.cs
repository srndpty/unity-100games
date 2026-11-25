using System;
using System.Collections.Generic;
using TileMotion;

namespace Game.Core
{
    /// <summary>進行フラグのビット集合。既知のフラグ名だけを索引で管理する。</summary>
    public sealed class FlagSet
    {
        private readonly GameData _data;
        private readonly uint[] _words;

        public FlagSet(GameData data)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            var count = Math.Max(1, (data.KnownFlags.Count + 31) / 32);
            _words = new uint[count];
        }

        private FlagSet(GameData data, uint[] words)
        {
            _data = data;
            _words = words;
        }

        public bool Get(string flagId)
        {
            if (!_data.TryGetFlagIndex(flagId, out var index))
            {
                return false;
            }

            return (_words[index >> 5] & (1u << (index & 31))) != 0;
        }

        public bool Set(string flagId, bool value = true)
        {
            if (!_data.TryGetFlagIndex(flagId, out var index))
            {
                return false;
            }

            var word = index >> 5;
            var bit = 1u << (index & 31);
            if (value)
            {
                _words[word] |= bit;
            }
            else
            {
                _words[word] &= ~bit;
            }

            return true;
        }

        public IReadOnlyList<string> ActiveFlags()
        {
            var list = new List<string>();
            for (var i = 0; i < _data.KnownFlags.Count; i++)
            {
                if ((_words[i >> 5] & (1u << (i & 31))) != 0)
                {
                    list.Add(_data.KnownFlags[i]);
                }
            }

            return list.AsReadOnly();
        }

        public FlagSet Clone() => new(_data, (uint[])_words.Clone());

        public void MixHash(ref StateHash hash)
        {
            hash.Mix(_words.Length);
            for (var i = 0; i < _words.Length; i++)
            {
                hash.Mix(_words[i]);
            }
        }
    }
}
