using System;
using System.Collections.Generic;
using Game.Core;
using IntGeometry;
using UnityEngine;
using UnityEngine.Tilemaps;
using Tile = Game.Core.Tile;

namespace Game.Unity
{
    /// <summary>
    /// 階の描画。<see cref="PlayerView"/> だけを読み、既知のマスの地形を Tilemap に塗る（視界の外は暗く）。
    /// 品は視界の中なら今の品、外なら記憶（半透明）、敵は視界の中だけ描く。1 マス = 1 ワールド単位で、Core の y（下向き）を上下に反転する。
    /// プレイ前の Scene ビューには、シード 1 の 1 階を焼いたものが見える（生成スクリプトが焼き、<see cref="Mismatch"/> で照合する）。
    /// </summary>
    public sealed class LanternStage : MonoBehaviour
    {
        /// <summary>Prefab に焼く見本の階（シード, 階）。</summary>
        public const int PreviewSeed = 1;
        public const int PreviewFloor = 1;

        public static readonly Color Remembered = new(.42f, .44f, .5f, 1f);
        public static readonly Color Lit = Color.white;
        public static readonly Color RememberedItem = new(1f, 1f, 1f, .45f);

        [SerializeField] private Tilemap _terrain;
        [SerializeField] private TileBase _room;
        [SerializeField] private TileBase _corridor;
        [SerializeField] private TileBase _wall;
        [SerializeField] private TileBase _stairs;
        [SerializeField] private SpriteRenderer _player;
        [SerializeField] private SpriteRenderer _facing;
        [SerializeField] private SpriteRenderer _monsterTemplate;
        [SerializeField] private SpriteRenderer _itemTemplate;
        [SerializeField] private SpriteRenderer _sleepTemplate;
        [SerializeField] private SpriteRenderer _hitTemplate;
        [SerializeField] private Sprite[] _monsterSprites = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] _itemSprites = Array.Empty<Sprite>();
        [SerializeField] private int _width = 48;
        [SerializeField] private int _height = 27;

        private readonly List<SpriteRenderer> _monsters = new();
        private readonly List<SpriteRenderer> _items = new();
        private readonly List<SpriteRenderer> _sleeps = new();
        private readonly List<SpriteRenderer> _hits = new();
        private int[] _painted = Array.Empty<int>();
        private PlayerView _shownView;
        private int _shownVersion = -1;

        public int Width => _width;

        public int Height => _height;

        public Tilemap Terrain => _terrain;

        public SpriteRenderer Player => _player;

        public SpriteRenderer Facing => _facing;

        /// <summary>今表示している描画物の数（テスト・計測用）。</summary>
        public int VisibleMonsters { get; private set; }

        public int VisibleItems { get; private set; }

        public int VisibleHits { get; private set; }

        /// <summary>前回の描画で塗り替えたマスの数（差分だけ塗っているかの確認用）。</summary>
        public int RepaintedCells { get; private set; }

        /// <summary>今まで作った描画物の数（見本を含まない）。</summary>
        public int PooledRenderers => _monsters.Count + _items.Count + _sleeps.Count + _hits.Count;

        public IReadOnlyList<SpriteRenderer> MonsterRenderers => _monsters;

        public IReadOnlyList<SpriteRenderer> ItemRenderers => _items;

        public Sprite MonsterSprite(MonsterKind kind) => _monsterSprites[(int)kind];

        public Sprite ItemSprite(ItemKind kind) => _itemSprites[(int)kind];

        public static Vector3Int Cell(Vec2i p, int height) => new(p.X, height - 1 - p.Y, 0);

        public static Vector3 CellCenter(Vec2i p, int height) => new(p.X + .5f, height - 1 - p.Y + .5f, 0);

        /// <summary>向きの印の回転（北が 0 度、時計回りに 45 度ずつ）。</summary>
        public static Quaternion FacingRotation(Direction direction) => Quaternion.Euler(0, 0, -45f * (int)direction);

