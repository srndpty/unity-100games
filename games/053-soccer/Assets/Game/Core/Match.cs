using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using IntGeometry;
using TileMotion;

namespace Game.Core
{
    public enum MatchPhase
    {
        /// <summary>得点の後、キックオフの並びへ戻る間（球は止まっている）。</summary>
        Stopped,

        /// <summary>キックオフ：蹴る人だけが蹴れる。相手はセンターの円に入れない。</summary>
        Kickoff,

        Play,

        /// <summary>得点の後の間。得点の判定はしない。</summary>
        Goal,

        /// <summary>試合終了。命令を受け付けない。</summary>
        Ended,
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

        /// <summary>球が壁で跳ね返った。</summary>
        Wall,

        /// <summary>得点（Team が得点したチーム）。</summary>
        Goal,

        /// <summary>キックオフの準備ができた（Team・Player は蹴る人）。</summary>
        Kickoff,

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
    /// 3 対 3（GK なし、各チーム 3 人とも自由に動く）の 1 試合（1 試合 = 1 シード）。コートは壁で囲まれ、球はゴールの口以外で跳ね返る（ライン外はない）。
    /// 命令（各選手の移動・行動）を受けて 1 Tick ずつ進める。
    /// <para>
    /// 1 Tick の順序：時計 → 段（得点の間・キックオフの待ち）の進行 → 残り Tick の減少 → 行動（蹴る・タックル）→ 移動（加速と最高速）→
    /// 押し合い・キックオフの距離 → 球（持っていれば足元へ、そうでなければ Step と保持の判定）→ 得点 → 試合終了。
    /// </para>
    /// 選手の番号：0〜2 が青、3〜5 が赤。
    /// </summary>
    public sealed class Match
    {
        public const int PlayerCount = 6;

        /// <summary>1 チームの人数。</summary>
        public const int TeamSize = 3;

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
            for (int i = 0; i < PlayerCount; i++) { _players[i] = new Footballer(i, TeamOf(i)); }
            _playersView = Array.AsReadOnly(_players);
            _eventsView = _events.AsReadOnly();
            BeginKickoff(Team.Blue, immediate: true);
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
            KickoffTeam = other.KickoffTeam;
            Taker = other.Taker;
            KickCount = other.KickCount;
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

        /// <summary>今の段（得点の間・キックオフの待ち・キックオフ）に入ってからの Tick。</summary>
        public int PhaseTicks { get; private set; }

        public IReadOnlyList<Footballer> Players => _playersView;

        public Ball Ball => _ball;

        /// <summary>球を持っている選手（いなければ -1）。</summary>
        public int Owner { get; private set; } = -1;

        /// <summary>最後に球に触れたチーム。</summary>
        public Team LastTouch { get; private set; }

        /// <summary>今の（直前の）キックオフのチーム。</summary>
        public Team KickoffTeam { get; private set; }

        /// <summary>キックオフで蹴る人（キックオフでなければ -1）。</summary>
        public int Taker { get; private set; } = -1;

        /// <summary>これまでに蹴った回数（キックのぶれの乱数の番号）。</summary>
        public int KickCount { get; private set; }

        public int Score(Team team) => _score[(int)team];

        /// <summary>最後の Tick に起きた出来事。</summary>
        public IReadOnlyList<MatchEvent> Events => _eventsView;

        public Match Clone() => new(this);

        /// <summary>チームの選手の番号（<paramref name="slot"/> は 0〜2）。</summary>
        public static int PlayerOf(Team team, int slot) => (team == Team.Blue ? 0 : TeamSize) + slot;

        public static Team TeamOf(int player) => player < TeamSize ? Team.Blue : Team.Red;

        /// <summary>
        /// 1 Tick 進める。<paramref name="commands"/> は選手の番号順に 6 つ。終わった試合では何もしない。
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

        /// <summary>状態のハッシュ（時計・段・得点・球・選手・保持・キックオフ・キックの回数）。</summary>
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
            h.Mix((int)KickoffTeam);
            h.Mix(Taker);
            h.Mix(KickCount);
            h.Mix((int)_lastGoalTeam);
            return h.Value;
        }

        // ---- 場面を作る口（テスト・撮影） ----

        /// <summary>インプレーの場面にする（キックオフの制約を外す）。</summary>
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
            CarryBall();
        }

        internal void SetClock(int clock) => Clock = clock;

        // ---- 段 ----

