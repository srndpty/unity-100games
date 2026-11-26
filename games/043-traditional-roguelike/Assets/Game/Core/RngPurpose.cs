namespace Game.Core
{
    /// <summary>乱数を使う場面。場面ごとに系列を分け、ある場面の使用回数が他の場面の結果を変えないようにする。</summary>
    public enum RngPurpose
    {
        /// <summary>地形（部屋・通路・階段・開始位置）。</summary>
        Terrain = 1,

        /// <summary>階に入ったときの敵と品の配置。</summary>
        Placement = 2,

        /// <summary>行動の結果（命中・ダメージ・敵の考え・瞬間移動の行き先）。</summary>
        Action = 3,
    }
}
