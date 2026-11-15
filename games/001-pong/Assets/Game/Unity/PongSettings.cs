using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>試合と CPU のパラメータを Inspector から調整するための設定アセット。</summary>
    [CreateAssetMenu(fileName = "PongSettings", menuName = "Game/Pong Settings")]
    public sealed class PongSettings : ScriptableObject
    {
        [SerializeField] private PongConfig _match = new();
        [SerializeField] private CpuConfig _cpu = new();

        public PongConfig Match => _match;

        public CpuConfig Cpu => _cpu;
    }
}
