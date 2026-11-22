using System;
using TileMotion;

namespace Game.Core
{
    public enum MatchPhase
    {
        /// <summary>ポイントの最中（サーブ待ち・トス・打ち合い）。</summary>
        Point,

        /// <summary>ポイントが決まり、判定を見せている間。終わると次のポイントか試合終了。</summary>
        Gap,

        /// <summary>試合終了。Tick と入力は状態を変えない。</summary>
        Finished,
    }

    /// <summary>画面・音に反映する出来事。</summary>
    [Flags]
    public enum MatchEvents
    {
        None = 0,
        Toss = 1 << 0,
        Serve = 1 << 1,
        Stroke = 1 << 2,
        Lob = 1 << 3,
        Whiff = 1 << 4,
        Bounce = 1 << 5,
        NetCord = 1 << 6,
        Fault = 1 << 7,
        PointWon = 1 << 8,
        GameWon = 1 << 9,
        TiebreakStarted = 1 << 10,
        MatchWon = 1 << 11,
        NextPoint = 1 << 12,
    }

    /// <summary>
    /// 試合：両選手・球・1 ポイントの状態機械（<see cref="Rally"/>）・得点（<see cref="MatchScore"/>）・CPU。
    /// <para>
    /// 1 Tick の順序：押し始めの判定（サーブ待ちならトス、トス中なら打つ、打ち合いなら構える）→ 移動（サーブ待ちのサーバーは横だけ）
    /// → 球の 1 Tick と判定（トス中に地面に落ちたら持ち直す、打ち合いならネット・バウンド・壁の線）→ 構えている選手の当たり判定と打球
    /// → 振りを進める → ポイントが決まっていれば得点を入れて間を置く。
    /// </para>
    /// 乱数は CPU だけが使う（試合のシードから判断とサーブの系列を分ける）。
    /// </summary>
    public sealed class TennisMatch
    {
        private readonly TennisConfig _config;
        private CpuBrain _cpu;
        private Countdown _gap;
        private MatchEvents _events;

        public TennisMatch(TennisConfig config, int seed, Side firstServer = Side.Near)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            _config = config.Copy();
            Seed = seed;
            Score = new MatchScore(_config.TiebreakAtGames, _config.TiebreakPoints, firstServer);
            Near = new Athlete(Side.Near, Vec3.Zero);
            Far = new Athlete(Side.Far, Vec3.Zero);
            Ball = new Ball(Vec3.Zero, Vec3.Zero);
            Rally = new Rally(Score.Server, Score.ServeCourt);
            _cpu = new CpuBrain(Side.Far, seed);
            StartPoint();
        }

        public int Seed { get; }

        public TennisConfig Config => _config;

        public long Tick { get; private set; }

        public MatchPhase Phase { get; private set; }

        public MatchScore Score { get; private set; }

        public Rally Rally { get; private set; }

        public Ball Ball { get; private set; }

        public Athlete Near { get; private set; }

        public Athlete Far { get; private set; }

        /// <summary>最後に決まったポイントの勝者と判定（まだなければ null）。</summary>
        public Side? LastPointWinner { get; private set; }

        public Verdict LastVerdict { get; private set; }

        /// <summary>最後の打球の早打ち・遅打ちのずれ（千分率、描画・案内用）。</summary>
        public int LastTiming { get; private set; }

        /// <summary>最後に球が弾んだ位置（描画用）。</summary>
        public Vec3? LastBounce { get; private set; }

        public int PointsPlayed { get; private set; }

        /// <summary>いちばん長いラリー（打った回数、サーブを含む）。</summary>
        public int LongestRally { get; private set; }

        public bool IsFinished => Phase == MatchPhase.Finished;

        public Side? Winner => Score.Winner;

        public Athlete AthleteOf(Side side) => side == Side.Near ? Near : Far;

        /// <summary>球が選手の手の中にある（サーブ待ち）。</summary>
        public bool BallInHand => Phase == MatchPhase.Point && Rally.Phase == RallyPhase.Ready;

