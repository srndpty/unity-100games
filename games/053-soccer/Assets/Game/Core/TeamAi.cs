using System;
using System.Collections.Generic;
using IntGeometry;

namespace Game.Core
{
    /// <summary>オフボールの役割（中心課題）。</summary>
    public enum Role
    {
        None,

        /// <summary>球を持っている（ドリブル・パス・シュートを選ぶ）。</summary>
        Carrier,

        /// <summary>支援：パスコースが相手から離れた点で受ける。</summary>
        Support,

        /// <summary>抜け出し：相手の裏で、ゴールに近い空いた点へ走る。</summary>
        Runner,

        /// <summary>寄せ：保持者とゴールの間へ詰め、届けばタックル。</summary>
        Press,

        /// <summary>カバー：保持者以外の相手とゴールの間に立ち、パスコースを切る。</summary>
        Cover,

        /// <summary>回収：こぼれ球へ一番早く届く選手が、届く点へ走る。</summary>
        Chase,

        /// <summary>待機：球と自陣のゴールの間に立つ。</summary>
        Hold,
    }

    /// <summary>
    /// チームの頭脳（CPU のチームと、人間側の操作していない味方の両方）。人間が操作している選手を除くフィールドの選手に命令を出す。
    /// <para>
    /// 役割の割当：局面（自チームが保持・相手が保持・こぼれ球）ごとの役割の組を、空いている選手へ割り当てる。割当は一定 Tick ごとと局面が変わった Tick に
    /// 決め直し、全ての割当（2 人なので 2 通り）のうち、目標の点への移動 Tick の和が最小のものを選ぶ（同じなら番号の小さい選手が先の役割）。
    /// 人間が操作している選手は役割から外し、その選手の位置に近い役割を人間が受け持つとみなす。
    /// </para>
    /// 保持者（CPU）は一定 Tick ごとに、シュート・パス（<see cref="PassEvaluator"/>）・ドリブルを方針（<see cref="Tactics"/>）で選ぶ。乱数は使わない。
    /// </summary>
    public sealed class TeamAi
    {
        /// <summary>役割を決め直す間隔（Tick）。</summary>
        public const int RoleTicks = 15;

        /// <summary>保持者が判断する間隔（Tick）。</summary>
        public const int DecisionTicks = 8;

        /// <summary>再開で蹴る人が、味方の位置取りを待つ Tick。</summary>
        public const int SetPieceWaitTicks = 40;

        private readonly Role[] _roles = new Role[Match.PlayerCount];
        private readonly Vec2i[] _targets = new Vec2i[Match.PlayerCount];
        private int _lastState = -1;
        private int _lastAssigned = int.MinValue;

        public TeamAi(Team team, Tactics tactics)
        {
            Team = team;
            Tactics = tactics;
        }

        public Team Team { get; }

        public Tactics Tactics { get; }

        public Role RoleOf(int player) => _roles[player];

        /// <summary>役割の目標の点（最後の判断のとき）。</summary>
        public Vec2i TargetOf(int player) => _targets[player];

        public TeamAi Clone()
        {
            var c = new TeamAi(Team, Tactics) { _lastState = _lastState, _lastAssigned = _lastAssigned };
            _roles.CopyTo(c._roles, 0);
            _targets.CopyTo(c._targets, 0);
            return c;
        }

        /// <summary>
        /// チームのフィールドの選手（<paramref name="human"/> を除く）の命令を <paramref name="commands"/> に書く。
        /// <paramref name="human"/> は人間が操作している選手（いなければ -1）。
        /// </summary>
        public void Decide(Match match, int human, PlayerCommand[] commands)
        {
            if (match == null) { throw new ArgumentNullException(nameof(match)); }
            if (commands == null) { throw new ArgumentNullException(nameof(commands)); }
            var field = new[] { Match.FieldPlayer(Team, 0), Match.FieldPlayer(Team, 1) };
            var idle = match.Phase is MatchPhase.Goal or MatchPhase.Ended || (match.Phase == MatchPhase.Stopped && match.Restart == RestartKind.Kickoff);
            if (idle)
            {
                foreach (var f in field)
                {
                    _roles[f] = Role.None;
                    if (f != human) { commands[f] = PlayerCommand.Idle; }
                }

                return;
            }

            var state = State(match);
            if (state != _lastState || match.Clock - _lastAssigned >= RoleTicks)
            {
                Assign(match, human, field, state);
                _lastState = state;
                _lastAssigned = match.Clock;
            }

            foreach (var f in field)
            {
                if (f == human) { continue; }
                if (match.Owner == f || match.Taker == f)
                {
                    _roles[f] = Role.Carrier;
                    commands[f] = Carry(match, f);
                    continue;
                }

                var target = Target(match, f, _roles[f]);
                _targets[f] = target;
                var p = match.Players[f];
                var action = PlayerAction.None;
                if (_roles[f] is Role.Press or Role.Chase && match.Owner >= 0 && match.Players[match.Owner].Team != Team && !match.Players[match.Owner].IsKeeper
                    && Vec2i.DistanceSquared(p.Position, match.Ball.Position) < (long)(match.Config.TackleRadius - 100) * (match.Config.TackleRadius - 100))
                {
                    action = PlayerAction.Tackle;
                }

                commands[f] = new PlayerCommand(target - p.Position, action);
            }
        }

