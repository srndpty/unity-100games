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
        public void SamePilot_AfterAnAbandonedBossFight_ReplaysTheSameGame()
        {
            // レビュー P3-1：ボス戦の途中でやめた試合の計画（選んだ手と残り Tick）が、同じ操縦役の次の試合へ残っていた。
            var pilot = Routes.Pilot();
            var first = Run(pilot, e => e.Status != ExpeditionStatus.Playing);
            Assert.That(first.IsWon, Is.True);

            // ボスの間に入って 50 Tick（判断の途中）でやめる。
            Run(pilot, e => e.RoomIndex == Rooms.Lair && e.RoomTicks == 50);

            var again = Run(pilot, e => e.Status != ExpeditionStatus.Playing);
            Assert.That(again.Hash(), Is.EqualTo(first.Hash()), "同じ操縦役で再戦しても同じ試合になる");
            Assert.That(again.Hash(), Is.EqualTo(0xD0072BE8u));
        }

        private static Expedition Run(RoutePilot pilot, Func<Expedition, bool> stop)
        {
            var expedition = new Expedition(new ExplorerConfig(), Rooms.Create());
            for (var tick = 0; tick < 20_000 && !stop(expedition); tick++) { expedition.Tick(pilot.InputFor(expedition)); }
            Assert.That(stop(expedition), Is.True);
            return expedition;
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
