using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using IntGeometry;
using TileMotion;

namespace Game.Core
{
    public enum MatchPhase
    {
        /// <summary>得点・ライン外の後、再開の位置へ戻るまで（球は止まっている）。</summary>
        Stopped,

        /// <summary>再開（キックオフ・スローイン・ゴールキック・コーナー）：蹴る人だけが蹴れる。相手は球から離れる。</summary>
        SetPiece,

        Play,

        /// <summary>得点の後の間。得点の判定はしない。</summary>
        Goal,

        /// <summary>試合終了。命令を受け付けない。</summary>
        Ended,
    }

    public enum RestartKind
    {
        Kickoff,
        ThrowIn,
        GoalKick,
        Corner,
    }

    public enum MatchEventKind
    {
        /// <summary>蹴った（Player・Value は <see cref="PlayerAction"/>）。</summary>
        Kick,

        /// <summary>球を自分のものにした（Player）。</summary>
        Capture,

        /// <summary>タックルで球を弾いた（Player がタックルした選手、Value が奪われた選手）。</summary>
        Tackle,

        /// <summary>タックルを空振りした（Player）。</summary>
        TackleMiss,

        /// <summary>球がポストに当たった。</summary>
        Post,

        /// <summary>得点（Team が得点したチーム）。</summary>
        Goal,

        /// <summary>球がライン外に出た（Team が再開するチーム、Value は <see cref="RestartKind"/>）。</summary>
        Out,

        /// <summary>再開の準備ができた（Team・Value は <see cref="RestartKind"/>、Player は蹴る人）。</summary>
        SetPiece,

        /// <summary>試合終了。</summary>
        Whistle,
    }

    public readonly struct MatchEvent
    {
        public MatchEvent(MatchEventKind kind, int player = -1, Team team = Team.Blue, int value = 0)
        {
            Kind = kind;
            Player = player;
            Team = team;
            Value = value;
        }

        public MatchEventKind Kind { get; }
        public int Player { get; }
        public Team Team { get; }
        public int Value { get; }

        public override string ToString() => $"{Kind}(p{Player}, {Team}, {Value})";
    }

    /// <summary>
    /// 3 対 3（各チーム GK 1・フィールド 2）の 1 試合（1 試合 = 1 シード）。命令（各選手の移動・行動）を受けて 1 Tick ずつ進める。
    /// <para>
    /// 1 Tick の順序：時計 → 段（得点の間・再開の待ち）の進行 → 残り Tick の減少 → 行動（蹴る・タックル。GK は自動）→ 移動（加速と最高速）→
    /// 押し合い・再開の距離 → 球（持っていれば足元へ、そうでなければ Step と保持の判定）→ 得点・ライン外 → 試合終了。
    /// </para>
    /// 選手の番号：0 青 GK・1, 2 青フィールド・3 赤 GK・4, 5 赤フィールド。GK の命令は無視する（Core が自動で動かす）。
    /// </summary>
    public sealed class Match
    {
        public const int PlayerCount = 6;

        /// <summary>乱数の用途（キックのぶれ）。</summary>
        public const int KickPurpose = 1;

        private readonly SoccerConfig _config;
        private readonly Footballer[] _players;
        private readonly ReadOnlyCollection<Footballer> _playersView;
        private readonly int[] _score = new int[2];
        private readonly List<MatchEvent> _events = new();
        private readonly ReadOnlyCollection<MatchEvent> _eventsView;
        private readonly Ball _ball = new();

        /// <summary>最後に得点したチーム（得点の間の後、相手のキックオフ）。</summary>
        private Team _lastGoalTeam;

        public Match(SoccerConfig config, int seed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            var errors = config.Validate();
            if (errors.Count > 0) { throw new ArgumentException(string.Join("\n", errors), nameof(config)); }
            _config = config.Copy();
            Passes = new PassTable(_config);
            Seed = seed;
            _players = new Footballer[PlayerCount];
            for (int i = 0; i < PlayerCount; i++)
            {
                var team = i < 3 ? Team.Blue : Team.Red;
                _players[i] = new Footballer(i, team, i % 3 == 0);
            }

            _playersView = Array.AsReadOnly(_players);
            _eventsView = _events.AsReadOnly();
            BeginRestart(RestartKind.Kickoff, Team.Blue, Vec2i.Zero, immediate: true);
        }