        public TennisMatch Clone()
        {
            var copy = (TennisMatch)MemberwiseClone();
            copy._cpu = _cpu.Clone();
            copy.Score = Score.Clone();
            copy.Rally = Rally.Clone();
            copy.Ball = Ball.Clone();
            copy.Near = Near.Clone();
            copy.Far = Far.Clone();
            return copy;
        }

        public MatchEvents TakeEvents()
        {
            var events = _events;
            _events = MatchEvents.None;
            return events;
        }

        /// <summary>1 Tick 進める。奥の選手は CPU が操作する。</summary>
        public void Step(AthleteCommand near)
        {
            if (IsFinished) { return; }
            Step(near, _cpu.Decide(this));
        }

        /// <summary>両選手の操作を指定して 1 Tick 進める（テスト用）。</summary>
        public void Step(AthleteCommand near, AthleteCommand far)
        {
            if (IsFinished) { return; }
            Tick++;
            if (Phase == MatchPhase.Gap)
            {
                StepGap(near, far);
                return;
            }

            Press(Near, near);
            Press(Far, far);
            Move(Near, near);
            Move(Far, far);
            StepBall();
            Resolve(Near);
            Resolve(Far);
            Near.AdvanceSwing(_config.SwingTicks);
            Far.AdvanceSwing(_config.SwingTicks);
            if (Rally.Phase == RallyPhase.Over) { EndPoint(); }
        }

        /// <summary>
        /// 打ち合いの場面を作る（テスト・撮影用）。<paramref name="lastHitter"/> が今打った球として、球を置き直す。
        /// </summary>
        public void StageRally(Side lastHitter, Vec3 ballPosition, Vec3 ballVelocity, Vec3 nearPosition, Vec3 farPosition)
        {
            if (IsFinished) { throw new InvalidOperationException("試合は終わっています。"); }
            Phase = MatchPhase.Point;
            Rally = Rally.Staged(Score.Server, Score.ServeCourt, lastHitter);
            Ball.Place(ballPosition, ballVelocity);
            Near.Reset(Near.Clamp(nearPosition, _config));
            Far.Reset(Far.Clamp(farPosition, _config));
            _cpu = _cpu.Restarted();
        }

        public uint Hash()
        {
            var h = StateHash.Start();
            h.Mix(Tick);
            h.Mix((int)Phase);
            h.Mix(_gap.Remaining);
            h.Mix(Score.Games(Side.Near));
            h.Mix(Score.Games(Side.Far));
            h.Mix(Score.Points(Side.Near));
            h.Mix(Score.Points(Side.Far));
            h.Mix(Score.InTiebreak);
            h.Mix((int)Score.GameServer);
            h.Mix((int)Rally.Phase);
            h.Mix(Rally.ServeNumber);
            h.Mix((int)Rally.LastHitter);
            h.Mix(Rally.Bounces);
            h.Mix(Rally.ServeInFlight);
            h.Mix(Rally.TossTick);
            h.Mix(Rally.TossSwung);
            h.Mix(Rally.Shots);
            MixVec(ref h, Ball.Position);
            MixVec(ref h, Ball.Velocity);
            h.Mix(Ball.IsDead);
            MixAthlete(ref h, Near);
            MixAthlete(ref h, Far);
            _cpu.Mix(ref h);
            return h.Value;
        }

        private static void MixAthlete(ref StateHash h, Athlete athlete)
        {
            MixVec(ref h, athlete.Position);
            h.Mix(athlete.SwingTick);
            h.Mix((int)athlete.SwingKind);
            h.Mix(athlete.SwingAim);
            h.Mix(athlete.Waited);
            h.Mix(athlete.StrokeHeld);
            h.Mix(athlete.LobHeld);
        }

        private static void MixVec(ref StateHash h, Vec3 v)
        {
            h.Mix(v.X);
            h.Mix(v.Y);
            h.Mix(v.Z);
        }

