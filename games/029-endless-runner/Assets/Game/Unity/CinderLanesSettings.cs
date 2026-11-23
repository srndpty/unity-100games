using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    [CreateAssetMenu(menuName = "Game/Cinder Lanes Settings")]
    public sealed class CinderLanesSettings : ScriptableObject
    {
        /// <summary>
        /// 試合の設定（自機の動き・障害物の大きさ・難度曲線・チャンクの並べ方）。チャンクの表はコード（<see cref="ChunkLibrary"/>）に持つ。
        /// シーン生成スクリプトはこのアセットを上書きしないので、コードの既定値を変えたらアセットも直す（024 の知見）。
        /// </summary>
        public RunConfig Rules = new();

        [Range(0, 1)] public float Volume = .12f;

        public void Validate()
        {
            if (Rules == null) { throw new ArgumentNullException(nameof(Rules)); }
            Rules.Validate();
            if (!float.IsFinite(Volume) || Volume < 0 || Volume > 1) { throw new ArgumentOutOfRangeException(nameof(Volume), Volume, "0〜1 で指定してください。"); }
        }

        /// <summary>設定と既定のチャンクを検証して組にする（到達性の検査を含む。重いので起動時に 1 回）。</summary>
        public RunRules BuildRules()
        {
            Validate();
            return RunRules.Build(Rules, ChunkLibrary.Default());
        }
    }
}
