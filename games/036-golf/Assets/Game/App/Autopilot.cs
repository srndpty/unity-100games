using System;
using Game.Core;
using IntGeometry;

namespace Game.App
{
    /// <summary>
    /// 撮影・テスト・PlayMode で共有する操縦役（002）。プレイヤーと同じ操作（クラブ・向き・スイングの押下）だけでラウンドを進める。
    /// <list type="bullet">
    /// <item>構えるたびに、クラブ × 向き × パワー（メーターの Tick）の候補を、実際と同じ <see cref="Predictor.Finish"/> で打ってみて、
    /// 止まった位置の評価（カップまでの距離＋地面の重み、池・OB は除外）が最もよい候補を選ぶ（021 の先読み）。粗く探してから最良の周りを細かく探す。</item>
    /// <item>メーターは選んだ Tick でパワーを決め、理想の Tick で打つ。パター以外は <see cref="ErrorPercent"/> % の確率で 1 Tick ずらす（周期に入らないよう、001）。</item>
    /// </list>
    /// 判断の状態は打つ位置ごとに作り直し、ラウンドが変わったら捨てる（023）。
    /// </summary>
    public sealed class Autopilot : IGolfPilot
    {
        public const int ErrorPercent = 15;

        /// <summary>パター以外の向きの候補の範囲（AimStep の数、±）と刻み。</summary>
        private const int AimRange = 8;

        private const int AimStride = 2;

        /// <summary>パターの向きの候補の範囲（PuttAimStep の数、±）と刻み。</summary>
        private const int PuttAimRange = 48;

        private const int PuttAimStride = 4;

        /// <summary>パター以外のパワーの Tick の刻み。</summary>
        private const int PowerStride = 2;

        /// <summary>これより近ければ、グリーンの外からでもパターを候補に入れる（m）。</summary>
        private const int PuttFromFringe = 15;

        private SeededRandom _random = new(1);
        private bool _hasPlan;
        private Plan _plan;

        public void Reset(int seed)
        {
            _random = new SeededRandom(unchecked(seed ^ 0x474F4C46));
            _hasPlan = false;
        }

        public void Act(GolfRound round)
        {
            if (round == null) { throw new ArgumentNullException(nameof(round)); }
            switch (round.Phase)
            {
                case RoundPhase.Aiming:
                    if (!_hasPlan)
                    {
                        _plan = Choose(round);
                        if (_plan.Club != Clubs.Putter && _random.Next(100) < ErrorPercent) { _plan = _plan.WithError(_random.Next(2) == 0 ? -1 : 1); }
                        _hasPlan = true;
                    }

                    round.SelectClub(_plan.Club);
                    round.SetAim(_plan.Aim);
                    round.SetImpact(0, 0);
                    round.Press();
                    break;
                case RoundPhase.Swinging:
                    var meter = round.Meter!;
                    if (!meter.PowerSet && meter.T == _plan.PowerTick) { round.Press(); }
                    else if (meter.PowerSet && meter.T == meter.IdealTick + _plan.Error) { round.Press(); }
                    break;
                default:
                    _hasPlan = false;
                    break;
            }
        }