        /// <summary>局面：0 自チームが保持、1 相手が保持、2 こぼれ球、3 自チームの再開、4 相手の再開。</summary>
        private int State(Match m)
        {
            if (m.Phase is MatchPhase.SetPiece or MatchPhase.Stopped) { return m.RestartTeam == Team ? 3 : 4; }
            if (m.Owner < 0) { return 2; }
            return m.Players[m.Owner].Team == Team ? 0 : 1;
        }

        /// <summary>役割を割り当てる。空いている選手と役割の組の、全ての割当のうち移動 Tick の和が最小のもの。</summary>
        private void Assign(Match m, int human, int[] field, int state)
        {
            var free = new List<int>();
            foreach (var f in field)
            {
                if (f == human || m.Owner == f || m.Taker == f) { continue; }
                free.Add(f);
            }

            foreach (var f in field) { _roles[f] = Role.None; }
            if (free.Count == 0) { return; }

            Role[] roles;
            switch (state)
            {
                case 0:
                case 3:
                    roles = free.Count == 2 || human >= 0 && m.Owner != human && m.Taker != human
                        ? new[] { Role.Support, Role.Runner }
                        : new[] { Tactics.Attack == AttackStyle.Direct ? Role.Runner : Role.Support };
                    break;
                case 1:
                case 4:
                    roles = new[] { Role.Press, Role.Cover };
                    break;
                default:
                    roles = new[] { Role.Chase, Role.Hold };
                    break;
            }

            // こぼれ球は、球に一番早く届く選手（人間を含む）が回収する。人間が一番なら、空いている選手は待機。
            if (state == 2)
            {
                var chaser = EarliestToBall(m, field);
                foreach (var f in free) { _roles[f] = f == chaser ? Role.Chase : Role.Hold; }
                return;
            }

            // 人間が受け持つ役割：人間の位置に一番近い目標の役割を外す。
            var pool = new List<Role>(roles);
            if (human >= 0 && pool.Count > free.Count)
            {
                var hp = m.Players[human].Position;
                var drop = pool[0];
                var dropCost = long.MaxValue;
                foreach (var r in pool)
                {
                    var d = Vec2i.DistanceSquared(hp, Target(m, human, r));
                    if (d < dropCost)
                    {
                        drop = r;
                        dropCost = d;
                    }
                }

                pool.Remove(drop);
            }

            if (free.Count == 1)
            {
                _roles[free[0]] = pool[0];
                return;
            }

            // 2 人 × 2 役割：2 通りの和を比べる（同じなら番号の小さい選手が最初の役割）。
            var a = free[0];
            var b = free[1];
            var r0 = pool[0];
            var r1 = pool.Count > 1 ? pool[1] : pool[0];
            var straight = Cost(m, a, r0) + Cost(m, b, r1);
            var swapped = Cost(m, a, r1) + Cost(m, b, r0);
            if (swapped < straight)
            {
                _roles[a] = r1;
                _roles[b] = r0;
            }
            else
            {
                _roles[a] = r0;
                _roles[b] = r1;
            }
        }

        /// <summary>役割の目標の点へ、止まった状態から走って着く Tick。</summary>
        private long Cost(Match m, int player, Role role) => TicksTo(m, m.Players[player].Position, Target(m, player, role));

