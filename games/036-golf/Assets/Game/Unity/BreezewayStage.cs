using System;
using System.Collections.Generic;
using Game.Core;
using IntGeometry;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>1 ホールの静止した部品（Stage Prefab の中）。</summary>
    [Serializable]
    public sealed class HoleView
    {
        public GameObject Root;
        public SpriteRenderer Ground;
        public SpriteRenderer Tee;
        public SpriteRenderer Cup;
        public SpriteRenderer[] Trees = Array.Empty<SpriteRenderer>();

        /// <summary>グリーンの傾きの矢印の親（グリーンを見る表示のときだけ出す）。</summary>
        public GameObject Slopes;

        public SpriteRenderer[] Arrows = Array.Empty<SpriteRenderer>();
    }

    /// <summary>
    /// コースの描画（真上から。1 単位 = 1 m、ホールの左下の角が原点）。
    /// ホールごとの地面・木・ティー・カップ・傾きの矢印は Stage Prefab（<c>BreezewayStage.prefab</c>）にコースのデータから置いておき（プレイ前から Scene ビューで見える）、
    /// 試合中は今のホールだけを出し、動く物（球と影・旗・予測線・実際の軌跡・着地の印）だけを更新する。
    /// 置き場所は <see cref="Layout"/> が決め、<see cref="Mismatch"/> も同じ関数の値と照合する（023・027 の知見）。
    /// 球の高さは影から画面の上へずらして描く。補間せず、その Tick の状態をそのまま描く。
    /// </summary>
    public sealed class BreezewayStage : MonoBehaviour
    {
        /// <summary>予測線・実際の軌跡の点の数（それぞれ）。</summary>
        public const int DotCount = 140;

        /// <summary>傾きの矢印を置く間隔（m）。</summary>
        public const int ArrowSpacing = 3;

        /// <summary>球・点・印の大きさ（カメラの縦の半分に対する割合）。</summary>
        public const float BallShare = .016f;

        public const float DotShare = .007f;

        public const float FlagShare = .09f;

        /// <summary>グリーンを見る表示の縦の半分（m）の下限。</summary>
        public const float GreenViewSize = 11f;

        private const float Tolerance = 1e-3f;

        private static readonly Color _predicted = new(1f, 1f, 1f, .85f);
        private static readonly Color _trail = new(1f, .62f, .2f, .95f);
        private static readonly Color _ideal = new(1f, 1f, 1f, .45f);

        [SerializeField] private HoleView[] _holes = Array.Empty<HoleView>();
        [SerializeField] private SpriteRenderer _ball;
        [SerializeField] private SpriteRenderer _shadow;
        [SerializeField] private SpriteRenderer _flag;
        [SerializeField] private SpriteRenderer _predictedLanding;
        [SerializeField] private SpriteRenderer _actualLanding;
        [SerializeField] private SpriteRenderer[] _predictedDots = Array.Empty<SpriteRenderer>();
        [SerializeField] private SpriteRenderer[] _trailDots = Array.Empty<SpriteRenderer>();

        private Vec2i[] _points = new Vec2i[1];
        private int[] _heights = new int[1];
        private Vec2i[] _trailPoints = new Vec2i[1];
        private int[] _trailHeights = new int[1];
        private GolfConfig _config;
        private float _heightScale = .5f;

        /// <summary>予測線のキャッシュの鍵（構え・パワー・位置・ホールが変わったら描き直す）。</summary>
        private PreviewKey _shownPreview;
        private int _previewCount;
        private bool _previewLanded;
        private Vec2i _previewLanding;

        public int HoleCount => _holes.Length;

        public HoleView Hole(int index) => _holes[index];

        public SpriteRenderer Ball => _ball;

        public SpriteRenderer Shadow => _shadow;

        public SpriteRenderer Flag => _flag;

        public SpriteRenderer PredictedLanding => _predictedLanding;

        public SpriteRenderer ActualLanding => _actualLanding;

        public SpriteRenderer PredictedDot(int index) => _predictedDots[index];

        public SpriteRenderer TrailDot(int index) => _trailDots[index];

        /// <summary>描いている予測線の点の数（最後に計算した軌跡の点の数）。</summary>
        public int PreviewCount => _previewCount;

        /// <summary>最後に描いた予測線の点（Core の座標）。</summary>
        public Vec2i PreviewPoint(int index) => _points[index];

        /// <summary>コースの座標 → Stage の座標（m）。</summary>
        public static Vector3 ToLocal(Vec2i point) => new((float)point.X / Units.PerMeter, (float)point.Y / Units.PerMeter, 0);

        /// <summary>高さを画面の上へずらした位置。</summary>
        public static Vector3 Lifted(Vec2i point, int z, float heightScale) => ToLocal(point) + new Vector3(0, (float)z / Units.PerMeter * heightScale, 0);

        /// <summary>描画に使う設定を受け取る（毎フレーム設定の複製を作らないよう、起動時に 1 回）。</summary>
        public void Configure(GolfConfig config, float heightScale)
        {
            _config = config?.Copy() ?? throw new ArgumentNullException(nameof(config));
            _heightScale = heightScale;
            var length = config.MaxShotTicks + 1;
            _points = new Vec2i[length];
            _heights = new int[length];
            _trailPoints = new Vec2i[length];
            _trailHeights = new int[length];
            _shownPreview = default;
        }

        /// <summary>
        /// 静止した部品をコースのデータどおりに置く（シーン生成が Prefab を作るときに使う）。
        /// </summary>
        public void Layout(Course course, GolfConfig config)
        {
            if (course == null) { throw new ArgumentNullException(nameof(course)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            for (var i = 0; i < course.Holes.Count; i++)
            {
                var hole = course.Holes[i];
                var view = _holes[i];
                Place(view.Ground.transform, Vector3.zero, 0, Vector3.one);
                Place(view.Tee.transform, ToLocal(hole.Tee), 0, Vector3.one);
                Place(view.Cup.transform, ToLocal(hole.Cup), 0, CircleScale(view.Cup.sprite, config.CupRadius));
                for (var t = 0; t < hole.Trees.Count; t++)
                {
                    var tree = hole.Trees[t];
                    Place(view.Trees[t].transform, ToLocal(tree.Center), 0, CircleScale(view.Trees[t].sprite, tree.Radius));
                }

                var arrows = Arrows(hole);
                for (var a = 0; a < arrows.Count; a++)
                {
                    var (position, angle, length) = arrows[a];
                    Place(view.Arrows[a].transform, position, angle, new Vector3(length, length, 1));
                }
            }
        }

        /// <summary>
        /// 傾きの矢印：グリーンのマスのうち <see cref="ArrowSpacing"/> m ごとのマスで、傾きがあるところ。
        /// 位置（マスの中心）・向き（度、低いほうへ）・大きさ（傾きの大きさに比例、最大で 1）。
        /// </summary>
        public static List<(Vector3 Position, float Angle, float Length)> Arrows(Game.Core.Hole hole)
        {
            if (hole == null) { throw new ArgumentNullException(nameof(hole)); }
            var arrows = new List<(Vector3, float, float)>();
            for (var y = 1; y < hole.Height; y += ArrowSpacing)
            {
                for (var x = 1; x < hole.Width; x += ArrowSpacing)
                {
                    if (hole.SurfaceOfCell(x, y) != Surface.Green) { continue; }
                    var grade = hole.GradeOfCell(x, y);
                    if (grade.IsZero) { continue; }
                    var magnitude = Mathf.Sqrt((float)grade.LengthSquared);
                    var angle = Mathf.Atan2(grade.Y, grade.X) * Mathf.Rad2Deg;
                    var length = .45f + (.55f * Mathf.Clamp01(magnitude / Game.Core.Hole.MaxGrade));
                    arrows.Add((new Vector3(x + .5f, y + .5f, 0), angle, length));
                }
            }

            return arrows;
        }

        /// <summary>
        /// カメラの置き場所と縦の半分の大きさ。全体の表示はホール全体（と HUD の余白）、グリーンの表示はカップと球の周り。
        /// </summary>
        public static (Vector3 Center, float Size) Frame(Game.Core.Hole hole, bool greenView, Vec2i ball, float aspect)
        {
            if (hole == null) { throw new ArgumentNullException(nameof(hole)); }
            if (!(aspect > 0)) { aspect = 16f / 9f; }
            if (greenView)
            {
                var cup = ToLocal(hole.Cup);
                var lie = ToLocal(ball);
                var center = (cup + lie) / 2;
                var span = Mathf.Max(Mathf.Abs(cup.x - lie.x) / aspect, Mathf.Abs(cup.y - lie.y));
                return (new Vector3(center.x, center.y, -10), Mathf.Max(GreenViewSize, (span / 2) + 6));
            }

            // 上下に HUD の帯（縦の 18% ずつ）、左右に少しの余白を残す。
            var size = Mathf.Max(hole.Height / 2f / .64f, hole.Width / 2f / aspect * 1.04f);
            return (new Vector3(hole.Width / 2f, hole.Height / 2f, -10), size);
        }

        /// <summary>
        /// ラウンドの状態を描く。<paramref name="round"/> が null（タイトル）なら <paramref name="titleHole"/> のホールだけを出す。
        /// <paramref name="cameraSize"/> は今のカメラの縦の半分（球・点・旗の見た目の大きさを画面に対して一定にする）。
        /// </summary>
        public void Render(GolfRound round, Course course, int titleHole, float cameraSize)
        {
            if (course == null) { throw new ArgumentNullException(nameof(course)); }
            var holeIndex = round?.HoleIndex ?? titleHole;
            for (var i = 0; i < _holes.Length; i++)
            {
                if (_holes[i].Root.activeSelf != (i == holeIndex)) { _holes[i].Root.SetActive(i == holeIndex); }
            }

            var hole = course.Holes[holeIndex];
            var ballScale = Vector3.one * (cameraSize * BallShare * 2 / Mathf.Max(.01f, _ball.sprite.bounds.size.x));
            _ball.transform.localScale = ballScale;
            _shadow.transform.localScale = ballScale;
            _flag.transform.localPosition = ToLocal(hole.Cup);
            _flag.transform.localScale = Vector3.one * (cameraSize * FlagShare / Mathf.Max(.01f, _flag.sprite.bounds.size.y));
            var dotScale = Vector3.one * (cameraSize * DotShare * 2 / Mathf.Max(.01f, _predictedDots.Length > 0 ? _predictedDots[0].sprite.bounds.size.x : 1));
            var markScale = Vector3.one * (cameraSize * DotShare * 6 / Mathf.Max(.01f, _predictedLanding.sprite.bounds.size.x));
            _predictedLanding.transform.localScale = markScale;
            _actualLanding.transform.localScale = markScale;

            if (round == null || _config == null)
            {
                Show(hole.Tee, 0, true);
                HideDots(_predictedDots, 0);
                HideDots(_trailDots, 0);
                _predictedLanding.enabled = false;
                _actualLanding.enabled = false;
                return;
            }

            var ball = round.Ball;
            switch (round.Phase)
            {
                case RoundPhase.Aiming:
                case RoundPhase.Swinging:
                    Show(round.Lie, 0, true);
                    DrawPreview(round, dotScale);
                    HideDots(_trailDots, 0);
                    _actualLanding.enabled = false;
                    break;
                case RoundPhase.Moving:
                case RoundPhase.Settling:
                case RoundPhase.Penalty:
                    Show(ball.Position, ball.Z, ball.Phase != BallPhase.Holed);
                    DrawShot(round, dotScale);
                    break;
                default:
                    Show(ball.Position, 0, ball.Phase != BallPhase.Holed);
                    HideDots(_predictedDots, 0);
                    HideDots(_trailDots, 0);
                    _predictedLanding.enabled = false;
                    _actualLanding.enabled = false;
                    break;
            }
        }

        private void Show(Vec2i position, int z, bool visible)
        {
            _ball.enabled = visible;
            _shadow.enabled = visible;
            _shadow.transform.localPosition = ToLocal(position);
            _ball.transform.localPosition = Lifted(position, z, _heightScale);
        }

        /// <summary>構えの予測線（打った後の軌跡と同じ関数）。構え・パワーが変わったときだけ計算し直す。</summary>
        private void DrawPreview(GolfRound round, Vector3 dotScale)
        {
            var meter = round.MeterState;
            var power = meter is { PowerSet: true } ? meter.Value.Power : 1000;
            var key = new PreviewKey(round, false, round.Lie, round.Club, round.Aim, round.ImpactX, round.ImpactY, power);
            if (!key.Equals(_shownPreview))
            {
                _shownPreview = key;
                var preview = round.Preview(_points, _heights);
                _previewCount = preview.Count;
                _previewLanded = preview.Landed && round.Club != Clubs.Putter;
                _previewLanding = preview.Landing;
                DrawDots(_predictedDots, _points, _heights, _previewCount, _predicted, dotScale);
            }
            else
            {
                ScaleDots(_predictedDots, dotScale);
            }

            _predictedLanding.enabled = _previewLanded;
            if (_previewLanded) { _predictedLanding.transform.localPosition = ToLocal(_previewLanding); }
        }

        /// <summary>打った後：ずれ 0 の予測（薄く）と、実際の軌跡（打ち出しから今の Tick まで）。</summary>
        private void DrawShot(GolfRound round, Vector3 dotScale)
        {
            var setup = round.LastSetup ?? default;
            var key = new PreviewKey(round, true, round.PreviousLie, setup.Club, setup.Aim, setup.ImpactX, setup.ImpactY, setup.Power);
            if (!key.Equals(_shownPreview))
            {
                _shownPreview = key;
                var ideal = round.TraceLastShot(true, _points, _heights, _config.MaxShotTicks);
                _previewCount = ideal.Count;
                _previewLanded = ideal.End.Landed && round.LastSetup?.Club != Clubs.Putter;
                _previewLanding = ideal.End.Landing;
                DrawDots(_predictedDots, _points, _heights, _previewCount, _ideal, dotScale);
            }
            else
            {
                ScaleDots(_predictedDots, dotScale);
            }

            _predictedLanding.enabled = _previewLanded;
            if (_previewLanded) { _predictedLanding.transform.localPosition = ToLocal(_previewLanding); }

            // 実際の軌跡は打ち出しから今の Tick まで、毎フレーム同じ関数で進め直す（割り当てなし）。
            var trail = round.TraceLastShot(false, _trailPoints, _trailHeights, round.Ball.Ticks);
            DrawDots(_trailDots, _trailPoints, _trailHeights, trail.Count, _trail, dotScale);
            _actualLanding.enabled = round.Ball.Landed && round.LastSetup?.Club != Clubs.Putter;
            if (_actualLanding.enabled) { _actualLanding.transform.localPosition = ToLocal(round.Ball.Landing); }
        }

        private void DrawDots(SpriteRenderer[] dots, Vec2i[] points, int[] heights, int count, Color color, Vector3 scale)
        {
            var stride = Mathf.Max(1, Mathf.CeilToInt((float)count / dots.Length));
            var used = 0;
            for (var i = 0; i < count && used < dots.Length; i += stride)
            {
                var dot = dots[used++];
                dot.enabled = true;
                dot.color = color;
                dot.transform.localPosition = Lifted(points[i], heights[i], _heightScale);
                dot.transform.localScale = scale;
            }

            HideDots(dots, used);
        }

        private static void ScaleDots(SpriteRenderer[] dots, Vector3 scale)
        {
            foreach (var dot in dots)
            {
                if (dot.enabled) { dot.transform.localScale = scale; }
            }
        }

        private static void HideDots(SpriteRenderer[] dots, int from)
        {
            for (var i = from; i < dots.Length; i++)
            {
                if (dots[i].enabled) { dots[i].enabled = false; }
            }
        }

        /// <summary>Stage Prefab がコースのデータどおりに置かれ、描画に必要な部品と絵をそろえているか。そろっていれば null、違えば理由。</summary>
        public string Mismatch(Course course, GolfConfig config)
        {
            if (course == null) { throw new ArgumentNullException(nameof(course)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            if (_holes.Length != course.Holes.Count) { return $"ホールの置き場がコースの {course.Holes.Count} つと違います（{_holes.Length} つ）。"; }
            foreach (var part in new[] { _ball, _shadow, _flag, _predictedLanding, _actualLanding })
            {
                if (part == null || part.sprite == null) { return "球・影・旗・着地の印のどれかの絵がありません。"; }
                if (part.transform.parent != transform) { return $"{part.name} が Stage の直下にありません。"; }
            }

            if (_predictedDots.Length != DotCount || _trailDots.Length != DotCount) { return $"予測線・軌跡の点は {DotCount} 個ずつにしてください。"; }
            foreach (var dot in _predictedDots)
            {
                if (dot == null || dot.sprite == null || dot.transform.parent != transform) { return "予測線の点の置き場か絵がないか、Stage の直下にありません。"; }
            }

            foreach (var dot in _trailDots)
            {
                if (dot == null || dot.sprite == null || dot.transform.parent != transform) { return "軌跡の点の置き場か絵がないか、Stage の直下にありません。"; }
            }

            for (var i = 0; i < course.Holes.Count; i++)
            {
                var problem = HoleMismatch(_holes[i], course.Holes[i], config);
                if (problem != null) { return $"{i + 1} 番ホール：{problem}"; }
            }

            return null;
        }

        private string HoleMismatch(HoleView view, Game.Core.Hole hole, GolfConfig config)
        {
            if (view == null || view.Root == null || view.Root.transform.parent != transform) { return "ホールの親がないか、Stage の直下にありません。"; }
            if (!Upright(view.Root.transform) || !Near(view.Root.transform.localPosition, Vector3.zero)) { return "ホールの親の位置・回転・大きさが変わっています。"; }
            if (view.Ground == null || view.Ground.sprite == null) { return "地面の絵がありません。"; }

            // 地面の絵は左下の角が基点で、1 単位 = 1 m の大きさ。
            var bounds = view.Ground.sprite.bounds;
            if (!Near((Vector2)bounds.min, Vector2.zero) || !Near((Vector2)bounds.size, new Vector2(hole.Width, hole.Height))) { return $"地面の絵の大きさか基点がホール（{hole.Width} × {hole.Height} m）と違います。"; }
            if (!Upright(view.Ground.transform) || !Near(view.Ground.transform.localPosition, Vector3.zero)) { return "地面の位置・回転・大きさが変わっています。"; }
            if (view.Tee == null || view.Tee.sprite == null || !Near(view.Tee.transform.localPosition, ToLocal(hole.Tee))) { return "ティーの印がないか、位置がコースと違います。"; }
            if (view.Cup == null || view.Cup.sprite == null || !Near(view.Cup.transform.localPosition, ToLocal(hole.Cup))) { return "カップがないか、位置がコースと違います。"; }
            if (!Near(view.Cup.transform.localScale, CircleScale(view.Cup.sprite, config.CupRadius))) { return "カップの大きさが設定の半径と違います。"; }
            if (view.Trees.Length != hole.Trees.Count) { return $"木の置き場がコースの {hole.Trees.Count} 本と違います（{view.Trees.Length} 本）。"; }
            for (var t = 0; t < hole.Trees.Count; t++)
            {
                var renderer = view.Trees[t];
                var tree = hole.Trees[t];
                if (renderer == null || renderer.sprite == null) { return $"{t + 1} 本目の木の絵がありません。"; }
                if (!Near(renderer.transform.localPosition, ToLocal(tree.Center))) { return $"{t + 1} 本目の木の位置がコースと違います。"; }
                if (!Near(renderer.transform.localScale, CircleScale(renderer.sprite, tree.Radius))) { return $"{t + 1} 本目の木の大きさがコースと違います。"; }
            }

            if (view.Slopes == null) { return "傾きの矢印の親がありません。"; }
            var arrows = Arrows(hole);
            if (view.Arrows.Length != arrows.Count) { return $"傾きの矢印がコースの {arrows.Count} 本と違います（{view.Arrows.Length} 本）。"; }
            for (var a = 0; a < arrows.Count; a++)
            {
                var renderer = view.Arrows[a];
                var (position, angle, length) = arrows[a];
                if (renderer == null || renderer.sprite == null) { return $"{a + 1} 本目の傾きの矢印の絵がありません。"; }
                if (!Near(renderer.transform.localPosition, position) || Mathf.Abs(Mathf.DeltaAngle(renderer.transform.localEulerAngles.z, angle)) > .01f ||
                    !Near(renderer.transform.localScale, new Vector3(length, length, 1)))
                {
                    return $"{a + 1} 本目の傾きの矢印がコースの傾きと違います。";
                }
            }

            return null;
        }

        /// <summary>円の絵を半径に合わせる拡大率（絵の直径を半径の 2 倍にする）。</summary>
        public static Vector3 CircleScale(Sprite sprite, int radius)
        {
            if (sprite == null) { return Vector3.one; }
            var scale = 2f * radius / Units.PerMeter / sprite.bounds.size.x;
            return new Vector3(scale, scale, 1);
        }

        private static void Place(Transform part, Vector3 position, float angle, Vector3 scale)
        {
            part.localPosition = position;
            part.localRotation = Quaternion.Euler(0, 0, angle);
            part.localScale = scale;
        }

        private static bool Near(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < Tolerance * Tolerance;

        private static bool Near(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < Tolerance * Tolerance;

        private static bool Upright(Transform part) =>
            Quaternion.Angle(part.localRotation, Quaternion.identity) < .01f && Near(part.localScale, Vector3.one);
    }

    /// <summary>
    /// 予測線のキャッシュの鍵。要素の多い ValueTuple は Mono で比べると入れ子の部分が箱に入って割り当てが起きるので、自前の値型にする。
    /// </summary>
    internal readonly struct PreviewKey : IEquatable<PreviewKey>
    {
        private readonly GolfRound _round;
        private readonly bool _shot;
        private readonly int _hole;
        private readonly int _strokes;
        private readonly Vec2i _lie;
        private readonly int _club;
        private readonly int _aim;
        private readonly int _impactX;
        private readonly int _impactY;
        private readonly int _power;

        public PreviewKey(GolfRound round, bool shot, Vec2i lie, int club, int aim, int impactX, int impactY, int power)
        {
            _round = round;
            _shot = shot;
            _hole = round.HoleIndex;
            _strokes = round.Strokes;
            _lie = lie;
            _club = club;
            _aim = aim;
            _impactX = impactX;
            _impactY = impactY;
            _power = power;
        }

        public bool Equals(PreviewKey other) =>
            ReferenceEquals(_round, other._round) && _shot == other._shot && _hole == other._hole && _strokes == other._strokes && _lie == other._lie &&
            _club == other._club && _aim == other._aim && _impactX == other._impactX && _impactY == other._impactY && _power == other._power;

        public override bool Equals(object obj) => obj is PreviewKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(_hole, _strokes, _lie, _club, _aim, _power);
    }
}
