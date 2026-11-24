using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using IntGeometry;
using TileMotion;

namespace Game.Core
{
    public enum RoundPhase
    {
        /// <summary>構え：向き・クラブ・打点を決める。</summary>
        Aiming,

        /// <summary>メーターが動いている。</summary>
        Swinging,

        /// <summary>球が動いている。</summary>
        Moving,

        /// <summary>球が止まった後の待ち（次の構えへ）。</summary>
        Settling,

        /// <summary>池・OB の後の待ち（前の位置から打ち直し）。</summary>
        Penalty,

        /// <summary>ホールが終わった後の待ち（次のホールへ）。</summary>
        HoleOut,

        /// <summary>3 ホールが終わった。以後の操作と <see cref="GolfRound.Step"/> は何もしない。</summary>
        Finished,
    }

    /// <summary>1 打の終わり方。</summary>
    public enum ShotOutcome
    {
        Rest,
        Holed,
        Water,
        OutOfBounds,
    }

    /// <summary>1 Tick（または操作）で起きたこと（音・演出・テストの照合用）。</summary>
    [Flags]
    public enum RoundEvents
    {
        None = 0,
        SwingStarted = 1,
        PowerSet = 1 << 1,
        SwingCanceled = 1 << 2,
        Launched = 1 << 3,
        Mishit = 1 << 4,
        Landed = 1 << 5,
        Bounced = 1 << 6,
        TreeHit = 1 << 7,
        LipOut = 1 << 8,
        Stopped = 1 << 9,
        Holed = 1 << 10,

        /// <summary>池・OB に入り、1 打罰を加えた。</summary>
        Penalty = 1 << 11,

        /// <summary>打数の上限に達してホールを終えた。</summary>
        GaveUp = 1 << 12,

        /// <summary>新しい構え（打ち直し・次の打つ位置・次のホール）。</summary>
        Ready = 1 << 13,

        HoleStarted = 1 << 14,

        Finished = 1 << 15,
    }

    /// <summary>1 打の記録：予測（ずれ 0）と実際の着地点・止まった位置。</summary>
    public readonly struct ShotRecord
    {
        public ShotRecord(int hole, int stroke, ShotSetup setup, Vec2i start, BallState predicted, BallState actual, ShotOutcome outcome)
        {
            Hole = hole;
            Stroke = stroke;
            Setup = setup;
            Start = start;
            Predicted = predicted;
            Actual = actual;
            Outcome = outcome;
        }

        /// <summary>ホールの番号（1〜3）。</summary>
        public int Hole { get; }

        /// <summary>このホールの何打目か（罰打を含めた数え方で、打った時点の打数）。</summary>
        public int Stroke { get; }

        public ShotSetup Setup { get; }

        public Vec2i Start { get; }

        /// <summary>ずれ 0 で打った場合に終わる球（予測線と同じ計算）。</summary>
        public BallState Predicted { get; }

        /// <summary>実際に終わった球。</summary>
        public BallState Actual { get; }

        public ShotOutcome Outcome { get; }

        /// <summary>
        /// 予測と実際の差（µm）。飛ばした打球は最初の着地点の距離、パター（または両方が着地しなかった）は終わった位置の距離。
        /// </summary>
        public long Difference =>
            Setup.Club != Clubs.Putter && Predicted.Landed && Actual.Landed
                ? IntMath.Sqrt(Vec2i.DistanceSquared(Predicted.Landing, Actual.Landing))
                : IntMath.Sqrt(Vec2i.DistanceSquared(Predicted.Position, Actual.Position));
    }

    /// <summary>
    /// 1 ラウンド（3 ホール）。構え → メーター → 球が動く → 止まる/入る/池・OB → 次の構え、を Tick で進める。
    /// 操作（<see cref="TurnAim"/>・<see cref="SelectClub"/>・<see cref="SetImpact"/>・<see cref="Press"/>）は Tick の間に呼び、受け付けたら true。
    /// 乱数はホールごとの風だけ（ラウンドのシードからホールごとに別の系列）。
    /// </summary>
    public sealed class GolfRound
    {
        private readonly GolfConfig _config;
        private readonly Course _course;
        private readonly Vec2i[] _winds;
        private readonly long[] _carries;
        private int[] _scores;
        private List<ShotRecord> _shots = new();
        private Meter? _meter;
        private ShotSetup _launched;
        private BallState _predicted;
        private RoundEvents _events;

