using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    [CreateAssetMenu(menuName = "Game/Mosslight Depths Settings")]
    public sealed class MossDepthsSettings : ScriptableObject
    {
        /// <summary>
        /// 試合の設定（速さ・跳躍・体力・敵とボスなど）。跳躍の値を変えると、設定の検証が能力のゲート（高い段に二段ジャンプでだけ届く）を
        /// 確かめる。既定値を変えると撮影とテストの入力の列（App の Routes）が合わなくなるので、App テストの RouteFinder で作り直す。
        /// </summary>
        public ExplorerConfig Rules = new();

        [Range(0, 1)] public float Volume = .12f;

        /// <summary>能力を取った・セーブしたときの案内を出しておく Tick 数。</summary>
        [Min(1)] public int ToastTicks = 180;

        public void Validate()
        {
            if (Rules == null) { throw new ArgumentNullException(nameof(Rules)); }
            Rules.Validate();
            if (!float.IsFinite(Volume) || Volume < 0 || Volume > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(Volume), Volume, "0〜1 で指定してください。");
            }

            if (ToastTicks < 1 || ToastTicks > 600)
            {
                throw new ArgumentOutOfRangeException(nameof(ToastTicks), ToastTicks, "1〜600 で指定してください。");
            }
        }
    }
}
