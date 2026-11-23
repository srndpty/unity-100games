using System;
using Game.Core;
using IntGeometry;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 警備員 1 人の描画（体・視界の扇・状態の記号・気づきのゲージ、F1 の詳しい表示：巡回路・向かう先・最後に見た位置）。
    /// 扇の Mesh はこの部品が作って持ち、壊すときに消す。
    /// </summary>
    public sealed class HushWardGuardView : MonoBehaviour
    {
        /// <summary>歩く姿の 1 コマの Tick 数。</summary>
        public const int FrameTicks = 10;

        [SerializeField] private SpriteRenderer _body;
        [SerializeField] private MeshFilter _cone;
        [SerializeField] private MeshRenderer _coneRenderer;
        [SerializeField] private SpriteRenderer _mark;
        [SerializeField] private SpriteRenderer _gaugeBack;
        [SerializeField] private SpriteRenderer _gaugeFill;
        [SerializeField] private SpriteRenderer _goal;
        [SerializeField] private SpriteRenderer _noticed;
        [SerializeField] private LineRenderer _route;
        [SerializeField] private Sprite[] _down = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] _up = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] _side = Array.Empty<Sprite>();
        [SerializeField] private Sprite _question;
        [SerializeField] private Sprite _exclaim;
        [SerializeField] private Sprite _searching;

        private Mesh _mesh;
        private Vector3[] _vertices = Array.Empty<Vector3>();
        private Color[] _colors = Array.Empty<Color>();
        private int[] _distances = Array.Empty<int>();

        public SpriteRenderer Body => _body;

        public SpriteRenderer Mark => _mark;

        public MeshRenderer ConeRenderer => _coneRenderer;

        public SpriteRenderer GaugeFill => _gaugeFill;

        public SpriteRenderer Goal => _goal;

        public LineRenderer Route => _route;

        /// <summary>今の扇の Mesh（描いた後の頂点を PlayMode で確かめる）。</summary>
        public Mesh ConeMesh => _mesh;

        /// <summary>描画に使う部品と絵（4 方向 2 コマ・記号 3 つ）がそろっているか。</summary>
        public bool IsComplete =>
            _body != null && _cone != null && _coneRenderer != null && _mark != null && _gaugeBack != null && _gaugeFill != null && _goal != null && _noticed != null && _route != null
            && _down.Length == 2 && _up.Length == 2 && _side.Length == 2 && Array.TrueForAll(_down, s => s != null) && Array.TrueForAll(_up, s => s != null) && Array.TrueForAll(_side, s => s != null)
            && _question != null && _exclaim != null && _searching != null;

        /// <summary>状態の記号の絵（巡回は null）。</summary>
        public Sprite MarkOf(GuardMode mode) => mode switch
        {
            GuardMode.Investigate => _question,
            GuardMode.Chase => _exclaim,
            GuardMode.Search => _searching,
            _ => null,
        };

        /// <summary>状態の扇の色。</summary>
        public static Color ConeColor(GuardMode mode) => mode switch
        {
            GuardMode.Investigate => new Color(1f, .62f, .2f, .48f),
            GuardMode.Chase => new Color(1f, .22f, .2f, .55f),
            GuardMode.Search => new Color(.72f, .5f, 1f, .45f),
            _ => new Color(1f, .93f, .55f, .38f),
        };

        /// <summary>試合の前の姿（巡回の始めの位置と向き、平常の視界）。</summary>
        public void RenderInitial(FacilityMap map, GuardPlan plan, VisionSpec vision, int rays)
        {
            if (plan == null) { throw new ArgumentNullException(nameof(plan)); }
            var position = plan.Start.Center;
            transform.localPosition = HushWardStage.Local(position);
            _body.sprite = SpriteFor(plan.StartFacing, 0);
            _body.flipX = FlipFor(plan.StartFacing);
            DrawCone(map, position, plan.StartFacing, vision, rays, GuardMode.Patrol, false);
            _mark.enabled = false;
            _gaugeBack.enabled = false;
            _gaugeFill.enabled = false;
            ShowDebug(false, null, plan, null, null);
        }

        public void Render(Infiltration match, Guard guard, int rays, bool debug)
        {
            if (match == null) { throw new ArgumentNullException(nameof(match)); }
            if (guard == null) { throw new ArgumentNullException(nameof(guard)); }
            var body = guard.Body;
            var brain = guard.Brain;
            transform.localPosition = HushWardStage.Local(body.Position);
            var moving = body.Position != body.PreviousPosition;
            _body.sprite = SpriteFor(body.Facing, moving ? (int)(match.Ticks / FrameTicks % 2) : 0);
            _body.flipX = FlipFor(body.Facing);
            DrawCone(match.Map, body.Position, body.Facing, match.Config.VisionAt(match.Level), rays, brain.Mode, guard.Sees);

            var mark = MarkOf(brain.Mode);
            _mark.enabled = mark != null;
            _mark.sprite = mark;

            // 気づき値は 0 より大きい間だけ、頭の上の横棒で出す。
            var awareness = brain.Awareness / (float)WardConfig.AwarenessMax;
            _gaugeBack.enabled = brain.Awareness > 0;
            _gaugeFill.enabled = brain.Awareness > 0;
            var fill = _gaugeFill.transform;
            fill.localScale = new Vector3(awareness, 1, 1);
            fill.localPosition = new Vector3(_gaugeBack.transform.localPosition.x - ((1 - awareness) * GaugeWidth / 2), fill.localPosition.y, fill.localPosition.z);
            _gaugeFill.color = brain.Awareness >= WardConfig.AwarenessMax ? new Color(1f, .3f, .25f) : Color.Lerp(new Color(1f, .9f, .4f), new Color(1f, .5f, .2f), awareness);
            ShowDebug(debug, brain.Goal, guard.Plan, brain.LastNoticed, brain.Mode == GuardMode.Search ? brain.SearchCenter : (Cell?)null);
        }

        /// <summary>ゲージの幅（ワールド単位、見本の横棒の大きさ）。</summary>
        private float GaugeWidth => _gaugeBack.sprite != null ? _gaugeBack.sprite.bounds.size.x * _gaugeBack.transform.localScale.x : 1;

        private void ShowDebug(bool debug, Cell? goal, GuardPlan plan, Vec2i? noticed, Cell? searchCenter)
        {
            _goal.enabled = debug && goal != null;
            if (goal != null) { _goal.transform.position = transform.parent.TransformPoint(HushWardStage.Local(goal.Value.Center)); }
            _noticed.enabled = debug && noticed != null;
            if (noticed != null) { _noticed.transform.position = transform.parent.TransformPoint(HushWardStage.Local(noticed.Value)); }
            _route.enabled = debug;
            if (!debug) { return; }

            // 巡回路は巡回点を順に結んだ折れ線（ワールド座標）。捜索中は捜索の中心まで線を延ばす。
            var count = plan.Route.Count + (searchCenter != null ? 1 : 0);
            _route.positionCount = count;
            for (var i = 0; i < plan.Route.Count; i++) { _route.SetPosition(i, transform.parent.TransformPoint(HushWardStage.Local(plan.Route[i].Cell.Center))); }
            if (searchCenter != null) { _route.SetPosition(count - 1, transform.parent.TransformPoint(HushWardStage.Local(searchCenter.Value.Center))); }
        }

        /// <summary>視界の扇を、判定と同じ <see cref="Vision.Fan"/> で作る（体の位置を原点にした Mesh）。</summary>
        private void DrawCone(FacilityMap map, Vec2i eye, int facing, VisionSpec vision, int rays, GuardMode mode, bool sees)
        {
            if (_mesh == null)
            {
                _mesh = new Mesh { name = $"{name}Cone" };
                _mesh.MarkDynamic();
                _cone.sharedMesh = _mesh;

                // Sprites/Default は SpriteRenderer が渡す色（_RendererColor）と反転（_Flip）を掛ける。MeshRenderer は渡さないので、
                // 直前の描画の値が残って扇が潰れたり暗くなったりする。自分で固定の値を渡す。
                var block = new MaterialPropertyBlock();
                block.SetColor("_RendererColor", Color.white);
                block.SetVector("_Flip", Vector4.one);
                _coneRenderer.SetPropertyBlock(block);
            }

            if (_distances.Length != rays)
            {
                _distances = new int[rays];
                _vertices = new Vector3[rays + 1];
                _colors = new Color[rays + 1];
                _mesh.Clear();
                var triangles = new int[(rays - 1) * 3];
                for (var i = 0; i < rays - 1; i++)
                {
                    triangles[i * 3] = 0;
                    triangles[(i * 3) + 1] = i + 2;
                    triangles[(i * 3) + 2] = i + 1;
                }

                _mesh.vertices = _vertices;
                _mesh.triangles = triangles;
            }

            Vision.Fan(map, eye, facing, vision, _distances);
            var color = ConeColor(mode);
            if (sees) { color.a = Mathf.Min(1, color.a + .12f); }
            _vertices[0] = Vector3.zero;
            _colors[0] = color;
            for (var i = 0; i < rays; i++)
            {
                var angle = Vision.FanAngle(facing, vision.HalfAngle, i, rays);
                var tip = Vision.PointAt(eye, angle, _distances[i]) - eye;
                _vertices[i + 1] = HushWardStage.Local(tip);
                _colors[i + 1] = new Color(color.r, color.g, color.b, color.a * .55f);
            }

            // 部分指定は頂点数を縮めるので、配列をそのまま渡す（027 の知見）。
            _mesh.vertices = _vertices;
            _mesh.colors = _colors;
            _mesh.RecalculateBounds();
        }

        /// <summary>向きの絵（右・上・左・下に近いほう）。</summary>
        private Sprite SpriteFor(int facing, int frame)
        {
            var quarter = Quarter(facing);
            var set = quarter == 1 ? _up : quarter == 3 ? _down : _side;
            return set.Length == 0 ? null : set[frame % set.Length];
        }

        private static bool FlipFor(int facing) => Quarter(facing) == 2;

        /// <summary>向きを 4 方向（0 右・1 上・2 左・3 下）に丸める。</summary>
        public static int Quarter(int facing) => ((Trig.Normalize(facing) + (Trig.FullTurn / 8)) / (Trig.FullTurn / 4)) % 4;

        private void OnDestroy()
        {
            if (_mesh != null) { Destroy(_mesh); }
        }
    }
}