        public GolfRound(GolfConfig config, Course course, int seed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            _course = course ?? throw new ArgumentNullException(nameof(course));
            config.Validate();
            _config = config.Copy();
            Seed = seed;
            _winds = new Vec2i[course.Holes.Count];
            for (var i = 0; i < _winds.Length; i++) { _winds[i] = WindFor(_config, seed, i); }
            _carries = Carries(_config);
            _scores = new int[course.Holes.Count];
            StartHole(0);
        }

        public int Seed { get; }

        /// <summary>設定の複製。</summary>
        public GolfConfig Config => _config.Copy();

        public Course Course => _course;

        public RoundPhase Phase { get; private set; }

        /// <summary>今のホールの番号（0 始まり）。</summary>
        public int HoleIndex { get; private set; }

        public Hole Hole => _course.Holes[HoleIndex];

        public Vec2i Wind => _winds[HoleIndex];

        /// <summary>ホール <paramref name="index"/> の風。</summary>
        public Vec2i WindOf(int index) => _winds[index];

        /// <summary>今のホールの打数（罰打を含む）。</summary>
        public int Strokes { get; private set; }

        /// <summary>今のホールの罰打の数。</summary>
        public int Penalties { get; private set; }

        /// <summary>今のホールを打数の上限で終えたか。</summary>
        public bool GaveUp { get; private set; }

        /// <summary>終えたホールの打数（まだなら 0）。</summary>
        public int ScoreOf(int index) => _scores[index];

        /// <summary>終えたホールの打数の合計。</summary>
        public int Total
        {
            get
            {
                var total = 0;
                foreach (var score in _scores) { total += score; }
                return total;
            }
        }

        /// <summary>終えたホールの par の合計。</summary>
        public int ParPlayed
        {
            get
            {
                var total = 0;
                for (var i = 0; i < _scores.Length; i++)
                {
                    if (_scores[i] > 0) { total += _course.Holes[i].Par; }
                }

                return total;
            }
        }

        public BallState Ball { get; private set; }

        /// <summary>今の打つ位置（構えの位置）。</summary>
        public Vec2i Lie { get; private set; }

        /// <summary>直前の打つ位置（池・OB の打ち直しに使う）。</summary>
        public Vec2i PreviousLie { get; private set; }

        public Surface LieSurface => Hole.SurfaceAt(Lie);

        public int Club { get; private set; }

        public int Aim { get; private set; }

        public int ImpactX { get; private set; }

        public int ImpactY { get; private set; }

        /// <summary>待ちの残り Tick（Settling・Penalty・HoleOut）。</summary>
        public int Countdown { get; private set; }

        public long Ticks { get; private set; }

        /// <summary>メーター（振っていなければ null）。複製を返す。</summary>
        public Meter? Meter => _meter?.Clone();

        /// <summary>直前に終わった打球の終わり方。</summary>
        public ShotOutcome LastOutcome { get; private set; }

        public ReadOnlyCollection<ShotRecord> Shots => _shots.AsReadOnly();

        /// <summary>クラブを最大のパワーで平らな地面に打ったときの最初の着地までの距離（µm、風なし）。</summary>
        public long CarryOf(int club) => _carries[club];

        /// <summary>カップまでの距離（µm）。</summary>
        public long DistanceToCup => IntMath.Sqrt(Vec2i.DistanceSquared(Lie, Hole.Cup));

        /// <summary>今の構えで選べるクラブか（グリーンの上はパターだけ）。</summary>
        public bool CanUse(int club) => club >= 0 && club < Clubs.Count && (LieSurface != Surface.Green || club == Clubs.Putter);

        /// <summary>今の構えと、パワー（‰）・ずれ 0 の決め事。</summary>
        public ShotSetup SetupFor(int power) => new(Club, Aim, ImpactX, ImpactY, power, 0, false);

        public GolfRound Clone()
        {
            var copy = (GolfRound)MemberwiseClone();
            copy._scores = (int[])_scores.Clone();
            copy._shots = new List<ShotRecord>(_shots);
            copy._meter = _meter?.Clone();
            return copy;
        }

