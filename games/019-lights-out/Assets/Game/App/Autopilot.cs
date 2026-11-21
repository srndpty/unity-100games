using System;
using Game.Core;

namespace Game.App
{
    /// <summary>今の盤の最短解を 1 マスずつ押す操縦役。撮影台本と App・PlayMode のテストで共有する。</summary>
    public static class Autopilot
    {
        /// <summary>次に押すマス。盤が全消灯なら -1。</summary>
        public static int NextPress(LightsOutGame game)
        {
            if (game == null) { throw new ArgumentNullException(nameof(game)); }
            var solution = game.MinimalSolution();
            for (var cell = 0; cell < LightsBoard.CellCount; cell++)
            {
                if ((solution & (1u << cell)) != 0) { return cell; }
            }

            return -1;
        }
    }
}
