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
            Assert.That(ai.RoleOf(0), Is.EqualTo(Role.Guard), "自陣の奥の選手がゴール前を守る（GK はいない）");

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
            var pressing = Enumerable.Range(0, 3).First(i => press.RoleOf(i) == Role.Press);
            var sitting = Enumerable.Range(0, 3).First(i => sit.RoleOf(i) == Role.Press);
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

            // もう 1 人の味方も受け手の隣（同じパスコースの先）に置く。
            m.PlacePlayer(0, new Vec2i(9500, 800));
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
        public void ShotEvaluation_OpenCornerIsClear_ShotAtTheDefenderIsNot()
        {
            var m = Lab.Open();
            m.PlacePlayer(1, new Vec2i(Pitch.HalfLength - 6000, 0));
            m.GiveBall(1);
            m.PlacePlayer(3, new Vec2i(Pitch.HalfLength - 800, -900));
            var corner = PassEvaluator.EvaluateShot(m, Team.Blue, new Vec2i(Pitch.HalfLength, Pitch.GoalHalfWidth - 400));
            var atDefender = PassEvaluator.EvaluateShot(m, Team.Blue, new Vec2i(Pitch.HalfLength, -900));
            Assert.That(corner.IsClear, Is.True);
            Assert.That(atDefender.IsClear, Is.False);
            Assert.That(PassEvaluator.BestShot(m, 1).Target.Y, Is.GreaterThan(0), "守る選手のいない側を狙う");
        }

        // ---- ゴール前の守り・CPU の強さ ----

        [Test]
        public void WhenTheOpponentAttacks_SomeoneGuardsTheGoalMouth()
        {
            var m = Lab.Open();
            m.PlacePlayer(4, new Vec2i(-6000, 3000));
            m.GiveBall(4);
            m.PlacePlayer(0, new Vec2i(-3000, -6000));
            m.PlacePlayer(1, new Vec2i(2000, 6000));
            m.PlacePlayer(2, new Vec2i(0, 0));
            var ai = new TeamAi(Team.Blue, _possession);
            var c = Lab.Idle();
            for (int t = 0; t < 120; t++)
            {
                ai.Decide(m, -1, c);
                for (int i = 3; i < Match.PlayerCount; i++) { c[i] = PlayerCommand.Idle; }
                m.Step(c);
            }

            var guard = Enumerable.Range(0, 3).Single(i => ai.RoleOf(i) == Role.Guard);
            var goal = new Vec2i(-Pitch.HalfLength, 0);
            Assert.That(Vec2i.DistanceSquared(m.Players[guard].Position, goal), Is.LessThan(5000L * 5000), "ゴールの前にいる");
        }

        [Test]
        public void RedSpeed_IsAMatchRule_AndMultipliesTheDribbleSpeed()
        {
            // 赤の走る速さは試合の規則。ドリブル中はドリブルの割合と掛け合わせる（75 % × 85 % = 63）。
            var c = new SoccerConfig { ShotSpread = 0, PassSpread = 0, RedSpeedPercent = 75 };
            var m = Lab.Open(c);
            m.PlacePlayer(4, new Vec2i(0, -6000));
            m.PlacePlayer(1, new Vec2i(-6000, 6000));
            m.PlacePlayer(5, new Vec2i(4000, 6000));
            m.PlaceBall(new Vec2i(4000 + c.DribbleOffset, 6000), Vec2i.Zero);
            m.GiveBall(5);
            Lab.Run(m, 60, (x, i) => i is 1 or 4 or 5 ? new PlayerCommand(new Vec2i(1000, 0)) : PlayerCommand.Idle);
            Assert.That(m.MaxSpeedOf(4), Is.EqualTo(75));
            Assert.That(m.MaxSpeedOf(1), Is.EqualTo(100));
            Assert.That(m.Players[4].Velocity.X, Is.EqualTo(75), "赤は 75 % で走る");
            Assert.That(m.Players[1].Velocity.X, Is.EqualTo(100), "青は 100 %");
            Assert.That(m.Owner, Is.EqualTo(5));
            Assert.That(m.Players[5].Velocity.X, Is.EqualTo(75 * c.DribbleSpeedPercent / 100), "赤のドリブルは 75 % × 85 %");
        }

        [Test]
        public void ArrivalPrediction_UsesTheSlowerRedSpeed_AndMatchesThePlay()
        {
            // 止まった球まで、止まった赤の選手がまっすぐ走る。赤の速さで予測した Tick に届く。
            var c = new SoccerConfig { ShotSpread = 0, PassSpread = 0, RedSpeedPercent = 75 };
            var m = Lab.Open(c);
            m.PlaceBall(new Vec2i(-6000, 0), Vec2i.Zero);
            m.PlacePlayer(4, new Vec2i(2000, 0));
            var trajectory = BallPredictor.Trajectory(m.Ball, c, 10);
            var expected = BallPredictor.ArrivalTick(trajectory, m.Players[4].Position, m.MaxSpeedOf(4), c.PlayerAcceleration, c.ControlRadius, c.KneeHeight, 600);
            var full = BallPredictor.ArrivalTick(trajectory, m.Players[4].Position, c.PlayerMaxSpeed, c.PlayerAcceleration, c.ControlRadius, c.KneeHeight, 600);
            Assert.That(expected, Is.GreaterThan(full), "遅い分だけ遅く着く");
            Assert.That(PassEvaluator.OpponentArrival(m, Team.Red, trajectory, 600), Is.EqualTo(expected), "パス候補評価の「相手が届く Tick」も赤の速さで");
            var arrived = -1;
            for (int t = 1; t <= 600 && arrived < 0; t++)
            {
                Lab.Run(m, 1, (x, i) => i == 4 ? new PlayerCommand(new Vec2i(-1000, 0)) : PlayerCommand.Idle);
                if (m.Owner == 4) { arrived = t; }
            }

            Assert.That(arrived, Is.EqualTo(expected));

        }

        [Test]
        public void Roles_AreReassignedAtOnce_WhenTheHumanSwitches_OrTheCarrierChanges()
        {
            // 役割の見直しは 240 Tick ごと（遅い）。それでも人間の選手と保持者が変わった Tick に決め直す。
            var m = Lab.Open();
            m.PlacePlayer(4, new Vec2i(0, 0));
            m.GiveBall(4);
            m.PlacePlayer(0, new Vec2i(-8000, -3000));
            m.PlacePlayer(1, new Vec2i(-2500, 0));
            m.PlacePlayer(2, new Vec2i(-8000, 7000));
            var ai = new TeamAi(Team.Blue, _possession, new AiSkill { RoleTicks = 240 });
            ai.Decide(m, 1, Lab.Idle());
            Assert.That(ai.RoleOf(1), Is.EqualTo(Role.None), "人間の選手");
            m.Step(Lab.Idle());

            // 人間が 1 → 2 に切り替えた次の Tick：1 は CPU の役割を持ち、2 は役割を外れる。
            ai.Decide(m, 2, Lab.Idle());
            Assert.That(ai.RoleOf(1), Is.Not.EqualTo(Role.None), "手放した選手にすぐ役割が付く");
            Assert.That(ai.RoleOf(2), Is.EqualTo(Role.None));

            // 同じチームの中で保持者が変わる（赤 4 → 赤 5）：赤のチームは同じ局面のまま決め直す。
            var red = new TeamAi(Team.Red, _possession, new AiSkill { RoleTicks = 240 });
            m.PlacePlayer(5, new Vec2i(3000, 4000));
            red.Decide(m, -1, Lab.Idle());
            Assert.That(red.RoleOf(4), Is.EqualTo(Role.Carrier));
            var before5 = red.RoleOf(5);
            Assert.That(before5, Is.Not.EqualTo(Role.None));
            m.GiveBall(5);
            m.Step(Lab.Idle());
            red.Decide(m, -1, Lab.Idle());
            Assert.That(red.RoleOf(5), Is.EqualTo(Role.Carrier));
            Assert.That(red.RoleOf(4), Is.Not.EqualTo(Role.None).And.Not.EqualTo(Role.Carrier), "前の保持者にすぐ役割が付く");

            // 複製も同じ判断をする（決め直しの記録を写す）。
            var clone = red.Clone();
            var c1 = Lab.Idle();
            var c2 = Lab.Idle();
            red.Decide(m, -1, c1);
            clone.Decide(m, -1, c2);
            Assert.That(c2, Is.EqualTo(c1));
        }

        [Test]
        public void TackleMargin_AtOrBeyondTheReach_NeverTackles()
        {
            // 届く距離 300 に慎重さ 500：縮めた距離は 0（負の二乗で広がらない）。比べに慎重さ 100 ならタックルする。
            var c = new SoccerConfig { ShotSpread = 0, PassSpread = 0, TackleRadius = 300 };
            PlayerAction ActionOf(int margin)
            {
                var m = Lab.Open(c);
                m.PlacePlayer(1, new Vec2i(0, 0));
                m.GiveBall(1);
                m.PlacePlayer(4, m.Ball.Position + new Vec2i(150, 0));
                var ai = new TeamAi(Team.Red, _possession, new AiSkill { TackleMargin = margin });
                var cmd = Lab.Idle();
                ai.Decide(m, -1, cmd);
                Assert.That(ai.RoleOf(4), Is.EqualTo(Role.Press));
                return cmd[4].Action;
            }

            Assert.That(ActionOf(100), Is.EqualTo(PlayerAction.Tackle));
            Assert.That(ActionOf(500), Is.EqualTo(PlayerAction.None));
        }

        [Test]
        public void WeakCpu_IsEasierForAHuman()
        {
            // 素朴な人間役との試合：既定の弱い相手（走る速さ 75 %・AiSkill.Weak）なら、人間役の得失点差が良く、勝ち越せる。
            (int Blue, int Red) Total(int redSpeed, AiSkill skill)
            {
                var config = new SoccerConfig { MatchTicks = 3 * 60 * Pitch.TickRate, RedSpeedPercent = redSpeed };
                int b = 0, r = 0;
                for (int seed = 0; seed < 3; seed++)
                {
                    var stats = Lab.NaiveHumanMatch(new Match(config, seed + 60), skill);
                    b += stats.Of(Team.Blue).Goals;
                    r += stats.Of(Team.Red).Goals;
                }

                return (b, r);
            }

            var full = Total(100, AiSkill.Full);
            var weak = Total(75, AiSkill.Weak);
            TestContext.WriteLine($"vs Full {full.Blue}-{full.Red}  vs Weak {weak.Blue}-{weak.Red}");
            Assert.That(weak.Blue - weak.Red, Is.GreaterThan(full.Blue - full.Red), "弱い相手の方が人間に易しい");
            Assert.That(weak.Blue, Is.GreaterThan(weak.Red), "素朴な操作でも勝ち越せる");
        }

        /// <summary>比較実験：素朴な人間役（相手の方針はバランス、各条件 16 シード × 3 分）。App の KeyPilot の実験と同じ条件。結果は README に記録する。</summary>
        [Test]
        [Explicit("比較実験（数十秒）")]
        public void Ablation_NaiveHuman()
        {
            var conditions = new (string Name, int Speed, int RoleTicks, int TackleMargin)[]
            {
                ("A Full", 100, 15, 100),
                ("B 速さだけ 75%", 75, 15, 100),
                ("C 決め直しだけ 30", 100, 30, 100),
                ("D 慎重さだけ 300", 100, 15, 300),
                ("E 既定（弱め）", 75, 30, 300),
            };
            TestContext.Progress.WriteLine("| 条件 | 得点 | シュート | 青の保持率 | パス成功率 | タックル |");
            foreach (var (name, speed, roleTicks, margin) in conditions)
            {
                var config = new SoccerConfig { MatchTicks = 3 * 60 * Pitch.TickRate, RedSpeedPercent = speed };
                var skill = new AiSkill { RoleTicks = roleTicks, TackleMargin = margin };
                int bg = 0, rg = 0, bs = 0, rs = 0, bp = 0, rp = 0, bpass = 0, bdone = 0, rpass = 0, rdone = 0, bt = 0, rt = 0;
                for (int i = 0; i < 16; i++)
                {
                    var st = Lab.NaiveHumanMatch(new Match(config, i + 500), skill);
                    var b = st.Of(Team.Blue);
                    var r = st.Of(Team.Red);
                    bg += b.Goals;
                    rg += r.Goals;
                    bs += b.Shots;
                    rs += r.Shots;
                    bp += b.PossessionTicks;
                    rp += r.PossessionTicks;
                    bpass += b.Passes;
                    bdone += b.PassesCompleted;
                    rpass += r.Passes;
                    rdone += r.PassesCompleted;
                    bt += b.Tackles;
                    rt += r.Tackles;
                }

                static string Rate(int a, int b) => b == 0 ? "-" : $"{a * 100 / b}%";
                TestContext.Progress.WriteLine($"| {name} | {bg}-{rg} | {bs}-{rs} | {Rate(bp, bp + rp)} | {Rate(bdone, bpass)}-{Rate(rdone, rpass)} | {bt}-{rt} |");
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
            Assert.That(m.Hash(), Is.EqualTo(0x6CC97930u));
        }
    }
}