        /// <summary>全ての状態（設定・コース・シードを含む）のハッシュ。</summary>
        public uint Hash()
        {
            var hash = StateHash.Start();
            _config.MixInto(ref hash);
            hash.Mix(_course.Fingerprint);
            hash.Mix(Seed);
            hash.Mix((int)Phase);
            hash.Mix(HoleIndex);
            hash.Mix(Strokes);
            hash.Mix(Penalties);
            hash.Mix(GaveUp);
            foreach (var score in _scores) { hash.Mix(score); }
            Ball.MixInto(ref hash);
            hash.Mix(Lie.X);
            hash.Mix(Lie.Y);
            hash.Mix(PreviousLie.X);
            hash.Mix(PreviousLie.Y);
            hash.Mix(Club);
            hash.Mix(Aim);
            hash.Mix(ImpactX);
            hash.Mix(ImpactY);
            hash.Mix(Countdown);
            hash.Mix(Ticks);
            hash.Mix((int)LastOutcome);
            hash.Mix(_meter != null);
            _meter?.MixInto(ref hash);
            hash.Mix(_shots.Count);
            foreach (var shot in _shots)
            {
                hash.Mix(shot.Hole);
                hash.Mix(shot.Stroke);
                hash.Mix(shot.Setup.GetHashCode());
                shot.Predicted.MixInto(ref hash);
                shot.Actual.MixInto(ref hash);
                hash.Mix((int)shot.Outcome);
            }

            return hash.Value;
        }

        /// <summary>向きを <paramref name="steps"/> 回ぶん回す（正は反時計回り＝左）。構えの間だけ。</summary>
        public bool TurnAim(int steps)
        {
            if (Phase != RoundPhase.Aiming) { return false; }
            var step = Club == Clubs.Putter ? _config.PuttAimStep : _config.AimStep;
            Aim = Trig.Normalize(Aim + (steps * step));
            return true;
        }

        /// <summary>向きを直接決める（操縦役用）。構えの間だけ。</summary>
        public bool SetAim(int angle)
        {
            if (Phase != RoundPhase.Aiming) { return false; }
            Aim = Trig.Normalize(angle);
            return true;
        }

        public bool SelectClub(int club)
        {
            if (Phase != RoundPhase.Aiming || !CanUse(club)) { return false; }
            Club = club;
            return true;
        }

        /// <summary>選べるクラブの中で <paramref name="delta"/> だけ替える（端で止まる）。</summary>
        public bool ShiftClub(int delta)
        {
            if (Phase != RoundPhase.Aiming) { return false; }
            var club = Club;
            var direction = Math.Sign(delta);
            for (var moved = 0; moved < Math.Abs(delta); moved++)
            {
                var next = club + direction;
                while (next >= 0 && next < Clubs.Count && !CanUse(next)) { next += direction; }
                if (next < 0 || next >= Clubs.Count) { break; }
                club = next;
            }

            if (club == Club) { return false; }
            Club = club;
            return true;
        }

        public bool SetImpact(int x, int y)
        {
            if (Phase != RoundPhase.Aiming || Math.Abs(x) > GolfConfig.MaxImpact || Math.Abs(y) > GolfConfig.MaxImpact) { return false; }
            ImpactX = x;
            ImpactY = y;
            return true;
        }

        /// <summary>スイングのボタン。構えでは振り始め、振っている間はメーターの押下。</summary>
        public bool Press()
        {
            switch (Phase)
            {
                case RoundPhase.Aiming:
                    _meter = new Meter(_config);
                    Phase = RoundPhase.Swinging;
                    _events |= RoundEvents.SwingStarted;
                    return true;
                case RoundPhase.Swinging:
                    var result = _meter!.Press();
                    if (result == MeterResult.PowerSet) { _events |= RoundEvents.PowerSet; }
                    if (result == MeterResult.Impact || result == MeterResult.Mishit) { Swing(result == MeterResult.Mishit); }
                    return result != MeterResult.None;
                default:
                    return false;
            }
        }

        /// <summary>振っている途中なら取り消して構えへ戻す（打数に数えない）。一時停止・フォーカス喪失で使う。</summary>
        public bool CancelSwing()
        {
            if (Phase != RoundPhase.Swinging) { return false; }
            _meter = null;
            Phase = RoundPhase.Aiming;
            _events |= RoundEvents.SwingCanceled;
            return true;
        }

