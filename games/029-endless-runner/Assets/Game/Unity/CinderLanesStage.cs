using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 道・自機・障害物の描画。<b>自機の奥行きを原点（z = 0）にして描く</b>：Core の long の距離から
    /// 「障害物の z − 自機の z」を作ってから float にするので、長く走っても位置の精度が落ちない。
    /// 障害物と道の区画は Stage Prefab の見本から作った固定数のプールを置き直す（試合中に生成・破棄しない）。
    /// </summary>
    public sealed class CinderLanesStage : MonoBehaviour
    {
        /// <summary>Core の 1 m の単位数。</summary>
        public const float UnitsPerMeter = 256f;

        [SerializeField] private Transform _runner;
        [SerializeField] private Transform _model;
        [SerializeField] private GameObject _wallTemplate;
        [SerializeField] private GameObject _hurdleTemplate;
        [SerializeField] private GameObject _beamTemplate;
        [SerializeField] private GameObject _segmentTemplate;
        [SerializeField] private float _segmentLength = 8f;
        [SerializeField] private int _segmentCount = 20;
        [SerializeField] private float _segmentsBehind = 16f;

        private readonly List<Transform>[] _pools = { new(), new(), new() };
        private readonly List<Transform> _segments = new();
        private readonly int[] _shown = new int[3];
        private readonly int[] _used = new int[3];
        private RunConfig _config;

        public Transform Runner => _runner;

        public Transform Model => _model;

        /// <summary>障害物の種類ごとのプールの大きさ。</summary>
        public int PoolSize(ObstacleKind kind) => _pools[(int)kind].Count;

        /// <summary>プールの <paramref name="index"/> 番目の障害物（描いているものは 0 から順に詰めて使う）。</summary>
        public Transform Pooled(ObstacleKind kind, int index) => _pools[(int)kind][index];

        public Transform Segment(int index) => _segments[index];

        /// <summary>今描いている障害物の数。</summary>
        public int Shown(ObstacleKind kind) => _shown[(int)kind];

        public int SegmentCount => _segments.Count;

        /// <summary>道の区画の長さ（m）。</summary>
        public float SegmentLength => _segmentLength;

        /// <summary>
        /// プールを作る。大きさは「生きているチャンクの上限 × 1 チャンクに含まれうるその種類の最大数」で、
        /// Core のリングが上限を超えない限り足りる。見本は隠す（Scene ビューでの配置の確認用）。
        /// </summary>
        public void Initialize(RunRules rules)
        {
            if (rules == null) { throw new ArgumentNullException(nameof(rules)); }
            if (_config != null) { throw new InvalidOperationException("Stage は 1 度だけ初期化します。"); }
            _config = rules.Config;
            var templates = new[] { _wallTemplate, _hurdleTemplate, _beamTemplate };
            var perChunk = new int[3];
            foreach (var chunk in rules.Chunks)
            {
                var counts = new int[3];
                foreach (var obstacle in chunk.Obstacles) { counts[(int)obstacle.Kind]++; }
                for (var kind = 0; kind < 3; kind++) { perChunk[kind] = Math.Max(perChunk[kind], counts[kind]); }
            }

            for (var kind = 0; kind < 3; kind++)
            {
                templates[kind].SetActive(false);
                for (var i = 0; i < perChunk[kind] * _config.MaxActiveChunks; i++)
                {
                    var item = Instantiate(templates[kind], transform).transform;
                    item.name = $"{templates[kind].name} {i}";
                    _pools[kind].Add(item);
                }
            }

            _segments.Add(_segmentTemplate.transform);
            for (var i = 1; i < _segmentCount; i++)
            {
                var item = Instantiate(_segmentTemplate, transform).transform;
                item.name = $"{_segmentTemplate.name} {i}";
                _segments.Add(item);
            }
        }

        /// <summary>試合の前：中央のレーンに立った自機と道だけ。</summary>
        public void RenderInitial()
        {
            PlaceSegments(0);
            for (var kind = 0; kind < 3; kind++) { Hide(kind, 0); }
            Pose(_config.LaneX(1), 0, 0, false);
        }

        /// <summary>
        /// その Tick の状態をそのまま描く（補間しない）。ぶつかった試合は、触れた時刻（<see cref="RunnerRun.CrashTime"/>）の位置で止めて描く。
        /// </summary>
        public void Render(RunnerRun run)
        {
            if (run == null) { throw new ArgumentNullException(nameof(run)); }
            var lane = run.Body.Lane;
            var vertical = run.Body.Vertical;
            long origin;
            long x;
            long y;
            if (run.IsOver)
            {
                origin = run.CrashTime.Lerp(run.PrevZ, run.Z);
                x = run.CrashTime.Lerp(lane.PrevX, lane.X);
                y = run.CrashTime.Lerp(vertical.PrevY, vertical.Y);
            }
            else
            {
                origin = run.Z;
                x = lane.X;
                y = vertical.Y;
            }

            PlaceSegments(origin);
            Pose(x, y, lane.IsMoving ? lane.X - lane.PrevX : 0, vertical.IsSliding);
            PlaceObstacles(run.Chunks, origin);
        }

        private void PlaceSegments(long origin)
        {
            // 区画の境目は道の上の決まった位置（区画の長さの倍数）にあり、自機が進むと手前へ流れる。
            var length = (long)(_segmentLength * UnitsPerMeter);
            var phase = origin % length;
            var first = -(long)(_segmentsBehind * UnitsPerMeter) - phase;
            for (var i = 0; i < _segments.Count; i++)
            {
                _segments[i].localPosition = new Vector3(0, 0, (first + (i * length)) / UnitsPerMeter);
            }
        }

        private void PlaceObstacles(ChunkTrack chunks, long origin)
        {
            var used = _used;
            Array.Clear(used, 0, used.Length);
            for (var i = 0; i < chunks.Count; i++)
            {
                var start = chunks.StartAt(i);
                var obstacles = chunks.ChunkAt(i).Obstacles;
                for (var o = 0; o < obstacles.Count; o++)
                {
                    var obstacle = obstacles[o];
                    var box = obstacle.Box(_config);
                    var kind = (int)obstacle.Kind;
                    var item = _pools[kind][used[kind]++];
                    var z = start - origin + box.Z;
                    item.localPosition = new Vector3((box.X + (box.Width / 2f)) / UnitsPerMeter, (box.Y + (box.Height / 2f)) / UnitsPerMeter, (z + (box.Depth / 2f)) / UnitsPerMeter);
                    item.localScale = new Vector3(box.Width / UnitsPerMeter, box.Height / UnitsPerMeter, box.Depth / UnitsPerMeter);
                    if (!item.gameObject.activeSelf) { item.gameObject.SetActive(true); }
                }
            }

            for (var kind = 0; kind < 3; kind++) { Hide(kind, used[kind]); }
        }

        private void Hide(int kind, int from)
        {
            var pool = _pools[kind];
            for (var i = from; i < pool.Count; i++)
            {
                if (pool[i].gameObject.activeSelf) { pool[i].gameObject.SetActive(false); }
            }

            _shown[kind] = from;
        }

        /// <summary>
        /// 自機の姿：位置とレーン移動の傾き。スライド中は足を前にして後ろへ 65° 倒す
        /// （立った高さ 1.75 m の 0.42 倍で、スライドの当たり箱の高さ 0.625 m とほぼ同じ）。
        /// </summary>
        private void Pose(long x, long y, long steer, bool sliding)
        {
            _runner.localPosition = new Vector3(x / UnitsPerMeter, y / UnitsPerMeter, 0);
            var lean = Mathf.Clamp(-steer, -1, 1) * 12f;
            _model.localRotation = Quaternion.Euler(sliding ? -65f : 0, 0, lean);
        }

        /// <summary>
        /// Prefab の道の幅が設定のレーンと合っているか（合っていなければ理由）。道の区画の中の "Road" の幅を見る。
        /// </summary>
        public string Mismatch(RunConfig config)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            var road = _segmentTemplate != null ? _segmentTemplate.transform.Find("Road") : null;
            if (road == null) { return "道の区画に Road がありません。"; }
            var width = RunConfig.Lanes * config.LaneWidth / UnitsPerMeter;
            if (Mathf.Abs(road.localScale.x - width) > .001f) { return $"道の幅 {road.localScale.x} m が 3 レーンの幅 {width} m と違います。"; }
            if (Mathf.Abs(road.localScale.z - _segmentLength) > .001f) { return $"道の区画の長さ {road.localScale.z} m が {_segmentLength} m と違います。"; }
            return (_segmentCount - 1) * _segmentLength < _segmentsBehind + config.LookAheadMeters
                ? $"道の区画 {_segmentCount} 個では後方 {_segmentsBehind} m から前方 {config.LookAheadMeters} m まで覆えません。"
                : null;
        }
    }
}
