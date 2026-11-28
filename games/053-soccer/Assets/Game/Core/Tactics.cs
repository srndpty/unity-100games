namespace Game.Core
{
    /// <summary>守備の方針。</summary>
    public enum DefenseStyle
    {
        /// <summary>前から奪う：どこでも保持者へ寄せる。</summary>
        PressHigh,

        /// <summary>引いて守る：保持者が自陣に入るまで寄せず、ゴール前を固める。</summary>
        SitBack,
    }

    /// <summary>攻撃の方針。</summary>
    public enum AttackStyle
    {
        /// <summary>つなぐ：安全なパスを優先し、はっきり空いたときだけシュート。</summary>
        Possession,

        /// <summary>蹴り込む：前への長いパス・ループと、早めのシュート。</summary>
        Direct,
    }

    /// <summary>チームの方針（CPU・人間側の味方の両方）。</summary>
    public readonly struct Tactics
    {
        public Tactics(DefenseStyle defense, AttackStyle attack)
        {
            Defense = defense;
            Attack = attack;
        }

        public DefenseStyle Defense { get; }
        public AttackStyle Attack { get; }

        public static Tactics Balanced => new(DefenseStyle.PressHigh, AttackStyle.Possession);

        public string Name => (Defense == DefenseStyle.PressHigh ? "前から奪う" : "引いて守る") + "・" + (Attack == AttackStyle.Possession ? "つなぐ" : "蹴り込む");

        public override string ToString() => Name;
    }
}
