using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>ゲームのパラメータ（速度・効果時間・面データなど）を Inspector から調整するための設定アセット。</summary>
    [CreateAssetMenu(fileName = "BreakoutSettings", menuName = "Game/Breakout Settings")]
    public sealed class BreakoutSettings : ScriptableObject
    {
        [SerializeField] private BreakoutConfig _game = new();

        public BreakoutConfig Game => _game;
    }
}
