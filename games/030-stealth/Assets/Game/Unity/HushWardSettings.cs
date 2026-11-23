using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    [CreateAssetMenu(menuName = "Game/Hush Ward Settings")]
    public sealed class HushWardSettings : ScriptableObject
    {
        /// <summary>
        /// 試合の設定（速さ・足音・視界・気づき値・FSM の時間・警戒レベル）。シーン生成スクリプトはこのアセットを上書きしないので、
        /// コードの既定値を変えたらアセットも直す（024 の知見）。
        /// </summary>
        public WardConfig Rules = new();

        [Range(0, 1)] public float Volume = .12f;

        /// <summary>見つかった・目的物を取ったなどの案内を出しておく Tick 数。</summary>
        [Min(1)] public int ToastTicks = 150;

        /// <summary>足音の届いた範囲を描いておく Tick 数。</summary>
        [Min(1)] public int NoiseShowTicks = 14;

        /// <summary>視界の扇の光線の本数。</summary>
        [Min(2)] public int FanRays = 33;

        public void Validate()
        {
            if (Rules == null) { throw new ArgumentNullException(nameof(Rules)); }
            Rules.Validate();
            if (!float.IsFinite(Volume) || Volume < 0 || Volume > 1) { throw new ArgumentOutOfRangeException(nameof(Volume), Volume, "0〜1 で指定してください。"); }
            if (ToastTicks < 1 || ToastTicks > 600) { throw new ArgumentOutOfRangeException(nameof(ToastTicks), ToastTicks, "1〜600 で指定してください。"); }
            if (NoiseShowTicks < 1 || NoiseShowTicks > 120) { throw new ArgumentOutOfRangeException(nameof(NoiseShowTicks), NoiseShowTicks, "1〜120 で指定してください。"); }
            if (FanRays < 2 || FanRays > 128) { throw new ArgumentOutOfRangeException(nameof(FanRays), FanRays, "2〜128 で指定してください。"); }
        }
    }
}