        private void StepGap(AthleteCommand near, AthleteCommand far)
        {
            // 間の Tick も押下の記録だけは更新する（押したままのキーで次のポイントのトスを上げない）。
            Near.TakePress(near);
            Far.TakePress(far);
            Ball.Step(_config.Physics);
            _gap.Tick();
            if (_gap.IsActive) { return; }

            if (Score.IsOver)
            {
                Phase = MatchPhase.Finished;
                return;
            }

            StartPoint();
            _events |= MatchEvents.NextPoint;
        }

        private void Press(Athlete athlete, AthleteCommand command)
        {
            var press = athlete.TakePress(command);
            if (press == null) { return; }

            var isServer = athlete.Side == Rally.Server;
            switch (Rally.Phase)
            {
                case RallyPhase.Ready when isServer:
                    Rally.Toss();
                    Ball.Place(athlete.Position.WithZ(_config.TossZ), new Vec3(0, 0, _config.TossSpeed));
                    _events |= MatchEvents.Toss;
                    break;
                case RallyPhase.Toss when isServer && !Rally.TossSwung:
                    HitServe(command);
                    break;
                case RallyPhase.InPlay:
                    athlete.StartSwing(press.Value, command.AimX * Court.RightOf(athlete.Side));
                    break;
            }
        }

        private void HitServe(AthleteCommand command)
        {
            var offset = _config.TossApexTick - Rally.TossTick;
            var hit = Math.Abs(offset) <= _config.ServeTimingSpan;
            Rally.SwingAtToss(hit);
            if (!hit)
            {
                _events |= MatchEvents.Whiff;
                return;
            }

            var server = Rally.Server;
            var timing = Assist(server, offset * HitWindow.MaxTiming / _config.ServeTimingSpan);
            var aim = command.AimX * Court.RightOf(server);
            var velocity = Shots.Plan(server, ShotKind.Serve, aim, timing, Ball.Position, Rally.ServeCourt, _config);
            Ball.Place(Ball.Position, velocity);
            LastTiming = timing;
            _events |= MatchEvents.Serve;
        }

        private void Move(Athlete athlete, AthleteCommand command)
        {
            var speed = athlete.Side == Side.Near ? _config.MoveSpeed : _config.CpuMoveSpeed;
            if (athlete.Side == Rally.Server)
            {
                // サーブ待ちのサーバーはベースラインに沿って横にだけ動き（立ち位置を選ぶ）、球も手と一緒に動く。トス中は動かない。
                if (Rally.Phase == RallyPhase.Ready)
                {
                    athlete.MoveAlongBaseline(command.MoveX, speed, Rally.ServeCourt, _config);
                    Ball.Place(athlete.Position.WithZ(_config.TossZ), Vec3.Zero);
                    return;
                }

                if (Rally.Phase == RallyPhase.Toss) { return; }
            }

            // 振り抜いている間だけ遅くなる（構えて待つ間はふつうに動ける）。
            if (athlete.IsSwinging && !athlete.IsReady) { speed = Math.Max(1, speed * _config.SwingMovePercent / 100); }
            athlete.Move(command.MoveX, command.MoveY, speed, _config);
        }

        private void StepBall()
        {
            switch (Rally.Phase)
            {
                case RallyPhase.Toss:
                    Rally.AdvanceToss();
                    if (Ball.Step(_config.Physics).Kind == BallEventKind.Bounce) { Rally.DropToss(); }
                    break;
                case RallyPhase.InPlay:
                    var e = Ball.Step(_config.Physics);
                    if (e.Kind == BallEventKind.Bounce)
                    {
                        LastBounce = e.Point;
                        _events |= MatchEvents.Bounce;
                    }
                    else if (e.Kind == BallEventKind.Net)
                    {
                        _events |= MatchEvents.NetCord;
                    }

                    var verdict = Rally.OnBall(e);
                    if (verdict == Verdict.None && Rally.Phase == RallyPhase.InPlay) { verdict = Rally.OnPosition(Ball.Position); }
                    if (verdict == Verdict.Fault)
                    {
                        LastVerdict = Verdict.Fault;
                        _events |= MatchEvents.Fault;
                        PlaceForServe();
                    }

                    break;
            }
        }

