using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    [CreateAssetMenu(menuName = "Game/Tide Claim Settings")]
    public sealed class TideClaimSettings : ScriptableObject
    {
        /// <summary>画面の配置はこの大きさの盤に合わせてあるので、盤の大きさは変えられない。</summary>
        public const int BoardWidth = 80;
        public const int BoardHeight = 56;

        public QixConfig Rules = new();
        [Range(0, 1)] public float Volume = .12f;

        public void Validate()
        {
            Rules.Validate();
            if (Rules.Width != BoardWidth || Rules.Height != BoardHeight)
            {
                throw new ArgumentOutOfRangeException(nameof(Rules), $"{Rules.Width}×{Rules.Height}", $"盤は {BoardWidth}×{BoardHeight} 固定です。");
            }

            if (!float.IsFinite(Volume) || Volume < 0 || Volume > 1) { throw new ArgumentOutOfRangeException(nameof(Volume), Volume, "0〜1 で指定してください。"); }
        }
    }
}
