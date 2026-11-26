namespace Game.Core
{
    /// <summary>乱数を使う場面。場面ごとに系列を分け、ある場面の使用回数が他の場面の結果を変えないようにする。</summary>
    public enum RngPurpose
    {
        /// <summary>草むらの遭遇（歩数ごとの判定と出現表の抽選・野生のレベル）。</summary>
        Encounter = 1,

        /// <summary>戦闘（命中・ダメージの乱数・状態異常の付与と持続・捕獲・逃走・CPU の考え）。番号は戦闘番号。</summary>
        Battle = 2,

        /// <summary>再戦（はじめから）の新しいシード。</summary>
        NextSeed = 3,
    }
}
