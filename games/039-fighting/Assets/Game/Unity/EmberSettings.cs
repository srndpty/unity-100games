using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    [CreateAssetMenu(menuName = "Game/Ember Duel Settings")]
    public sealed class EmberSettings : ScriptableObject
    {
        /// <summary>試合の設定。コマンドの猶予・ラウンドの長さ・CPU と打ち合う値は試遊でここを変える。</summary>
        public DuelConfig Rules = new();

        [Range(0, 1)] public float Volume = .15f;

        public DuelConfig Validate()
        {
            if (Rules == null) { throw new ArgumentNullException(nameof(Rules)); }
            if (!float.IsFinite(Volume) || Volume < 0f || Volume > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(Volume), Volume, "0〜1 で指定してください。");
            }

            return Rules.Copy().Validate();
        }
    }
}