        /// <summary>見本の階で描くマス：床と、床に接する壁（遊んでいて見える形と同じ。盤を埋める壁は描かない）。</summary>
        public static bool ShowsInPreview(FloorGrid grid, Vec2i p)
        {
            if (grid.IsWalkable(p)) { return true; }
            for (int d = 0; d < Directions.Count; d++)
            {
                if (grid.IsWalkable(p + ((Direction)d).Offset())) { return true; }
            }

            return false;
        }

        public TileBase TileFor(Tile tile) => tile switch
        {
            Tile.Room => _room,
            Tile.Corridor => _corridor,
            Tile.Stairs => _stairs,
            _ => _wall,
        };

        /// <summary>Prefab が見本の階と一致するか。参照の欠け・絵の数・焼いた地形の種類と位置まで比べる（023）。違えば理由、一致すれば null。</summary>
        public string Mismatch(FloorGrid preview)
        {
            if (preview == null) { throw new ArgumentNullException(nameof(preview)); }
            if (_terrain == null || _room == null || _corridor == null || _wall == null || _stairs == null) { return "地形の参照が欠けています。"; }
            if (_player == null || _facing == null || _monsterTemplate == null || _itemTemplate == null || _sleepTemplate == null || _hitTemplate == null)
            {
                return "描画物の参照が欠けています。";
            }

            if (_monsterSprites.Length != CrawlConfig.MonsterKindCount || Array.IndexOf(_monsterSprites, null) >= 0) { return "敵の絵が 5 種そろっていません。"; }
            if (_itemSprites.Length != CrawlConfig.ItemKindCount || Array.IndexOf(_itemSprites, null) >= 0) { return "品の絵が 8 種そろっていません。"; }
            if (preview.Width != _width || preview.Height != _height) { return $"盤の大きさ {_width}×{_height} が見本 {preview.Width}×{preview.Height} と違います。"; }
            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    var p = new Vec2i(x, y);
                    var expected = ShowsInPreview(preview, p) ? TileFor(preview[p]) : null;
                    if (_terrain.GetTile(Cell(p, _height)) != expected) { return $"焼いた地形 ({x}, {y}) が見本の階と違います。"; }
                }
            }

            return null;
        }

        /// <summary>試合の前の表示：見本の階の全体を暗く（既知の記憶と同じ色で）描き、敵と品は出さない。起動直後とタイトルへ戻った後で同じ絵になる。</summary>
        public void RenderTitle(FloorLayout preview)
        {
            if (preview == null) { throw new ArgumentNullException(nameof(preview)); }
            EnsureCells();
            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    var p = new Vec2i(x, y);
                    if (ShowsInPreview(preview.Grid, p)) { Paint(p, preview.Grid[p], false); }
                    else { Erase(p); }
                }
            }

            HideFrom(_monsters, 0);
            HideFrom(_items, 0);
            HideFrom(_sleeps, 0);
            HideFrom(_hits, 0);
            VisibleMonsters = 0;
            VisibleItems = 0;
            VisibleHits = 0;
            PlacePlayer(preview.Start, Direction.South, false);
            _shownView = null;
            _shownVersion = -1;
        }

        /// <summary>試合の今の状態を描く。窓口の版が変わっていなければ何もしない。</summary>
        public void Render(PlayerView view)
        {
            if (view == null) { throw new ArgumentNullException(nameof(view)); }
            if (ReferenceEquals(view, _shownView) && view.Version == _shownVersion) { return; }
            _shownView = view;
            _shownVersion = view.Version;
            EnsureCells();
            RepaintedCells = 0;

            int items = 0;
            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    var p = new Vec2i(x, y);
                    var tile = view.TileAt(p);
                    if (tile.HasValue) { Paint(p, tile.Value, view.IsVisible(p)); }
                    else { Erase(p); }

                    var item = view.ItemAt(p);
                    if (!item.HasValue) { continue; }
                    var r = Take(_items, _itemTemplate, items++);
                    r.sprite = _itemSprites[(int)item.Value];
                    r.color = view.IsVisible(p) ? Lit : RememberedItem;
                    r.transform.localPosition = CellCenter(p, _height);
                }
            }

            HideFrom(_items, items);
            VisibleItems = items;

            int sleeping = 0;
            for (int i = 0; i < view.Monsters.Count; i++)
            {
                var m = view.Monsters[i];
                var r = Take(_monsters, _monsterTemplate, i);
                r.sprite = _monsterSprites[(int)m.Kind];
                r.color = m.Sleeping ? new Color(.75f, .8f, 1f) : Lit;
                r.transform.localPosition = CellCenter(m.Position, _height);
                if (m.Sleeping)
                {
                    var z = Take(_sleeps, _sleepTemplate, sleeping++);
                    z.transform.localPosition = CellCenter(m.Position, _height) + new Vector3(.35f, .4f, 0);
                }
            }

            HideFrom(_monsters, view.Monsters.Count);
            HideFrom(_sleeps, sleeping);
            VisibleMonsters = view.Monsters.Count;

            // 直前の操作で当たった攻撃の印（見えた出来事だけ。次の操作まで残す）。
            int hits = 0;
            foreach (var e in view.Events)
            {
                if (!IsHit(e)) { continue; }
                var target = e.Target == 0 ? view.Position : e.Position;
                var r = Take(_hits, _hitTemplate, hits++);

                // 敵の絵を隠さないよう、小さくしてマスの右上に出す。
                r.transform.localPosition = CellCenter(target, _height) + new Vector3(.3f, .3f, 0);
                r.transform.localScale = new Vector3(.55f, .55f, 1);
                r.color = e.Target == 0 ? new Color(1f, .35f, .3f, .9f) : new Color(1f, 1f, 1f, .9f);
            }

            HideFrom(_hits, hits);
            VisibleHits = hits;
            PlacePlayer(view.Position, view.Facing, view.Asleep);
            _player.color = view.State == RunState.Dead ? new Color(.5f, .3f, .3f) : view.Asleep ? new Color(.7f, .75f, 1f) : Lit;
        }

        private static bool IsHit(RunEvent e) =>
            (e.Kind == EventKind.Attack || e.Kind == EventKind.Shot) && e.Value >= 0
            || (e.Kind == EventKind.ThrowHit && e.Value > 0);

        private void PlacePlayer(Vec2i position, Direction facing, bool asleep)
        {
            _player.enabled = true;
            _player.color = asleep ? new Color(.7f, .75f, 1f) : Lit;
            _player.transform.localPosition = CellCenter(position, _height);
            _facing.enabled = true;
            _facing.transform.localPosition = CellCenter(position, _height);
            _facing.transform.localRotation = FacingRotation(facing);
        }

        private void EnsureCells()
        {
            if (_painted.Length == _width * _height) { return; }
            _painted = new int[_width * _height];
            for (int i = 0; i < _painted.Length; i++) { _painted[i] = int.MinValue; }
        }

        /// <summary>マスを塗る。前回と同じ地形・明るさなら書き換えない。</summary>
        private void Paint(Vec2i p, Tile tile, bool lit)
        {
            var code = ((int)tile * 2) + (lit ? 1 : 0);
            var i = (p.Y * _width) + p.X;
            if (_painted[i] == code) { return; }
            _painted[i] = code;
            RepaintedCells++;
            var cell = Cell(p, _height);
            _terrain.SetTile(cell, TileFor(tile));
            _terrain.SetTileFlags(cell, TileFlags.None);
            _terrain.SetColor(cell, lit ? Lit : Remembered);
        }

        private void Erase(Vec2i p)
        {
            var i = (p.Y * _width) + p.X;
            if (_painted[i] == -1) { return; }
            _painted[i] = -1;
            RepaintedCells++;
            _terrain.SetTile(Cell(p, _height), null);
        }

        private SpriteRenderer Take(List<SpriteRenderer> pool, SpriteRenderer template, int index)
        {
            while (pool.Count <= index)
            {
                var copy = Instantiate(template, template.transform.parent);
                copy.name = $"{template.name}{pool.Count:00}";
                pool.Add(copy);
            }

            var r = pool[index];
            r.enabled = true;
            return r;
        }

        private static void HideFrom(List<SpriteRenderer> pool, int start)
        {
            for (int i = start; i < pool.Count; i++) { pool[i].enabled = false; }
        }
    }
}
