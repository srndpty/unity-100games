using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    [CreateAssetMenu(menuName = "Game/Dewdrop Snip Settings")]
    public sealed class DewdropSnipSettings : ScriptableObject
    {
        /// <summary>
        /// 物理の設定（重力・減衰・反復・露玉の半径と重さ・移動の上限・摩擦）。
        /// シーン生成スクリプトはこのアセットを上書きしないので、コードの既定値を変えたらアセットも直す（024 の知見）。
        /// </summary>
        public RopeConfig Rules = new();

        /// <summary>問題集（<see cref="PuzzleBook"/> の書式）。</summary>
        public TextAsset Puzzles;

        /// <summary>解答の手順（撮影だけが使う）。</summary>
        public TextAsset Solutions;

        [Range(0, 1)] public float Volume = .12f;

        /// <summary>切った瞬間の演出を見せる Tick 数。</summary>
        [Min(1)] public int EffectTicks = 18;

        /// <summary>なぞりの軌跡を残す Tick 数。</summary>
        [Min(1)] public int TrailTicks = 10;

        /// <summary>設定と問題集を検証し、問題集を返す。</summary>
        public PuzzleBook Validate()
        {
            if (Rules == null) { throw new ArgumentNullException(nameof(Rules)); }
            if (Puzzles == null) { throw new ArgumentNullException(nameof(Puzzles)); }
            var book = PuzzleBook.Production(Puzzles.text, Rules);
            var capacity = DewdropSnipStage.Capacity(book, Rules);
            if (capacity != null) { throw new ArgumentOutOfRangeException(nameof(Puzzles), capacity); }
            if (!float.IsFinite(Volume) || Volume < 0 || Volume > 1) { throw new ArgumentOutOfRangeException(nameof(Volume), Volume, "0〜1 で指定してください。"); }
            if (EffectTicks < 1 || EffectTicks > 120) { throw new ArgumentOutOfRangeException(nameof(EffectTicks), EffectTicks, "1〜120 で指定してください。"); }
            if (TrailTicks < 1 || TrailTicks > 60) { throw new ArgumentOutOfRangeException(nameof(TrailTicks), TrailTicks, "1〜60 で指定してください。"); }
            return book;
        }
    }
}
