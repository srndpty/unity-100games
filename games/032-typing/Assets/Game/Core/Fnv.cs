namespace Game.Core
{
    /// <summary>状態のハッシュ（FNV-1a、32 ビット）。Mono と .NET で同じ値になるよう、整数と文字コードだけを混ぜる。</summary>
    internal struct Fnv
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