        internal static long TicksTo(Match m, Vec2i from, Vec2i to)
        {
            var c = m.Config;
            var d = IntMath.Sqrt(Vec2i.DistanceSquared(from, to));
            var t = 0;
            while (BallPredictor.Reach(t, c.PlayerMaxSpeed, c.PlayerAcceleration) < d && t < 100000) { t += 5; }
            return t;
        }

        private static int EarliestToBall(Match m, int[] field)
        {
            var c = m.Config;
            var trajectory = BallPredictor.Trajectory(m.Ball, c, PassEvaluator.Horizon);
            var best = field[0];
            var bestTick = int.MaxValue;
            foreach (var f in field)
            {
                var t = BallPredictor.ArrivalTick(trajectory, m.Players[f].Position, c.PlayerMaxSpeed, c.PlayerAcceleration, c.ControlRadius, c.KneeHeight, PassEvaluator.Horizon * 2);
                if (t < 0) { t = int.MaxValue - 1; }
                if (t < bestTick)
                {
                    best = f;
                    bestTick = t;
                }
            }

            return best;
        }

        // ---- 目標の点 ----

        internal Vec2i Target(Match m, int player, Role role)
        {
            var p = m.Players[player];
            var team = p.Team;
            var dir = Pitch.Direction(team);
            var ownGoal = new Vec2i(Pitch.OwnGoalX(team), 0);
            var ball = m.Ball.Position;
            switch (role)
            {
                case Role.Support:
                    return SupportSpot(m, player);
                case Role.Runner:
                    return RunnerSpot(m, player);
                case Role.Press:
                    {
                        var carrier = m.Owner >= 0 ? m.Players[m.Owner].Position : ball;
                        if (Tactics.Defense == DefenseStyle.SitBack && (carrier.X * dir) > 0)
                        {
                            // 引いて守る：相手が自陣に入るまでは、自陣の中ほどで保持者とゴールの間。
                            return new Vec2i(-dir * 3000, carrier.Y / 2);
                        }

                        // 相手の GK が持っている間は寄せない（球から離れなければならない）。GK の前を外で待つ。
                        var toGoal = ownGoal - carrier;
                        var keeperHolds = m.Owner >= 0 && m.Players[m.Owner].IsKeeper;
                        return toGoal.IsZero ? carrier : carrier + IntMath.Scale(toGoal, keeperHolds ? Pitch.ExclusionRadius + 500 : 700);
                    }

                case Role.Cover:
                    {
                        var mark = OtherAttacker(m, team);
                        var toGoal = ownGoal - mark;
                        var gap = Tactics.Defense == DefenseStyle.SitBack ? 3000 : 1500;
                        var spot = toGoal.IsZero ? mark : mark + IntMath.Scale(toGoal, gap);
                        if (Tactics.Defense == DefenseStyle.SitBack) { spot = new Vec2i(dir > 0 ? Math.Min(spot.X, -2000) : Math.Max(spot.X, 2000), spot.Y); }
                        return Clear(m, PassEvaluator.Inside(spot), team);
                    }

                case Role.Chase:
                    {
                        var c = m.Config;
                        var trajectory = BallPredictor.Trajectory(m.Ball, c, PassEvaluator.Horizon);
                        var t = BallPredictor.ArrivalTick(trajectory, p.Position, c.PlayerMaxSpeed, c.PlayerAcceleration, c.ControlRadius, c.KneeHeight, PassEvaluator.Horizon * 2);
                        if (t <= 0 || trajectory.Count == 0) { return ball; }
                        return trajectory[Math.Min(trajectory.Count, t) - 1].Position;
                    }

                case Role.Hold:
                    {
                        var mid = new Vec2i((ball.X + ownGoal.X) / 2, ball.Y / 2);
                        return Clear(m, PassEvaluator.Inside(mid), team);
                    }

                default:
                    return p.Position;
            }
        }

        /// <summary>相手チームのフィールドの選手のうち、球を持っていない方（再開・こぼれ球では球から遠い方）。</summary>
        private static Vec2i OtherAttacker(Match m, Team defending)
        {
            var attacking = Pitch.Opponent(defending);
            var a = m.Players[Match.FieldPlayer(attacking, 0)];
            var b = m.Players[Match.FieldPlayer(attacking, 1)];
            if (m.Owner == a.Index) { return b.Position; }
            if (m.Owner == b.Index) { return a.Position; }
            return Vec2i.DistanceSquared(a.Position, m.Ball.Position) >= Vec2i.DistanceSquared(b.Position, m.Ball.Position) ? a.Position : b.Position;
        }