        /// <summary>
        /// 構えている選手の当たり判定。押した Tick に球が届く範囲にあればその場で打つ（球の位置で早打ち・遅打ち）。
        /// まだ向かってくる途中なら待ち、球が理想の打点に来たら打つ。届かないまま通り過ぎた・待ちきれなかったら空振り。
        /// </summary>
        private void Resolve(Athlete athlete)
        {
            if (!athlete.IsReady) { return; }
            if (Rally.Phase != RallyPhase.InPlay)
            {
                athlete.Release();
                return;
            }

            var check = HitWindow.Check(athlete, Ball, Rally.Incoming == athlete.Side, Rally.ServeInFlight, _config);
            var coming = StillComing(athlete);
            if (check == HitCheck.Ok && (athlete.Waited == 0 || !coming))
            {
                var timing = Assist(athlete.Side, HitWindow.Timing(athlete, Ball.Position, _config));
                var velocity = Shots.Plan(athlete.Side, athlete.SwingKind, athlete.SwingAim, timing, Ball.Position, Rally.ServeCourt, _config);
                Ball.Place(Ball.Position, velocity);
                Rally.Hit(athlete.Side);
                LastTiming = timing;
                athlete.Release();
                _events |= athlete.SwingKind == ShotKind.Lob ? MatchEvents.Lob : MatchEvents.Stroke;
                return;
            }

            if (coming && athlete.Waited < _config.SwingWaitTicks) { return; }
            athlete.Release();
            _events |= MatchEvents.Whiff;
        }

        /// <summary>球が選手へ向かってくる途中で、まだ理想の打点より前（ネット寄り）にあるか。</summary>
        private bool StillComing(Athlete athlete)
        {
            if (Rally.Incoming != athlete.Side || Ball.IsDead) { return false; }
            var forward = Court.Forward(athlete.Side);
            var ahead = ((long)Ball.Position.Y - athlete.Position.Y) * forward;
            return ahead > _config.ContactAhead && Ball.Velocity.Y * forward < 0;
        }

        /// <summary>プレイヤー（手前）の打球のずれを補助で弱める。CPU はそのまま。</summary>
        private int Assist(Side side, int timing) => side == Side.Near ? timing * _config.PlayerTimingAssistPercent / 100 : timing;

        private void EndPoint()
        {
            var winner = Rally.Winner!.Value;
            LastPointWinner = winner;
            LastVerdict = Rally.LastVerdict;
            PointsPlayed++;
            LongestRally = Math.Max(LongestRally, Rally.Shots);
            var change = Score.Award(winner);
            _events |= MatchEvents.PointWon;
            if ((change & ScoreChange.Game) != 0) { _events |= MatchEvents.GameWon; }
            if ((change & ScoreChange.TiebreakStarted) != 0) { _events |= MatchEvents.TiebreakStarted; }
            if ((change & ScoreChange.Set) != 0) { _events |= MatchEvents.MatchWon; }
            Near.StopSwing();
            Far.StopSwing();
            Phase = MatchPhase.Gap;
            _gap.Arm(_config.PointGapTicks);
        }

        private void StartPoint()
        {
            Phase = MatchPhase.Point;
            Rally = new Rally(Score.Server, Score.ServeCourt);
            LastBounce = null;
            var receiver = Court.Other(Rally.Server);
            AthleteOf(receiver).Reset(Positions.Receive(receiver, Rally.ServeCourt, _config));
            PlaceForServe();
        }

        /// <summary>サーバーを立ち位置に戻し、球を持たせる（ポイントの始めと、フォルトの後）。</summary>
        private void PlaceForServe()
        {
            var server = AthleteOf(Rally.Server);
            server.Reset(Positions.Serve(Rally.Server, Rally.ServeCourt, _config));
            Ball.Place(server.Position.WithZ(_config.TossZ), Vec3.Zero);
        }
    }
}
