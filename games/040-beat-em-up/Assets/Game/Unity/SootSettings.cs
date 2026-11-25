using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    [CreateAssetMenu(menuName = "Game/Soot Alley Settings")]
    public sealed class SootSettings : ScriptableObject
    {
        /// <summary>面と規則の設定。先行入力・攻撃権の数・出現の上限・残機などは試遊でここを変える。</summary>
        public BrawlConfig Rules = new();

        [Range(0, 1)] public float Volume = .15f;

        /// <summary>検証済みの設定の複製。画面が描ける登場者の数（<see cref="SootStage.Pool"/>）を超える置き場は拒否する。</summary>
        public BrawlConfig Validate()
        {
            if (Rules == null) { throw new ArgumentNullException(nameof(Rules)); }
            if (!float.IsFinite(Volume) || Volume < 0f || Volume > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(Volume), Volume, "0〜1 で指定してください。");
            }

            var rules = Rules.Copy().Validate();
            if (rules.Capacity > SootStage.Pool - 1)
            {
                throw new ArgumentOutOfRangeException(nameof(Rules), rules.Capacity, $"敵の置き場は画面が描ける {SootStage.Pool - 1} 体までです。");
            }

            return rules;
        }
    }
}
