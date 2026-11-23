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
        [SerializeField] private Animator _animator;

        // モーションの再生位置（クリップの正規化時間）。実時間では進めず、Core の状態から毎回決める（撮影で同じ絵になる）。
        [Tooltip("走りの 1 周期で進む距離（m）")]
        [SerializeField] private float _runStride = 2.6f;
        [Tooltip("ジャンプのクリップのうち、空中の間に再生する範囲")]
        [SerializeField] private Vector2 _jumpWindow = new(.25f, .75f);
        [Tooltip("スライドのクリップのうち、スライドの間に再生する範囲")]
        [SerializeField] private Vector2 _slideWindow = new(.1f, .75f);
        [Tooltip("被弾（倒れる）のクリップの長さ（秒）。生成スクリプトがクリップから書き込む")]
        [SerializeField] private float _crashLength = 3.6f;
        [Tooltip("被弾のクリップのうち再生する範囲の終わり（寝そべったところで止め、起き上がりは見せない）。生成スクリプトが頭の高さから求めて書き込む")]
        [SerializeField] private float _crashEnd = .7f;
        [Tooltip("空中でぶつかったとき、地面に落ちるまでの被弾のクリップの割合")]
        [SerializeField] private float _crashFall = .2f;
        [Tooltip("ジャンプ・スライドから走りへ戻るブレンドの Tick 数")]
        [SerializeField] private int _recoverTicks = 12;
        [SerializeField] private GameObject _wallTemplate;
        [SerializeField] private GameObject _hurdleTemplate;
        [SerializeField] private GameObject _beamTemplate;
        [SerializeField] private GameObject _segmentTemplate;

        // 自機が通り過ぎた障害物（奥の面が自機の中心より後ろ）は、カメラと自機の間で視界をふさがないよう半透明のマテリアルに替える。
        // 種類の順（Wall・Hurdle・Beam）。当たり判定は変わらない（通り過ぎた後は触れない位置にある）。
        [SerializeField] private Material[] _passedMaterials = new Material[3];

        // 通り過ぎた障害物の描き方：Beam と自機と同じレーンのものは、カメラと自機の間に入って画面を覆うので、通り過ぎたらすぐ描かない。
        // 隣のレーンの壁・Hurdle は横で急に消えないよう半透明にし、自機よりこの距離（m）以上後ろへ流れたら描かない。
        [SerializeField] private float _hideBehind = 1.5f;
        [SerializeField] private float _segmentLength = 8f;
        [SerializeField] private int _segmentCount = 20;
        [SerializeField] private float _segmentsBehind = 16f;

        private readonly List<Transform>[] _pools = { new(), new(), new() };
        private readonly List<Transform> _segments = new();
        private readonly List<MeshRenderer>[] _renderers = { new(), new(), new() };
        private readonly List<bool>[] _passed = { new(), new(), new() };
        private readonly Material[] _aheadMaterials = new Material[3];
        private readonly int[] _shown = new int[3];
        private readonly int[] _used = new int[3];
        private RunConfig _config;

        public Transform Runner => _runner;

        public Transform Model => _model;

        public Animator Animator => _animator;

        /// <summary>倒れるモーションを止める位置（正規化時間）。</summary>
        public float CrashEnd => _crashEnd;

        public const string IdleState = "Idle";
        public const string RunState = "Run";
        public const string JumpState = "Jump";
        public const string SlideState = "Slide";
        public const string CrashState = "Crash";

        private static readonly int _idle = Animator.StringToHash(IdleState);
        private static readonly int _run = Animator.StringToHash(RunState);
        private static readonly int _jump = Animator.StringToHash(JumpState);
        private static readonly int _slide = Animator.StringToHash(SlideState);
        private static readonly int _crash = Animator.StringToHash(CrashState);

        /// <summary>今見せているモーションの状態とその位置（テスト用）。</summary>
        public (string State, float Time) Motion { get; private set; }

        /// <summary>ジャンプ・スライドから走りへ戻るブレンドの進み具合（0〜1。ブレンドしていなければ 1）。</summary>
        public float Recovery { get; private set; } = 1;

        /// <summary>ぶつかってから見せた被弾のモーションの秒数。</summary>
        public float CrashElapsed => _crashElapsed;

        // 直前に見せたジャンプ・スライドの姿勢（走りへ戻るときのブレンドの元）。試合が変わったら捨てる。
        private RunnerRun _lastRun;
        private int _lastHash;
        private float _lastTime;
        private long _lastTick = long.MinValue;
        private float _crashElapsed;

        /// <summary>障害物の種類ごとのプールの大きさ。</summary>
        public int PoolSize(ObstacleKind kind) => _pools[(int)kind].Count;

        /// <summary>プールの <paramref name="index"/> 番目の障害物（描いているものは 0 から順に詰めて使う）。</summary>
        public Transform Pooled(ObstacleKind kind, int index) => _pools[(int)kind][index];

        public Transform Segment(int index) => _segments[index];

        /// <summary>プールの障害物を描いているか（通り過ぎた Beam と自機のレーンのもの、後ろへ流れたものは描かない）。</summary>
        public bool IsVisible(ObstacleKind kind, int index) => _renderers[(int)kind][index].enabled;

        /// <summary>自機より後ろのこの距離（m）を過ぎた障害物は描かない。</summary>
        public float HideBehind => _hideBehind;

        /// <summary>プールの障害物が通り過ぎた表示（半透明）か。</summary>
        public bool IsPassedLook(ObstacleKind kind, int index) => _renderers[(int)kind][index].sharedMaterial == _passedMaterials[(int)kind];

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
            _animator.speed = 0;
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
                if (_passedMaterials == null || _passedMaterials.Length != 3 || _passedMaterials[kind] == null) { throw new InvalidOperationException("通り過ぎた障害物のマテリアルが Stage Prefab にありません。"); }
                _aheadMaterials[kind] = templates[kind].GetComponent<MeshRenderer>().sharedMaterial;
                templates[kind].SetActive(false);
                for (var i = 0; i < perChunk[kind] * _config.MaxActiveChunks; i++)
                {
                    var item = Instantiate(templates[kind], transform).transform;
                    item.name = $"{templates[kind].name} {i}";
                    _pools[kind].Add(item);
                    _renderers[kind].Add(item.GetComponent<MeshRenderer>());
                    _passed[kind].Add(false);
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
            Pose(_config.LaneX(1), 0, 0);
            Animate(_idle, IdleState, 0);
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

            if (!ReferenceEquals(run, _lastRun))
            {
                _lastRun = run;
                _lastTick = long.MinValue;
                _crashElapsed = 0;
            }

            // 被弾のモーションの進み具合。空中でぶつかったら、その間に地面へ落とす。
            var crash = Mathf.Clamp01(_crashElapsed / Mathf.Max(.01f, _crashLength) / _crashEnd);
            if (run.IsOver) { y = (long)(y * (1 - Mathf.Clamp01(crash / _crashFall))); }

            PlaceSegments(origin);
            Pose(x, y, lane.IsMoving ? lane.X - lane.PrevX : 0);
            Recovery = 1;
            if (run.IsOver) { Animate(_crash, CrashState, crash * _crashEnd); }
            else if (!vertical.IsGrounded)
            {
                // 跳んでからの Tick 数は縦の速さから決まる（初速から重力ずつ減る）。
                var airborne = (_config.JumpVelocity - vertical.Velocity) / (float)_config.Gravity;
                var airtime = (2f * _config.JumpVelocity / _config.Gravity) + 1;
                Remember(_jump, Mathf.Lerp(_jumpWindow.x, _jumpWindow.y, Mathf.Clamp01(airborne / airtime)), run.Ticks);
                Animate(_jump, JumpState, _lastTime);
            }
            else if (vertical.IsSliding)
            {
                var progress = (_config.SlideTicks - vertical.Slide.Remaining) / (float)_config.SlideTicks;
                Remember(_slide, Mathf.Lerp(_slideWindow.x, _slideWindow.y, progress), run.Ticks);
                Animate(_slide, SlideState, _lastTime);
            }
            else
            {
                // 走りの周期は進んだ距離で決める（速さが上がると脚の回転も速くなる）。
                var stride = (long)(_runStride * UnitsPerMeter);
                var time = origin % stride / (float)stride;
                var since = run.Ticks - _lastTick;
                if (since > 0 && since < _recoverTicks) { Blend(time, since / (float)_recoverTicks); }
                else { Animate(_run, RunState, time); }
            }
            PlaceObstacles(run.Chunks, origin, lane.To);
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

        private void PlaceObstacles(ChunkTrack chunks, long origin, int runnerLane)
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
                    var index = used[kind]++;
                    var item = _pools[kind][index];
                    var z = start - origin + box.Z;
                    var passed = z + box.Depth <= 0;
                    if (_passed[kind][index] != passed)
                    {
                        _passed[kind][index] = passed;
                        _renderers[kind][index].sharedMaterial = passed ? _passedMaterials[kind] : _aheadMaterials[kind];
                    }

                    var blocksView = obstacle.Kind == ObstacleKind.Beam || obstacle.Lane == runnerLane;
                    var visible = blocksView ? !passed : z + box.Depth > -(long)(_hideBehind * UnitsPerMeter);
                    if (_renderers[kind][index].enabled != visible) { _renderers[kind][index].enabled = visible; }

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

        /// <summary>自機の位置と、レーン移動の向きへの小さな傾き。姿勢はモーションが決める。</summary>
        private void Pose(long x, long y, long steer)
        {
            _runner.localPosition = new Vector3(x / UnitsPerMeter, y / UnitsPerMeter, 0);
            var lean = Mathf.Clamp(-steer, -1, 1) * 8f;
            _model.localRotation = Quaternion.Euler(0, 0, lean);
        }

        /// <summary>ぶつかった後の被弾のモーションを進める（Core は止まっているので表示の時計で進める。撮影では台本が決まった秒数を渡す）。</summary>
        public void AdvanceCrash(float seconds)
        {
            if (!float.IsFinite(seconds) || seconds < 0) { throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "0 以上の有限の値にしてください。"); }
            _crashElapsed = Mathf.Min(_crashElapsed + seconds, _crashLength * _crashEnd);
        }

        private void Remember(int hash, float time, long tick)
        {
            _lastHash = hash;
            _lastTime = time;
            _lastTick = tick;
        }

        /// <summary>
        /// 直前のジャンプ・スライドの姿勢から走りへ、<paramref name="progress"/>（0〜1）だけ移った姿勢を見せる。
        /// Animator のクロスフェードを、移り具合を指定して作る（実時間では進めない）。
        /// </summary>
        private void Blend(float time, float progress)
        {
            Motion = (RunState, time);
            Recovery = progress;
            _animator.Play(_lastHash, 0, _lastTime);
            _animator.Update(0);
            _animator.CrossFade(_run, 1f, 0, time, progress);
            _animator.Update(0);
        }

        /// <summary>状態 <paramref name="hash"/> のクリップを位置 <paramref name="time"/>（0〜1）で止めて見せる。</summary>
        private void Animate(int hash, string state, float time)
        {
            Motion = (state, time);
            _animator.Play(hash, 0, time);
            _animator.Update(0);
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
