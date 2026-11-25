using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 音の方針。音量は控えめを既定にし、テストなど画面なしのバッチ実行と証跡撮影では消音する
    /// （裏で動いているだけのときに突然鳴らさない）。
    /// </summary>
    public static class AudioPolicy
    {
        public static bool ShouldMute => Application.isBatchMode || EvidenceCapture.IsRunning;
    }
}
