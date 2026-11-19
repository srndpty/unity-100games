using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>対戦画面の描画。左右の盤面に状態を渡す。</summary>
    public sealed class SporeBloomView : MonoBehaviour
    {
        [SerializeField] private SporeFieldView _player;
        [SerializeField] private SporeFieldView _cpu;

        public SporeFieldView Player => _player;
        public SporeFieldView Cpu => _cpu;

        /// <summary>試合の状態を描く。null なら空の盤面を描く。</summary>
        public void Render(VersusMatch match, int settleTicksPerRow, bool showNext)
        {
            _player.Render(match?.Player, settleTicksPerRow, showNext);
            _cpu.Render(match?.Cpu, settleTicksPerRow, showNext);
        }
    }
}
