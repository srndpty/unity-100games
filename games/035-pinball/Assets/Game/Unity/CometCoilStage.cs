using System;
using Game.Core;
using IntGeometry;
using UnityEngine;
using Collider = Game.Core.Collider;

namespace Game.Unity
{
    /// <summary>
    /// 台の描画。静止した部品（壁・扉・バンパー・ターゲット・フリッパーの軸）は Stage Prefab（<c>CometCoilStage.prefab</c>）に
    /// Core の台データから置いておき（プレイ前から Scene ビューで見える）、試合中は動く物（球・フリッパー・プランジャー・倒れたターゲット・光るバンパー）だけを更新する。
    /// 置き場所は <see cref="Layout"/> が決め、<see cref="Mismatch"/> も同じ関数の値と照合する（023・027 の知見）。
    /// 台の座標と Stage の座標は <see cref="ToLocal"/> で変換する（1 マス = 1 単位、台の中心が原点）。補間せず、その Tick の状態をそのまま描く。
    /// </summary>
    public sealed class CometCoilStage : MonoBehaviour
    {
        public const string RailSpriteName = "Rail";
        public const string GateSpriteName = "Gate";

        /// <summary>引ききったときに球とプランジャーを下げる距離（Stage の単位）。</summary>
        public const float PullTravel = .9f;

        private const float Tolerance = 1e-3f;

        private static readonly Color _lit = new(1f, .95f, .7f, 1f);
        private static readonly Color _down = new(1f, 1f, 1f, .22f);

        [SerializeField] private SpriteRenderer _background;
        [SerializeField] private SpriteRenderer[] _rails = Array.Empty<SpriteRenderer>();
        [SerializeField] private SpriteRenderer[] _bumpers = Array.Empty<SpriteRenderer>();
        [SerializeField] private SpriteRenderer[] _targets = Array.Empty<SpriteRenderer>();
        [SerializeField] private SpriteRenderer _leftFlipper;
        [SerializeField] private SpriteRenderer _rightFlipper;
        [SerializeField] private SpriteRenderer[] _balls = Array.Empty<SpriteRenderer>();
        [SerializeField] private SpriteRenderer _plunger;

        /// <summary>まだ光っていないバンパーの印（引き算で桁あふれさせないよう、比べる前に除く）。</summary>
        private const long NeverLit = -1;

        private readonly long[] _litAt = { NeverLit, NeverLit, NeverLit };
        private PinballRun _shownRun;
        private int _plungerMaxTicks = 1;
        private float _ballRadius = .5f;

        public SpriteRenderer Background => _background;

        public SpriteRenderer LeftFlipper => _leftFlipper;

        public SpriteRenderer RightFlipper => _rightFlipper;

        public SpriteRenderer Plunger => _plunger;

        public int RailCount => _rails.Length;

        public SpriteRenderer Rail(int index) => _rails[index];

        public SpriteRenderer Bumper(int number) => _bumpers[number];

        public SpriteRenderer Target(int number) => _targets[number];

        public SpriteRenderer Ball(int slot) => _balls[slot];

        public SpriteRenderer Flipper(FlipperSide side) => side == FlipperSide.Left ? _leftFlipper : _rightFlipper;

        /// <summary>台の座標 → Stage の座標。</summary>
        public static Vector3 ToLocal(Vec2i point) =>
            new(((float)point.X / Board.Unit) - (Board.Columns / 2f), ((float)point.Y / Board.Unit) - (Board.Rows / 2f), 0);

        /// <summary>角度（<c>Trig</c> の単位）→ 度。</summary>
        public static float Degrees(int angle) => angle * 360f / Trig.FullTurn;