        /// <summary>前回の取り出し以降に、操作と Tick で起きたことを取り出す。</summary>
        public RoundEvents TakeEvents()
        {
            var events = _events;
            _events = RoundEvents.None;
            return events;
        }

        /// <summary>今の構え・パワー（メーターで決めていればその値、なければ最大）・ずれ 0 の予測線。</summary>
        public Prediction Preview(Vec2i[] points, int[]? heights)
        {
            if (Phase != RoundPhase.Aiming && Phase != RoundPhase.Swinging) { return new Prediction(0, false, Lie, Ball); }
            var power = _meter != null && _meter.PowerSet ? _meter.Power : 1000;
            var start = Launch.From(_config, SetupFor(power), Lie, LieSurface);
            var ticks = Club == Clubs.Putter ? _config.PuttPreviewTicks : _config.MaxShotTicks;
            return Predictor.Trace(start, Hole, Wind, _config, points, heights, ticks);
        }

        /// <summary>1 Tick 進めて、その Tick に起きたことを返す（<see cref="TakeEvents"/> の分も含めて取り出す）。</summary>
        public RoundEvents Step()
        {
            if (Phase == RoundPhase.Finished) { return TakeEvents(); }
            Ticks++;
            switch (Phase)
            {
                case RoundPhase.Swinging:
                    var result = _meter!.Step();
                    if (result == MeterResult.Canceled)
                    {
                        _meter = null;
                        Phase = RoundPhase.Aiming;
                        _events |= RoundEvents.SwingCanceled;
                    }
                    else if (result == MeterResult.Mishit)
                    {
                        Swing(true);
                    }

                    break;
                case RoundPhase.Moving:
                    Move();
                    break;
                case RoundPhase.Settling:
                    if (--Countdown > 0) { break; }
                    if (Strokes >= _config.StrokeCap(Hole.Par)) { EndHole(true); }
                    else { Ready(Ball.Position); }
                    break;
                case RoundPhase.Penalty:
                    if (--Countdown > 0) { break; }
                    if (Strokes >= _config.StrokeCap(Hole.Par)) { EndHole(true); }
                    else { Ready(PreviousLie); }
                    break;
                case RoundPhase.HoleOut:
                    if (--Countdown > 0) { break; }
                    if (HoleIndex + 1 < _course.Holes.Count)
                    {
                        StartHole(HoleIndex + 1);
                    }
                    else
                    {
                        Phase = RoundPhase.Finished;
                        _events |= RoundEvents.Finished;
                    }

                    break;
            }

            return TakeEvents();
        }

        /// <summary>ホール <paramref name="index"/> の風：16 方位 × 0〜<see cref="GolfConfig.MaxWindSteps"/> 段。</summary>
        public static Vec2i WindFor(GolfConfig config, int seed, int index)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            var random = new SeededRandom(unchecked(seed ^ (int)(0x57494E44u + ((uint)(index + 1) * 0x9E3779B9u))));
            var angle = random.Next(16) * (Trig.FullTurn / 16);
            var steps = random.Range(0, config.MaxWindSteps);
            return Launch.Direction(angle, steps * config.WindStep);
        }

        /// <summary>各クラブの最大のパワーの最初の着地までの距離（平らなフェアウェイ、風なし）。</summary>
        public static long[] Carries(GolfConfig config)
        {
            var field = OpenField();
            var carries = new long[Clubs.Count];
            for (var club = 0; club < Clubs.Count; club++)
            {
                var start = Launch.From(config, new ShotSetup(club, 0, 0, 0, 1000, 0, false), field.Tee, Surface.Fairway);
                var end = Predictor.Finish(start, field, Vec2i.Zero, config);
                carries[club] = (end.Landed ? end.Landing.X : end.Position.X) - field.Tee.X;
            }

            return carries;
        }

        /// <summary>平らなフェアウェイだけの広いホール（木なし）。</summary>
        internal static Hole OpenField()
        {
            const int width = Core.Hole.MaxWidth;
            const int height = 40;
            var cells = new Surface[width * height];
            for (var i = 0; i < cells.Length; i++) { cells[i] = Surface.Fairway; }
            var tee = new Vec2i(Units.PerMeter, height * Units.PerMeter / 2);
            var cup = new Vec2i((width - 1) * Units.PerMeter, height * Units.PerMeter / 2);
            return new Hole(0, 3, width, height, cells, new short[cells.Length], new short[cells.Length], tee, cup, Array.Empty<Tree>());
        }