        private Match(Match other)
        {
            _config = other._config;
            Passes = other.Passes;
            Seed = other.Seed;
            _players = new Footballer[PlayerCount];
            for (int i = 0; i < PlayerCount; i++) { _players[i] = other._players[i].Clone(); }
            _playersView = Array.AsReadOnly(_players);
            _eventsView = _events.AsReadOnly();
            other._score.CopyTo(_score, 0);
            _ball = other._ball.Clone();
            Clock = other.Clock;
            Phase = other.Phase;
            PhaseTicks = other.PhaseTicks;
            Owner = other.Owner;
            LastTouch = other.LastTouch;
            Restart = other.Restart;
            RestartTeam = other.RestartTeam;
            RestartSpot = other.RestartSpot;
            Taker = other.Taker;
            KickCount = other.KickCount;
            KeeperHold = other.KeeperHold;
            _lastGoalTeam = other._lastGoalTeam;
        }

        /// <summary>試合で使う設定（検証を通った複製）。書き換えないこと。</summary>
        internal SoccerConfig Config => _config;

        public int Seed { get; }

        /// <summary>地を這うパスの速さの表（設定から作る。複製で共有する）。</summary>
        public PassTable Passes { get; }

        /// <summary>経過 Tick（試合時間）。</summary>
        public int Clock { get; private set; }

        public int RemainingTicks => Math.Max(0, _config.MatchTicks - Clock);

        public MatchPhase Phase { get; private set; }

        /// <summary>今の段（得点の間・再開の待ち・再開）に入ってからの Tick。</summary>
        public int PhaseTicks { get; private set; }

        public IReadOnlyList<Footballer> Players => _playersView;

        public Ball Ball => _ball;

        /// <summary>球を持っている選手（いなければ -1）。</summary>
        public int Owner { get; private set; } = -1;

        /// <summary>最後に球に触れたチーム（ライン外の再開を決める）。</summary>
        public Team LastTouch { get; private set; }

        public RestartKind Restart { get; private set; }

        public Team RestartTeam { get; private set; }

        public Vec2i RestartSpot { get; private set; }

        /// <summary>再開で蹴る人（再開でなければ -1）。</summary>
        public int Taker { get; private set; } = -1;

        /// <summary>これまでに蹴った回数（キックのぶれの乱数の番号）。</summary>
        public int KickCount { get; private set; }

        /// <summary>GK が球を持ってからの Tick。</summary>
        public int KeeperHold { get; private set; }

        public int Score(Team team) => _score[(int)team];

        /// <summary>最後の Tick に起きた出来事。</summary>
        public IReadOnlyList<MatchEvent> Events => _eventsView;

        public Match Clone() => new(this);

        public Footballer Keeper(Team team) => _players[team == Team.Blue ? 0 : 3];

        /// <summary>チームのフィールドの選手 2 人の番号。</summary>
        public static int FieldPlayer(Team team, int slot) => (team == Team.Blue ? 1 : 4) + slot;

        /// <summary>チームの選手か（GK を含む）。</summary>
        public static Team TeamOf(int player) => player < 3 ? Team.Blue : Team.Red;

        /// <summary>
        /// 1 Tick 進める。<paramref name="commands"/> は選手の番号順に 6 つ（GK の分は無視する）。終わった試合では何もしない。
        /// </summary>
        public void Step(IReadOnlyList<PlayerCommand> commands)
        {
            if (commands == null) { throw new ArgumentNullException(nameof(commands)); }
            if (commands.Count != PlayerCount) { throw new ArgumentException($"命令は {PlayerCount} 人分です（{commands.Count}）", nameof(commands)); }
            _events.Clear();
            if (Phase == MatchPhase.Ended) { return; }

            Clock++;
            PhaseTicks++;
            AdvancePhase();
            foreach (var p in _players)
            {
                if (p.Recover > 0) { p.Recover--; }
                if (p.KickCooldown > 0) { p.KickCooldown--; }
            }

            if (Owner >= 0 && _players[Owner].IsKeeper) { KeeperHold++; }

            Act(commands);
            var starts = new Vec2i[PlayerCount];
            for (int i = 0; i < PlayerCount; i++) { starts[i] = _players[i].Position; }
            Move(commands);
            Separate();
            MoveBall(starts);
            if (Phase == MatchPhase.Play) { Judge(); }

            if (Clock >= _config.MatchTicks)
            {
                Phase = MatchPhase.Ended;
                Owner = -1;
                _events.Add(new MatchEvent(MatchEventKind.Whistle));
            }
        }

