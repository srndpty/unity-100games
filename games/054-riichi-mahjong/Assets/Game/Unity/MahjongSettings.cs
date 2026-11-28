using UnityEngine;

namespace Game.Unity
{
    public sealed class MahjongSettings : ScriptableObject
    {
        [SerializeField, Range(0.05f, 2f)] private float _cpuDelay = 0.32f;
        [SerializeField, Range(0f, 0.2f)] private float _volume = 0.06f;

        public float CpuDelay => float.IsFinite(_cpuDelay) && _cpuDelay is >= 0.05f and <= 2f
            ? _cpuDelay : throw new System.InvalidOperationException("CPU待機時間が範囲外です。");
        public float Volume => float.IsFinite(_volume) && _volume is >= 0f and <= 0.2f
            ? _volume : throw new System.InvalidOperationException("音量が範囲外です。");
    }
}
