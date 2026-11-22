namespace TileMotion
{
    /// <summary>状態のハッシュ（FNV-1a）。整数だけを混ぜるので Mono と .NET で同じ結果になる。</summary>
    public struct StateHash
    {
        public const uint Seed = 2166136261u;

        public StateHash(uint value) => Value = value;

        public uint Value { get; private set; }

        public static StateHash Start() => new(Seed);

        public void Mix(long value)
        {
            unchecked
            {
                Value = (Value ^ (uint)value) * 16777619u;
                Value = (Value ^ (uint)(value >> 32)) * 16777619u;
            }
        }

        public void Mix(bool value) => Mix(value ? 1 : 0);
    }
}
