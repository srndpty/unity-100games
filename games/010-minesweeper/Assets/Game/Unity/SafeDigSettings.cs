using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    [CreateAssetMenu(menuName = "Game/Safe Dig Settings")]
    public sealed class SafeDigSettings : ScriptableObject
    {
        /// <summary>画面の配置はこの盤面の大きさ専用。Core は他の大きさも扱えるが、この作品では受け付けない。</summary>
        public const int BoardWidth = 16;
        public const int BoardHeight = 16;

        public BoardConfig Board = new();
        [Range(0, 1)] public float Volume = .12f;

        public void Validate()
        {
            Board.Validate();
            if (Board.Width != BoardWidth || Board.Height != BoardHeight)
            {
                throw new System.ArgumentOutOfRangeException(nameof(Board), $"盤面は {BoardWidth}×{BoardHeight} 固定です。");
            }

            if (!float.IsFinite(Volume) || Volume < 0 || Volume > 1) { throw new System.ArgumentOutOfRangeException(nameof(Volume)); }
        }
    }
}