        /// <summary>状態のハッシュ（時計・段・得点・球・選手・保持・再開・キックの回数）。</summary>
        public uint Hash()
        {
            var h = StateHash.Start();
            h.Mix(Seed);
            h.Mix(Clock);
            h.Mix((int)Phase);
            h.Mix(PhaseTicks);
            h.Mix(_score[0]);
            h.Mix(_score[1]);
            h.Mix(_ball.Position.X);
            h.Mix(_ball.Position.Y);
            h.Mix(_ball.Height);
            h.Mix(_ball.Velocity.X);
            h.Mix(_ball.Velocity.Y);
            h.Mix(_ball.VerticalSpeed);
            foreach (var p in _players)
            {
                h.Mix(p.Position.X);
                h.Mix(p.Position.Y);
                h.Mix(p.Velocity.X);
                h.Mix(p.Velocity.Y);
                h.Mix(p.Facing.X);
                h.Mix(p.Facing.Y);
                h.Mix(p.Recover);
                h.Mix(p.KickCooldown);
            }

            h.Mix(Owner);
            h.Mix((int)LastTouch);
            h.Mix((int)Restart);
            h.Mix((int)RestartTeam);
            h.Mix(RestartSpot.X);
            h.Mix(RestartSpot.Y);
            h.Mix(Taker);
            h.Mix(KickCount);
            h.Mix(KeeperHold);
            h.Mix((int)_lastGoalTeam);
            return h.Value;
        }

        // ---- 場面を作る口（テスト・撮影） ----

        /// <summary>インプレーの場面にする（再開の制約を外す）。</summary>
        internal void ForcePlay()
        {
            Phase = MatchPhase.Play;
            PhaseTicks = 0;
            Taker = -1;
        }

        internal void PlaceBall(Vec2i position, Vec2i velocity, int height = 0, int verticalSpeed = 0)
        {
            Owner = -1;
            _ball.Place(position, height);
            _ball.Launch(velocity, verticalSpeed);
        }

        internal void PlacePlayer(int index, Vec2i position, Vec2i velocity = default)
        {
            _players[index].Position = position;
            _players[index].Velocity = velocity;
        }

        internal void GiveBall(int index)
        {
            Owner = index;
            LastTouch = _players[index].Team;
            KeeperHold = 0;
            CarryBall();
        }

        internal void SetClock(int clock) => Clock = clock;

        internal void SetLastTouch(Team team) => LastTouch = team;

        // ---- 段 ----

        private void AdvancePhase()
        {
            switch (Phase)
            {
                case MatchPhase.Goal when PhaseTicks >= _config.GoalCelebrationTicks:
                    // 失点した側のキックオフ。
                    var conceded = _lastGoalTeam == Team.Blue ? Team.Red : Team.Blue;
                    BeginRestart(RestartKind.Kickoff, conceded, Vec2i.Zero, immediate: false);
                    break;
                case MatchPhase.Stopped when PhaseTicks >= _config.RestartDelayTicks:
                    SetUpSetPiece();
                    break;
                case MatchPhase.SetPiece when PhaseTicks >= _config.SetPieceTimeoutTicks && Taker >= 0 && Owner == Taker:
                    // 蹴る人が蹴らなければ、いちばん近い味方へ出す（試合が止まり続けない）。
                    var mate = NearestTeammate(Taker);
                    Kick(Taker, PlayerAction.Pass, _players[mate].Position);
                    break;
                default:
                    break;
            }
        }

