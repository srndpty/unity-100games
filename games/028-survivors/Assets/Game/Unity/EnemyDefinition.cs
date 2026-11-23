using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 敵 1 種の定義アセット。数値（<see cref="Data"/>）は Core の <see cref="EnemyDef"/> そのもので、見た目（名前・絵の区画）だけを足す。
    /// 設定（<see cref="ThornTideSettings"/>）の並びが Core の敵の番号になる。
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Thorn Tide Enemy")]
    public sealed class EnemyDefinition : ScriptableObject
    {
        public string DisplayName = "";

        public EnemyDef Data = new();

        /// <summary>敵・宝石・弾をまとめて描く絵（<see cref="QuadLayer"/>）の区画の番号。ボスは別の絵で描くので使わない。</summary>
        public int ArtCell;
    }
}
