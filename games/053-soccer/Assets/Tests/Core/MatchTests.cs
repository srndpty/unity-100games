using System;
using System.Linq;
using Game.Core;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class MatchTests
    {
        // ---- 保持 ----

        [Test]
        public void SlowBall_IsTrapped_FastBall_PassesBy_HighBall_PassesOver()
        {
            var c = Lab.Exact;
            var slow = Lab.Open();
            slow.PlacePlayer(1, new Vec2i(2000, 0));
            slow.PlaceBall(Vec2i.Zero, new Vec2i(150, 0));
            var e1 = Lab.Run(slow, 30);
            Assert.That(e1.Single(e => e.Kind == MatchEventKind.Capture).Player, Is.EqualTo(1));
            Assert.That(slow.Owner, Is.EqualTo(1));

            var fast = Lab.Open();
            fast.PlacePlayer(1, new Vec2i(2000, 0));
            fast.PlaceBall(Vec2i.Zero, new Vec2i(c.TrapSpeed + 20, 0));
            Lab.Run(fast, 1);
            Assert.That(fast.Owner, Is.EqualTo(-1), "止められない速さの球は通り過ぎる");

            var high = Lab.Open();
            high.PlacePlayer(1, new Vec2i(1000, 0));
            high.PlaceBall(Vec2i.Zero, new Vec2i(150, 0), c.KneeHeight + 400, 20);
            Lab.Run(high, 8);
            Assert.That(high.Owner, Is.EqualTo(-1), "膝より高い球は止められない");
        }

        [Test]
        public void FastBall_IsCaughtEvenIfItWouldJumpOverThePlayerInOneTick()
        {
            // 1 Tick で選手を飛び越える距離でも、軌跡どうしの掃引で止める（端点だけの判定なら見逃す）。
            var c = Lab.Exact;
            var m = Lab.Open();
            m.PlacePlayer(1, new Vec2i(300, 0));
            m.PlaceBall(Vec2i.Zero, new Vec2i(c.TrapSpeed - 10, 0));
            Lab.Run(m, 1);
            Assert.That(m.Owner, Is.EqualTo(1));
        }

        [Test]
        public void Dribble_KeepsTheBallInFront_AndSlowsThePlayer()
        {
            var c = Lab.Exact;
            var m = Lab.Open();
            m.PlacePlayer(1, Vec2i.Zero);
            m.GiveBall(1);
            Lab.Run(m, 60, (x, i) => i == 1 ? new PlayerCommand(new Vec2i(0, 1000)) : PlayerCommand.Idle);
            var p = m.Players[1];
            Assert.That(m.Owner, Is.EqualTo(1));
            Assert.That(p.Velocity.Y, Is.EqualTo(c.PlayerMaxSpeed * c.DribbleSpeedPercent / 100));
            Assert.That(m.Ball.Position, Is.EqualTo(p.Position + new Vec2i(0, c.DribbleOffset)));
        }

        // ---- キック ----

        [Test]
        public void GroundPass_ArrivesAtTheTarget_StillRolling()
        {
            var c = Lab.Exact;
            foreach (var distance in new[] { 3000, 8000, 14000 })
            {
                var m = Lab.Open();
                m.PlacePlayer(1, new Vec2i(-6000, 0));
                m.GiveBall(1);
                var target = m.Ball.Position + new Vec2i(distance, 0);
                Lab.Run(m, 1, (x, i) => i == 1 ? new PlayerCommand(Vec2i.Zero, PlayerAction.Pass, target) : PlayerCommand.Idle);
                var path = BallPredictor.Trajectory(m.Ball, c, 600);
                var reach = path.FirstOrDefault(s => s.Position.X >= target.X - 200);
                if (m.Passes.Reach(m.Passes.MaxSpeed) >= distance)
                {
                    Assert.That(reach.Tick, Is.GreaterThan(0), $"{distance} に届く");
                }

                Assert.That(path[path.Count - 1].Position.X, Is.LessThan(target.X + 6000), "強すぎない");
            }
        }

        [Test]
        public void Lob_LandsNearTheTarget()
        {
            var c = Lab.Exact;
            var m = Lab.Open();
            m.PlacePlayer(1, new Vec2i(-6000, 0));
            m.GiveBall(1);
            var target = m.Ball.Position + new Vec2i(9000, 2000);
            Lab.Run(m, 1, (x, i) => i == 1 ? new PlayerCommand(Vec2i.Zero, PlayerAction.Lob, target) : PlayerCommand.Idle);
            var path = BallPredictor.Trajectory(m.Ball, c, 400);
            var landing = path.First(s => s.Height == 0);
            Assert.That(IntMath.Sqrt(Vec2i.DistanceSquared(landing.Position, target)), Is.LessThan(600));
            Assert.That(path.Max(s => s.Height), Is.GreaterThan(c.KneeHeight), "間の選手の上を越える");
        }

        [Test]
        public void ShotSpread_ComesFromTheSeed()
        {
            int Aim(int seed)
            {
                var m = Lab.Open(new SoccerConfig(), seed);
                m.PlacePlayer(1, new Vec2i(8000, 0));
                m.GiveBall(1);
                Lab.Run(m, 1, (x, i) => i == 1 ? new PlayerCommand(Vec2i.Zero, PlayerAction.Shoot, new Vec2i(Pitch.HalfLength, 0)) : PlayerCommand.Idle);
                return m.Ball.Velocity.Y;
            }

            Assert.That(Aim(7), Is.EqualTo(Aim(7)));
            Assert.That(Enumerable.Range(0, 10).Select(Aim).Distinct().Count(), Is.GreaterThan(3));
        }

        [Test]
        public void KickRules_OnlyTheOwnerKicks_AndCannotRetrapAtOnce()
        {
            var m = Lab.Open();
            m.PlacePlayer(1, Vec2i.Zero);
            m.PlacePlayer(2, new Vec2i(0, 3000));
            m.GiveBall(1);
            var e = Lab.Run(m, 1, (x, i) => i == 2 ? new PlayerCommand(Vec2i.Zero, PlayerAction.Shoot, new Vec2i(Pitch.HalfLength, 0)) : PlayerCommand.Idle);
            Assert.That(Lab.Count(e, MatchEventKind.Kick), Is.EqualTo(0), "持っていない選手は蹴れない");
            Assert.That(m.Owner, Is.EqualTo(1));

            var k = Lab.Run(m, 1, (x, i) => i == 1 ? new PlayerCommand(Vec2i.Zero, PlayerAction.Pass, new Vec2i(400, 0)) : PlayerCommand.Idle);
            Assert.That(Lab.Count(k, MatchEventKind.Kick), Is.EqualTo(1));
            Assert.That(m.Owner, Is.EqualTo(-1), "蹴った直後に自分で止めない");
            Assert.That(m.Players[1].KickCooldown, Is.GreaterThan(0));
        }

        // ---- タックル ----

        /// <summary>壁の近くの保持を見るための設定：届く距離を小さく、止められる速さを大きく、壁は速さを落とさない。</summary>
        private static SoccerConfig Fine => new()
        {
            ShotSpread = 0,
            PassSpread = 0,
            ControlRadius = 250,
            DribbleOffset = 50,
            TrapSpeed = 2000,
            WallRestitutionPercent = 100,
            RollFrictionPerMille = 1,
        };

        [Test]
        public void Capture_FollowsTheBentPathAtTheWall()
        {
            // 球は (0, 壁 − 500) から斜めに壁へ当たり、(500, 壁) で折れて戻る。始めと終わりを結ぶ直線は壁から 500 離れている。
            var c = Fine;
            var wallY = Pitch.HalfWidth - c.BallRadius;
            var apex = Lab.Open(c);
            apex.PlacePlayer(1, new Vec2i(500, Pitch.HalfWidth - c.PlayerRadius));
            apex.PlaceBall(new Vec2i(0, wallY - 500), new Vec2i(1000, 1000));
            var e1 = Lab.Run(apex, 1);
            Assert.That(e1.Any(e => e.Kind == MatchEventKind.Wall), Is.True);
            Assert.That(apex.Owner, Is.EqualTo(1), "折れた点（壁際）の選手が止める（直線の掃引では届かない）");

            var chord = Lab.Open(c);
            chord.PlacePlayer(1, new Vec2i(500, wallY - 500));
            chord.PlaceBall(new Vec2i(0, wallY - 500), new Vec2i(1000, 1000));
            Lab.Run(chord, 1);
            Assert.That(chord.Owner, Is.EqualTo(-1), "球が通っていない直線の上の選手は止められない");
        }

        [Test]
        public void Capture_UsesTheVelocityOfTheSegment_AfterTheWallSlowsTheBall()
        {
            // 壁へ向かう速い球（止められない）が、壁で 20 % になって戻ってきたところを止める。
            var c = new SoccerConfig { ShotSpread = 0, PassSpread = 0, TrapSpeed = 300, WallRestitutionPercent = 20 };
            var wallY = Pitch.HalfWidth - c.BallRadius;
            var m = Lab.Open(c);
            m.PlacePlayer(1, new Vec2i(0, Pitch.HalfWidth - c.PlayerRadius));
            m.PlaceBall(new Vec2i(0, wallY - 100), new Vec2i(0, 500));
            Lab.Run(m, 1);
            Assert.That(m.Owner, Is.EqualTo(1), "跳ね返った後の区間の速さで判定する");
        }

        [Test]
        public void Tackle_InReach_PokesTheBall_OutOfReach_IsAMiss()
        {
            var c = Lab.Exact;
            var m = Lab.Open();
            m.PlacePlayer(1, Vec2i.Zero);
            m.GiveBall(1);
            m.PlacePlayer(4, new Vec2i(c.DribbleOffset + 400, 0));
            var e = Lab.Run(m, 1, (x, i) => i == 4 ? new PlayerCommand(Vec2i.Zero, PlayerAction.Tackle) : PlayerCommand.Idle);
            var t = e.Single(x => x.Kind == MatchEventKind.Tackle);
            Assert.That(t.Player, Is.EqualTo(4));
            Assert.That(t.Value, Is.EqualTo(1));
            Assert.That(m.Owner, Is.Not.EqualTo(1));
            Assert.That(m.Players[1].Recover, Is.GreaterThan(0));

            var miss = Lab.Open();
            miss.PlacePlayer(1, Vec2i.Zero);
            miss.GiveBall(1);
            miss.PlacePlayer(4, new Vec2i(5000, 0));
            var e2 = Lab.Run(miss, 1, (x, i) => i == 4 ? new PlayerCommand(Vec2i.Zero, PlayerAction.Tackle) : PlayerCommand.Idle);
            Assert.That(Lab.Count(e2, MatchEventKind.TackleMiss), Is.EqualTo(1));
            Assert.That(miss.Owner, Is.EqualTo(1));
            Assert.That(miss.Players[4].Recover, Is.EqualTo(c.TackleMissRecoverTicks), "空振りの代償");

            // 空振りの後は動けない。
            var at = miss.Players[4].Position;
            Lab.Run(miss, 5, (x, i) => i == 4 ? new PlayerCommand(new Vec2i(-1, 0)) : PlayerCommand.Idle);
            Assert.That(miss.Players[4].Position, Is.EqualTo(at));
        }

        [Test]
        public void Tackle_CannotTakeTheBallFromATeammate()
        {
            var m = Lab.Open();
            m.PlacePlayer(1, new Vec2i(-6000, 0));
            m.GiveBall(1);
            m.PlacePlayer(2, new Vec2i(-6000, 400));
            var e = Lab.Run(m, 1, (x, i) => i == 2 ? new PlayerCommand(Vec2i.Zero, PlayerAction.Tackle) : PlayerCommand.Idle);
            Assert.That(Lab.Count(e, MatchEventKind.Tackle), Is.EqualTo(0));
            Assert.That(m.Owner, Is.EqualTo(1));
        }

        // ---- 時計・終了 ----

        [Test]
        public void MatchEndsOnTime_AndIgnoresEverythingAfter()
        {
            var m = Lab.Open(new SoccerConfig { MatchTicks = 3600 });
            m.SetClock(3598);
            var e = Lab.Run(m, 2);
            Assert.That(m.Phase, Is.EqualTo(MatchPhase.Ended));
            Assert.That(Lab.Count(e, MatchEventKind.Whistle), Is.EqualTo(1));
            var hash = m.Hash();
            var after = Lab.Run(m, 30, Lab.Random(3));
            Assert.That(after, Is.Empty);
            Assert.That(m.Hash(), Is.EqualTo(hash), "終わった後は何も変わらない");
            Assert.That(m.RemainingTicks, Is.EqualTo(0));
        }

        [Test]
        public void Step_RejectsAWrongNumberOfCommands()
        {
            var m = new Match(Lab.Exact, 1);
            Assert.Throws<ArgumentException>(() => m.Step(new PlayerCommand[5]));
            Assert.Throws<ArgumentNullException>(() => m.Step(null!));
        }

        // ---- 再現性・複製 ----

        [Test]
        public void SameSeedAndCommands_SameMatch_CloneIsIndependent()
        {
            var a = new Match(new SoccerConfig(), 9);
            var b = new Match(new SoccerConfig(), 9);
            Lab.Run(a, 3000, Lab.Random(4));
            Lab.Run(b, 3000, Lab.Random(4));
            Assert.That(b.Hash(), Is.EqualTo(a.Hash()));

            var c = a.Clone();
            Assert.That(c.Hash(), Is.EqualTo(a.Hash()));
            Lab.Run(c, 100, Lab.Random(5));
            Assert.That(c.Hash(), Is.Not.EqualTo(a.Hash()));
            var d = a.Clone();
            Lab.Run(a, 100, Lab.Random(6));
            Lab.Run(d, 100, Lab.Random(6));
            Assert.That(d.Hash(), Is.EqualTo(a.Hash()));
        }

        [Test]
        public void PlayersStayApart_AndInsideTheirBounds()
        {
            var m = new Match(new SoccerConfig(), 2);
            Lab.Run(m, 2000, (x, i) =>
            {
                Assert.That(Math.Abs(x.Players[i].Position.X), Is.LessThanOrEqualTo(Pitch.HalfLength));
                Assert.That(Math.Abs(x.Players[i].Position.Y), Is.LessThanOrEqualTo(Pitch.HalfWidth));
                return new PlayerCommand(x.Ball.Position - x.Players[i].Position, PlayerAction.Tackle);
            });
        }

        /// <summary>固定のシードと乱択の命令の最終ハッシュ（.NET と Mono で同じ値）。</summary>
        [Test]
        public void FinalHash_IsFixed()
        {
            var m = new Match(new SoccerConfig(), 2026);
            Lab.Run(m, 6000, Lab.Random(77));
            TestContext.WriteLine($"hash 0x{m.Hash():X8} score {m.Score(Team.Blue)}-{m.Score(Team.Red)} kicks {m.KickCount}");
            Assert.That(m.Hash(), Is.EqualTo(0x5E141709u));
        }

        // ---- 設定 ----

        [Test]
        public void Config_Defaults_AreValid_AndBoundsAreChecked()
        {
            Assert.That(new SoccerConfig().Validate(), Is.Empty);
            Assert.That(new SoccerConfig { MatchTicks = 10 }.Validate(), Has.Some.Contains("MatchTicks"));
            Assert.That(new SoccerConfig { MaxPassSpeed = 50, MinPassSpeed = 100 }.Validate(), Has.Some.Contains("MaxPassSpeed"));
            Assert.That(new SoccerConfig { DribbleOffset = 500, ControlRadius = 450 }.Validate(), Has.Some.Contains("DribbleOffset"));
            Assert.That(new SoccerConfig { WallRestitutionPercent = 101 }.Validate(), Has.Some.Contains("WallRestitutionPercent"));
            Assert.Throws<ArgumentException>(() => _ = new Match(new SoccerConfig { Gravity = 0 }, 1));
        }

        [Test]
        public void Config_IsCopied_SoLaterChangesDoNotAffectTheMatch()
        {
            var config = new SoccerConfig();
            var m = new Match(config, 1);
            config.MatchTicks = 1;
            Assert.That(m.RemainingTicks, Is.EqualTo(new SoccerConfig().MatchTicks));
        }
    }
}