        private void BeginRestart(RestartKind kind, Team team, Vec2i spot, bool immediate)
        {
            Restart = kind;
            RestartTeam = team;
            RestartSpot = spot;
            Owner = -1;
            Taker = -1;
            _ball.Place(spot);
            if (kind == RestartKind.Kickoff) { PlaceFormation(); }
            Phase = MatchPhase.Stopped;
            PhaseTicks = 0;
            if (immediate) { SetUpSetPiece(); }
        }

        /// <summary>再開の準備：蹴る人を選び、球の後ろに置いて球を持たせる。</summary>
        private void SetUpSetPiece()
        {
            var team = RestartTeam;
            int taker;
            if (Restart == RestartKind.GoalKick)
            {
                taker = Keeper(team).Index;
            }
            else
            {
                var a = FieldPlayer(team, 0);
                var b = FieldPlayer(team, 1);
                taker = Vec2i.DistanceSquared(_players[b].Position, RestartSpot) < Vec2i.DistanceSquared(_players[a].Position, RestartSpot) ? b : a;
            }

            var p = _players[taker];
            var toward = RestartTowardField(team);
            p.Facing = toward;
            p.Position = RestartSpot - IntMath.Scale(toward, _config.DribbleOffset);
            p.Velocity = Vec2i.Zero;
            p.Recover = 0;
            Taker = taker;
            Owner = taker;
            KeeperHold = 0;
            LastTouch = team;
            _ball.Place(RestartSpot);
            Phase = MatchPhase.SetPiece;
            PhaseTicks = 0;
            _events.Add(new MatchEvent(MatchEventKind.SetPiece, taker, team, (int)Restart));
        }

        /// <summary>再開で蹴る人が向く向き：キックオフは攻める向き、ほかはコートの中心へ。</summary>
        private Vec2i RestartTowardField(Team team)
        {
            if (Restart == RestartKind.Kickoff) { return new Vec2i(Pitch.Direction(team) * Footballer.FacingLength, 0); }
            var d = Vec2i.Zero - RestartSpot;
            return d.IsZero ? new Vec2i(Pitch.Direction(team) * Footballer.FacingLength, 0) : IntMath.Scale(d, Footballer.FacingLength);
        }

        /// <summary>キックオフの並び：GK はゴール前、フィールドは自陣の左右。</summary>
        private void PlaceFormation()
        {
            foreach (var p in _players)
            {
                var dir = Pitch.Direction(p.Team);
                if (p.IsKeeper)
                {
                    p.Position = new Vec2i(Pitch.OwnGoalX(p.Team) + (dir * 800), 0);
                }
                else
                {
                    var slot = p.Index - FieldPlayer(p.Team, 0);
                    p.Position = new Vec2i(-dir * 4000, slot == 0 ? -3000 : 3000);
                }

                p.Velocity = Vec2i.Zero;
                p.Facing = new Vec2i(dir * Footballer.FacingLength, 0);
                p.Recover = 0;
                p.KickCooldown = 0;
            }
        }

        // ---- 行動 ----

        private void Act(IReadOnlyList<PlayerCommand> commands)
        {
            if (Phase is not (MatchPhase.Play or MatchPhase.SetPiece)) { return; }
            if (Owner >= 0 && _players[Owner].IsKeeper && KeeperHold >= _config.KeeperHoldTicks)
            {
                var k = Owner;
                Kick(k, PlayerAction.Pass, _players[NearestTeammate(k)].Position);
            }

            for (int i = 0; i < PlayerCount; i++)
            {
                var p = _players[i];
                if (p.IsKeeper || !p.CanAct) { continue; }
                var c = commands[i];
                switch (c.Action)
                {
                    case PlayerAction.Pass:
                    case PlayerAction.Lob:
                    case PlayerAction.Shoot:
                        if (Owner != i || p.KickCooldown > 0) { break; }
                        if (Phase == MatchPhase.SetPiece && (Taker != i || (Restart == RestartKind.ThrowIn && c.Action == PlayerAction.Shoot))) { break; }
                        Kick(i, c.Action, c.Target);
                        break;
                    case PlayerAction.Tackle:
                        if (Phase == MatchPhase.Play && Owner != i) { Tackle(i); }
                        break;
                    default:
                        break;
                }
            }
        }