        private void AdvancePhase()
        {
            switch (Phase)
            {
                case MatchPhase.Goal when PhaseTicks >= _config.GoalCelebrationTicks:
                    // 失点した側のキックオフ。
                    BeginKickoff(Pitch.Opponent(_lastGoalTeam), immediate: false);
                    break;
                case MatchPhase.Stopped when PhaseTicks >= _config.RestartDelayTicks:
                    SetUpKickoff();
                    break;
                case MatchPhase.Kickoff when PhaseTicks >= _config.KickoffTimeoutTicks && Taker >= 0 && Owner == Taker:
                    // 蹴る人が蹴らなければ、いちばん近い味方へ出す（試合が止まり続けない）。
                    Kick(Taker, PlayerAction.Pass, _players[NearestTeammate(Taker)].Position);
                    break;
                default:
                    break;
            }
        }

        private void BeginKickoff(Team team, bool immediate)
        {
            KickoffTeam = team;
            Owner = -1;
            Taker = -1;
            _ball.Place(Vec2i.Zero);
            PlaceFormation();
            Phase = MatchPhase.Stopped;
            PhaseTicks = 0;
            if (immediate) { SetUpKickoff(); }
        }

        /// <summary>キックオフの準備：真ん中の選手（スロット 1）を球の後ろに置いて球を持たせる。</summary>
        private void SetUpKickoff()
        {
            var team = KickoffTeam;
            var taker = PlayerOf(team, 1);
            var p = _players[taker];
            var toward = new Vec2i(Pitch.Direction(team) * Footballer.FacingLength, 0);
            p.Facing = toward;
            p.Position = Vec2i.Zero - IntMath.Scale(toward, _config.DribbleOffset);
            p.Velocity = Vec2i.Zero;
            p.Recover = 0;
            Taker = taker;
            Owner = taker;
            LastTouch = team;
            _ball.Place(Vec2i.Zero);
            Phase = MatchPhase.Kickoff;
            PhaseTicks = 0;
            _events.Add(new MatchEvent(MatchEventKind.Kickoff, taker, team));
        }

        /// <summary>キックオフの並び：自陣の後ろ・左・右。</summary>
        private void PlaceFormation()
        {
            foreach (var p in _players)
            {
                var dir = Pitch.Direction(p.Team);
                var slot = p.Index - PlayerOf(p.Team, 0);
                p.Position = slot switch
                {
                    0 => new Vec2i(-dir * 4000, -4000),
                    1 => new Vec2i(-dir * 9000, 0),
                    _ => new Vec2i(-dir * 4000, 4000),
                };
                p.Velocity = Vec2i.Zero;
                p.Facing = new Vec2i(dir * Footballer.FacingLength, 0);
                p.Recover = 0;
                p.KickCooldown = 0;
            }
        }

        // ---- 行動 ----