        /// <summary>今の構えで最もよい打ち方（ずれ 0 の予測で評価）。</summary>
        public static Plan Choose(GolfRound round)
        {
            if (round == null) { throw new ArgumentNullException(nameof(round)); }
            var search = new Search(round);
            var toCup = round.Hole.Cup - round.Lie;
            var baseAim = Trig.Atan2(toCup.Y, toCup.X, 0);
            var distance = round.DistanceToCup;
            var half = search.Config.MeterHalfTicks;
            var meter = new Meter(search.Config);
            var putt = round.CanUse(Clubs.Putter) &&
                (round.LieSurface == Surface.Green || (distance < (long)PuttFromFringe * Units.PerMeter && round.LieSurface != Surface.Bunker));
            if (putt)
            {
                var step = search.Config.PuttAimStep;
                for (var offset = -PuttAimRange; offset <= PuttAimRange; offset += PuttAimStride)
                {
                    for (var tick = 1; tick <= half; tick++) { search.Try(Clubs.Putter, baseAim + (offset * step), tick); }
                }

                search.Refine(step, 3);
            }

            if (round.LieSurface != Surface.Green)
            {
                var step = search.Config.AimStep;
                for (var club = Clubs.Wood; club <= Clubs.Wedge; club++)
                {
                    for (var tick = 6; tick <= half; tick += PowerStride)
                    {
                        // 届きすぎる組は試さない（飛ぶ距離はパワーのおよそ 2 乗）。
                        var power = meter.GaugeAt(tick);
                        var estimate = round.CarryOf(club) * power / 1000 * power / 1000;
                        if (estimate > (distance * 5 / 4) + (20L * Units.PerMeter)) { continue; }
                        for (var offset = -AimRange; offset <= AimRange; offset += AimStride) { search.Try(club, baseAim + (offset * step), tick); }
                    }
                }

                if (search.Best.Club != Clubs.Putter) { search.Refine(step, 1); }
            }

            return search.Best;
        }

        /// <summary>選んだ打ち方。</summary>
        public readonly struct Plan
        {
            public Plan(int club, int aim, int powerTick, int error, long cost)
            {
                Club = club;
                Aim = aim;
                PowerTick = powerTick;
                Error = error;
                Cost = cost;
            }

            public int Club { get; }

            public int Aim { get; }

            /// <summary>パワーを決めるメーターの Tick（1〜MeterHalfTicks）。</summary>
            public int PowerTick { get; }

            /// <summary>インパクトを理想からずらす Tick 数。</summary>
            public int Error { get; }

            /// <summary>評価（小さいほどよい）。</summary>
            public long Cost { get; }

            public Plan WithError(int error) => new(Club, Aim, PowerTick, error, Cost);
        }

        /// <summary>候補を打ってみて最良を覚える。</summary>
        private sealed class Search
        {
            private readonly GolfRound _round;
            private readonly Meter _meter;

            public Search(GolfRound round)
            {
                _round = round;
                Config = round.Config;
                _meter = new Meter(Config);
                Best = new Plan(round.Club, round.Aim, Config.MeterHalfTicks, 0, long.MaxValue);
            }

            public GolfConfig Config { get; }

            public Plan Best { get; private set; }

            public void Try(int club, int aim, int tick)
            {
                if (tick < 1 || tick > Config.MeterHalfTicks) { return; }
                var setup = new ShotSetup(club, aim, 0, 0, _meter.GaugeAt(tick), 0, false);
                var start = Launch.From(Config, setup, _round.Lie, _round.LieSurface);
                var end = Predictor.Finish(start, _round.Hole, _round.Wind, Config);
                var cost = Cost(end);
                if (cost < Best.Cost) { Best = new Plan(club, Trig.Normalize(aim), tick, 0, cost); }
            }

            /// <summary>最良の周りを、向き ±<paramref name="range"/> 刻み × パワー ±1 Tick で試す。</summary>
            public void Refine(int aimStep, int range)
            {
                var center = Best;
                for (var offset = -range; offset <= range; offset++)
                {
                    for (var tick = center.PowerTick - 1; tick <= center.PowerTick + 1; tick++) { Try(center.Club, center.Aim + (offset * aimStep), tick); }
                }
            }

            private long Cost(BallState end)
            {
                switch (end.Phase)
                {
                    case BallPhase.Holed:
                        return 0;
                    case BallPhase.Out:
                        return long.MaxValue / 2;
                }

                var distance = IntMath.Sqrt(Vec2i.DistanceSquared(end.Position, _round.Hole.Cup));
                var surface = _round.Hole.SurfaceAt(end.Position);
                long penalty = surface switch
                {
                    Surface.Green => 0,
                    Surface.Fairway => 4,
                    Surface.Tee => 4,
                    Surface.Rough => 12,
                    Surface.Bunker => 30,
                    _ => 0,
                };
                return distance + (penalty * Units.PerMeter);
            }
        }
    }
}
