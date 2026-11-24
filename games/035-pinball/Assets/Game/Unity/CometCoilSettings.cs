using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    [CreateAssetMenu(menuName = "Game/Comet Coil Settings")]
    public sealed class CometCoilSettings : ScriptableObject
    {
        /// <summary>
        /// 物理とルールの設定（重力・速さ・反発・フリッパー・プランジャー・得点・球の数）。
        /// シーン生成スクリプトはこのアセットを上書きしないので、コードの既定値を変えたらアセットも直す（024 の知見）。
        /// </summary>
        public PinballConfig Rules = new();

        /// <summary>台（<see cref="Table"/> の書式）。</summary>
        public TextAsset Table;

        [Range(0, 1)] public float Volume = .12f;

        /// <summary>当たったバンパーを光らせる Tick 数。</summary>
        [Min(1)] public int FlashTicks = 8;

        /// <summary>撮影で操縦役に渡すシード（マルチボールの場面が撮れる値を選んである）。</summary>
        public int CapturePilotSeed = 12;

        /// <summary>設定と台を検証し、台を返す。</summary>
        public Table Validate()
        {
            if (Rules == null) { throw new ArgumentNullException(nameof(Rules)); }
            if (Table == null) { throw new ArgumentNullException(nameof(Table)); }
            var table = Game.Core.Table.Parse(Table.text, Rules);
            if (!float.IsFinite(Volume) || Volume < 0 || Volume > 1) { throw new ArgumentOutOfRangeException(nameof(Volume), Volume, "0〜1 で指定してください。"); }
            if (FlashTicks < 1 || FlashTicks > 120) { throw new ArgumentOutOfRangeException(nameof(FlashTicks), FlashTicks, "1〜120 で指定してください。"); }
            return table;
        }
    }
}
