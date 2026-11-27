using System;
using System.Collections.Generic;
using IntGeometry;

namespace Game.Core
{
    /// <summary>パスの候補 1 つ：受け手・種類・目標の点、受け手と相手が軌跡に届く最初の Tick、点数。</summary>
    public readonly struct PassOption
    {
        public PassOption(int receiver, PlayerAction kind, Vec2i target, int receiverTick, int opponentTick, int score)
        {
            Receiver = receiver;
            Kind = kind;
            Target = target;
            ReceiverTick = receiverTick;
            OpponentTick = opponentTick;
            Score = score;
        }

        public int Receiver { get; }
        public PlayerAction Kind { get; }
        public Vec2i Target { get; }

        /// <summary>受け手が届く最初の Tick（届かなければ -1）。</summary>
        public int ReceiverTick { get; }

        /// <summary>相手の誰かが届く最初の Tick（届かなければ -1）。</summary>
        public int OpponentTick { get; }

        public int Score { get; }

        /// <summary>受け手が相手より <see cref="PassEvaluator.SafetyTicks"/> 以上早く届く。</summary>
        public bool IsSafe => ReceiverTick >= 0 && (OpponentTick < 0 || OpponentTick > ReceiverTick + PassEvaluator.SafetyTicks);

        public override string ToString() => $"p{Receiver} {Kind} {Target} r{ReceiverTick} o{OpponentTick} s{Score}";
    }

    /// <summary>シュートの候補：狙いの点、球がゴールラインを越える Tick、それより前に相手が届く Tick、点数（相手が届かなければ大きい）。</summary>
    public readonly struct ShotOption
    {
        public ShotOption(Vec2i target, int crossTick, int opponentTick, int score)
        {
            Target = target;
            CrossTick = crossTick;
            OpponentTick = opponentTick;
            Score = score;
        }

        public Vec2i Target { get; }

        /// <summary>球がゴールの口を越える Tick（越えなければ -1）。</summary>
        public int CrossTick { get; }

        public int OpponentTick { get; }

        public int Score { get; }

        /// <summary>ゴールの口を越え、その前に誰も届かない。</summary>
        public bool IsClear => CrossTick >= 0 && OpponentTick < 0;
    }

    /// <summary>
    /// パス候補評価（重点）。受け手ごとに足元・前・斜め前の点 × 地を這う／ループの軌道を作り、本番と同じ Step で軌跡を予測して、
    /// 受け手と相手（GK を含む）が軌跡に届く最初の Tick を比べる。相手が先に届く候補は安全でない。
    /// 点数は「受け手が相手より何 Tick 早いか（上限あり）」と「前進量」。シュートは、ゴールの口を越える前に相手が届かない余裕で点数を付ける。
    /// </summary>
    public static class PassEvaluator
    {
        /// <summary>受け手が相手よりこの Tick 以上早く届けば安全。</summary>
        public const int SafetyTicks = 4;

        /// <summary>予測する Tick の上限。</summary>
        public const int Horizon = 150;

        /// <summary>余裕の点数の上限（これ以上早くても同じ）。</summary>
        public const int MarginCap = 40;

        /// <summary>球を持つ選手のパスの候補（安全かどうかを問わず全て）。</summary>
        public static IReadOnlyList<PassOption> Options(Match match, int carrier)
        {
            if (match == null) { throw new ArgumentNullException(nameof(match)); }
            var list = new List<PassOption>();
            var p = match.Players[carrier];
            var dir = Pitch.Direction(p.Team);
            foreach (var r in match.Players)
            {
                if (r.Team != p.Team || r.Index == carrier || r.IsKeeper) { continue; }
                var spots = new[]
                {
                    r.Position,
                    r.Position + new Vec2i(dir * 2500, 0),
                    r.Position + new Vec2i(dir * 1500, 2000),
                    r.Position + new Vec2i(dir * 1500, -2000),
                };
                foreach (var spot in spots)
                {
                    var target = Inside(spot);
                    foreach (var kind in new[] { PlayerAction.Pass, PlayerAction.Lob })
                    {
                        list.Add(Evaluate(match, carrier, r.Index, kind, target));
                    }
                }
            }

            return list.AsReadOnly();
        }

        /// <summary>
        /// 一番点数の高いパス。<paramref name="cone"/> を与えると、その向きから ±60° の受け手に限る（人間の操作）。
        /// <paramref name="safeOnly"/> なら安全な候補だけ。なければ null。同じ点数は候補の順（受け手の番号・点・地を這う → ループ）。
        /// </summary>
        public static PassOption? Best(Match match, int carrier, bool safeOnly, Vec2i? cone = null, PlayerAction? kind = null)
        {
            PassOption? best = null;
            var from = match.Ball.Position;
            foreach (var o in Options(match, carrier))
            {
                if (safeOnly && !o.IsSafe) { continue; }
                if (kind.HasValue && o.Kind != kind.Value) { continue; }
                if (cone.HasValue && !cone.Value.IsZero && !InCone(cone.Value, match.Players[o.Receiver].Position - from)) { continue; }
                if (!best.HasValue || o.Score > best.Value.Score) { best = o; }
            }

            return best;
        }

