using Game.Core;

namespace Game.App
{
    /// <summary>プレイヤー枠を、CPU と同じ速度計画（前の車をよける）で走らせる。撮影・テスト・PlayMode で共有する。</summary>
    public sealed class Autopilot : IRacePilot
    {
        public void Reset()
        {
        }

        /// <summary>プレイヤー枠の割合は 1000（<see cref="KartConfig.PaceOf"/> の 0 番）。</summary>
        public Drive DriveFor(Race race) => race.PlanFor(0, 1000);
    }
}