        private void Kick(int kicker, PlayerAction action, Vec2i target)
        {
            var p = _players[kicker];
            var from = _ball.Position;
            var rng = RandomSeries.Derive(Seed, KickCount, KickPurpose);
            KickCount++;
            var spread = action == PlayerAction.Shoot ? _config.ShotSpread : _config.PassSpread;
            var aim = target - from;
            if (aim.IsZero) { aim = p.Facing; }

            // 狙いの点を、狙いの向きと直角にぶらす（シュートはゴールの位置で、パスは受け手の位置で ± spread）。
            var offset = rng.Range(-spread, spread);
            var perpendicular = IntMath.Scale(new Vec2i(-aim.Y, aim.X), Math.Abs(offset));
            var aimed = target + (offset < 0 ? -perpendicular : perpendicular);
            var (velocity, vertical) = KickVelocity(action, from, aimed);
            _ball.Launch(velocity, vertical);
            Owner = -1;
            Taker = -1;
            KeeperHold = 0;
            LastTouch = p.Team;
            p.KickCooldown = _config.KickCooldownTicks;
            if (Phase == MatchPhase.SetPiece) { Phase = MatchPhase.Play; PhaseTicks = 0; }
            _events.Add(new MatchEvent(MatchEventKind.Kick, kicker, p.Team, (int)action));
        }

        /// <summary>蹴る速度（横・縦）。パスは受け手の位置で <see cref="SoccerConfig.PassArriveSpeed"/> が残る速さ、ロブは着地が目標になる速さ。</summary>
        internal (Vec2i Velocity, int Vertical) KickVelocity(PlayerAction action, Vec2i from, Vec2i target)
        {
            var c = _config;
            var d = target - from;
            var distance = IntMath.Sqrt(d.LengthSquared);
            switch (action)
            {
                case PlayerAction.Pass:
                    {
                        // 受け手の位置で PassArriveSpeed が残るいちばん遅い速さ（本番と同じ減り方で作った表から）。
                        return (IntMath.Scale(d, Passes.SpeedFor(distance)), 0);
                    }

                case PlayerAction.Lob:
                    {
                        var flight = FlightTicks(c.LobVerticalSpeed, c.Gravity);
                        var speed = (int)Math.Min(c.MaxLobSpeed, Math.Max(1, (distance + flight - 1) / flight));
                        return (IntMath.Scale(d, speed), c.LobVerticalSpeed);
                    }

                default:
                    return (IntMath.Scale(d, c.ShotSpeed), c.ShotVerticalSpeed);
            }
        }

        /// <summary>縦の速さ vz で地面から上げた球が、次に地面に着くまでの Tick（<see cref="Ball.Step"/> と同じ式）。</summary>
        internal static int FlightTicks(int verticalSpeed, int gravity)
        {
            long z = 0;
            long v = verticalSpeed;
            var t = 0;
            do
            {
                v -= gravity;
                z += v;
                t++;
            }
            while (z > 0);
            return t;
        }

        private void Tackle(int tackler)
        {
            var t = _players[tackler];
            if (Owner < 0 || _players[Owner].Team == t.Team || _players[Owner].IsKeeper)
            {
                t.Recover = _config.TackleMissRecoverTicks;
                _events.Add(new MatchEvent(MatchEventKind.TackleMiss, tackler, t.Team));
                return;
            }

            if (Vec2i.DistanceSquared(t.Position, _ball.Position) > (long)_config.TackleRadius * _config.TackleRadius)
            {
                t.Recover = _config.TackleMissRecoverTicks;
                _events.Add(new MatchEvent(MatchEventKind.TackleMiss, tackler, t.Team));
                return;
            }

            var victim = Owner;
            var away = _ball.Position - t.Position;
            if (away.IsZero) { away = t.Facing; }
            Owner = -1;
            _ball.Launch(IntMath.Scale(away, _config.PokeSpeed), 0);
            _players[victim].Recover = _config.TackledStunTicks;
            _players[victim].KickCooldown = _config.KickCooldownTicks;
            t.Recover = _config.TackleRecoverTicks;
            LastTouch = t.Team;
            _events.Add(new MatchEvent(MatchEventKind.Tackle, tackler, t.Team, victim));
        }

