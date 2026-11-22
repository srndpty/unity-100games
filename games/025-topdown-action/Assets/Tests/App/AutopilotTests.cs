using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>完走：操縦役で探索 → 鍵 → ボス → 秘宝を最後まで進められる（体力 0 なら入口から再開して続ける）。</summary>
    public sealed class AutopilotTests
    {
        /// <summary>1 試合の Tick 数の上限（60 Tick/秒で 10 分）。</summary>
        public const int Limit = 36_000;

        /// <summary>操縦役で 1 試合を最後まで進める。勝てば true。</summary>
        public static bool Play(Dungeon dungeon, IPilot pilot, int limit = Limit)
        {
            for (var tick = 0; tick < limit; tick++)
            {
                if (dungeon.IsWon) { return true; }
                if (dungeon.Status == DungeonStatus.Down) { dungeon.Continue(); }
                dungeon.Tick(pilot.InputFor(dungeon));
            }

            return dungeon.IsWon;
        }

        [Test]
        public void TwentySeeds_AllWinWithinTheLimit()
        {
            var total = 0L;
            var deaths = 0;
            var worst = 0L;
            for (var seed = 1; seed <= 20; seed++)
            {
                var dungeon = new Dungeon(new VaultConfig(), Rooms.Create(), seed);
                Assert.That(Play(dungeon, new Autopilot()), Is.True, $"シード {seed} で勝てない（部屋 {dungeon.RoomIndex}、目標 {Autopilot.Goal(dungeon).Kind}、{dungeon.ElapsedTicks} Tick）");
                total += dungeon.ElapsedTicks;
                deaths += dungeon.Deaths;
                worst = System.Math.Max(worst, dungeon.ElapsedTicks);
                Assert.That(dungeon.State.BossDefeated, Is.True);
                Assert.That(dungeon.State.HasBossKey, Is.True);
                Assert.That(dungeon.State.SmallKeys, Is.EqualTo(0), "小鍵 2 本を小扉 2 枚に使った");
            }

            TestContext.WriteLine($"平均 {total / 20} Tick、最長 {worst} Tick、死亡 {deaths} 回（20 試合）");
        }

        /// <summary>シード 1 の通しの最終状態。整数と Tick だけで書いたルールなので、.NET と Mono（Unity）で同じ値になる。</summary>
        [Test]
        public void SeedOne_FinalHash_IsFixed()
        {
            var dungeon = new Dungeon(new VaultConfig(), Rooms.Create(), 1);
            Assert.That(Play(dungeon, new Autopilot()), Is.True);
            TestContext.WriteLine($"{dungeon.ElapsedTicks} Tick、ハッシュ 0x{dungeon.Hash():X8}");
            Assert.That(dungeon.ElapsedTicks, Is.EqualTo(ExpectedTicks));
            Assert.That(dungeon.Hash(), Is.EqualTo(ExpectedHash));
        }

        [Test]
        public void SamePilot_AfterAnAbandonedGame_ReplaysTheSameGame()
        {
            var pilot = new Autopilot();
            var first = new Dungeon(new VaultConfig(), Rooms.Create(), 1);
            Assert.That(Play(first, pilot), Is.True);

            // 別の試合をボスの間の途中（判断の途中）でやめる。
            var abandoned = new Dungeon(new VaultConfig(), Rooms.Create(), 2);
            for (var tick = 0; tick < Limit && !(abandoned.RoomIndex == Rooms.Lair && abandoned.RoomTicks == 70); tick++) { abandoned.Tick(pilot.InputFor(abandoned)); }
            Assert.That(abandoned.RoomIndex, Is.EqualTo(Rooms.Lair));

            var again = new Dungeon(new VaultConfig(), Rooms.Create(), 1);
            Assert.That(Play(again, pilot), Is.True);
            Assert.That(again.Hash(), Is.EqualTo(first.Hash()), "同じ操縦役で次の試合をしても同じ試合になる");
        }

        private const long ExpectedTicks = 2923;
        private const uint ExpectedHash = 0xFCD220F3u;
    }
}
