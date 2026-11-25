using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class CpuTests
    {
        [Test]
        public void ThreeCpusFinishThreeLapsAroundAStoppedPlayer()
        {
            // プレイヤーはグリッドに止まったまま。CPU は 2 周目以降、止まった車をよけてフィニッシュを越える。
            var config = new KartConfig();
            var race = TestKit.CircuitRace();
            while (!race.IsOver && !CpusFinished(race)) { race.Step(Drive.None); }

            for (var i = 1; i <= 3; i++)
            {
                var kart = race.KartAt(i);
                Assert.That(kart.Finished, Is.True, $"CPU {i} が完走していない（Tick {race.Tick}、周回 {kart.Laps}、次 {kart.NextGate}）");
                Assert.That(kart.FinishTick, Is.LessThan(config.MaxRaceTicks));
                Assert.That(kart.FinishTick, Is.GreaterThan(0));
            }

            Assert.That(race.IsOver, Is.False, "プレイヤーが完走するまでレースは続く");
        }

        [Test]
        public void ThePlayerPaceFinishesFirstAndEndsTheRace()
        {
            var race = TestKit.CircuitRace();
            while (!race.IsOver) { race.Step(race.PlanFor(0, 1000)); }

            var player = race.KartAt(0);
            Assert.That(player.Finished, Is.True, Describe(race));
            Assert.That(race.PlaceOf(0), Is.EqualTo(1), Describe(race));
            Assert.That(race.Tick, Is.EqualTo(player.FinishTick), "プレイヤーが完走した Tick に終わる");
            for (var i = 1; i <= 3; i++) { Assert.That(race.KartAt(i).Finished, Is.False, $"CPU {i} は走行中のまま順位が付く"); }
            AssertPermutation(race);
        }

        [Test]
        public void ACpuThatFinishesFirstStaysAheadOfTheLatePlayer()
        {
            // プレイヤーがグリッドで待ち、CPU が全員完走してから走り出す。プレイヤーは完走した Tick に 4 位で終わる。
            var race = TestKit.CircuitRace();
            while (!CpusFinished(race)) { race.Step(Drive.None); }
            while (!race.IsOver) { race.Step(race.PlanFor(0, 1000)); }
            Assert.That(race.KartAt(0).Finished, Is.True);
            Assert.That(race.PlaceOf(0), Is.EqualTo(4));
            AssertPermutation(race);
        }

        [Test]
        public void AKartNoseIntoTheWallRecovers()
        {
            var config = new KartConfig();
            var race = new Race(config, TestKit.Circuit());
            for (var i = 0; i < config.StartDelayTicks + 5; i++) { race.Step(Drive.None); }
            for (var i = 0; i < 400 && race.KartAt(0).RecoverLeft == 0; i++)
            {
                race.Step(new Drive(true, false, 1, false));
            }

            var stuck = race.KartAt(0);
            Assert.That(stuck.RecoverLeft, Is.GreaterThan(0), "壁に付いても回復を始めていない");
            var gate = stuck.NextGate;
            var laps = stuck.Laps;
            var reached = false;
            for (var i = 0; i < 4000 && !race.IsOver; i++)
            {
                race.Step(race.PlanFor(0, 1000));
                var kart = race.KartAt(0);
                if (kart.Laps > laps || kart.NextGate != gate)
                {
                    reached = true;
                    break;
                }
            }

            Assert.That(reached, Is.True, "壁から戻って次のゲートへ届かない");
        }

        [Test]
        public void TheStallCountWaitsUntilTheRecoveryEnds()
        {
            // 回復の後退中も停滞を数えると、回復の直後にまた回復に入り、後退し続ける。
            var config = new KartConfig();
            var race = new Race(config, TestKit.Circuit());
            for (var i = 0; i < config.StartDelayTicks + 5; i++) { race.Step(Drive.None); }
            for (var i = 0; i < 400 && race.KartAt(0).RecoverLeft == 0; i++) { race.Step(new Drive(true, false, 1, false)); }
            Assert.That(race.KartAt(0).RecoverLeft, Is.GreaterThan(0));
            while (race.KartAt(0).RecoverLeft > 0)
            {
                race.Step(race.PlanFor(0, 1000));
                if (race.KartAt(0).RecoverLeft > 0) { Assert.That(race.KartAt(0).StallTicks, Is.EqualTo(0), $"Tick {race.Tick}"); }
            }

            for (var i = 0; i < config.StuckTicks - 1; i++)
            {
                race.Step(race.PlanFor(0, 1000));
                Assert.That(race.KartAt(0).RecoverLeft, Is.EqualTo(0), $"回復の直後にまた回復に入った（Tick {race.Tick}）");
            }
        }

        [Test]
        public void ASharpCornerDriftsAndBoostsOnTheExit()
        {
            var config = new KartConfig();
            var race = TestKit.CircuitRace();
            var drifted = false;
            var boosted = false;
            var minSpeed = int.MaxValue;
            while (!race.IsOver && race.Tick < 8000)
            {
                var moment = race.Step(race.PlanFor(0, 1000));
                var kart = race.KartAt(0);
                if (kart.DriftTicks > 0) { drifted = true; }
                if ((moment & RaceMoment.Boost) != 0 && kart.BoostLeft > 0) { boosted = true; }
                if (kart.Speed > 0 && kart.Speed < minSpeed) { minSpeed = kart.Speed; }
                if (drifted && boosted) { break; }
            }

            Assert.That(drifted, Is.True, "曲率の大きい角でドリフトしない");
            Assert.That(boosted, Is.True, "角の出口でブーストしない");
            Assert.That(minSpeed, Is.LessThan(config.MaxSpeed), "角で減速しない");
        }

        [Test]
        public void NoInputLeavesThePlayerLastAndTheRaceStillEnds()
        {
            var config = new KartConfig();
            var race = TestKit.CircuitRace();
            while (!race.IsOver) { race.Step(Drive.None); }
            Assert.That(race.Tick, Is.EqualTo(config.MaxRaceTicks));
            Assert.That(race.PlaceOf(0), Is.EqualTo(4));
            for (var i = 1; i <= 3; i++) { Assert.That(race.KartAt(i).Finished, Is.True, $"CPU {i}"); }
        }

        [Test]
        public void ACpuSteersAroundAKartAheadInItsLane()
        {
            var track = TestKit.Circuit();
            var config = new KartConfig();
            var karts = Lined(track, config, ahead: 4000, otherLateral: 0, selfLateral: 0);
            var lane = CpuPlan.AvoidLane(karts, 1, track, config);
            var clearance = (config.Radius * 2) + config.AvoidGap;
            Assert.That(System.Math.Abs(lane), Is.GreaterThanOrEqualTo(clearance), "中心線へ戻る前の車とも離れる筋を通る");

            var right = Lined(track, config, ahead: 4000, otherLateral: 300, selfLateral: -200);
            Assert.That(CpuPlan.AvoidLane(right, 1, track, config), Is.LessThanOrEqualTo(-clearance), "右にいれば右から抜く");
            var left = Lined(track, config, ahead: 4000, otherLateral: -300, selfLateral: 200);
            Assert.That(CpuPlan.AvoidLane(left, 1, track, config), Is.GreaterThanOrEqualTo(clearance), "左にいれば左から抜く");
        }

        [Test]
        public void ACpuIgnoresKartsBehindBesideOrFarAhead()
        {
            var track = TestKit.Circuit();
            var config = new KartConfig();
            var clearance = (config.Radius * 2) + config.AvoidGap;
            Assert.That(CpuPlan.AvoidLane(Lined(track, config, -3000, 0, 0), 1, track, config), Is.EqualTo(0), "後ろの車");
            Assert.That(CpuPlan.AvoidLane(Lined(track, config, 3000, clearance + 10, 0), 1, track, config), Is.EqualTo(0), "隣の筋の車");
            Assert.That(CpuPlan.AvoidLane(Lined(track, config, config.AvoidRange + 500, 0, 0), 1, track, config), Is.EqualTo(0), "遠い車");
            var finished = Lined(track, config, 3000, 0, 0);
            finished[0].Finished = true;
            Assert.That(CpuPlan.AvoidLane(finished, 1, track, config), Is.EqualTo(0), "完走した車");
        }

        [Test]
        public void TheAvoidLaneStaysOnTheTarmac()
        {
            var track = TestKit.Circuit();
            var config = new KartConfig();
            var room = track.HalfWidth(0) - config.Radius - 500;
            for (var lateral = -4000; lateral <= 4000; lateral += 250)
            {
                var lane = CpuPlan.AvoidLane(Lined(track, config, 3000, lateral, lateral), 1, track, config);
                Assert.That(lane, Is.InRange(-room, room), $"横 {lateral}");
            }
        }

        /// <summary>0 番を 1 番の <paramref name="ahead"/> mm 先（中心線に沿って）に置いた 4 台。2・3 番は遠くに置く。</summary>
        private static Kart[] Lined(Track track, KartConfig config, int ahead, int otherLateral, int selfLateral)
        {
            var karts = new Kart[KartConfig.KartCount];
            var origin = new TrackPlace(5, 0);
            karts[1] = At(track, origin, selfLateral);
            var other = ahead >= 0 ? track.Travel(origin, ahead) : new TrackPlace(2, 4000 + ahead);
            karts[0] = At(track, other, otherLateral);
            karts[2] = At(track, new TrackPlace(60, 0), 0);
            karts[3] = At(track, new TrackPlace(70, 0), 0);
            return karts;
        }

        private static Kart At(Track track, TrackPlace place, int lateral)
        {
            var position = track.Offset(place, lateral);
            var projected = track.Project(position, place.Station);
            return new Kart { Position = position, Station = projected.Station, Along = projected.Along, FinishTick = -1, NextGate = 1 };
        }

        private static bool CpusFinished(Race race) => race.KartAt(1).Finished && race.KartAt(2).Finished && race.KartAt(3).Finished;

        private static void AssertPermutation(Race race)
        {
            var seen = new bool[KartConfig.KartCount];
            for (var i = 0; i < KartConfig.KartCount; i++)
            {
                var place = race.PlaceOf(i);
                Assert.That(place, Is.InRange(1, KartConfig.KartCount));
                Assert.That(seen[place - 1], Is.False);
                seen[place - 1] = true;
            }
        }

        private static string Describe(Race race) =>
            $"順位 {race.PlaceOf(0)} 記録 {race.KartAt(0).FinishTick}/{race.KartAt(1).FinishTick}/{race.KartAt(2).FinishTick}/{race.KartAt(3).FinishTick}";
    }
}
