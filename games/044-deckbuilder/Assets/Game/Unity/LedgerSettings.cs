using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 遊んで決める値（規則・演出の長さ・音量）とデータ。シーン生成スクリプトはこのアセットを上書きしないので、
    /// コードの既定値を変えたらアセットも直す（024 の知見）。PlayMode のテストで検証する。
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Cinder Ledger Settings")]
    public sealed class LedgerSettings : ScriptableObject
    {
        public DeckConfig Rules = new();

        public PlaybackTiming Timing = new();

        [Range(0, 1)] public float Volume = .1f;

        public TextAsset Cards;
        public TextAsset Enemies;
        public TextAsset Encounters;

        /// <summary>検証してデータを読む。</summary>
        public GameData Validate()
        {
            if (Rules == null) { throw new ArgumentNullException(nameof(Rules)); }
            if (Timing == null) { throw new ArgumentNullException(nameof(Timing)); }
            var errors = Rules.Validate();
            if (errors.Count > 0) { throw new ArgumentException("規則の値が不正です：" + string.Join(" / ", errors), nameof(Rules)); }
            if (Rules.MaxEnemies != LedgerStage.SlotCount) { throw new ArgumentOutOfRangeException(nameof(Rules), $"画面の配置は敵 {LedgerStage.SlotCount} 体専用です（010 の知見）。"); }
            if (Rules.HandLimit > 10) { throw new ArgumentOutOfRangeException(nameof(Rules), "手札の並びは 10 枚までです。"); }
            Timing.Validate();
            if (!float.IsFinite(Volume) || Volume < 0 || Volume > 1) { throw new ArgumentOutOfRangeException(nameof(Volume), Volume, "0〜1 で指定してください。"); }
            if (Cards == null || Enemies == null || Encounters == null) { throw new ArgumentNullException(nameof(Cards), "データの TextAsset がありません。"); }
            return GameData.Load(Cards.text, Enemies.text, Encounters.text, Rules);
        }
    }
}
