#nullable enable
using System;
using Game.Core;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Game.Unity
{
    /// <summary>
    /// 1 枚のフィールド地図の Prefab 描画。地形は <see cref="Tilemap"/>、宝箱・鉄格子・人物・魔将・主人公は <see cref="SpriteRenderer"/> で表し、
    /// プレイ前から Scene ビューで確認できる。
    /// </summary>
    public sealed class EmberlockMapStage : MonoBehaviour
    {
        [SerializeField] private string _mapId = string.Empty;
        [SerializeField] private Tilemap? _terrainTilemap;
        [SerializeField] private TileBase? _wallTile;
        [SerializeField] private TileBase? _floorTile;
        [SerializeField] private TileBase? _grassTile;
        [SerializeField] private TileBase? _bridgeTile;
        [SerializeField] private TileBase? _stairsTile;
        [SerializeField] private TileBase? _waterTile;
        [SerializeField] private SpriteRenderer[] _chests = Array.Empty<SpriteRenderer>();
        [SerializeField] private Sprite? _chestClosedSprite;
        [SerializeField] private Sprite? _chestOpenSprite;
        [SerializeField] private SpriteRenderer[] _doors = Array.Empty<SpriteRenderer>();
        [SerializeField] private Sprite? _doorClosedSprite;
        [SerializeField] private SpriteRenderer[] _npcs = Array.Empty<SpriteRenderer>();
        [SerializeField] private SpriteRenderer? _bossRenderer;
        [SerializeField] private SpriteRenderer? _heroRenderer;

        /// <summary>Direction 順（Down=0, Left=1, Right=2, Up=3）× 2 コマ = 8 枚。</summary>
        [SerializeField] private Sprite[] _heroSprites = Array.Empty<Sprite>();

        public string MapId => _mapId;
        public SpriteRenderer? HeroRenderer => _heroRenderer;

        /// <summary>Core のタイル座標（北=0）を Stage ローカル座標（セル中心）へ変換する。</summary>
        public static Vector3 CellCenter(int x, int y, int mapHeight)
        {
            return new Vector3(x + 0.5f, (mapHeight - 1 - y) + 0.5f, 0f);
        }

        public static Vector3Int TileCell(int x, int y, int mapHeight)
        {
            return new Vector3Int(x, mapHeight - 1 - y, 0);
        }

        public TileBase? TileForTerrain(TileTerrain terrain) => terrain switch
        {
            TileTerrain.Wall => _wallTile,
            TileTerrain.Floor => _floorTile,
            TileTerrain.Grass => _grassTile,
            TileTerrain.Bridge => _bridgeTile,
            TileTerrain.Stairs => _stairsTile,
            TileTerrain.Water => _waterTile,
            TileTerrain.LockedDoor => _floorTile,
            _ => _wallTile,
        };

        /// <summary>タイトル画面・Scene ビュー用の初期配置へ戻す。</summary>
        public void ShowStaticPreview(GameData data)
        {
            if (data == null || !data.TryGetMap(_mapId, out var map) || map == null)
            {
                return;
            }

            for (var i = 0; i < _chests.Length && i < map.Chests.Count; i++)
            {
                if (_chests[i] != null)
                {
                    _chests[i].sprite = _chestClosedSprite;
                    _chests[i].enabled = true;
                }
            }

            for (var i = 0; i < _doors.Length && i < map.Doors.Count; i++)
            {
                if (_doors[i] != null)
                {
                    _doors[i].sprite = _doorClosedSprite;
                    _doors[i].enabled = true;
                }
            }

            if (_bossRenderer != null)
            {
                _bossRenderer.enabled = map.Boss.Enabled;
            }

            if (_heroRenderer != null)
            {
                var isStartMap = string.Equals(_mapId, data.Rules.StartMapId, StringComparison.Ordinal);
                _heroRenderer.enabled = isStartMap;
                if (isStartMap)
                {
                    _heroRenderer.transform.localPosition = CellCenter(data.Rules.StartX, data.Rules.StartY, map.Height);
                    _heroRenderer.sprite = HeroSpriteFor(Direction.Down, frame: 0);
                }
            }
        }

        /// <summary>その Tick の <see cref="World"/> の状態をそのまま描画へ反映する。</summary>
        public void Show(World world, out Vector3 heroWorldPos)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            var map = world.Data.GetMap(_mapId);
            for (var i = 0; i < _chests.Length && i < map.Chests.Count; i++)
            {
                var opened = world.Flags.Get(map.Chests[i].OpenedFlag);
                if (_chests[i] != null)
                {
                    _chests[i].sprite = opened ? _chestOpenSprite : _chestClosedSprite;
                    _chests[i].enabled = true;
                }
            }

            for (var i = 0; i < _doors.Length && i < map.Doors.Count; i++)
            {
                var opened = world.Flags.Get(map.Doors[i].OpenedFlag);
                if (_doors[i] != null)
                {
                    _doors[i].enabled = !opened;
                }
            }

            if (_bossRenderer != null)
            {
                _bossRenderer.enabled = map.Boss.Enabled && !world.Flags.Get(map.Boss.DefeatedFlag);
            }

            var exp = world.Explorer;
            Vector3 localHero;
            if (exp.MoveRemaining > 0 && world.Data.Rules.StepTicks > 0)
            {
                var t = (world.Data.Rules.StepTicks - exp.MoveRemaining) / (float)world.Data.Rules.StepTicks;
                var from = CellCenter(exp.FromX, exp.FromY, map.Height);
                var to = CellCenter(exp.X, exp.Y, map.Height);
                localHero = Vector3.Lerp(from, to, t);
            }
            else
            {
                localHero = CellCenter(exp.X, exp.Y, map.Height);
            }

            var walkFrame = exp.MoveRemaining > 0
                ? ((exp.MoveRemaining / Math.Max(1, world.Data.Rules.StepTicks / 2)) % 2)
                : (exp.TotalSteps % 2);

            if (_heroRenderer != null)
            {
                _heroRenderer.enabled = true;
                _heroRenderer.transform.localPosition = localHero;
                _heroRenderer.sprite = HeroSpriteFor(exp.Facing, walkFrame);
            }

            heroWorldPos = transform.TransformPoint(localHero);
        }

        /// <summary>Prefab の配置と Core の <see cref="FieldMap"/> の一致を検査する（一致なら null）。</summary>
        public string? Mismatch(FieldMap map)
        {
            if (map == null)
            {
                return "FieldMap が null です。";
            }

            if (!string.Equals(_mapId, map.Id, StringComparison.Ordinal))
            {
                return $"MapId が一致しません（{_mapId} != {map.Id}）。";
            }

            if (_terrainTilemap == null)
            {
                return "TerrainTilemap が未設定です。";
            }

            for (var y = 0; y < map.Height; y++)
            {
                for (var x = 0; x < map.Width; x++)
                {
                    var expectedTile = TileForTerrain(map.TerrainAt(x, y));
                    var actualTile = _terrainTilemap.GetTile(TileCell(x, y, map.Height));
                    if (actualTile != expectedTile)
                    {
                        return $"タイル ({x},{y}) が {map.TerrainAt(x, y)} と一致しません。";
                    }
                }
            }

            if (_chests.Length != map.Chests.Count)
            {
                return $"宝箱数が一致しません（{_chests.Length} != {map.Chests.Count}）。";
            }

            for (var i = 0; i < _chests.Length; i++)
            {
                var expected = CellCenter(map.Chests[i].X, map.Chests[i].Y, map.Height);
                if (_chests[i] == null || Vector3.Distance(_chests[i].transform.localPosition, expected) > 0.01f)
                {
                    return $"宝箱 {i} の座標が地図と一致しません。";
                }
            }

            if (_doors.Length != map.Doors.Count)
            {
                return $"施錠扉数が一致しません（{_doors.Length} != {map.Doors.Count}）。";
            }

            for (var i = 0; i < _doors.Length; i++)
            {
                var expected = CellCenter(map.Doors[i].X, map.Doors[i].Y, map.Height);
                if (_doors[i] == null || Vector3.Distance(_doors[i].transform.localPosition, expected) > 0.01f)
                {
                    return $"施錠扉 {i} の座標が地図と一致しません。";
                }
            }

            if (_npcs.Length != map.Npcs.Count)
            {
                return $"NPC 数が一致しません（{_npcs.Length} != {map.Npcs.Count}）。";
            }

            for (var i = 0; i < _npcs.Length; i++)
            {
                var expected = CellCenter(map.Npcs[i].X, map.Npcs[i].Y, map.Height);
                if (_npcs[i] == null || Vector3.Distance(_npcs[i].transform.localPosition, expected) > 0.01f)
                {
                    return $"NPC {i} ({map.Npcs[i].Id}) の座標が地図と一致しません。";
                }
            }

            if (map.Boss.Enabled)
            {
                var expected = CellCenter(map.Boss.BossTileX, map.Boss.BossTileY, map.Height);
                if (_bossRenderer == null || Vector3.Distance(_bossRenderer.transform.localPosition, expected) > 0.01f)
                {
                    return "ボス配置座標が地図と一致しません。";
                }
            }

            if (_heroRenderer == null || _heroSprites.Length < 8)
            {
                return "主人公の SpriteRenderer または歩行スプライトが不足しています。";
            }

            return null;
        }

        private Sprite? HeroSpriteFor(Direction dir, int frame)
        {
            var idx = (((int)dir) * 2) + (Math.Abs(frame) % 2);
            return idx >= 0 && idx < _heroSprites.Length ? _heroSprites[idx] : null;
        }
    }
}