        /// <summary>
        /// 静止した部品を台のデータどおりに置く（シーン生成が Prefab を作るときに使う）。
        /// 壁・扉は台のデータの順に <see cref="RailCount"/> 個、バンパー・ターゲットは番号の順。
        /// </summary>
        public void Layout(Table table, PinballConfig config)
        {
            if (table == null) { throw new ArgumentNullException(nameof(table)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            var rail = 0;
            foreach (var collider in table.Colliders)
            {
                switch (collider.Kind)
                {
                    case ColliderKind.Wall:
                    case ColliderKind.Gate:
                        PlaceSegment(_rails[rail++], collider);
                        break;
                    case ColliderKind.Bumper:
                        PlaceCircle(_bumpers[collider.Number], collider);
                        break;
                    case ColliderKind.Target:
                        PlaceSegment(_targets[collider.Number], collider);
                        break;
                }
            }

            PlaceFlipper(_leftFlipper, table.Left);
            PlaceFlipper(_rightFlipper, table.Right);
            foreach (var ball in _balls) { ball.transform.localScale = CircleScale(ball.sprite, config.BallRadius); }
            _plunger.transform.localPosition = PlungerAt(table, config, 0);
        }

        /// <summary>描画に使う設定の値を受け取る（毎フレーム設定の複製を作らないよう、起動時に 1 回）。</summary>
        public void Configure(PinballConfig config)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            _plungerMaxTicks = config.PlungerMaxTicks;
            _ballRadius = (float)config.BallRadius / Board.Unit;
        }

        /// <summary>試合の状態を描く（割り当てなし）。<paramref name="flashTicks"/> はバンパーを光らせる長さ。</summary>
        public void Render(PinballRun run, int flashTicks)
        {
            if (run == null) { throw new ArgumentNullException(nameof(run)); }
            if (!ReferenceEquals(_shownRun, run))
            {
                _shownRun = run;
                for (var i = 0; i < _litAt.Length; i++) { _litAt[i] = NeverLit; }
            }

            var pullShare = (float)run.Pull / _plungerMaxTicks;
            for (var slot = 0; slot < _balls.Length; slot++)
            {
                var ball = run.BallAt(slot);
                _balls[slot].enabled = ball.State != BallState.Idle;
                if (!_balls[slot].enabled) { continue; }
                var position = ToLocal(ball.Position);
                if (ball.State == BallState.Lane) { position.y -= pullShare * PullTravel; }
                _balls[slot].transform.localPosition = position;
            }

            _leftFlipper.transform.localRotation = Quaternion.Euler(0, 0, Degrees(run.FlipperAngle(FlipperSide.Left)));
            _rightFlipper.transform.localRotation = Quaternion.Euler(0, 0, Degrees(run.FlipperAngle(FlipperSide.Right)));
            _plunger.transform.localPosition = ToLocal(run.Table.LaneRest) + new Vector3(0, -_ballRadius - (pullShare * PullTravel), 0);

            for (var i = 0; i < _targets.Length; i++) { _targets[i].color = run.TargetUp(i) ? Color.white : _down; }
            for (var i = 0; i < _bumpers.Length; i++)
            {
                var lit = _litAt[i] != NeverLit && run.Ticks - _litAt[i] < flashTicks;
                _bumpers[i].color = lit ? _lit : Color.white;
            }
        }

        /// <summary>Tick <paramref name="tick"/> に当たったバンパー（ビット）を光らせる。</summary>
        public void Flash(int bumpers, long tick)
        {
            for (var i = 0; i < _litAt.Length; i++)
            {
                if ((bumpers & (1 << i)) != 0) { _litAt[i] = tick; }
            }
        }

        /// <summary>Stage Prefab が台のデータどおりに置かれ、描画に必要な部品と絵をそろえているか。そろっていれば null、違えば理由。</summary>
        public string Mismatch(Table table, PinballConfig config)
        {
            if (table == null) { throw new ArgumentNullException(nameof(table)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            if (_background == null || _background.sprite == null) { return "背景の絵がありません。"; }
            if (_plunger == null || _plunger.sprite == null) { return "プランジャーの絵がありません。"; }
            var rails = 0;
            foreach (var collider in table.Colliders)
            {
                if (collider.Kind == ColliderKind.Wall || collider.Kind == ColliderKind.Gate) { rails++; }
            }

            if (_rails.Length != rails) { return $"壁・扉の置き場が台の {rails} 本と違います（{_rails.Length} 本）。"; }
            if (_bumpers.Length != Table.BumperCount || _targets.Length != Table.TargetCount) { return "バンパー・ターゲットの置き場の数が違います。"; }
            if (_balls.Length != PinballConfig.MaxBalls) { return "球の置き場の数が違います。"; }

            var rail = 0;
            for (var i = 0; i < table.Colliders.Count; i++)
            {
                var collider = table.Colliders[i];
                string problem;
                switch (collider.Kind)
                {
                    case ColliderKind.Wall:
                    case ColliderKind.Gate:
                        var name = collider.Kind == ColliderKind.Gate ? GateSpriteName : RailSpriteName;
                        problem = SegmentMismatch(_rails[rail], collider) ?? (_rails[rail].sprite.name == name ? null : $"の絵が {name} ではありません");
                        rail++;
                        break;
                    case ColliderKind.Bumper:
                        problem = CircleMismatch(_bumpers[collider.Number], collider);
                        break;
                    default:
                        problem = SegmentMismatch(_targets[collider.Number], collider);
                        break;
                }

                if (problem != null) { return $"台の {i + 1} 番目の衝突物（{collider.Kind}）{problem}。"; }
            }

            var flipper = FlipperMismatch(_leftFlipper, table.Left) ?? FlipperMismatch(_rightFlipper, table.Right);
            if (flipper != null) { return flipper; }
            foreach (var ball in _balls)
            {
                if (ball == null || ball.sprite == null) { return "球の絵がありません。"; }
                if (!Near(ball.transform.localScale, CircleScale(ball.sprite, config.BallRadius))) { return "球の大きさが設定の半径と違います。"; }
            }

            return null;
        }

        /// <summary>引き（0〜1）のときのプランジャーの位置（上端がレーンの球の下に接する）。</summary>
        public static Vector3 PlungerAt(Table table, PinballConfig config, float pull)
        {
            if (table == null) { throw new ArgumentNullException(nameof(table)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            return ToLocal(table.LaneRest) + new Vector3(0, -((float)config.BallRadius / Board.Unit) - (pull * PullTravel), 0);
        }

        private static void PlaceSegment(SpriteRenderer renderer, Collider collider)
        {
            var (position, angle, size) = Segment(collider);
            renderer.drawMode = SpriteDrawMode.Sliced;
            renderer.size = size;
            renderer.transform.localPosition = position;
            renderer.transform.localRotation = Quaternion.Euler(0, 0, angle);
            renderer.transform.localScale = Vector3.one;
        }

        private static void PlaceCircle(SpriteRenderer renderer, Collider collider)
        {
            renderer.transform.localPosition = ToLocal(collider.A);
            renderer.transform.localRotation = Quaternion.identity;
            renderer.transform.localScale = CircleScale(renderer.sprite, collider.Radius);
        }

        private static void PlaceFlipper(SpriteRenderer renderer, FlipperSpec spec)
        {
            renderer.transform.localPosition = ToLocal(spec.Pivot);
            renderer.transform.localRotation = Quaternion.Euler(0, 0, Degrees(spec.RestAngle));
            renderer.transform.localScale = Vector3.one;
        }

        /// <summary>線分のカプセルを描く 9 分割の絵の置き方：中点・向き（度）・大きさ（長さ + 2 × 半径、2 × 半径）。</summary>
        public static (Vector3 Position, float Angle, Vector2 Size) Segment(Collider collider)
        {
            var a = ToLocal(collider.A);
            var b = ToLocal(collider.B);
            var radius = (float)collider.Radius / Board.Unit;
            var direction = b - a;
            var angle = direction.sqrMagnitude > 0 ? Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg : 0;
            return ((a + b) / 2, angle, new Vector2(direction.magnitude + (2 * radius), 2 * radius));
        }

        /// <summary>円の絵を半径に合わせる拡大率（絵の直径を半径の 2 倍にする）。</summary>
        public static Vector3 CircleScale(Sprite sprite, int radius)
        {
            if (sprite == null) { return Vector3.one; }
            var scale = 2f * radius / Board.Unit / sprite.bounds.size.x;
            return new Vector3(scale, scale, 1);
        }

        private static string SegmentMismatch(SpriteRenderer renderer, Collider collider)
        {
            if (renderer == null || renderer.sprite == null) { return "の置き場か絵がありません"; }
            if (renderer.drawMode != SpriteDrawMode.Sliced) { return "は 9 分割の表示にしてください"; }
            var (position, angle, size) = Segment(collider);
            if (!Near(renderer.transform.localPosition, position)) { return "の位置が台のデータと違います"; }
            if (Mathf.Abs(Mathf.DeltaAngle(renderer.transform.localEulerAngles.z, angle)) > .01f) { return "の向きが台のデータと違います"; }
            if (!Near(renderer.size, size) || !Near(renderer.transform.localScale, Vector3.one)) { return "の大きさが台のデータと違います"; }
            return null;
        }

        private static string CircleMismatch(SpriteRenderer renderer, Collider collider)
        {
            if (renderer == null || renderer.sprite == null) { return "の置き場か絵がありません"; }
            if (!Near(renderer.transform.localPosition, ToLocal(collider.A))) { return "の位置が台のデータと違います"; }
            if (!Near(renderer.transform.localScale, CircleScale(renderer.sprite, collider.Radius))) { return "の大きさが台のデータと違います"; }
            return null;
        }

        /// <summary>フリッパーの絵は、軸（絵の基点）が台の軸に、長さ・太さが台のデータに合っているか。</summary>
        private static string FlipperMismatch(SpriteRenderer renderer, FlipperSpec spec)
        {
            var side = spec.Side == FlipperSide.Left ? "左" : "右";
            if (renderer == null || renderer.sprite == null) { return $"{side}のフリッパーの絵がありません。"; }
            if (!Near(renderer.transform.localPosition, ToLocal(spec.Pivot))) { return $"{side}のフリッパーの軸の位置が台のデータと違います。"; }
            if (!Near(renderer.transform.localScale, Vector3.one)) { return $"{side}のフリッパーの大きさが変わっています。"; }
            var sprite = renderer.sprite;
            var radius = (float)spec.Radius / Board.Unit;
            var length = (float)spec.Length / Board.Unit;
            var bounds = sprite.bounds;

            // 絵は軸の円の中心が基点。左端が −半径、右端が 長さ + 半径、上下が ±半径。
            if (Mathf.Abs(bounds.min.x + radius) > .05f || Mathf.Abs(bounds.max.x - (length + radius)) > .05f || Mathf.Abs(bounds.size.y - (2 * radius)) > .1f)
            {
                return $"{side}のフリッパーの絵の大きさか基点が台のデータ（長さ {length}・半径 {radius}）と違います。";
            }

            return null;
        }

        private static bool Near(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < Tolerance * Tolerance;

        private static bool Near(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < Tolerance * Tolerance;
    }
}