        /// <summary>
        /// 支援の点：保持者（再開では球）から 4〜10 m の輪の上の候補点のうち、パスコース（保持者から点への線分）と相手の最短距離が大きく、
        /// 前にあり、今の位置から近い点。
        /// </summary>
        internal static Vec2i SupportSpot(Match m, int player)
        {
            var p = m.Players[player];
            var dir = Pitch.Direction(p.Team);
            var from = m.Ball.Position;
            var best = p.Position;
            var bestScore = long.MinValue;
            foreach (var radius in new[] { 4000, 6000, 8000, 10000 })
            {
                for (int k = 0; k < 12; k++)
                {
                    var angle = k * Trig.FullTurn / 12;
                    var spot = from + new Vec2i(radius * Trig.Cos(angle) / Trig.One, radius * Trig.Sin(angle) / Trig.One);
                    if (Math.Abs(spot.X) > Pitch.HalfLength - 1500 || Math.Abs(spot.Y) > Pitch.HalfWidth - 1000) { continue; }
                    var score = LaneScore(m, p.Team, from, spot) + ((spot.X - from.X) * dir / 4) - (IntMath.Sqrt(Vec2i.DistanceSquared(spot, p.Position)) / 5);
                    if (score > bestScore)
                    {
                        best = spot;
                        bestScore = score;
                    }
                }
            }

            return best;
        }

        /// <summary>抜け出しの点：保持者より前の格子のうち、一番近い相手から離れていて、ゴールに近い点。</summary>
        internal static Vec2i RunnerSpot(Match m, int player)
        {
            var p = m.Players[player];
            var dir = Pitch.Direction(p.Team);
            var goalX = Pitch.AttackGoalX(p.Team);
            var from = m.Ball.Position;
            var best = p.Position;
            var bestScore = long.MinValue;
            for (int step = 1; step <= 6; step++)
            {
                var x = from.X + (dir * step * 2000);
                if ((x * dir) > (goalX * dir) - 2000) { break; }
                for (int y = -6000; y <= 6000; y += 2000)
                {
                    var spot = new Vec2i(x, y);
                    var score = Math.Min(6000, NearestOpponent(m, p.Team, spot)) + ((x * dir) / 3) - (IntMath.Sqrt(Vec2i.DistanceSquared(spot, p.Position)) / 5);
                    if (score > bestScore)
                    {
                        best = spot;
                        bestScore = score;
                    }
                }
            }

            return best;
        }

        /// <summary>パスコース（線分 a→b）と相手（GK を含む）の最短距離（上限 4 m）× 2。</summary>
        internal static long LaneScore(Match m, Team team, Vec2i a, Vec2i b)
        {
            long best = 4000;
            foreach (var o in m.Players)
            {
                if (o.Team == team) { continue; }
                best = Math.Min(best, SegmentDistance(a, b, o.Position));
            }

            return best * 2;
        }

        internal static long SegmentDistance(Vec2i a, Vec2i b, Vec2i p)
        {
            var ab = b - a;
            var ap = p - a;
            var len2 = ab.LengthSquared;
            if (len2 == 0) { return IntMath.Sqrt(ap.LengthSquared); }
            var t = Vec2i.Dot(ap, ab);
            if (t <= 0) { return IntMath.Sqrt(ap.LengthSquared); }
            if (t >= len2) { return IntMath.Sqrt(Vec2i.DistanceSquared(p, b)); }

            // 線分への垂線の長さ = |ab × ap| / |ab|。
            var cross = Math.Abs(((long)ab.X * ap.Y) - ((long)ab.Y * ap.X));
            return cross / IntMath.Sqrt(len2);
        }

        private static long NearestOpponent(Match m, Team team, Vec2i spot)
        {
            var best = long.MaxValue;
            foreach (var o in m.Players)
            {
                if (o.Team == team) { continue; }
                best = Math.Min(best, IntMath.Sqrt(Vec2i.DistanceSquared(o.Position, spot)));
            }

            return best;
        }

