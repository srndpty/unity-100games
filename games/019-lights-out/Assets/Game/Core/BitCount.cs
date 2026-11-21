namespace Game.Core
{
    /// <summary>立っているビットの数（netstandard2.1 に BitOperations がないため）。</summary>
    public static class BitCount
    {
        public static int Of(uint value)
        {
            value -= (value >> 1) & 0x55555555u;
            value = (value & 0x33333333u) + ((value >> 2) & 0x33333333u);
            value = (value + (value >> 4)) & 0x0F0F0F0Fu;
            return (int)((value * 0x01010101u) >> 24);
        }
    }
}