        // ---- 移動 ----

        private void Move(IReadOnlyList<PlayerCommand> commands)
        {
            foreach (var p in _players)
            {
                Vec2i desired;
                var max = _config.PlayerMaxSpeed;
                if (Owner == p.Index) { max = max * _config.DribbleSpeedPercent / 100; }
                if (p.IsKeeper)
                {
                    desired = Toward(p.Position, KeeperTarget(p), _config.KeeperSpeed);
                }
                else if (!p.CanAct || (Phase == MatchPhase.SetPiece && Taker == p.Index) || Phase == MatchPhase.Ended)
                {
                    desired = Vec2i.Zero;
                }
                else
                {
                    var move = commands[p.Index].Move;
                    desired = move.IsZero ? Vec2i.Zero : IntMath.Scale(move, max);
                    if (!move.IsZero) { p.Facing = IntMath.Scale(move, Footballer.FacingLength); }
                }

                var dv = desired - p.Velocity;
                var accel = p.IsKeeper ? _config.KeeperSpeed : _config.PlayerAcceleration;
                if (dv.LengthSquared > (long)accel * accel) { dv = IntMath.Scale(dv, accel); }
                p.Velocity += dv;
                p.Position += p.Velocity;
                if (p.IsKeeper && !p.Velocity.IsZero) { p.Facing = IntMath.Scale(p.Velocity, Footballer.FacingLength); }
                Clamp(p);
            }
        }

        /// <summary>目標の点へ向かう速度（近ければ減速して止まる）。</summary>
        internal static Vec2i Toward(Vec2i from, Vec2i to, int speed)
        {
            var d = to - from;
            if (d.IsZero) { return Vec2i.Zero; }
            var distance = IntMath.Sqrt(d.LengthSquared);
            return distance <= speed ? d : IntMath.Scale(d, speed);
        }

        private static void Clamp(Footballer p)
        {
            var x = IntMath.Clamp(p.Position.X, -Pitch.HalfLength - Pitch.RunOffMargin, Pitch.HalfLength + Pitch.RunOffMargin);
            var y = IntMath.Clamp(p.Position.Y, -Pitch.HalfWidth - Pitch.RunOffMargin, Pitch.HalfWidth + Pitch.RunOffMargin);
            var pos = new Vec2i(x, y);
            if (p.IsKeeper)
            {
                // GK はゴールの中心からの半円の中（コートの内側）。
                var goal = new Vec2i(Pitch.OwnGoalX(p.Team), 0);
                var dir = Pitch.Direction(p.Team);
                if ((pos.X - goal.X) * dir < 0) { pos = new Vec2i(goal.X, pos.Y); }
                var d = pos - goal;
                if (d.LengthSquared > (long)Pitch.KeeperAreaRadius * Pitch.KeeperAreaRadius) { pos = goal + IntMath.Scale(d, Pitch.KeeperAreaRadius); }
            }

            if (pos != p.Position)
            {
                p.Position = pos;
                p.Velocity = Vec2i.Zero;
            }
        }

        /// <summary>GK の目標：シュートがゴールラインを越える位置、球が自分の範囲で転がっていれば球、それ以外はゴールと球を結ぶ線の上。</summary>
        private Vec2i KeeperTarget(Footballer k)
        {
            var goal = new Vec2i(Pitch.OwnGoalX(k.Team), 0);
            var dir = Pitch.Direction(k.Team);
            if (Owner == k.Index) { return k.Position; }
            if (Owner < 0 && Phase == MatchPhase.Play)
            {
                var crossing = GoalCrossing(k.Team);
                if (crossing.HasValue) { return new Vec2i(goal.X + (dir * 400), crossing.Value); }
                if (Vec2i.DistanceSquared(_ball.Position, goal) < (long)Pitch.KeeperAreaRadius * Pitch.KeeperAreaRadius) { return _ball.Position; }
            }

            var toBall = _ball.Position - goal;
            if (toBall.IsZero) { return goal + new Vec2i(dir * 1000, 0); }
            var guard = goal + IntMath.Scale(toBall, 1200);
            return new Vec2i(guard.X, IntMath.Clamp(guard.Y, -Pitch.GoalHalfWidth - 300, Pitch.GoalHalfWidth + 300));
        }

