using Game.Core;

namespace Game.App
{
    /// <summary>プレイヤー枠を、CPU と同じ速度計画で走らせる。撮影・テスト・PlayMode で共有する。</summary>
    public sealed class Autopilot : IRacePilot
    {
        private KartConfig? _config;

        public void Reset() => _config = null;

        public Drive DriveFor(Race race)
        {
            if (_config == null) { _config = race.Config; }
            return CpuPlan.Intent(race.KartAt(0), race.Track, _config, _config.PaceOf(0));
        }
    }
}
