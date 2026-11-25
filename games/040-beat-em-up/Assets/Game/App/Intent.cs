namespace Game.App
{
    public enum IntentKind
    {
        LeftDown,
        LeftUp,
        RightDown,
        RightUp,
        UpDown,
        UpUp,
        DownDown,
        DownUp,
        AttackDown,
        AttackUp,
        JumpDown,
        JumpUp,
        SpecialDown,
        SpecialUp,
        Submit,
        Cancel,
        Back,
        Overlay,
    }

    /// <summary>時刻つきのキー。同じ動作の物理キーはビットで別々に持つ。</summary>
    public readonly struct Intent
    {
        public Intent(IntentKind kind, double time, int keyBit = 1)
        {
            Kind = kind;
            Time = time;
            KeyBit = keyBit == 0 ? 1 : keyBit;
        }

        public IntentKind Kind { get; }

        public double Time { get; }

        public int KeyBit { get; }
    }
}
