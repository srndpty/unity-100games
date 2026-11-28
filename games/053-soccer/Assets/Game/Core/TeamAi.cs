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

        /// <summary>カバー：保持者でない相手のうち自陣のゴールに近い方とゴールの間に立ち、パスコースを切る。</summary>
        Cover,

        /// <summary>ゴール前：GK がいないので、球と自陣のゴールを結ぶ線の上でゴールの前を守る。</summary>
        Guard,

        /// <summary>回収：こぼれ球へ一番早く届く選手が、届く点へ走る。</summary>
        Chase,

        /// <summary>待機：球と自陣のゴールの間に立つ。</summary>
        Hold,
    }

    /// <summary>
    /// CPU の判断の強さ（判断の間隔・役割を決め直す間隔・タックルの慎重さ）。走る速さは体の能力なので試合の規則（<see cref="SoccerConfig.RedSpeedPercent"/>）に置き、
    /// 到着の予測も同じ速さで行う。
    /// </summary>
    public sealed class AiSkill
    {
        /// <summary>保持者が判断する間隔（Tick）。</summary>
        public int DecisionTicks { get; set; } = 8;

        /// <summary>役割を決め直す間隔（Tick）。</summary>
        public int RoleTicks { get; set; } = 15;

        /// <summary>タックルを始める距離を、届く距離からこれだけ縮める（大きいほど慎重）。</summary>
        public int TackleMargin { get; set; } = 100;

        /// <summary>人間の味方と同じ強さ。</summary>
        public static AiSkill Full => new();

        /// <summary>CPU の既定（弱め）：役割を決め直すのが遅く、タックルが慎重。</summary>
        public static AiSkill Weak => new() { DecisionTicks = 8, RoleTicks = 30, TackleMargin = 300 };

        public IReadOnlyList<string> Validate()
        {
            var e = new List<string>();
            if (DecisionTicks < 1 || DecisionTicks > 120) { e.Add($"DecisionTicks は 1〜120（{DecisionTicks}）"); }
            if (RoleTicks < 1 || RoleTicks > 240) { e.Add($"RoleTicks は 1〜240（{RoleTicks}）"); }
            if (TackleMargin < 0 || TackleMargin > 500) { e.Add($"TackleMargin は 0〜500（{TackleMargin}）"); }
            return e;
        }

        public AiSkill Copy() => (AiSkill)MemberwiseClone();
    }

    /// <summary>
    /// チームの頭脳（CPU のチームと、人間側の操作していない味方の両方）。人間が操作している選手を除く 3 人に命令を出す。
    /// <para>
    /// 役割の割当：局面（自チームが保持・相手が保持・こぼれ球）ごとの役割の組を、空いている選手へ割り当てる。割当は一定 Tick ごとと局面が変わった Tick に
    /// 決め直し、全ての割当（3 人なら 6 通り）のうち、目標の点へ走る Tick の和が最小のものを選ぶ（同じなら先に数えた割当）。
    /// 人間が操作している選手は役割から外し、その選手の位置に一番近い役割を人間が受け持つとみなす。
    /// </para>
    /// 保持者（CPU）は一定 Tick ごとに、シュート・パス（<see cref="PassEvaluator"/>）・ドリブルを方針（<see cref="Tactics"/>）で選ぶ。乱数は使わない。
    /// </summary>
    public sealed class TeamAi
    {
        /// <summary>キックオフで蹴る人が、味方の位置取りを待つ Tick。</summary>
        public const int KickoffWaitTicks = 40;

        private readonly Role[] _roles = new Role[Match.PlayerCount];
        private readonly Vec2i[] _targets = new Vec2i[Match.PlayerCount];
        private readonly AiSkill _skill;
        private int _lastState = -1;
        private int _lastHuman = -1;
        private int _lastOwner = -1;
        private int _lastTaker = -1;
        private int _lastAssigned = int.MinValue;

        public TeamAi(Team team, Tactics tactics, AiSkill? skill = null)
        {
            Team = team;
            Tactics = tactics;
            _skill = (skill ?? AiSkill.Full).Copy();
            var errors = _skill.Validate();
            if (errors.Count > 0) { throw new ArgumentException(string.Join("\n", errors), nameof(skill)); }
        }

        public Team Team { get; }

        public Tactics Tactics { get; }

        public AiSkill Skill => _skill.Copy();

        public Role RoleOf(int player) => _roles[player];

        /// <summary>役割の目標の点（最後の判断のとき）。</summary>
        public Vec2i TargetOf(int player) => _targets[player];

        public TeamAi Clone()
        {
            var c = new TeamAi(Team, Tactics, _skill)
            {
                _lastState = _lastState,
                _lastHuman = _lastHuman,
                _lastOwner = _lastOwner,
                _lastTaker = _lastTaker,
                _lastAssigned = _lastAssigned,
            };
            _roles.CopyTo(c._roles, 0);
            _targets.CopyTo(c._targets, 0);
            return c;
        }

        /// <summary>
        /// チームの選手（<paramref name="human"/> を除く）の命令を <paramref name="commands"/> に書く。
        /// <paramref name="human"/> は人間が操作している選手（いなければ -1）。
        /// </summary>
        public void Decide(Match match, int human, PlayerCommand[] commands)
        {
            if (match == null) { throw new ArgumentNullException(nameof(match)); }
            if (commands == null) { throw new ArgumentNullException(nameof(commands)); }
            var team = new int[Match.TeamSize];
            for (int i = 0; i < team.Length; i++) { team[i] = Match.PlayerOf(Team, i); }
            if (match.Phase is MatchPhase.Goal or MatchPhase.Ended or MatchPhase.Stopped)
            {
                foreach (var f in team)
                {
                    _roles[f] = Role.None;
                    if (f != human) { commands[f] = PlayerCommand.Idle; }
                }

                return;
            }

            var state = State(match);
            // 局面・人間の選手・保持者・キックオフの蹴る人が変わったらすぐ、そうでなければ一定 Tick ごとに決め直す
            // （CPU の弱さの「決め直しの遅さ」は同じ顔ぶれの見直しだけに効かせ、人間が手放した選手を役割なしで残さない）。
            if (state != _lastState || human != _lastHuman || match.Owner != _lastOwner || match.Taker != _lastTaker
                || match.Clock - _lastAssigned >= _skill.RoleTicks)
            {
                Assign(match, human, team, state);
                _lastState = state;
                _lastHuman = human;
                _lastOwner = match.Owner;
                _lastTaker = match.Taker;
                _lastAssigned = match.Clock;
            }

            foreach (var f in team)
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
                // 慎重さの分だけ縮めた距離（負にはしない。0 ならタックルしない）。
                var reach = Math.Max(0, match.Config.TackleRadius - _skill.TackleMargin);
                if (_roles[f] is Role.Press or Role.Chase or Role.Guard && match.Owner >= 0 && match.Players[match.Owner].Team != Team
                    && Vec2i.DistanceSquared(p.Position, match.Ball.Position) < (long)reach * reach)
                {
                    action = PlayerAction.Tackle;
                }

                commands[f] = new PlayerCommand(target - p.Position, action);
            }
        }

        /// <summary>局面：0 自チームが保持、1 相手が保持、2 こぼれ球、3 自チームのキックオフ、4 相手のキックオフ。</summary>
        private int State(Match m)
        {
            if (m.Phase == MatchPhase.Kickoff) { return m.KickoffTeam == Team ? 3 : 4; }
            if (m.Owner < 0) { return 2; }
            return m.Players[m.Owner].Team == Team ? 0 : 1;
        }

        /// <summary>役割を割り当てる。空いている選手と役割の組の、全ての割当のうち走る Tick の和が最小のもの。</summary>
        private void Assign(Match m, int human, int[] team, int state)
        {
            var free = new List<int>();
            foreach (var f in team)
            {
                _roles[f] = Role.None;
                if (f == human || m.Owner == f || m.Taker == f) { continue; }
                free.Add(f);
            }

            if (free.Count == 0) { return; }

            List<Role> pool;
            switch (state)
            {
                case 0:
                case 3:
                    // つなぐ：支援と、後ろでゴール前を守る。蹴り込む：抜け出しと支援。
                    pool = Tactics.Attack == AttackStyle.Direct
                        ? new List<Role> { Role.Runner, Role.Support, Role.Guard }
                        : new List<Role> { Role.Support, Role.Guard, Role.Runner };
                    break;
                case 1:
                case 4:
                    pool = new List<Role> { Role.Press, Role.Guard, Role.Cover };
                    break;
                default:
                    {
                        // こぼれ球は、球に一番早く届く選手（人間を含む）が回収する。ほかはゴール前と待機。
                        var chaser = EarliestToBall(m, team);
                        var rest = new List<int>();
                        foreach (var f in free)
                        {
                            if (f == chaser) { _roles[f] = Role.Chase; }
                            else { rest.Add(f); }
                        }

                        Choose(m, rest, new List<Role> { Role.Guard, Role.Hold, Role.Hold });
                        return;
                    }
            }

            // 人間（保持者でないとき）は、人間の位置に一番近い目標の役割を受け持つとみなして外す。
            if (human >= 0 && m.Owner != human && m.Taker != human)
            {
                var hp = m.Players[human].Position;
                var drop = 0;
                var dropCost = long.MaxValue;
                for (int i = 0; i < Math.Min(pool.Count, free.Count + 1); i++)
                {
                    var d = Vec2i.DistanceSquared(hp, Target(m, human, pool[i]));
                    if (d < dropCost)
                    {
                        drop = i;
                        dropCost = d;
                    }
                }

                pool.RemoveAt(drop);
            }

            Choose(m, free, pool);
        }

        /// <summary>選手 <paramref name="players"/> に役割の候補 <paramref name="pool"/> の先頭から人数分を、走る Tick の和が最小になるよう割り当てる。</summary>
        private void Choose(Match m, List<int> players, List<Role> pool)
        {
            if (players.Count == 0) { return; }
            var roles = pool.GetRange(0, Math.Min(pool.Count, players.Count));
            var best = long.MaxValue;
            int[]? bestOrder = null;
            foreach (var order in Permutations(roles.Count))
            {
                long cost = 0;
                for (int i = 0; i < order.Length; i++) { cost += Cost(m, players[i], roles[order[i]]); }
                if (cost < best)
                {
                    best = cost;
                    bestOrder = order;
                }
            }

            for (int i = 0; i < bestOrder!.Length; i++) { _roles[players[i]] = roles[bestOrder[i]]; }
        }

        private static IEnumerable<int[]> Permutations(int n)
        {
            if (n == 1)
            {
                yield return new[] { 0 };
                yield break;
            }

            if (n == 2)
            {
                yield return new[] { 0, 1 };
                yield return new[] { 1, 0 };
                yield break;
            }

            yield return new[] { 0, 1, 2 };
            yield return new[] { 0, 2, 1 };
            yield return new[] { 1, 0, 2 };
            yield return new[] { 1, 2, 0 };
            yield return new[] { 2, 0, 1 };
            yield return new[] { 2, 1, 0 };
        }

        /// <summary>役割の目標の点へ、止まった状態から走って着く Tick。</summary>
        private long Cost(Match m, int player, Role role) => TicksTo(m, player, Target(m, player, role));

        /// <summary>選手が止まった状態から点へ走って着く Tick（その選手の最高速で、5 Tick 刻み）。</summary>
        internal static long TicksTo(Match m, int player, Vec2i to)
        {
            var c = m.Config;
            var d = IntMath.Sqrt(Vec2i.DistanceSquared(m.Players[player].Position, to));
            var max = m.MaxSpeedOf(player);
            var t = 0;
            while (BallPredictor.Reach(t, max, c.PlayerAcceleration) < d && t < 100000) { t += 5; }
            return t;
        }

        private static int EarliestToBall(Match m, int[] team)
        {
            var c = m.Config;
            var trajectory = BallPredictor.Trajectory(m.Ball, c, PassEvaluator.Horizon);
            var best = team[0];
            var bestTick = int.MaxValue;
            foreach (var f in team)
            {
                var t = BallPredictor.ArrivalTick(trajectory, m.Players[f].Position, m.MaxSpeedOf(f), c.PlayerAcceleration, c.ControlRadius, c.KneeHeight, PassEvaluator.Horizon * 2);
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

                        var toGoal = ownGoal - carrier;
                        return toGoal.IsZero ? carrier : carrier + IntMath.Scale(toGoal, 700);
                    }

                case Role.Cover:
                    {
                        var mark = Mark(m, team);
                        var toGoal = ownGoal - mark;
                        var gap = Tactics.Defense == DefenseStyle.SitBack ? 3000 : 1500;
                        return Clear(m, PassEvaluator.Inside(toGoal.IsZero ? mark : mark + IntMath.Scale(toGoal, gap)), team);
                    }

                case Role.Guard:
                    {
                        // ゴールの前 2.5 m（引いて守るは 1.5 m）、横は球の側へ寄る（ゴールの幅の中）。
                        var depth = Tactics.Defense == DefenseStyle.SitBack ? 1500 : 2500;
                        var y = IntMath.Clamp(ball.Y / 3, -Pitch.GoalHalfWidth, Pitch.GoalHalfWidth);
                        return Clear(m, new Vec2i(ownGoal.X + (dir * depth), y), team);
                    }

                case Role.Chase:
                    {
                        var c = m.Config;
                        var trajectory = BallPredictor.Trajectory(m.Ball, c, PassEvaluator.Horizon);
                        var t = BallPredictor.ArrivalTick(trajectory, p.Position, m.MaxSpeedOf(player), c.PlayerAcceleration, c.ControlRadius, c.KneeHeight, PassEvaluator.Horizon * 2);
                        if (t <= 0 || trajectory.Count == 0) { return ball; }
                        return trajectory[Math.Min(trajectory.Count, t) - 1].Position;
                    }

                case Role.Hold:
                    return Clear(m, PassEvaluator.Inside(new Vec2i((ball.X + ownGoal.X) / 2, ball.Y / 2)), team);
                default:
                    return p.Position;
            }
        }

        /// <summary>カバーが見る相手：保持者でない相手のうち、自陣のゴールに近い方。</summary>
        private static Vec2i Mark(Match m, Team defending)
        {
            var attacking = Pitch.Opponent(defending);
            var goal = new Vec2i(Pitch.OwnGoalX(defending), 0);
            var best = m.Ball.Position;
            var bestDistance = long.MaxValue;
            for (int slot = 0; slot < Match.TeamSize; slot++)
            {
                var o = m.Players[Match.PlayerOf(attacking, slot)];
                if (o.Index == m.Owner) { continue; }
                var d = Vec2i.DistanceSquared(o.Position, goal);
                if (d < bestDistance)
                {
                    best = o.Position;
                    bestDistance = d;
                }
            }

            return best;
        }

        /// <summary>
        /// 支援の点：保持者（キックオフでは球）から 4〜10 m の輪の上の候補点のうち、パスコース（保持者から点への線分）と相手の最短距離が大きく、
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

        /// <summary>パスコース（線分 a→b）と相手の最短距離（上限 4 m）× 2。</summary>
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

        /// <summary>相手のキックオフの間は、センターの円の外へ。</summary>
        private static Vec2i Clear(Match m, Vec2i spot, Team team)
        {
            if (m.Phase != MatchPhase.Kickoff || m.KickoffTeam == team) { return spot; }
            var r = Pitch.ExclusionRadius + 200;
            if (spot.LengthSquared >= (long)r * r) { return spot; }
            return IntMath.Scale(spot.IsZero ? new Vec2i(-Pitch.Direction(m.KickoffTeam), 0) : spot, r);
        }

        // ---- 保持者 ----

        /// <summary>保持者（CPU）の命令：一定 Tick ごとにシュート・パス・ドリブルを選ぶ。それ以外の Tick はドリブルを続ける。</summary>
        private PlayerCommand Carry(Match m, int carrier)
        {
            var p = m.Players[carrier];
            var dir = Pitch.Direction(Team);
            if (m.Phase == MatchPhase.Kickoff)
            {
                if (m.PhaseTicks < KickoffWaitTicks) { return PlayerCommand.Idle; }
                var restartPass = PassEvaluator.Best(m, carrier, safeOnly: true) ?? PassEvaluator.Best(m, carrier, safeOnly: false);
                return restartPass.HasValue ? new PlayerCommand(Vec2i.Zero, restartPass.Value.Kind, restartPass.Value.Target) : PlayerCommand.Idle;
            }

            var dribble = DribbleDirection(m, carrier);
            if ((m.Clock + carrier) % _skill.DecisionTicks != 0) { return new PlayerCommand(dribble); }

            var shot = PassEvaluator.BestShot(m, carrier);
            var pass = PassEvaluator.Best(m, carrier, safeOnly: true);
            var pressure = NearestOpponent(m, Team, p.Position) < 2500;
            var goalDistance = Math.Abs(Pitch.AttackGoalX(Team) - p.Position.X);
            if (Tactics.Attack == AttackStyle.Direct)
            {
                // 蹴り込む：空いた・または 14 m 以内のシュート。前への安全なパス・ループを優先する。
                if (shot.IsClear || (goalDistance < 14000 && shot.CrossTick >= 0)) { return new PlayerCommand(dribble, PlayerAction.Shoot, shot.Target); }
                var forward = PassEvaluator.Best(m, carrier, safeOnly: true, new Vec2i(dir * 1000, 0));
                if (forward.HasValue && (forward.Value.Target.X - p.Position.X) * dir > 2000) { return new PlayerCommand(dribble, forward.Value.Kind, forward.Value.Target); }
                if (pressure && pass.HasValue) { return new PlayerCommand(dribble, pass.Value.Kind, pass.Value.Target); }
                return new PlayerCommand(dribble);
            }

            // つなぐ：10 m 以内の空いたシュート。寄せられたか点数の高い安全なパス。7 m 以内なら打つ。
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