        /// <summary>このまま誰も触らなければ、球が自陣のゴールの口（ポストの間・クロスバーの下）を越える横の位置。越えなければ null。</summary>
        private int? GoalCrossing(Team defending)
        {
            var goalX = Pitch.OwnGoalX(defending);
            var dir = Pitch.Direction(defending);
            if (_ball.Velocity.X * dir >= 0) { return null; }
            foreach (var s in BallPredictor.Trajectory(_ball, _config, 120))
            {
                if ((s.Position.X - goalX) * dir > 0) { continue; }
                if (Math.Abs(s.Position.Y) < Pitch.GoalHalfWidth + 400 && s.Height < Pitch.CrossbarHeight) { return IntMath.Clamp(s.Position.Y, -Pitch.GoalHalfWidth, Pitch.GoalHalfWidth); }
                return null;
            }

            return null;
        }

        /// <summary>重なった選手を押し離す（番号の順）。再開では、相手を球から離す。</summary>
        private void Separate()
        {
            var min = 2 * _config.PlayerRadius;
            for (int i = 0; i < PlayerCount; i++)
            {
                for (int j = i + 1; j < PlayerCount; j++)
                {
                    var a = _players[i];
                    var b = _players[j];
                    var d = b.Position - a.Position;
                    var dist2 = d.LengthSquared;
                    if (dist2 >= (long)min * min) { continue; }
                    var dist = (int)IntMath.Sqrt(dist2);
                    var push = (min - dist + 1) / 2;
                    var n = d.IsZero ? new Vec2i(0, 1) : d;
                    var shift = IntMath.Scale(n, push);
                    a.Position -= shift;
                    b.Position += shift;
                    Clamp(a);
                    Clamp(b);
                }
            }

            if (Phase is MatchPhase.SetPiece or MatchPhase.Stopped)
            {
                foreach (var p in _players)
                {
                    if (p.Team == RestartTeam || p.IsKeeper) { continue; }
                    var d = p.Position - RestartSpot;
                    if (d.LengthSquared >= (long)Pitch.ExclusionRadius * Pitch.ExclusionRadius) { continue; }
                    var n = d.IsZero ? new Vec2i(-Pitch.Direction(RestartTeam), 0) : d;
                    p.Position = RestartSpot + IntMath.Scale(n, Pitch.ExclusionRadius);
                    p.Velocity = Vec2i.Zero;
                }
            }
        }

        // ---- 球 ----

        private void MoveBall(Vec2i[] starts)
        {
            if (Owner >= 0)
            {
                CarryBall();
                return;
            }

            if (Phase is not MatchPhase.Play) { return; }
            var from = _ball.Position;
            var before = _ball.Velocity;
            var hit = _ball.Step(_config);
            if ((hit & BallHit.Post) != 0) { _events.Add(new MatchEvent(MatchEventKind.Post)); }
            Capture(starts, from, before);
        }

        private void CarryBall()
        {
            var o = _players[Owner];
            _ball.Carry(o.Position + IntMath.Scale(o.Facing, o.IsKeeper ? 0 : _config.DribbleOffset), o.Velocity);
        }

        /// <summary>
        /// 球を止める選手を決める。Tick の中の選手と球の軌跡どうしが「届く距離」より近づき、高さが上限以下で、相対速度が止められる速さ以下の選手のうち、
        /// 終わりの位置で球に最も近い選手（同じなら番号の小さい方）。GK は届く距離・高さが大きく、速さを問わない。
        /// </summary>
        private void Capture(Vec2i[] starts, Vec2i ballFrom, Vec2i ballVelocity)
        {
            var best = -1;
            var bestDistance = long.MaxValue;
            for (int i = 0; i < PlayerCount; i++)
            {
                var p = _players[i];
                if (!p.CanAct || p.KickCooldown > 0) { continue; }
                var reach = p.IsKeeper ? _config.KeeperReach : _config.ControlRadius;
                var height = p.IsKeeper ? _config.KeeperReachHeight : _config.KneeHeight;
                if (_ball.Height > height) { continue; }
                if (!p.IsKeeper && (ballVelocity - p.Velocity).LengthSquared > (long)_config.TrapSpeed * _config.TrapSpeed) { continue; }
                if (!Sweep.Touches(ballFrom, _ball.Position, starts[i], p.Position, reach)) { continue; }
                var dist = Vec2i.DistanceSquared(p.Position, _ball.Position);
                if (dist < bestDistance)
                {
                    best = i;
                    bestDistance = dist;
                }
            }

            if (best < 0) { return; }
            Owner = best;
            LastTouch = _players[best].Team;
            KeeperHold = 0;
            var o = _players[best];
            var toBall = _ball.Position - o.Position;
            if (!o.IsKeeper && !toBall.IsZero) { o.Facing = IntMath.Scale(toBall, Footballer.FacingLength); }
            CarryBall();
            _events.Add(new MatchEvent(MatchEventKind.Capture, best, o.Team));
        }

