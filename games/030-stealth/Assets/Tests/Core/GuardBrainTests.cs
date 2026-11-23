using IntGeometry;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>
    /// 判断（FSM）を地図なしで確かめる。作った <see cref="Percept"/> を与えるだけで、巡回 → 調査 → 追跡 → 捜索 → 巡回の
    /// すべての遷移が起きる（センサと判断の分離）。題材固有テスト「音だけで即発見としない」「見失った後に警戒が減衰」。
    /// </summary>
    public sealed class GuardBrainTests
    {
        private const int Range = 6 * T;
        private static readonly Cell _home = new(2, 2);
        private static readonly Cell _far = new(9, 2);

        [Test]
        public void PatrolWalksToEachWaypointAndLooksAround()
        {
            var config = Steady();
            var brain = NewBrain(config, out _);
            var order = brain.Think(Percept.Nothing(null, 0), Range);
            Assert.That(order.IsLook, Is.False);
            Assert.That(order.Goal, Is.EqualTo(_home));

            // 着いた Tick から見回し、巡回点の DwellTicks（30）が終わったら次の点へ歩く。
            for (var t = 0; t < 30; t++)
            {
                order = brain.Think(Percept.Nothing(_home, 0), Range);
                Assert.That(order.IsLook, Is.True, $"{t}");
                Assert.That(order.LookElapsed, Is.EqualTo(t));
            }

            order = brain.Think(Percept.Nothing(_home, 0), Range);
            Assert.That(order.IsLook, Is.False);
            Assert.That(order.Goal, Is.EqualTo(_far));
            Assert.That(brain.Mode, Is.EqualTo(GuardMode.Patrol));
        }

        [Test]
        public void FootstepsAloneNeverStartAChase()
        {
            var brain = NewBrain(Steady(), out _);
            var noise = new Cell(5, 5);
            for (var t = 0; t < 5000; t++)
            {
                brain.Think(new Percept(null, 0, noise, null, null, t % 2 == 0 ? noise : (Cell?)null, 0), Range);
                Assert.That(brain.Mode, Is.EqualTo(GuardMode.Investigate), $"{t}");
                Assert.That(brain.Awareness, Is.Zero);
                Assert.That(brain.StartedChase, Is.False);
            }

            Assert.That(brain.Goal, Is.EqualTo(noise));
        }

        [Test]
        public void RadioAloneLeadsToInvestigation()
        {
            var brain = NewBrain(Steady(), out _);
            var place = new Cell(7, 4);
            brain.Think(new Percept(null, 0, null, place, null, null, 0), Range);
            Assert.That(brain.Mode, Is.EqualTo(GuardMode.Investigate));
            Assert.That(brain.Goal, Is.EqualTo(place));
            Assert.That(brain.Awareness, Is.Zero);
        }

        [Test]
        public void SightBuildsAwarenessUntilChase()
        {
            // 目の前（距離 0）で見えると 1 Tick に 60 増える。16 Tick で 960（調査）、17 Tick 目に 1000 で追跡。
            var brain = NewBrain(Steady(), out _);
            var at = C(4, 4);
            for (var t = 1; t <= 16; t++)
            {
                brain.Think(new Percept(at, 0, null, null, null, null, 0), Range);
                Assert.That(brain.Mode, Is.EqualTo(GuardMode.Investigate), $"{t}");
                Assert.That(brain.Awareness, Is.EqualTo(60 * t));
            }

            var order = brain.Think(new Percept(at, 0, null, null, null, null, 0), Range);
            Assert.That(brain.Mode, Is.EqualTo(GuardMode.Chase));
            Assert.That(brain.StartedChase, Is.True);
            Assert.That(brain.Awareness, Is.EqualTo(WardConfig.AwarenessMax));
            Assert.That(order.Pace, Is.EqualTo(Pace.Chase));
            Assert.That(order.FacePoint, Is.EqualTo(at));

            brain.Think(new Percept(at, 0, null, null, null, null, 0), Range);
            Assert.That(brain.StartedChase, Is.False, "追跡に入るのは 1 回だけ");
        }

        [Test]
        public void GainFallsLinearlyWithDistance()
        {
            var config = Steady();
            Assert.That(GuardBrain.GainAt(config, 0, Range), Is.EqualTo(60));
            Assert.That(GuardBrain.GainAt(config, Range, Range), Is.EqualTo(10));
            Assert.That(GuardBrain.GainAt(config, Range / 2, Range), Is.EqualTo(35));
            Assert.That(GuardBrain.GainAt(config, Range * 2, Range), Is.EqualTo(10), "視程の外は視程と同じ");
        }

        [Test]
        public void TouchStartsAChaseAtOnce()
        {
            var brain = NewBrain(Steady(), out _);
            brain.Think(new Percept(null, 0, null, null, C(3, 3), null, 0), Range);
            Assert.That(brain.Mode, Is.EqualTo(GuardMode.Chase));
            Assert.That(brain.Goal, Is.EqualTo(new Cell(3, 3)));
        }

        [Test]
        public void ChaseFollowsFootstepsWhenOutOfSight()
        {
            var brain = Chasing(Steady(), out _);
            var noise = new Cell(8, 6);
            brain.Think(new Percept(null, 0, noise, null, null, null, 0), Range);
            Assert.That(brain.Mode, Is.EqualTo(GuardMode.Chase));
            Assert.That(brain.Goal, Is.EqualTo(noise));
        }

        [Test]
        public void LosingTrackLeadsToSearchThenPatrolWhileAwarenessDecays()
        {
            var config = Steady();
            var brain = Chasing(config, out var area);
            var lost = brain.Goal;

            // 見失ったまま最後の位置へ歩く間は追跡のまま、気づき値は 1 Tick に 3 ずつ減る。
            var previous = brain.Awareness;
            for (var t = 0; t < 10; t++)
            {
                brain.Think(Percept.Nothing(null, 0), Range);
                Assert.That(brain.Mode, Is.EqualTo(GuardMode.Chase));
                Assert.That(brain.Awareness, Is.EqualTo(previous - config.AwarenessDecay));
                previous = brain.Awareness;
            }

            // 最後の位置に着いたら捜索。中心の周りを選んで歩く。
            brain.Think(Percept.Nothing(lost, 0), Range);
            Assert.That(brain.Mode, Is.EqualTo(GuardMode.Search));
            Assert.That(brain.SearchCenter, Is.EqualTo(lost));
            Assert.That(area.Picked, Has.Count.EqualTo(1));
            Assert.That(brain.Goal, Is.EqualTo(area.Picked[0]));

            // 捜索は SearchTicks で終わり、巡回へ戻る。気づき値は単調に減って 0 で止まる。
            var ticks = 1;
            while (brain.Mode == GuardMode.Search)
            {
                previous = brain.Awareness;
                brain.Think(Percept.Nothing(ticks % 3 == 0 ? brain.Goal : (Cell?)null, 0), Range);
                Assert.That(brain.Awareness, Is.LessThanOrEqualTo(previous));
                ticks++;
            }

            Assert.That(ticks, Is.EqualTo(config.SearchTicks));
            Assert.That(brain.Mode, Is.EqualTo(GuardMode.Patrol));
            Assert.That(area.Picked.Count, Is.GreaterThan(1), "捜索中に何か所か巡る");
            for (var t = 0; t < 400; t++) { brain.Think(Percept.Nothing(null, 0), Range); }
            Assert.That(brain.Awareness, Is.Zero);
        }

        [Test]
        public void SeenAgainDuringSearchResumesTheChaseQuickly()
        {
            // 捜索の間は気づき値が残っているので、もう一度見えるとすぐ追跡に戻る。
            var brain = Chasing(Steady(), out _);
            brain.Think(Percept.Nothing(brain.Goal, 0), Range);
            Assert.That(brain.Mode, Is.EqualTo(GuardMode.Search));
            brain.Think(new Percept(C(6, 6), Range, null, null, null, null, 0), Range);
            Assert.That(brain.Mode, Is.EqualTo(GuardMode.Chase));
        }

        [Test]
        public void InvestigationEndsWithALookAroundThenPatrol()
        {
            var config = Steady();
            var brain = NewBrain(config, out _);
            var noise = new Cell(5, 5);
            brain.Think(new Percept(null, 0, noise, null, null, null, 0), Range);
            for (var t = 0; t < config.InvestigateLookTicks; t++)
            {
                var order = brain.Think(Percept.Nothing(noise, Trig.Up), Range);
                Assert.That(order.IsLook, Is.True);
                Assert.That(order.LookBase, Is.EqualTo(Trig.Up), "着いたときの向きを中心に見回す");
            }

            Assert.That(brain.Mode, Is.EqualTo(GuardMode.Patrol));
        }

        [Test]
        public void CloneIsIndependent()
        {
            var brain = NewBrain(Steady(), out _);
            var copy = brain.Clone();
            brain.Think(new Percept(C(4, 4), 0, null, null, null, null, 0), Range);
            Assert.That(copy.Awareness, Is.Zero);
            Assert.That(copy.Mode, Is.EqualTo(GuardMode.Patrol));
        }

        [Test]
        public void RejectsInvalidInput()
        {
            var brain = NewBrain(Steady(), out _);
            Assert.That(() => brain.Think(Percept.Nothing(null, 0), 0), Throws.InstanceOf<System.ArgumentOutOfRangeException>());
            Assert.That(() => new Percept(null, -1, null, null, null, null, 0), Throws.InstanceOf<System.ArgumentOutOfRangeException>());
            Assert.That(() => new Waypoint(_home, 0, 0), Throws.InstanceOf<System.ArgumentOutOfRangeException>());
            Assert.That(() => new GuardBrain(Steady(), System.Array.Empty<Waypoint>(), new RightOfCenter(), new SeededRandom(1)), Throws.ArgumentException);
        }

        private static GuardBrain NewBrain(WardConfig config, out RightOfCenter area)
        {
            area = new RightOfCenter();
            return new GuardBrain(config, new[] { new Waypoint(_home, 30, Trig.Right), new Waypoint(_far, 30, Trig.Left) }, area, new SeededRandom(5));
        }

        private static GuardBrain Chasing(WardConfig config, out RightOfCenter area)
        {
            var brain = NewBrain(config, out area);
            brain.Think(new Percept(null, 0, null, null, C(4, 4), null, 0), Range);
            Assert.That(brain.Mode, Is.EqualTo(GuardMode.Chase));
            return brain;
        }
    }
}
