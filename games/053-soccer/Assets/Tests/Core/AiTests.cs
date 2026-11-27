using System.Linq;
using Game.Core;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>役割割当（中心課題）・パス候補評価（重点）・CPU。</summary>
    public sealed class AiTests
    {
        private static readonly Tactics _possession = new(DefenseStyle.PressHigh, AttackStyle.Possession);

        // ---- 題材固有：保持者以外の味方が支援位置へ動く ----

        [Test]
        public void NonCarrierTeammate_MovesToASupportSpot_WithAnOpenerPassingLane()
        {
            var m = Lab.Open();
            m.PlacePlayer(1, new Vec2i(-2000, 0));
            m.GiveBall(1);

            // 味方（2）は相手（4）の陰（パスコースの上）にいる。
            m.PlacePlayer(4, new Vec2i(2000, 1000));
            m.PlacePlayer(2, new Vec2i(5000, 2000));
            m.PlacePlayer(5, new Vec2i(6000, -6000));
            var lane = TeamAi.LaneScore(m, Team.Blue, m.Ball.Position, m.Players[2].Position);
            var ai = new TeamAi(Team.Blue, _possession);
            var c = Lab.Idle();
            for (int t = 0; t < 150; t++)
            {
                ai.Decide(m, 1, c);
                c[1] = PlayerCommand.Idle;
                c[4] = PlayerCommand.Idle;
                c[5] = PlayerCommand.Idle;
                m.Step(c);
            }

            Assert.That(ai.RoleOf(2), Is.EqualTo(Role.Support));
            var p = m.Players[2].Position;
            Assert.That(Vec2i.DistanceSquared(p, ai.TargetOf(2)), Is.LessThan(600L * 600), "支援の点の近くにいる");
            Assert.That(TeamAi.LaneScore(m, Team.Blue, m.Ball.Position, p), Is.GreaterThan(lane), "パスコースが相手から離れた");
            var d = IntMath.Sqrt(Vec2i.DistanceSquared(p, m.Ball.Position));
            Assert.That(d, Is.InRange(3500, 10500), "保持者から 4〜10 m の輪");
        }

        [Test]
        public void HumanControlledPlayer_GetsNoCommand_AndNoRole()
        {
            var m = new Match(Lab.Exact, 1);
            m.ForcePlay();
            var ai = new TeamAi(Team.Blue, _possession);
            var c = Lab.Idle();
            var sentinel = new PlayerCommand(new Vec2i(7, 7), PlayerAction.Tackle);
            for (int t = 0; t < 200; t++)
            {
                c[1] = sentinel;
                ai.Decide(m, 1, c);
                Assert.That(c[1], Is.EqualTo(sentinel), "人間の選手の命令に書かない");
                Assert.That(ai.RoleOf(1), Is.EqualTo(Role.None));
                m.Step(c);
            }
        }

        [Test]
        public void Defending_TheCloserPlayerPresses_TheOtherCovers()
        {
            var m = Lab.Open();
            m.PlacePlayer(4, new Vec2i(0, 0));
            m.GiveBall(4);
            m.PlacePlayer(5, new Vec2i(-4000, 6000));
            m.PlacePlayer(1, new Vec2i(-8000, 7000));
            m.PlacePlayer(2, new Vec2i(-2500, 0));
            var ai = new TeamAi(Team.Blue, _possession);
            ai.Decide(m, -1, Lab.Idle());
            Assert.That(ai.RoleOf(2), Is.EqualTo(Role.Press), "保持者に近い方が寄せる");
            Assert.That(ai.RoleOf(1), Is.EqualTo(Role.Cover));

            // 入れ替えると役割も入れ替わる（移動 Tick の和が最小の割当）。
            m.PlacePlayer(1, new Vec2i(-2500, 0));
            m.PlacePlayer(2, new Vec2i(-8000, 7000));
            var again = new TeamAi(Team.Blue, _possession);
            again.Decide(m, -1, Lab.Idle());
            Assert.That(again.RoleOf(1), Is.EqualTo(Role.Press));
            Assert.That(again.RoleOf(2), Is.EqualTo(Role.Cover));
        }

        [Test]
        public void LooseBall_TheEarliestPlayerChases()
        {
            var m = Lab.Open();
            m.PlaceBall(new Vec2i(0, 0), new Vec2i(150, 0));
            m.PlacePlayer(1, new Vec2i(-5000, 0));
            m.PlacePlayer(2, new Vec2i(6000, 1000));
            var ai = new TeamAi(Team.Blue, _possession);
            ai.Decide(m, -1, Lab.Idle());
            Assert.That(ai.RoleOf(2), Is.EqualTo(Role.Chase), "球の進む先にいる方が早い");
            Assert.That(ai.RoleOf(1), Is.EqualTo(Role.Hold));
        }

        [Test]
        public void SitBack_DoesNotPressInTheOpponentsHalf()
        {
            var m = Lab.Open();
            m.PlacePlayer(4, new Vec2i(8000, 0));
            m.GiveBall(4);
            var press = new TeamAi(Team.Blue, new Tactics(DefenseStyle.PressHigh, AttackStyle.Possession));
            var sit = new TeamAi(Team.Blue, new Tactics(DefenseStyle.SitBack, AttackStyle.Possession));
            press.Decide(m, -1, Lab.Idle());
            sit.Decide(m, -1, Lab.Idle());
            var pressing = Enumerable.Range(1, 2).First(i => press.RoleOf(i) == Role.Press);
            var sitting = Enumerable.Range(1, 2).First(i => sit.RoleOf(i) == Role.Press);
            Assert.That(press.TargetOf(pressing).X, Is.GreaterThan(6000), "前から奪う：保持者の近く");
            Assert.That(sit.TargetOf(sitting).X, Is.LessThan(0), "引いて守る：自陣で待つ");
        }

        // ---- パス候補評価 ----

        [Test]
        public void PassEvaluator_RejectsALaneTheOpponentReachesFirst_AndLobsOver()
        {
            var m = Lab.Open();
            m.PlacePlayer(1, new Vec2i(-6000, 0));
            m.GiveBall(1);
            // 受け手は遠く、相手は間のパスコースの上（ループの着地点へは走っても間に合わない）。
            m.PlacePlayer(2, new Vec2i(9000, 0));
            m.PlacePlayer(4, new Vec2i(-2000, 0));
            var ground = PassEvaluator.Evaluate(m, 1, 2, PlayerAction.Pass, m.Players[2].Position);
            Assert.That(ground.IsSafe, Is.False, "間の相手が先に届く");
            Assert.That(ground.OpponentTick, Is.LessThan(ground.ReceiverTick));
            var lob = PassEvaluator.Evaluate(m, 1, 2, PlayerAction.Lob, m.Players[2].Position);
            Assert.That(lob.IsSafe, Is.True, "ループは上を越える");
            var best = PassEvaluator.Best(m, 1, safeOnly: true);
            Assert.That(best.HasValue, Is.True);
            Assert.That(best!.Value.Kind, Is.EqualTo(PlayerAction.Lob));

            // 相手がいなければ地を這うパスで足りる（ループは点数が低い）。
            m.PlacePlayer(4, new Vec2i(-2000, 8000));
            Assert.That(PassEvaluator.Best(m, 1, safeOnly: true)!.Value.Kind, Is.EqualTo(PlayerAction.Pass));
        }

        [Test]
        public void PassEvaluator_ArrivalTicks_MatchThePlay()
        {
            // 安全と評価したパスを蹴ると、受け手が評価した Tick に球を止める（受け手は評価した点へ走る）。
            var m = Lab.Open();
            m.PlacePlayer(1, new Vec2i(-6000, 0));
            m.GiveBall(1);
            m.PlacePlayer(2, new Vec2i(2000, 3000));
            var option = PassEvaluator.Evaluate(m, 1, 2, PlayerAction.Pass, m.Players[2].Position);
            Assert.That(option.IsSafe, Is.True);
            Lab.Run(m, 1, (x, i) => i == 1 ? new PlayerCommand(Vec2i.Zero, PlayerAction.Pass, option.Target) : PlayerCommand.Idle);
            var caught = -1;
            for (int t = 1; t <= 200 && caught < 0; t++)
            {
                Lab.Run(m, 1, (x, i) => i == 2 ? new PlayerCommand(x.Ball.Position - x.Players[i].Position) : PlayerCommand.Idle);
                if (m.Owner == 2) { caught = t; }
            }

            Assert.That(caught, Is.GreaterThan(0));
            Assert.That(caught, Is.LessThanOrEqualTo(option.ReceiverTick + 1), "評価した Tick までに届く");
        }

        [Test]
        public void ShotEvaluation_OpenCornerIsClear_ShotAtTheKeeperIsNot()
        {
            var m = Lab.Open();
            m.PlacePlayer(1, new Vec2i(Pitch.HalfLength - 6000, 0));
            m.GiveBall(1);
            m.PlacePlayer(3, new Vec2i(Pitch.HalfLength - 800, -900));
            var corner = PassEvaluator.EvaluateShot(m, Team.Blue, new Vec2i(Pitch.HalfLength, Pitch.GoalHalfWidth - 400));
            var atKeeper = PassEvaluator.EvaluateShot(m, Team.Blue, new Vec2i(Pitch.HalfLength, -900));
            Assert.That(corner.IsClear, Is.True);
            Assert.That(atKeeper.IsClear, Is.False);
            Assert.That(PassEvaluator.BestShot(m, 1).Target.Y, Is.GreaterThan(0), "GK のいない側を狙う");
        }

        // ---- GK ----

        [Test]
        public void Keeper_CannotReachACloseCornerShot_ButSavesALongOne()
        {
            int Goals(int distance)
            {
                var m = Lab.Open();
                m.PlacePlayer(3, new Vec2i(Pitch.HalfLength - 800, 0));
                m.PlacePlayer(1, new Vec2i(Pitch.HalfLength - distance, 0));
                m.GiveBall(1);
                Lab.Run(m, 60, (x, i) => i == 1 && x.Clock == 1 ? new PlayerCommand(Vec2i.Zero, PlayerAction.Shoot, new Vec2i(Pitch.HalfLength, Pitch.GoalHalfWidth - 400)) : PlayerCommand.Idle);
                return m.Score(Team.Blue);
            }

            Assert.That(Goals(7000), Is.EqualTo(1), "反応の遅れで近い隅には届かない");
            Assert.That(Goals(15000), Is.EqualTo(0), "遠いシュートは止める");
        }

        [Test]
        public void Keeper_TakesTheBallFromADribbler_AndOpponentsBackOff()
        {
            var m = Lab.Open();
            m.PlacePlayer(3, new Vec2i(Pitch.HalfLength - 1000, 0));
            m.PlacePlayer(1, new Vec2i(Pitch.HalfLength - 3000, 0));
            m.GiveBall(1);
            var e = Lab.Run(m, 40, (x, i) => i == 1 ? new PlayerCommand(new Vec2i(1000, 0)) : PlayerCommand.Idle);
            Assert.That(e.Any(x => x.Kind == MatchEventKind.Capture && x.Player == 3), Is.True);
            Assert.That(m.Score(Team.Blue), Is.EqualTo(0), "ドリブルで運び込めない");
            Lab.Run(m, 5, (x, i) => i == 1 ? new PlayerCommand(x.Ball.Position - x.Players[i].Position) : PlayerCommand.Idle);
            if (m.Owner == 3)
            {
                Assert.That(Vec2i.DistanceSquared(m.Players[1].Position, m.Ball.Position), Is.GreaterThanOrEqualTo((long)(Pitch.ExclusionRadius - 2) * (Pitch.ExclusionRadius - 2)), "GK が持つ間は離れる");
            }
        }

        // ---- CPU の試合と戦術差 ----

        [Test]
        public void CpuMatch_CompletesWithGoalsPassesAndShots()
        {
            var m = new Match(new SoccerConfig(), 11);
            var stats = Lab.CpuMatch(m, Tactics.Balanced, new Tactics(DefenseStyle.SitBack, AttackStyle.Direct));
            Assert.That(m.Phase, Is.EqualTo(MatchPhase.Ended));
            var blue = stats.Of(Team.Blue);
            var red = stats.Of(Team.Red);
            Assert.That(blue.Goals + red.Goals, Is.EqualTo(m.Score(Team.Blue) + m.Score(Team.Red)));
            Assert.That(blue.Shots + red.Shots, Is.GreaterThan(5));
            Assert.That(blue.PassesCompleted + red.PassesCompleted, Is.GreaterThan(40));
            Assert.That(blue.Tackles + red.Tackles, Is.GreaterThan(5));
        }

        [Test]
        public void Tactics_ShowMeasurableDifferences()
        {
            // 青は同じ方針に固定し、赤の方針だけを変えて 3 シードずつ 3 分の試合を回す。
            var config = new SoccerConfig { MatchTicks = 3 * 60 * Pitch.TickRate };
            TeamStats Sum(Tactics red)
            {
                var total = new TeamStats();
                for (int seed = 0; seed < 3; seed++)
                {
                    var s = Lab.CpuMatch(new Match(config, seed + 40), Tactics.Balanced, red).Of(Team.Red);
                    total.Shots += s.Shots;
                    total.Passes += s.Passes;
                    total.Lobs += s.Lobs;
                    total.Recoveries += s.Recoveries;
                    total.RecoveryDistanceSum += s.RecoveryDistanceSum;
                }

                return total;
            }

            var possession = Sum(new Tactics(DefenseStyle.PressHigh, AttackStyle.Possession));
            var direct = Sum(new Tactics(DefenseStyle.PressHigh, AttackStyle.Direct));
            var sitBack = Sum(new Tactics(DefenseStyle.SitBack, AttackStyle.Possession));
            TestContext.WriteLine($"shots P{possession.Shots} D{direct.Shots}  passes P{possession.Passes} D{direct.Passes}  recovery P{possession.AverageRecoveryDistance} S{sitBack.AverageRecoveryDistance}");
            Assert.That(direct.Shots, Is.GreaterThan(possession.Shots), "蹴り込むはシュートが多い");
            Assert.That(possession.Passes, Is.GreaterThan(direct.Passes), "つなぐはパスが多い");
            Assert.That(sitBack.AverageRecoveryDistance, Is.LessThan(possession.AverageRecoveryDistance), "引いて守るは自陣の深い位置で奪う");
        }

        /// <summary>CPU どうしの 1 試合の最終ハッシュ（.NET と Mono で同じ値）。</summary>
        [Test]
        public void CpuMatch_FinalHash_IsFixed()
        {
            var m = new Match(new SoccerConfig { MatchTicks = 2 * 60 * Pitch.TickRate }, 2026);
            var stats = Lab.CpuMatch(m, Tactics.Balanced, new Tactics(DefenseStyle.SitBack, AttackStyle.Direct));
            TestContext.WriteLine($"hash 0x{m.Hash():X8} score {m.Score(Team.Blue)}-{m.Score(Team.Red)} passes {stats.Of(Team.Blue).Passes}");
            Assert.That(m.Hash(), Is.EqualTo(0x6B88B074u));
        }
    }
}
