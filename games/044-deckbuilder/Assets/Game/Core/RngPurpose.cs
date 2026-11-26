namespace Game.Core
{
    /// <summary>乱数を使う場面。場面ごとに系列を分け、ある場面の使用回数が他の場面の結果を変えないようにする。</summary>
    public enum RngPurpose
    {
        /// <summary>ルート（各節の遭遇の候補）。</summary>
        Route = 1,

        /// <summary>報酬のカードの候補。</summary>
        Reward = 2,

        /// <summary>戦闘の山札の混ぜ直し。</summary>
        Shuffle = 3,

        /// <summary>敵の行動の抽選。</summary>
        EnemyAi = 4,

        /// <summary>カードの効果の乱択（無作為の対象・無作為の廃棄）。</summary>
        Effect = 5,
    }
}