        private void Swing(bool mishit)
        {
            var meter = _meter!;
            var setup = new ShotSetup(Club, Aim, ImpactX, ImpactY, meter.Power, meter.Error, mishit);
            _meter = null;
            Strokes++;
            PreviousLie = Lie;
            _launched = setup;
            var start = Launch.From(_config, setup, Lie, LieSurface);
            _predicted = Predictor.Finish(Launch.From(_config, setup.Ideal, Lie, LieSurface), Hole, Wind, _config);
            Ball = start;
            Phase = RoundPhase.Moving;
            _events |= RoundEvents.Launched | (mishit ? RoundEvents.Mishit : RoundEvents.None);
        }

        private void Move()
        {
            var ball = Ball;
            var events = BallSim.Step(ref ball, Hole, Wind, _config);
            Ball = ball;
            if ((events & BallEvents.Landed) != 0) { _events |= RoundEvents.Landed; }
            if ((events & BallEvents.Bounced) != 0) { _events |= RoundEvents.Bounced; }
            if ((events & BallEvents.TreeHit) != 0) { _events |= RoundEvents.TreeHit; }
            if ((events & BallEvents.LipOut) != 0) { _events |= RoundEvents.LipOut; }
            switch (ball.Phase)
            {
                case BallPhase.Holed:
                    Record(ShotOutcome.Holed);
                    _events |= RoundEvents.Holed;
                    EndHole(false);
                    break;
                case BallPhase.Out:
                    Record(ball.OutSurface == Surface.Water ? ShotOutcome.Water : ShotOutcome.OutOfBounds);
                    Strokes = Math.Min(Strokes + 1, _config.StrokeCap(Hole.Par));
                    Penalties++;
                    Phase = RoundPhase.Penalty;
                    Countdown = _config.PenaltyTicks;
                    _events |= RoundEvents.Penalty;
                    break;
                case BallPhase.Resting:
                    Record(ShotOutcome.Rest);
                    Phase = RoundPhase.Settling;
                    Countdown = _config.RestTicks;
                    _events |= RoundEvents.Stopped;
                    break;
            }
        }

        private void Record(ShotOutcome outcome)
        {
            LastOutcome = outcome;
            _shots.Add(new ShotRecord(Hole.Number, Strokes, _launched, PreviousLie, _predicted, Ball, outcome));
        }

        private void StartHole(int index)
        {
            HoleIndex = index;
            Strokes = 0;
            Penalties = 0;
            GaveUp = false;
            PreviousLie = Hole.Tee;
            _events |= RoundEvents.HoleStarted;
            Ready(Hole.Tee);
        }

        private void EndHole(bool gaveUp)
        {
            GaveUp = gaveUp;
            var cap = _config.StrokeCap(Hole.Par);
            if (gaveUp) { Strokes = cap; }
            _scores[HoleIndex] = Math.Min(Strokes, cap);
            Phase = RoundPhase.HoleOut;
            Countdown = _config.HoleOutTicks;
            if (gaveUp) { _events |= RoundEvents.GaveUp; }
        }

        /// <summary>
        /// <paramref name="lie"/> で構える：向きはカップへ、打点は中央、クラブはグリーンならパター、
        /// それ以外は届く中で最も短いクラブ（どれも届かなければウッド）。
        /// </summary>
        private void Ready(Vec2i lie)
        {
            Lie = lie;
            Ball = BallState.RestingAt(lie);
            Phase = RoundPhase.Aiming;
            Countdown = 0;
            _meter = null;
            ImpactX = 0;
            ImpactY = 0;
            var toCup = Hole.Cup - lie;
            Aim = Trig.Atan2(toCup.Y, toCup.X, 0);
            Club = LieSurface == Surface.Green ? Clubs.Putter : AutoClub(DistanceToCup);
            _events |= RoundEvents.Ready;
        }

        private int AutoClub(long distance)
        {
            for (var club = Clubs.Wedge; club >= Clubs.Wood; club--)
            {
                if (_carries[club] * _config.LiePower[(int)LieSurface] / 1000 >= distance) { return club; }
            }

            return Clubs.Wood;
        }
    }
}