        /// <summary>相手の再開の間は、球から入れない距離の外へ。</summary>
        private static Vec2i Clear(Match m, Vec2i spot, Team team)
        {
            if (m.Phase is not (MatchPhase.SetPiece or MatchPhase.Stopped) || m.RestartTeam == team) { return spot; }
            var d = spot - m.RestartSpot;
            var r = Pitch.ExclusionRadius + 200;
            if (d.LengthSquared >= (long)r * r) { return spot; }
            return m.RestartSpot + IntMath.Scale(d.IsZero ? new Vec2i(-Pitch.Direction(m.RestartTeam), 0) : d, r);
        }

        // ---- 保持者 ----

        /// <summary>保持者（CPU）の命令：一定 Tick ごとにシュート・パス・ドリブルを選ぶ。それ以外の Tick はドリブルを続ける。</summary>
        private PlayerCommand Carry(Match m, int carrier)
        {
            var p = m.Players[carrier];
            var dir = Pitch.Direction(Team);
            if (m.Phase == MatchPhase.SetPiece)
            {
                if (m.PhaseTicks < SetPieceWaitTicks) { return PlayerCommand.Idle; }
                var restartPass = PassEvaluator.Best(m, carrier, safeOnly: true) ?? PassEvaluator.Best(m, carrier, safeOnly: false);
                return restartPass.HasValue ? new PlayerCommand(Vec2i.Zero, restartPass.Value.Kind, restartPass.Value.Target) : PlayerCommand.Idle;
            }

            var dribble = DribbleDirection(m, carrier);
            if ((m.Clock + carrier) % DecisionTicks != 0) { return new PlayerCommand(dribble); }

            var shot = PassEvaluator.BestShot(m, carrier);
            var pass = PassEvaluator.Best(m, carrier, safeOnly: true);
            var pressure = NearestOpponent(m, Team, p.Position) < 2500;
            var goalDistance = Math.Abs(Pitch.AttackGoalX(Team) - p.Position.X);
            if (Tactics.Attack == AttackStyle.Direct)
            {
                // 蹴り込む：14 m 以内なら空いていなくても打つ。前への安全なパス・ループを優先する。
                if (shot.IsClear || (goalDistance < 14000 && shot.CrossTick >= 0)) { return new PlayerCommand(dribble, PlayerAction.Shoot, shot.Target); }
                var forward = PassEvaluator.Best(m, carrier, safeOnly: true, new Vec2i(dir * 1000, 0));
                if (forward.HasValue && (forward.Value.Target.X - p.Position.X) * dir > 2000) { return new PlayerCommand(dribble, forward.Value.Kind, forward.Value.Target); }
                if (pressure && pass.HasValue) { return new PlayerCommand(dribble, pass.Value.Kind, pass.Value.Target); }
                return new PlayerCommand(dribble);
            }

            // つなぐ：空いたシュートだけ打つ。GK の範囲の手前（7 m）まで来たら、安全なパスがなければ打つ（GK に運び込まない）。
            if (shot.IsClear && goalDistance < 10000) { return new PlayerCommand(dribble, PlayerAction.Shoot, shot.Target); }
            if (pass.HasValue && (pressure || pass.Value.Score > 350)) { return new PlayerCommand(dribble, pass.Value.Kind, pass.Value.Target); }
            if (goalDistance < 7000 && shot.CrossTick >= 0) { return new PlayerCommand(dribble, PlayerAction.Shoot, shot.Target); }
            return new PlayerCommand(dribble);
        }

        /// <summary>ドリブルの向き：相手のゴールへ。3 m 以内の前の相手からは横へ逃げる。</summary>
        private Vec2i DribbleDirection(Match m, int carrier)
        {
            var p = m.Players[carrier];
            var goal = new Vec2i(Pitch.AttackGoalX(Team), 0);
            var toGoal = IntMath.Scale(goal - p.Position, 1000);
            foreach (var o in m.Players)
            {
                if (o.Team == Team) { continue; }
                var d = o.Position - p.Position;
                if (d.LengthSquared > 3000L * 3000 || Vec2i.Dot(d, toGoal) <= 0) { continue; }
                var side = (((long)toGoal.X * d.Y) - ((long)toGoal.Y * d.X)) > 0 ? -1 : 1;
                var away = new Vec2i(-toGoal.Y * side, toGoal.X * side);
                if (Math.Abs(p.Position.Y + (away.Y * 3)) > Pitch.HalfWidth - 1500) { away = -away; }
                return toGoal + away;
            }

            return toGoal;
        }
    }
}
