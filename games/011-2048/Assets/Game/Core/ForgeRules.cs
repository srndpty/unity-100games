using System;

namespace Game.Core
{
    /// <summary>試合のルール設定。盤面の大きさと目標値は画面の配置に関わるので定数にする。</summary>
    [Serializable]
    public sealed class ForgeRules
    {
        public const int TargetValue = 2048;
        public const int InitialTiles = 2;

        /// <summary>新しい板が 4 になる確率（%）。残りは 2 になる。</summary>
        public int FourChancePercent = 10;

        public void Validate() => SpawnRules.RequirePercent(FourChancePercent);

        public ForgeRules Copy() => new() { FourChancePercent = FourChancePercent };
    }
}