        // ---- 得点・ライン外 ----

        /// <summary>
        /// 球の全体がラインを越えたかを判定する。ゴールの口（ポストの間・クロスバーの下）を越えたら得点（得点の間に入り、以後は判定しない）、
        /// それ以外のゴールラインはゴールキックかコーナー、タッチラインはスローイン。再開は最後に触れた側の相手。
        /// </summary>
        private void Judge()
        {
            var b = _ball.Position;
            var r = _config.BallRadius;
            if (Math.Abs(b.X) - r > Pitch.HalfLength)
            {
                var goalX = Math.Sign(b.X) * Pitch.HalfLength;
                var attacking = Pitch.AttackGoalX(Team.Blue) == goalX ? Team.Blue : Team.Red;
                var defending = Pitch.Opponent(attacking);
                if (Math.Abs(b.Y) < Pitch.GoalHalfWidth && _ball.Height + (2 * r) <= Pitch.CrossbarHeight)
                {
                    ScoreGoal(attacking);
                    return;
                }

                if (LastTouch == attacking)
                {
                    Out(RestartKind.GoalKick, defending, new Vec2i(Pitch.OwnGoalX(defending) + (Pitch.Direction(defending) * 2500), 0));
                }
                else
                {
                    Out(RestartKind.Corner, attacking, new Vec2i(goalX, Math.Sign(b.Y == 0 ? 1 : b.Y) * Pitch.HalfWidth));
                }

                return;
            }

            if (Math.Abs(b.Y) - r > Pitch.HalfWidth)
            {
                var spot = new Vec2i(IntMath.Clamp(b.X, -Pitch.HalfLength, Pitch.HalfLength), Math.Sign(b.Y) * Pitch.HalfWidth);
                Out(RestartKind.ThrowIn, Pitch.Opponent(LastTouch), spot);
            }
        }

        private void ScoreGoal(Team team)
        {
            _score[(int)team]++;
            _lastGoalTeam = team;
            Owner = -1;

            // 球はゴールの中で止める（ネットの中で跳ねても、ラインを戻っても、得点の間は判定しない）。
            var x = IntMath.Clamp(_ball.Position.X, -Pitch.HalfLength - Pitch.GoalDepth, Pitch.HalfLength + Pitch.GoalDepth);
            _ball.Place(new Vec2i(x, _ball.Position.Y));
            Phase = MatchPhase.Goal;
            PhaseTicks = 0;
            _events.Add(new MatchEvent(MatchEventKind.Goal, -1, team));
        }

        private void Out(RestartKind kind, Team team, Vec2i spot)
        {
            Owner = -1;
            _events.Add(new MatchEvent(MatchEventKind.Out, -1, team, (int)kind));
            BeginRestart(kind, team, spot, immediate: false);
        }

        private int NearestTeammate(int player)
        {
            var p = _players[player];
            var best = -1;
            var bestDistance = long.MaxValue;
            for (int slot = 0; slot < 2; slot++)
            {
                var m = FieldPlayer(p.Team, slot);
                if (m == player) { continue; }
                var d = Vec2i.DistanceSquared(_players[m].Position, p.Position);
                if (d < bestDistance)
                {
                    best = m;
                    bestDistance = d;
                }
            }

            return best;
        }
    }
}
