using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Game.Unity
{
    /// <summary>
    /// 1 つの階の描画物：床と壁の Tilemap、階段、入口の印、配置ごとの敵（配置番号の順）。
    /// 生成スクリプトが地図から作り、プレイ前から Scene ビューで見える。実行中は敵の SpriteRenderer をそのまま動かす。
    /// </summary>
    public sealed class AshenFloorView : MonoBehaviour
    {
        [SerializeField] private Tilemap _ground;
        [SerializeField] private Tilemap _walls;
        [SerializeField] private SpriteRenderer _stairs;
        [SerializeField] private SpriteRenderer _entrance;
        [SerializeField] private SpriteRenderer[] _foes = Array.Empty<SpriteRenderer>();

        public Tilemap Ground => _ground;

        public Tilemap Walls => _walls;

        public SpriteRenderer Stairs => _stairs;

        public IReadOnlyList<SpriteRenderer> Foes => _foes;

        /// <summary>地図と一致するか。種類と位置まで比べる（023）。違えば理由、一致すれば null。</summary>
        public string Mismatch(FloorMap map, Func<FoeKind, Sprite> spriteOf)
        {
            if (map == null) { throw new ArgumentNullException(nameof(map)); }
            if (_ground == null || _walls == null || _entrance == null) { return $"{map.Number} 階の参照が欠けています。"; }
            for (int row = -1; row <= map.Height; row++)
            {
                for (int column = -1; column <= map.Width; column++)
                {
                    var cell = new Vector3Int(column, row, 0);
                    bool inside = column >= 0 && row >= 0 && column < map.Width && row < map.Height;
                    bool wall = inside && map.IsSolid(column, row);
                    if (wall != _walls.HasTile(cell)) { return $"{map.Number} 階の壁 ({column}, {row}) が地図と違います。"; }
                    if ((inside && !wall) != _ground.HasTile(cell)) { return $"{map.Number} 階の床 ({column}, {row}) が地図と違います。"; }
                }
            }

            if (map.HasStairs != (_stairs != null)) { return $"{map.Number} 階の階段の有無が地図と違います。"; }
            if (map.HasStairs && _stairs.transform.localPosition != AshenStage.Local(FloorMap.TileCenter(map.StairsColumn, map.StairsRow)))
            {
                return $"{map.Number} 階の階段の位置が地図と違います。";
            }

            if (_entrance.transform.localPosition != AshenStage.Local(map.Entrance)) { return $"{map.Number} 階の入口の位置が地図と違います。"; }
            if (_foes.Length != map.Spawns.Count) { return $"{map.Number} 階の敵の数 {_foes.Length} が配置 {map.Spawns.Count} と違います。"; }
            for (int i = 0; i < _foes.Length; i++)
            {
                var spawn = map.Spawns[i];
                if (_foes[i] == null) { return $"{map.Number} 階の配置 {i} の敵がありません。"; }
                if (_foes[i].sprite != spriteOf(spawn.Kind)) { return $"{map.Number} 階の配置 {i} の種類が {spawn.Kind} と違います。"; }
                if (_foes[i].transform.localPosition != AshenStage.Local(spawn.Center)) { return $"{map.Number} 階の配置 {i} の位置が地図と違います。"; }
            }

            return null;
        }
    }
}
