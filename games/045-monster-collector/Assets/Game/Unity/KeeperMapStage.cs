#nullable enable
using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Game.Unity
{
    /// <summary>
    /// 1 枚の地図の Prefab。地形は <see cref="Tilemap"/>、トレーナーと主人公は <see cref="SpriteRenderer"/> で、プレイ前から Scene ビューで見える。
    /// 置き場所の計算（<see cref="CellCenter"/>・<see cref="TileCell"/>）は生成スクリプトと照合（<see cref="Mismatch"/>）で同じものを使う（023）。
    /// </summary>
    public sealed class KeeperMapStage : MonoBehaviour
    {
        [SerializeField] private string _mapId = string.Empty;
        [SerializeField] private Tilemap? _terrain;
        [SerializeField] private TileBase? _ground;
        [SerializeField] private TileBase? _road;
        [SerializeField] private TileBase? _flower;
        [SerializeField] private TileBase? _grassA;
        [SerializeField] private TileBase? _grassB;
        [SerializeField] private TileBase? _grassC;
        [SerializeField] private TileBase? _water;
        [SerializeField] private TileBase? _wall;
        [SerializeField] private TileBase? _house;
        [SerializeField] private TileBase? _counter;
        [SerializeField] private SpriteRenderer[] _trainers = Array.Empty<SpriteRenderer>();
        [SerializeField] private string[] _trainerIds = Array.Empty<string>();
        [SerializeField] private SpriteRenderer? _hero;

        /// <summary>向き（Up, Right, Down, Left）× 2 コマ = 8 枚。</summary>
        [SerializeField] private Sprite[] _heroSprites = Array.Empty<Sprite>();

        public string MapId => _mapId;

        public SpriteRenderer? Hero => _hero;

        public IReadOnlyList<SpriteRenderer> Trainers => _trainers;

        /// <summary>Core のマス（上が y=0）を Stage の座標（マスの中心、上が +y）へ。</summary>
        public static Vector3 CellCenter(int x, int y, int mapHeight) => new(x + 0.5f, mapHeight - 1 - y + 0.5f, 0f);

        public static Vector3Int TileCell(int x, int y, int mapHeight) => new(x, mapHeight - 1 - y, 0);

        public TileBase? TileFor(char c) => c switch
        {
            Tiles.Ground => _ground,
            Tiles.Road => _road,
            Tiles.Flower => _flower,
            '"' => _grassA,
            ';' => _grassB,
            ':' => _grassC,
            Tiles.Water => _water,
            Tiles.House => _house,
            Tiles.Counter => _counter,
            _ => _wall,
        };

        /// <summary>タイトル・Scene ビュー用の初期の姿（全員が立ち、主人公は開始位置）。</summary>
        public void ShowPreview(MonsterData data)
        {
            foreach (var t in _trainers)
            {
                if (t != null) { t.enabled = true; }
            }

            if (_hero == null) { return; }
            var isStart = string.Equals(_mapId, data.Rules.StartMapId, StringComparison.Ordinal);
            _hero.enabled = isStart;
            if (isStart)
            {
                _hero.transform.localPosition = CellCenter(data.Rules.StartX, data.Rules.StartY, data.Map(_mapId).Height);
                _hero.sprite = HeroSprite(Direction.Up, 0);
            }
        }

        /// <summary>その Tick の世界をそのまま描く（歩きの途中は 1 マスの間を Tick の割合で置く）。</summary>
        public void Show(World world, out Vector3 heroWorld)
        {
            var map = world.Data.Map(_mapId);
            for (int i = 0; i < _trainers.Length; i++)
            {
                if (_trainers[i] != null) { _trainers[i].enabled = !world.IsDefeated(world.Data.Trainer(_trainerIds[i])); }
            }

            var e = world.Explorer;
            var step = world.Data.Rules.StepTicks;
            var local = e.MoveRemaining > 0
                ? Vector3.Lerp(CellCenter(e.FromX, e.FromY, map.Height), CellCenter(e.X, e.Y, map.Height), (step - e.MoveRemaining) / (float)step)
                : CellCenter(e.X, e.Y, map.Height);
            var frame = e.MoveRemaining > 0 ? (e.MoveRemaining * 2 / Math.Max(1, step)) % 2 : 0;
            if (_hero != null)
            {
                _hero.enabled = true;
                _hero.transform.localPosition = local;
                _hero.sprite = HeroSprite(e.Facing, frame);
            }

            heroWorld = transform.TransformPoint(local);
        }

        /// <summary>Prefab の地形・トレーナーの種類と位置が Core の地図と一致するか（一致なら null）。</summary>
        public string? Mismatch(MonsterData data)
        {
            if (!data.TryGetMap(_mapId, out var map)) { return $"地図 {_mapId} がありません。"; }
            if (_terrain == null) { return "Tilemap が未設定です。"; }
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    var expected = TileFor(map.TileAt(x, y));
                    if (expected == null) { return $"{map.TileAt(x, y)} のタイルが未設定です。"; }
                    if (_terrain.GetTile(TileCell(x, y, map.Height)) != expected) { return $"マス ({x},{y}) が {map.TileAt(x, y)} と一致しません。"; }
                }
            }

            var trainers = new List<TrainerDef>();
            foreach (var t in data.Trainers)
            {
                if (string.Equals(t.MapId, _mapId, StringComparison.Ordinal)) { trainers.Add(t); }
            }

            if (_trainers.Length != trainers.Count || _trainerIds.Length != trainers.Count) { return $"トレーナーの数が一致しません（{_trainers.Length} != {trainers.Count}）。"; }
            for (int i = 0; i < trainers.Count; i++)
            {
                if (!string.Equals(_trainerIds[i], trainers[i].Id, StringComparison.Ordinal)) { return $"トレーナー {i} が {trainers[i].Id} ではありません。"; }
                if (_trainers[i] == null || Vector3.Distance(_trainers[i].transform.localPosition, CellCenter(trainers[i].X, trainers[i].Y, map.Height)) > 0.01f)
                {
                    return $"トレーナー {trainers[i].Id} の位置が地図と一致しません。";
                }
            }

            if (_hero == null || _heroSprites.Length != 8) { return "主人公の SpriteRenderer か歩きの絵が足りません。"; }
            return null;
        }

        private Sprite? HeroSprite(Direction dir, int frame)
        {
            var i = (int)dir * 2 + (frame & 1);
            return i < _heroSprites.Length ? _heroSprites[i] : null;
        }
    }
}
