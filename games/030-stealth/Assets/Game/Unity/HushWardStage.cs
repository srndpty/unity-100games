using System;
using System.Collections.Generic;
using Game.Core;
using IntGeometry;
using TileMotion;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Game.Unity
{
    /// <summary>
    /// 施設の描画。床・壁・木箱は Tilemap、目的物・自機・警備員は SpriteRenderer で、どれも生成スクリプトが Core の地図から
    /// Prefab に置く（プレイ前から Scene ビューで見える）。1 タイル = 1 ワールド単位で、Stage のローカル座標は Core の単位を
    /// <see cref="Units.Tile"/> で割った値。Tick の状態をそのまま描き、補間しない。
    /// 足音が届いたマスは、Core と同じ <see cref="Hearing"/> で数え直して重ねる。
    /// </summary>
    public sealed class HushWardStage : MonoBehaviour
    {
        /// <summary>歩く姿の 1 コマの Tick 数。</summary>
        public const int FrameTicks = 8;

        [SerializeField] private Tilemap _floor;
        [SerializeField] private Tilemap _walls;
        [SerializeField] private Tilemap _crates;
        [SerializeField] private Tilemap _noise;
        [SerializeField] private TileBase _floorTile;
        [SerializeField] private TileBase _exitTile;
        [SerializeField] private TileBase _noiseTile;
        [SerializeField] private TileBase _wallTile;
        [SerializeField] private TileBase _wallFaceTile;
        [SerializeField] private TileBase _crateTile;
        [SerializeField] private SpriteRenderer _objective;
        [SerializeField] private SpriteRenderer _intruder;
        [SerializeField] private SpriteRenderer _carried;
        [SerializeField] private Sprite[] _intruderDown = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] _intruderUp = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] _intruderSide = Array.Empty<Sprite>();
        [SerializeField] private Sprite _intruderSneak;

        /// <summary>施設の警備員の順（<see cref="Facility.Guards"/>）。</summary>
        [SerializeField] private HushWardGuardView[] _guards = Array.Empty<HushWardGuardView>();

        private readonly List<Vector3Int> _noiseCells = new();
        private Hearing _hearing;
        private FacilityMap _hearingMap;
        private long _noiseShown = -1;

        public SpriteRenderer Intruder => _intruder;

        public SpriteRenderer Objective => _objective;

        public SpriteRenderer Carried => _carried;

        public Tilemap Noise => _noise;

        public IReadOnlyList<HushWardGuardView> Guards => _guards;

        /// <summary>今描いている足音のマスの数。</summary>
        public int NoiseCells => _noiseCells.Count;

        /// <summary>Core の単位の位置を Stage のローカル座標へ。</summary>
        public static Vector3 Local(Vec2i point) => new(point.X / (float)Units.Tile, point.Y / (float)Units.Tile, 0);

        /// <summary>マスの中央（Stage のローカル座標）。描画と Prefab の照合・生成で共有する。</summary>
        public static Vector3 CellCenter(Cell cell) => Local(cell.Center);

        /// <summary>下が壁でない壁は、手前の面のタイルにする（生成と照合で同じ規則を使う）。</summary>
        public static bool IsWallFace(FacilityMap map, int column, int row)
        {
            if (map == null) { throw new ArgumentNullException(nameof(map)); }
            return row > 0 && map[column, row - 1] != TileKind.Wall;
        }

        /// <summary>
        /// 描画に要る部品（Tilemap・タイルアセット・絵・Renderer・警備員の部品）がそろい、床・壁・木箱のタイルの種類と、
        /// 置かれた物（目的物・自機・警備員）の位置が Core の施設と一致するか。違えば理由、一致すれば null。例外は投げない。
        /// </summary>
        public string Mismatch(Facility facility)
        {
            if (facility == null) { throw new ArgumentNullException(nameof(facility)); }
            var missing = MissingPart();
            if (missing != null) { return missing + " がありません。"; }
            var map = facility.Map;
            for (var row = -1; row <= map.Height; row++)
            {
                for (var column = -1; column <= map.Width; column++)
                {
                    var inside = map.Contains(column, row);
                    var kind = map[column, row];
                    var cell = new Vector3Int(column, row, 0);
                    var floor = !inside ? null : kind == TileKind.Exit ? _exitTile : _floorTile;
                    if (_floor.GetTile(cell) != floor) { return $"床 ({column}, {row}) が地図と違います。"; }
                    var wall = inside && kind == TileKind.Wall ? IsWallFace(map, column, row) ? _wallFaceTile : _wallTile : null;
                    if (_walls.GetTile(cell) != wall) { return $"壁 ({column}, {row}) のタイルが地図と違います。"; }
                    if (_crates.GetTile(cell) != (inside && kind == TileKind.Crate ? _crateTile : null)) { return $"木箱 ({column}, {row}) のタイルが地図と違います。"; }
                    if (_noise.HasTile(cell)) { return $"足音のマス ({column}, {row}) が最初から置かれています。"; }
                }
            }

            // 置かれた物は数だけでなく位置も、Core の施設から期待値を出して比べる（023 の知見）。
            if (!At(_objective, CellCenter(facility.Objective))) { return $"目的物の位置が地図の {facility.Objective} と違います。"; }
            if (!At(_intruder, CellCenter(facility.Exit))) { return $"自機の位置が入口 {facility.Exit} と違います。"; }
            if (_guards.Length != facility.Guards.Count) { return "警備員の数が施設と違います。"; }
            for (var i = 0; i < _guards.Length; i++)
            {
                var plan = facility.Guards[i];
                if (_guards[i] == null) { return $"警備員 {plan.Name} の描画がありません。"; }
                if (!_guards[i].IsComplete) { return $"警備員 {plan.Name} の描画の部品か絵が欠けています。"; }
                if ((_guards[i].transform.localPosition - CellCenter(plan.Start)).sqrMagnitude > 1e-6f) { return $"警備員 {plan.Name} の位置が巡回の始め {plan.Start} と違います。"; }
            }

            return null;
        }

        /// <summary>欠けている部品の名前（そろっていれば null）。</summary>
        private string MissingPart()
        {
            if (_floor == null) { return "床の Tilemap"; }
            if (_walls == null) { return "壁の Tilemap"; }
            if (_crates == null) { return "木箱の Tilemap"; }
            if (_noise == null) { return "足音の Tilemap"; }
            if (_floorTile == null || _exitTile == null) { return "床・入口のタイル"; }
            if (_wallTile == null || _wallFaceTile == null) { return "壁のタイル"; }
            if (_crateTile == null) { return "木箱のタイル"; }
            if (_noiseTile == null) { return "足音のタイル"; }
            if (_objective == null) { return "目的物の Renderer"; }
            if (_intruder == null) { return "自機の Renderer"; }
            if (_carried == null) { return "運んでいる印の Renderer"; }
            if (!Pair(_intruderDown) || !Pair(_intruderUp) || !Pair(_intruderSide)) { return "自機の向きの絵（2 コマずつ）"; }
            if (_intruderSneak == null) { return "自機の忍び足の絵"; }
            return _guards == null ? "警備員の描画の並び" : null;
        }

        private static bool Pair(Sprite[] sprites) => sprites != null && sprites.Length == 2 && sprites[0] != null && sprites[1] != null;

        private static bool At(SpriteRenderer renderer, Vector3 expected) =>
            renderer != null && (renderer.transform.localPosition - expected).sqrMagnitude < 1e-6f;

        /// <summary>
        /// 試合の前の姿を描く（タイトル）。Core の施設の初期配置から作り直すので、遊んだ後にタイトルへ戻っても前の試合の状態は残らない。
        /// </summary>
        public void RenderInitial(Facility facility, WardConfig config, int rays)
        {
            if (facility == null) { throw new ArgumentNullException(nameof(facility)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            _objective.enabled = true;
            _carried.enabled = false;
            _intruder.transform.localPosition = CellCenter(facility.Exit);
            _intruder.sprite = _intruderUp[0];
            _intruder.flipX = false;
            _intruder.color = Color.white;
            for (var i = 0; i < _guards.Length; i++) { _guards[i].RenderInitial(facility.Map, facility.Guards[i], config.VisionAt(AlertLevel.Normal), rays); }
            ClearNoise();
        }

        public void Render(Infiltration match, int rays, bool debug, int noiseShowTicks)
        {
            if (match == null) { throw new ArgumentNullException(nameof(match)); }
            _objective.enabled = !match.HasObjective;
            DrawIntruder(match);
            for (var i = 0; i < _guards.Length; i++) { _guards[i].Render(match, match.Guards[i], rays, debug); }
            DrawNoise(match, noiseShowTicks);
        }

        private void DrawIntruder(Infiltration match)
        {
            var intruder = match.Intruder;
            _intruder.transform.localPosition = Local(intruder.Center);
            var moving = intruder.Motion.Start != intruder.Motion.End;
            var frame = moving ? (int)(match.Ticks / FrameTicks % 2) : 0;
            if (intruder.Gait == Gait.Sneak && moving) { _intruder.sprite = _intruderSneak; }
            else if (intruder.FacingY > 0 && intruder.FacingX == 0) { _intruder.sprite = _intruderUp[frame]; }
            else if (intruder.FacingY < 0 && intruder.FacingX == 0) { _intruder.sprite = _intruderDown[frame]; }
            else { _intruder.sprite = _intruderSide[frame]; }
            _intruder.flipX = intruder.FacingX < 0;

            // 捕まったら赤く、忍び足は少し透ける。目的物を持っていれば頭の上に出す。
            _intruder.color = match.Status == MatchStatus.Caught ? new Color(1f, .45f, .45f) : intruder.Gait == Gait.Sneak ? new Color(1, 1, 1, .75f) : Color.white;
            _carried.enabled = match.HasObjective;
        }

        /// <summary>最後の足音が届いたマスを、出てから <paramref name="showTicks"/> Tick の間だけ薄れながら描く。</summary>
        private void DrawNoise(Infiltration match, int showTicks)
        {
            var noise = match.LastNoise;
            var age = match.Ticks - match.LastNoiseTick;
            if (noise == null || age >= showTicks)
            {
                ClearNoise();
                return;
            }

            if (_noiseShown != match.LastNoiseTick)
            {
                ClearNoise();
                _noiseShown = match.LastNoiseTick;
                if (_hearing == null || _hearingMap != match.Map)
                {
                    _hearingMap = match.Map;
                    _hearing = new Hearing(match.Map);
                }

                _hearing.Spread(noise.Value.Source, noise.Value.Radius);
                var map = match.Map;
                for (var row = 0; row < map.Height; row++)
                {
                    for (var column = 0; column < map.Width; column++)
                    {
                        if (!_hearing.Hears(new Cell(column, row))) { continue; }
                        var cell = new Vector3Int(column, row, 0);
                        _noise.SetTile(cell, _noiseTile);
                        _noiseCells.Add(cell);
                    }
                }
            }

            var color = noise.Value.Gait switch
            {
                Gait.Run => new Color(1f, .55f, .35f),
                Gait.Sneak => new Color(.6f, .85f, 1f),
                _ => new Color(1f, .95f, .7f),
            };
            color.a = .12f * (1f - ((float)age / showTicks));
            _noise.color = color;
        }

        private void ClearNoise()
        {
            foreach (var cell in _noiseCells) { _noise.SetTile(cell, null); }
            _noiseCells.Clear();
            _noiseShown = -1;
        }
    }
}
