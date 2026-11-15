namespace Game.Core
{
    /// <summary>ブロックの種類。面データの 1 文字に対応する（<see cref="StageParser"/>）。</summary>
    public enum BrickKind
    {
        /// <summary>1 発で壊れる。</summary>
        Normal,

        /// <summary>2 発で壊れる。</summary>
        Hard,

        /// <summary>1 発で壊れ、貫通弾のカプセルを落とす。</summary>
        PierceItem,

        /// <summary>1 発で壊れ、パドル拡張のカプセルを落とす。</summary>
        ExpandItem,
    }

    /// <summary>パワーアップの効果。</summary>
    public enum EffectKind
    {
        /// <summary>ブロックで反射せず、HP に関係なく壊して進む。</summary>
        Pierce,

        /// <summary>パドルの幅を広げる。</summary>
        Expand,
    }

    public static class BrickKindExtensions
    {
        /// <summary>壊すまでに必要な当たり回数。</summary>
        public static int MaxHits(this BrickKind kind) => kind == BrickKind.Hard ? 2 : 1;

        /// <summary>壊したときに落とすカプセルの効果。落とさなければ null。</summary>
        public static EffectKind? DropsEffect(this BrickKind kind) => kind switch
        {
            BrickKind.PierceItem => EffectKind.Pierce,
            BrickKind.ExpandItem => EffectKind.Expand,
            _ => null,
        };
    }
}
