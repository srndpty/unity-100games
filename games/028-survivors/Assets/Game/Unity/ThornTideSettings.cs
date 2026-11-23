using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 試合の設定。敵・武器の数値は定義アセット（<see cref="Enemies"/>・<see cref="Weapons"/>）が正で、
    /// <see cref="Rules"/> の Enemies・Weapons は使わない（<see cref="BuildRules"/> が定義アセットから入れ直す）。
    /// シーン生成スクリプトはこのアセットと定義アセットを上書きしないので、コードの既定値を変えたらアセットも直す（024 の知見）。
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Thorn Tide Settings")]
    public sealed class ThornTideSettings : ScriptableObject
    {
        /// <summary>自機・出現の表・上限など（敵・武器以外）。出現の表の重みは <see cref="Enemies"/> と同じ並び。</summary>
        public TideConfig Rules = new();

        public EnemyDefinition[] Enemies = Array.Empty<EnemyDefinition>();

        public WeaponDefinition[] Weapons = Array.Empty<WeaponDefinition>();

        [Range(0, 1)] public float Volume = .12f;

        /// <summary>定義アセットから敵・武器を入れた Core の設定（複製）を作って確かめる。誤りがあれば例外。</summary>
        public TideConfig BuildRules()
        {
            if (Rules == null) { throw new ArgumentNullException(nameof(Rules)); }
            if (Enemies == null || Array.Exists(Enemies, e => e == null)) { throw new InvalidOperationException("敵の定義アセットが欠けています。"); }
            if (Weapons == null || Array.Exists(Weapons, w => w == null)) { throw new InvalidOperationException("武器の定義アセットが欠けています。"); }
            var rules = Rules.Copy();
            rules.Enemies = Array.ConvertAll(Enemies, e => e.Data.Copy());
            rules.Weapons = Array.ConvertAll(Weapons, w => w.Data.Copy());
            rules.Validate();
            if (!float.IsFinite(Volume) || Volume < 0 || Volume > 1) { throw new ArgumentOutOfRangeException(nameof(Volume), Volume, "0〜1 で指定してください。"); }
            for (var i = 0; i < Enemies.Length; i++)
            {
                var cell = Enemies[i].ArtCell;
                if (Enemies[i].Data.Behavior != EnemyBehavior.Boss && (cell < 0 || cell >= QuadLayer.Cells))
                {
                    throw new ArgumentOutOfRangeException($"{nameof(Enemies)}[{i}].{nameof(EnemyDefinition.ArtCell)}", cell, $"0〜{QuadLayer.Cells - 1} で指定してください。");
                }
            }

            return rules;
        }

        /// <summary>敵の定義の番号ごとの絵の区画。</summary>
        public int[] EnemyCells() => Array.ConvertAll(Enemies, e => e.ArtCell);
    }
}
