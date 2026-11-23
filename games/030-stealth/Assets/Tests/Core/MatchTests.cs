using IntGeometry;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>試合（センサ・判断・体・捕獲・脱出のつながり）。</summary>
    public sealed class MatchTests
    {
        private static readonly string[] _walled =
        {
            "############",
            "#.....#...O#",
            "#.....#....#",
            "#E....#....#",
            "#..........#",
            "############",
        };

        private static readonly string[] _blocked =
        {
            "####################",
            "#..................#",
            "#..................#",
            "#.....#######......#",
            "#.....#######......#",
            "#.....#######......#",
            "#..................#",
            "#E................O#",
            "####################",
        };

        [Test]
        public void SentryNeverSeesThroughAWall()
        {
            var match = new Infiltration(Steady(), WithSentry(_walled, new Cell(8, 3), Trig.Left), 1);
            match.PlaceIntruder(new Cell(4, 3));
            Repeat(match, IntruderInput.None, 600);
            var guard = match.Guards[0];
            Assert.That(guard.Brain.Awareness, Is.Zero);
            Assert.That(guard.Brain.Mode, Is.EqualTo(GuardMode.Patrol));
            Assert.That(match.Level, Is.EqualTo(AlertLevel.Normal));
        }

        [Test]
        public void SentryInTheOpenDiscoversAndCatches()
        {
            var match = new Infiltration(Steady(), WithSentry(OpenRoom(), new Cell(8, 4), Trig.Left), 1);
            match.PlaceIntruder(new Cell(4, 4));
            match.Tick(IntruderInput.None);
            Assert.That(match.Guards[0].Sees, Is.True);
            Assert.That(match.Guards[0].Brain.Mode, Is.EqualTo(GuardMode.Investigate), "見えた最初の Tick はまだ発見ではない");
            Repeat(match, IntruderInput.None, 600);
            Assert.That(match.Status, Is.EqualTo(MatchStatus.Caught));
            Assert.That(match.CaughtBy, Is.Zero);
            Assert.That(match.Discoveries, Is.EqualTo(1));
        }

        [Test]
        public void FootstepsBehindTheGuardOnlyLeadToInvestigation()
        {
            // 背後 3 マスで走る。聞こえた Tick は調査になるだけで、気づき値も警戒レベルも上がらない。
            var match = new Infiltration(Steady(), WithSentry(OpenRoom(), new Cell(8, 4), Trig.Right), 1);
            match.PlaceIntruder(new Cell(5, 4));
            match.Tick(Run(0, 1));
            var guard = match.Guards[0];
            Assert.That(guard.Hears, Is.True);
            Assert.That(guard.Sees, Is.False);
            Assert.That(guard.Brain.Mode, Is.EqualTo(GuardMode.Investigate));
            Assert.That(guard.Brain.Awareness, Is.Zero);
            Assert.That(match.Level, Is.EqualTo(AlertLevel.Normal));
            Assert.That(match.Discoveries, Is.Zero);
        }

        [Test]
        public void SneakingIsQuieterThanWalkingAndRunning()
        {
            // 背後 2 マス：忍び足（1 マス）は聞こえず、歩き（3 マス）は聞こえる。
            foreach (var (input, heard) in new[] { (Sneak(0, 1), false), (Move(0, 1), true), (Run(0, 1), true) })
            {
                var match = new Infiltration(Steady(), WithSentry(OpenRoom(), new Cell(8, 4), Trig.Right), 1);
                match.PlaceIntruder(new Cell(6, 4));
                match.Tick(input);
                Assert.That(match.Guards[0].Hears, Is.EqualTo(heard), input.Gait.ToString());
            }
        }

        [Test]
        public void AfterLosingTrackTheGuardSearchesAndEverythingCalmsDown()
        {
            var config = new WardConfig { LookSweep = 0, AlertTicks = 100, CautionTicks = 120, SearchTicks = 200 };
            config.Validate();
            var match = new Infiltration(config, WithSentry(_blocked, new Cell(3, 6), Trig.Right), 1);
            match.PlaceIntruder(new Cell(5, 6));
            var guard = match.Guards[0];
            var ticks = 0;
            while (guard.Brain.Mode != GuardMode.Chase && ticks++ < 200) { match.Tick(IntruderInput.None); }
            Assert.That(guard.Brain.Mode, Is.EqualTo(GuardMode.Chase));
            Assert.That(match.Level, Is.EqualTo(AlertLevel.Alert));

            // 塊の向こうの遠くへ消える（止まっているので足音もない）。
            match.PlaceIntruder(new Cell(18, 1));
            var sawSearch = false;
            var previous = guard.Brain.Awareness;
            for (var t = 0; t < 2000 && !(guard.Brain.Mode == GuardMode.Patrol && sawSearch); t++)
            {
                match.Tick(IntruderInput.None);
                Assert.That(guard.Sees, Is.False, $"{t}");
                Assert.That(guard.Brain.Awareness, Is.LessThanOrEqualTo(previous));
                previous = guard.Brain.Awareness;
                sawSearch |= guard.Brain.Mode == GuardMode.Search;
            }

            Assert.That(sawSearch, Is.True);
            Assert.That(guard.Brain.Mode, Is.EqualTo(GuardMode.Patrol));
            Repeat(match, IntruderInput.None, 1000);
            Assert.That(guard.Brain.Awareness, Is.Zero);
            Assert.That(match.Level, Is.EqualTo(AlertLevel.Normal));
            Assert.That(match.Status, Is.EqualTo(MatchStatus.Playing));
            Assert.That(match.Discoveries, Is.EqualTo(1));
        }

        [Test]
        public void TouchingAnUnawareGuardIsNotACaptureButStartsAChase()
        {
            // 足音の届く距離 0 の忍び足で、背を向けた見張りに後ろから触れる。触れた Tick は捕まらず、次の Tick に追跡されて捕まる。
            var config = new WardConfig { LookSweep = 0, SneakNoise = 0 };
            config.Validate();
            var match = new Infiltration(config, WithSentry(OpenRoom(), new Cell(8, 4), Trig.Right), 1);
            match.PlaceIntruder(new Cell(5, 4));
            var guard = match.Guards[0];
            var touched = false;
            for (var t = 0; t < 200 && !touched; t++)
            {
                match.Tick(Sneak(1, 0));
                touched = match.Intruder.Body.Overlaps(guard.Body.Box);
            }

            Assert.That(touched, Is.True);
            Assert.That(match.Status, Is.EqualTo(MatchStatus.Playing));
            Assert.That(guard.Brain.Mode, Is.Not.EqualTo(GuardMode.Chase));
            match.Tick(IntruderInput.None);
            Assert.That(guard.Brain.Mode, Is.EqualTo(GuardMode.Chase));
            Assert.That(match.Status, Is.EqualTo(MatchStatus.Caught));
        }

        [Test]
        public void CaptureWinsOverEscapeInTheSameTick()
        {
            // 出口 (1,1) の右上に、上を向いた見張りが立つ。足音 0 の忍び足で後ろから触れて追跡させ、目的物を持った次の Tick に
            // 出口へ走り込む。左へずらす量を変えて、出口に入る Tick と捕まる Tick が重なる場面を探す。重なったら捕獲。
            var config = new WardConfig { LookSweep = 0, SneakNoise = 0 };
            config.Validate();
            var rows = new[] { "######", "#....#", "#....#", "#E..O#", "######" };
            var sameTick = 0;
            for (var shift = 0; shift < 20; shift++)
            {
                var match = new Infiltration(config, WithSentry(rows, new Cell(2, 2), Trig.Up), 1);
                match.PlaceIntruder(new Cell(2, 1));
                var guard = match.Guards[0];
                Repeat(match, Sneak(-1, 0), shift);
                while (!match.Intruder.Body.Overlaps(guard.Body.Box)) { match.Tick(Sneak(0, 1)); }
                Assert.That(match.Status, Is.EqualTo(MatchStatus.Playing));
                match.GiveObjective();
                match.Tick(Run(-1, 0));
                Assert.That(match.Status, Is.EqualTo(MatchStatus.Caught), $"{shift}");
                if (Cell.Of(match.Intruder.Center) == match.Facility.Exit) { sameTick++; }
            }

            Assert.That(sameTick, Is.GreaterThan(0));
        }

        [Test]
        public void ObjectiveThenExitWins()
        {
            var match = new Infiltration(Steady(), WithGuards(OpenRoom()), 1);
            Repeat(match, Move(0, 1), 30);
            Repeat(match, Move(0, -1), 60);
            Assert.That(match.Status, Is.EqualTo(MatchStatus.Playing), "目的物なしで出口に入っても終わらない");

            Repeat(match, Move(1, 0), 400);
            Repeat(match, Move(0, 1), 200);
            Assert.That(match.HasObjective, Is.True);
            Assert.That(match.TakeEvents() & WardEvents.ObjectiveTaken, Is.EqualTo(WardEvents.ObjectiveTaken));
            Repeat(match, Move(-1, 0), 400);
            Repeat(match, Move(0, -1), 200);
            Assert.That(match.Status, Is.EqualTo(MatchStatus.Escaped));
            Assert.That(match.Discoveries, Is.Zero);
        }

        [Test]
        public void FinishedMatchIgnoresTicks()
        {
            var match = new Infiltration(Steady(), WithSentry(OpenRoom(), new Cell(8, 4), Trig.Left), 1);
            match.PlaceIntruder(new Cell(4, 4));
            Repeat(match, IntruderInput.None, 600);
            Assert.That(match.IsOver, Is.True);
            var hash = match.Hash();
            var ticks = match.Ticks;
            match.Tick(Run(1, 1));
            match.Tick(Run(-1, 0));
            Assert.That(match.Hash(), Is.EqualTo(hash));
            Assert.That(match.Ticks, Is.EqualTo(ticks));
        }

        [Test]
        public void SameSeedAndInputsGiveTheSameHashAndClonesAreIndependent()
        {
            var a = new Infiltration(new WardConfig(), Facility.Ward(), 3);
            var b = new Infiltration(new WardConfig(), Facility.Ward(), 3);
            var random = new SeededRandom(9);
            Infiltration? clone = null;
            for (var t = 0; t < 3000; t++)
            {
                var input = new IntruderInput(random.Range(-1, 1), random.Range(-1, 1), random.Next(3) == 0, random.Next(4) == 0);
                a.Tick(input);
                b.Tick(input);
                clone?.Tick(input);
                Assert.That(b.Hash(), Is.EqualTo(a.Hash()), $"{t}");
                if (clone != null) { Assert.That(clone.Hash(), Is.EqualTo(a.Hash()), $"{t}"); }
                if (t == 500) { clone = a.Clone(); }
            }

            var before = a.Hash();
            var other = a.Clone();
            Repeat(other, Run(1, 0), 100);
            Assert.That(a.Hash(), Is.EqualTo(before), "複製を進めても元は変わらない");
        }

        [Test]
        public void SightIsJudgedFromThePoseKeptForDrawing()
        {
            // 視界の扇は、その Tick の知覚に使った姿勢（動く前の位置と向き、Tick の始めの視界）で描く。その姿勢で判定し直すと
            // 見えたかどうかが毎 Tick 一致する。追跡・警戒で視程が変わる試合で確かめる。
            var match = new Infiltration(new WardConfig(), Facility.Ward(), 4);
            var random = new SeededRandom(17);
            var seenTicks = 0;
            for (var t = 0; t < 3000 && !match.IsOver; t++)
            {
                match.Tick(t < 200 ? Move(1, 1) : new IntruderInput(random.Range(-1, 1), random.Range(-1, 1), random.Next(2) == 0, false));
                foreach (var guard in match.Guards)
                {
                    Assert.That(Vision.Sees(match.Map, guard.SightOrigin, guard.SightFacing, guard.SightVision, match.Intruder.Center), Is.EqualTo(guard.Sees), $"{t} {guard.Plan.Name}");
                    if (guard.Sees) { seenTicks++; }
                }
            }

            Assert.That(seenTicks, Is.GreaterThan(0), "見えた場面を含む");
        }

        [Test]
        public void HashSeesStateThatOnlyMattersLater()
        {
            // 位置は同じでも、足音の間隔の途中かどうかで以後の足音の時機が違う。ハッシュも違う。
            var a = new Infiltration(Steady(), WithGuards(OpenRoom()), 1);
            var b = new Infiltration(Steady(), WithGuards(OpenRoom()), 1);
            a.Tick(Move(1, 0));
            a.Tick(Move(-1, 0));
            b.Tick(IntruderInput.None);
            b.Tick(IntruderInput.None);
            Assert.That(a.Intruder.Body, Is.EqualTo(b.Intruder.Body));
            Assert.That(a.Hash(), Is.Not.EqualTo(b.Hash()));
        }
    }
}