        private void Act(IReadOnlyList<PlayerCommand> commands)
        {
            if (Phase is not (MatchPhase.Play or MatchPhase.Kickoff)) { return; }
            for (int i = 0; i < PlayerCount; i++)
            {
                var p = _players[i];
                if (!p.CanAct) { continue; }
                var c = commands[i];
                switch (c.Action)
                {
                    case PlayerAction.Pass:
                    case PlayerAction.Lob:
                    case PlayerAction.Shoot:
                        if (Owner != i || p.KickCooldown > 0) { break; }
                        if (Phase == MatchPhase.Kickoff && Taker != i) { break; }
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
            LastTouch = p.Team;
            p.KickCooldown = _config.KickCooldownTicks;
            if (Phase == MatchPhase.Kickoff)
            {
                Phase = MatchPhase.Play;
                PhaseTicks = 0;
            }

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
                    // 受け手の位置で PassArriveSpeed が残るいちばん遅い速さ（本番と同じ減り方で作った表から）。
                    return (IntMath.Scale(d, Passes.SpeedFor(distance)), 0);
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
            if (Owner < 0 || _players[Owner].Team == t.Team
                || Vec2i.DistanceSquared(t.Position, _ball.Position) > (long)_config.TackleRadius * _config.TackleRadius)
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
                if (!p.CanAct || (Phase == MatchPhase.Kickoff && Taker == p.Index) || Phase is MatchPhase.Ended or MatchPhase.Stopped or MatchPhase.Goal)
                {
                    desired = Vec2i.Zero;
                }
                else
                {
                    var move = commands[p.Index].Move;

                    // 移動の長さが最高速以下なら、その分だけ動く（目標の点に止まれる）。長ければ最高速。
                    desired = move.LengthSquared <= (long)max * max ? move : IntMath.Scale(move, max);
                    if (!move.IsZero) { p.Facing = IntMath.Scale(move, Footballer.FacingLength); }
                }

                var dv = desired - p.Velocity;
                var accel = _config.PlayerAcceleration;
                if (dv.LengthSquared > (long)accel * accel) { dv = IntMath.Scale(dv, accel); }
                p.Velocity += dv;
                p.Position += p.Velocity;
                Clamp(p);
            }
        }

        /// <summary>選手はコートの中（壁の内側に体が収まる）。</summary>
        private void Clamp(Footballer p)
        {
            var r = _config.PlayerRadius;
            var x = IntMath.Clamp(p.Position.X, -Pitch.HalfLength + r, Pitch.HalfLength - r);
            var y = IntMath.Clamp(p.Position.Y, -Pitch.HalfWidth + r, Pitch.HalfWidth - r);
            var pos = new Vec2i(x, y);
            if (pos != p.Position)
            {
                // 壁に当たった向きの速さだけ 0 にする（壁沿いには走れる）。
                var v = p.Velocity;
                p.Velocity = new Vec2i(x != p.Position.X ? 0 : v.X, y != p.Position.Y ? 0 : v.Y);
                p.Position = pos;
            }
        }

        /// <summary>重なった選手を押し離す（番号の順）。キックオフでは、相手をセンターの円から出す。</summary>
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

            if (Phase is not (MatchPhase.Kickoff or MatchPhase.Stopped)) { return; }
            foreach (var p in _players)
            {
                if (p.Team == KickoffTeam) { continue; }
                var d = p.Position;
                if (d.LengthSquared >= (long)Pitch.ExclusionRadius * Pitch.ExclusionRadius) { continue; }
                var n = d.IsZero ? new Vec2i(-Pitch.Direction(KickoffTeam), 0) : d;
                p.Position = IntMath.Scale(n, Pitch.ExclusionRadius);
                p.Velocity = Vec2i.Zero;
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
            if ((hit & BallHit.Wall) != 0) { _events.Add(new MatchEvent(MatchEventKind.Wall)); }
            Capture(starts, from, before);
        }

        private void CarryBall()
        {
            var o = _players[Owner];
            var at = o.Position + IntMath.Scale(o.Facing, _config.DribbleOffset);

            // 足元の球も壁の内側に置く（ゴールの口だけは越えられる）。
            var r = _config.BallRadius;
            var y = IntMath.Clamp(at.Y, -(Pitch.HalfWidth - r), Pitch.HalfWidth - r);
            var x = Math.Abs(y) < Pitch.GoalHalfWidth ? at.X : IntMath.Clamp(at.X, -(Pitch.HalfLength - r), Pitch.HalfLength - r);
            _ball.Carry(new Vec2i(x, y), o.Velocity);
        }

        /// <summary>
        /// 球を止める選手を決める。Tick の中の選手と球の軌跡どうしが「届く距離」より近づき、高さが膝以下で、相対速度が止められる速さ以下の選手のうち、
        /// 終わりの位置で球に最も近い選手（同じなら番号の小さい方）。
        /// </summary>
        private void Capture(Vec2i[] starts, Vec2i ballFrom, Vec2i ballVelocity)
        {
            var best = -1;
            var bestDistance = long.MaxValue;
            for (int i = 0; i < PlayerCount; i++)
            {
                var p = _players[i];
                if (!p.CanAct || p.KickCooldown > 0) { continue; }
                if (_ball.Height > _config.KneeHeight) { continue; }
                if ((ballVelocity - p.Velocity).LengthSquared > (long)_config.TrapSpeed * _config.TrapSpeed) { continue; }
                if (!Sweep.Touches(ballFrom, _ball.Position, starts[i], p.Position, _config.ControlRadius)) { continue; }
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
            var o = _players[best];
            var toBall = _ball.Position - o.Position;
            if (!toBall.IsZero) { o.Facing = IntMath.Scale(toBall, Footballer.FacingLength); }
            CarryBall();
            _events.Add(new MatchEvent(MatchEventKind.Capture, best, o.Team));
        }

        // ---- 得点 ----

        /// <summary>
        /// 球の全体がゴールの口（ポストの間・クロスバーの下）からゴールラインを越えたら得点（得点の間に入り、以後は判定しない）。
        /// ゴールの口の外は壁なので、球はそれ以外でラインを越えない（<see cref="Ball.Step"/>）。
        /// </summary>
        private void Judge()
        {
            var b = _ball.Position;
            if (Math.Abs(b.X) - _config.BallRadius <= Pitch.HalfLength) { return; }
            var goalX = Math.Sign(b.X) * Pitch.HalfLength;
            ScoreGoal(Pitch.AttackGoalX(Team.Blue) == goalX ? Team.Blue : Team.Red);
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

        private int NearestTeammate(int player)
        {
            var p = _players[player];
            var best = -1;
            var bestDistance = long.MaxValue;
            for (int slot = 0; slot < TeamSize; slot++)
            {
                var m = PlayerOf(p.Team, slot);
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
