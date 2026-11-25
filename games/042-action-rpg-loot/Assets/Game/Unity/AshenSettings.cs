using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 遊んで決める値と、データの表（戦利品・地図の TextAsset）。シーン生成スクリプトはこのアセットを上書きしないので、
    /// コードの既定値を変えたらアセットも直す（024 の知見）。PlayMode のテストで検証と通しを走らせる。
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Ashen Reliquary Settings")]
    public sealed class AshenSettings : ScriptableObject
    {
        public DelveConfig Rules = new();

        public TextAsset Loot;

        public TextAsset Floors;

        [Range(0, 1)] public float Volume = .1f;

        /// <summary>画面に映す範囲の高さ（タイル）。幅は画面の縦横比から決める。</summary>
        [Range(8, 23)] public float ViewHeightTiles = 13.5f;

        public void Validate()
        {
            if (Rules == null) { throw new ArgumentNullException(nameof(Rules)); }
            Rules.Validate();
            if (Loot == null || Floors == null) { throw new InvalidOperationException("Loot・Floors の TextAsset を指定してください。"); }
            if (!float.IsFinite(Volume) || Volume < 0 || Volume > 1) { throw new ArgumentOutOfRangeException(nameof(Volume), Volume, "0〜1 で指定してください。"); }
            if (!float.IsFinite(ViewHeightTiles) || ViewHeightTiles < 8 || ViewHeightTiles > 23) { throw new ArgumentOutOfRangeException(nameof(ViewHeightTiles)); }
        }

        public LootData ParseLoot() => LootData.Parse(Loot.text);

        public FloorSet ParseFloors() => FloorSet.Parse(Floors.text);
    }
}
