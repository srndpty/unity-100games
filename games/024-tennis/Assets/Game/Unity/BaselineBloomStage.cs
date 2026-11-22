using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// コートの描画：コートとネット（Prefab に置いたまま）、両選手・球・影・最後のバウンドの跡（試合の状態から毎回置き直す）。
    /// 位置は <see cref="CourtProjection"/> で写し、手前ほど前・大きく描く。補間せず、その Tick の状態をそのまま描く。
    /// </summary>
    public sealed class BaselineBloomStage : MonoBehaviour
    {
        /// <summary>選手を実寸より大きく描く倍率（見やすさのため。当たり判定には関わらない）。</summary>
        public const float AthleteScale = 1.35f;

        /// <summary>球を実寸より大きく描く倍率。</summary>
        public const float BallScale = 1.6f;

        /// <summary>走りの絵を切り替える移動の距離（0.3 m）。</summary>
        private const int StrideUnits = 3000;

        [SerializeField] private SpriteRenderer _court;
        [SerializeField] private SpriteRenderer _net;
        [SerializeField] private SpriteRenderer _near;
        [SerializeField] private SpriteRenderer _far;
        [SerializeField] private SpriteRenderer _ball;
        [SerializeField] private SpriteRenderer _shadow;
        [SerializeField] private SpriteRenderer _bounceMark;

        // 姿：0・1 = 構え（走り）、2 = 振りかぶり、3 = 振り抜き、4 = トス。
        [SerializeField] private Sprite[] _nearPoses = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] _farPoses = Array.Empty<Sprite>();

        public const int PoseCount = 5;

        public SpriteRenderer Near => _near;

        public SpriteRenderer Far => _far;

        public SpriteRenderer Ball => _ball;

        public SpriteRenderer Shadow => _shadow;

        public SpriteRenderer BounceMark => _bounceMark;

        public SpriteRenderer Net => _net;

        public SpriteRenderer CourtRenderer => _court;

        /// <summary>試合の前（タイトル）の表示：最初のポイントの立ち位置で、サーバーが球を持っている。試合の状態は作らない。</summary>
        public void RenderInitial(TennisConfig config)
        {
            var server = Positions.Serve(Side.Near, ServeCourt.Deuce, config);
            var receiver = Positions.Receive(Side.Far, ServeCourt.Deuce, config);
            PlaceAthlete(_near, _nearPoses, server, 0);
            PlaceAthlete(_far, _farPoses, receiver, 0);
            PlaceBall(server.WithZ(config.TossZ), true);
            _bounceMark.enabled = false;
        }

        public void Render(TennisMatch match)
        {
            if (match == null) { throw new ArgumentNullException(nameof(match)); }
            PlaceAthlete(_near, _nearPoses, match.Near.Position, Pose(match, match.Near));
            PlaceAthlete(_far, _farPoses, match.Far.Position, Pose(match, match.Far));
            _near.flipX = match.Near.Facing < 0;
            _far.flipX = match.Far.Facing > 0;
            PlaceBall(match.Ball.Position, !match.IsFinished);

            if (match.LastBounce is Vec3 bounce)
            {
                _bounceMark.enabled = true;
                Place(_bounceMark.transform, bounce, 1);
                _bounceMark.sortingOrder = -900;
            }
            else
            {
                _bounceMark.enabled = false;
            }
        }

        /// <summary>選手の姿の番号（0〜4）。</summary>
        public static int Pose(TennisMatch match, Athlete athlete)
        {
            if (match == null) { throw new ArgumentNullException(nameof(match)); }
            if (athlete == null) { throw new ArgumentNullException(nameof(athlete)); }
            var rally = match.Rally;
            if (match.Phase == MatchPhase.Point && athlete.Side == rally.Server)
            {
                if (rally.Phase == RallyPhase.Toss) { return rally.TossSwung ? 3 : 4; }
                if (rally.Phase == RallyPhase.Ready) { return 0; }
            }

            if (athlete.IsSwinging) { return athlete.SwingTick < match.Config.ImpactTicks ? 2 : 3; }
            var stride = (Math.Abs(athlete.Position.X) + Math.Abs(athlete.Position.Y)) / StrideUnits;
            return stride % 2;
        }

        /// <summary>Prefab の配置が投影と一致しないときの説明（一致すれば null）。</summary>
        public string Mismatch()
        {
            if (_court == null || _net == null || _near == null || _far == null || _ball == null || _shadow == null || _bounceMark == null)
            {
                return "描画物の参照が欠けています。";
            }

            if (_nearPoses.Length != PoseCount || _farPoses.Length != PoseCount) { return $"選手の姿は {PoseCount} 枚ずつ必要です。"; }
            if (_court.transform.localPosition != Vector3.zero) { return "コートの絵は原点に置いてください（投影の原点＝ネットの中央）。"; }
            if ((_net.transform.localPosition - CourtProjection.ToWorld(0, 0, 0)).sqrMagnitude > 1e-6f) { return "ネットの位置が投影と一致しません。"; }
            return null;
        }

        private void PlaceAthlete(SpriteRenderer renderer, Sprite[] poses, Vec3 position, int pose)
        {
            renderer.sprite = poses[pose];
            Place(renderer.transform, position, AthleteScale);
            renderer.sortingOrder = CourtProjection.SortingOrder(position.Y);
        }

        private void PlaceBall(Vec3 position, bool visible)
        {
            _ball.enabled = visible;
            _shadow.enabled = visible;
            Place(_ball.transform, position, BallScale);
            Place(_shadow.transform, position.WithZ(0), BallScale);

            // 球は同じ奥行きの選手より前に描く。影は地面の上（選手より後ろ）。
            _ball.sortingOrder = CourtProjection.SortingOrder(position.Y) + 1;
            _shadow.sortingOrder = -800;
        }

        private static void Place(Transform target, Vec3 position, float scale)
        {
            target.localPosition = CourtProjection.ToWorld(position);
            var k = CourtProjection.Scale(CourtProjection.Meters(position.Y)) * scale;
            target.localScale = new Vector3(k, k, 1);
        }
    }
}
