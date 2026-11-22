using System;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>全編の通し：入力の列とボスの操縦役で、最初から最後までを被弾なしで進める。</summary>
    public sealed class RouteTests
    {
        private static Expedition PlayThrough(out int[] roomsVisited)
        {
            var expedition = new Expedition(new ExplorerConfig(), Rooms.Create());
            var pilot = Routes.Pilot();
            var visits = new System.Collections.Generic.List<int> { expedition.RoomIndex };
            for (var tick = 0; tick < 20_000 && expedition.Status == ExpeditionStatus.Playing; tick++)
            {
                expedition.Tick(pilot.InputFor(expedition));
                if ((expedition.TakeEvents() & ExpeditionEvents.RoomChanged) != 0) { visits.Add(expedition.RoomIndex); }
            }

            roomsVisited = visits.ToArray();
            return expedition;
        }

        [Test]
        public void Routes_FollowTheBacktrackingPath()
        {
            PlayThrough(out var visited);
            Assert.That(visited, Is.EqualTo(new[] { Rooms.Hub, Rooms.Corridor, Rooms.Cave, Rooms.Corridor, Rooms.Hub, Rooms.Tower, Rooms.Lair }), "能力を取って戻る道順");
        }

        [Test]
        public void Routes_BeatTheBossWithoutDying()
        {
            var e = PlayThrough(out _);
            TestContext.Out.WriteLine($"{e.ElapsedTicks} Tick、体力 {e.Player.Health}、ハッシュ 0x{e.Hash():X8}");
            Assert.That(e.Status, Is.EqualTo(ExpeditionStatus.Won));
            Assert.That(e.Deaths, Is.Zero);
            Assert.That(e.State.Has(Ability.DoubleJump | Ability.BreakShot), Is.True);
            Assert.That(e.Save.Room, Is.EqualTo(Rooms.Tower), "塔の像でセーブした");
            Assert.That(e.Save.State.Has(Ability.BreakShot), Is.True, "破壊ショットを取ってからセーブした");
            Assert.That(e.Save.State.BrokenCount, Is.Zero, "ひびの壁はセーブの後に壊した");

            // 整数だけで動くので、.NET と Mono（Unity）で同じ最終状態になる。
            Assert.That(e.ElapsedTicks, Is.EqualTo(2565));
            Assert.That(e.Hash(), Is.EqualTo(0xD0072BE8u));
        }

        [Test]
        public void Routes_SegmentsTakeNoDamage()
        {
            var expedition = new Expedition(new ExplorerConfig(), Rooms.Create());
            var pilot = new RoutePilot(Routes.All());
            for (var tick = 0; tick < 20_000 && expedition.RoomIndex != Rooms.Lair; tick++)
            {
                expedition.Tick(pilot.InputFor(expedition));
                Assert.That(expedition.TakeEvents() & ExpeditionEvents.Hurt, Is.EqualTo(ExpeditionEvents.None), $"{tick} Tick 目に被弾");
            }

            Assert.That(expedition.RoomIndex, Is.EqualTo(Rooms.Lair));
            Assert.That(expedition.Player.Health, Is.EqualTo(new ExplorerConfig().MaxHealth));
        }

        [Test]
        public void RouteScript_ParsesAndFormats()
        {
            var script = RouteScript.Parse("R3 LJ2 S1 -2");
            Assert.That(script.Length, Is.EqualTo(8));
            Assert.That(script.InputAt(3), Is.EqualTo(new PlayerInput(-1, true, false)));
            Assert.That(script.InputAt(5), Is.EqualTo(new PlayerInput(0, false, true)));
            Assert.That(script.InputAt(99), Is.EqualTo(PlayerInput.None), "列の後は何も押さない");
            Assert.That(RouteScript.Format(script.Inputs), Is.EqualTo("R3 LJ2 S1 -2"));
            Assert.Throws<FormatException>(() => RouteScript.Parse("Q3"));
            Assert.Throws<FormatException>(() => RouteScript.Parse("RR3"));
            Assert.Throws<FormatException>(() => RouteScript.Parse("R0"));
            Assert.Throws<FormatException>(() => RouteScript.Parse("R"));
            Assert.Throws<FormatException>(() => RouteScript.Parse($"R{RouteScript.MaxTicks + 1}"));
            Assert.Throws<ArgumentNullException>(() => RouteScript.Parse(null!));
        }
    }
}