        public static PassOption Evaluate(Match match, int carrier, int receiver, PlayerAction kind, Vec2i target)
        {
            if (match == null) { throw new ArgumentNullException(nameof(match)); }
            var c = match.Config;
            var team = match.Players[carrier].Team;
            var trajectory = Launch(match, kind, target);
            var r = match.Players[receiver];
            var receiverTick = BallPredictor.ArrivalTick(trajectory, r.Position, c.PlayerMaxSpeed, c.PlayerAcceleration, c.ControlRadius, c.KneeHeight, Horizon);
            var opponentTick = OpponentArrival(match, Pitch.Opponent(team), trajectory, Horizon);
            var dir = Pitch.Direction(team);
            var progress = (target.X - match.Ball.Position.X) * dir;
            int score;
            if (receiverTick < 0) { score = -1000; }
            else
            {
                var margin = opponentTick < 0 ? MarginCap : Math.Min(MarginCap, opponentTick - receiverTick);
                score = (margin * 10) + (progress / 100) - (kind == PlayerAction.Lob ? 20 : 0);
            }

            return new PassOption(receiver, kind, target, receiverTick, opponentTick, score);
        }

        /// <summary>ゴールの左・中・右を狙うシュートのうち、一番点数の高いもの。</summary>
        public static ShotOption BestShot(Match match, int carrier)
        {
            if (match == null) { throw new ArgumentNullException(nameof(match)); }
            var team = match.Players[carrier].Team;
            var goalX = Pitch.AttackGoalX(team);
            var aim = Pitch.GoalHalfWidth - 400;
            ShotOption? best = null;
            foreach (var y in new[] { -aim, aim, 0 })
            {
                var o = EvaluateShot(match, team, new Vec2i(goalX, y));
                if (!best.HasValue || o.Score > best.Value.Score) { best = o; }
            }

            return best!.Value;
        }

        public static ShotOption EvaluateShot(Match match, Team team, Vec2i target)
        {
            if (match == null) { throw new ArgumentNullException(nameof(match)); }
            var trajectory = Launch(match, PlayerAction.Shoot, target);
            var goalX = Pitch.AttackGoalX(team);
            var dir = Pitch.Direction(team);
            var r = match.Config.BallRadius;
            var cross = -1;
            var before = new List<BallSample>();
            foreach (var s in trajectory)
            {
                if ((s.Position.X * dir) - r > goalX * dir)
                {
                    if (Math.Abs(s.Position.Y) < Pitch.GoalHalfWidth && s.Height + (2 * r) <= Pitch.CrossbarHeight) { cross = s.Tick; }
                    break;
                }

                before.Add(s);
            }

            if (cross < 0) { return new ShotOption(target, -1, -1, -1000); }
            var opponent = OpponentArrival(match, Pitch.Opponent(team), before, cross);
            var score = opponent < 0 ? 100 - cross : opponent - cross - 20;
            return new ShotOption(target, cross, opponent, score);
        }

        /// <summary>チーム <paramref name="team"/> の誰か（GK は届く距離・高さ・速さが違う）が軌跡に届く最初の Tick（届かなければ -1）。</summary>
        public static int OpponentArrival(Match match, Team team, IReadOnlyList<BallSample> trajectory, int horizon)
        {
            if (match == null) { throw new ArgumentNullException(nameof(match)); }
            var c = match.Config;
            var best = -1;
            foreach (var o in match.Players)
            {
                if (o.Team != team) { continue; }
                var t = o.IsKeeper
                    ? BallPredictor.ArrivalTick(trajectory, o.Position, c.KeeperSpeed, c.KeeperSpeed, c.KeeperReach, c.KeeperReachHeight, horizon, c.KeeperReactionTicks)
                    : BallPredictor.ArrivalTick(trajectory, o.Position, c.PlayerMaxSpeed, c.PlayerAcceleration, c.ControlRadius, c.KneeHeight, horizon);
                if (t >= 0 && (best < 0 || t < best)) { best = t; }
            }

            return best;
        }

        /// <summary>今の球の位置から蹴った軌跡（ぶれなし）。</summary>
        private static IReadOnlyList<BallSample> Launch(Match match, PlayerAction kind, Vec2i target)
        {
            var ball = match.Ball.Clone();
            ball.Place(ball.Position);
            var (velocity, vertical) = match.KickVelocity(kind, ball.Position, target);
            ball.Launch(velocity, vertical);
            return BallPredictor.Trajectory(ball, match.Config, Horizon);
        }

        /// <summary>向き <paramref name="direction"/> から ±60° の中か（cos 60° = 1/2）。</summary>
        private static bool InCone(Vec2i direction, Vec2i to)
        {
            if (to.IsZero) { return true; }
            direction = IntMath.Scale(direction, 1000);
            var dot = Vec2i.Dot(direction, to);
            if (dot <= 0) { return false; }

            // dot / (|d||t|) >= 1/2  ⇔  4 dot^2 >= |d|^2 |t|^2。向きは長さ 1000 程度・距離はコートの中なので、どちらも long に収まる。
            return 4 * dot * dot >= direction.LengthSquared * to.LengthSquared;
        }

        /// <summary>コートの内側（端から 0.8 m）へ寄せる。</summary>
        internal static Vec2i Inside(Vec2i p) => new(
            IntMath.Clamp(p.X, -Pitch.HalfLength + 800, Pitch.HalfLength - 800),
            IntMath.Clamp(p.Y, -Pitch.HalfWidth + 800, Pitch.HalfWidth - 800));
    }
}
