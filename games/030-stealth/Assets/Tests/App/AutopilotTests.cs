using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>完了判定「見つからず／見つかって両経路でクリア可能」。本番の施設を操縦役で通す。</summary>
    public sealed class AutopilotTests
    {
        private const int Limit = 20000;
        private const long StealthTicks = 1819;
        private const uint StealthHash = 0xA0BB2468;
        private const uint BoldSeed1Hash = 0x3C5EC9CF;

        [Test]
        public void StealthRouteEscapesWithoutBeingNoticed()
        {
            var match = new Infiltration(new WardConfig(), Facility.Ward(), 1);
            var pilot = new StealthPilot();
            var noticed = false;
            while (!match.IsOver && match.Ticks < Limit)
            {
                match.Tick(pilot.InputFor(match));
                noticed |= !StealthPilot.Unnoticed(match);
            }

            Assert.That(match.Status, Is.EqualTo(MatchStatus.Escaped), $"Ticks {match.Ticks} at {match.Intruder.Center}");
            Assert.That(noticed, Is.False, "誰も調査・追跡に入らず、見えも聞こえもしない");
            Assert.That(match.Discoveries, Is.Zero);
            TestContext.WriteLine($"隠密：{match.Ticks} Tick、試行 {pilot.Trials} 回、ハッシュ 0x{match.Hash():X8}");

            // 見つからない限り乱数を使わないので、シードに依らず同じ道になる。整数だけの規則なので .NET と Mono で一致する。
            Assert.That(match.Ticks, Is.EqualTo(StealthTicks));
            Assert.That(match.Hash(), Is.EqualTo(StealthHash));
        }

        [Test]
        public void BoldRouteIsDiscoveredButEscapes([Range(1, 20)] int seed)
        {
            var match = new Infiltration(new WardConfig(), Facility.Ward(), seed);
            var pilot = new BoldPilot();
            while (!match.IsOver && match.Ticks < Limit) { match.Tick(pilot.InputFor(match)); }
            Assert.That(match.Status, Is.EqualTo(MatchStatus.Escaped), $"Ticks {match.Ticks} at {match.Intruder.Center} caught by {match.CaughtBy}");
            Assert.That(match.Discoveries, Is.GreaterThanOrEqualTo(1));
            TestContext.WriteLine($"強行 シード {seed}：{match.Ticks} Tick、発見 {match.Discoveries} 回、ハッシュ 0x{match.Hash():X8}");
            if (seed == 1) { Assert.That(match.Hash(), Is.EqualTo(BoldSeed1Hash), ".NET と Mono で一致する"); }
        }

        [Test]
        public void StealthRouteIsTheSameForEverySeed()
        {
            var match = new Infiltration(new WardConfig(), Facility.Ward(), 12345);
            var pilot = new StealthPilot();
            while (!match.IsOver && match.Ticks < Limit) { match.Tick(pilot.InputFor(match)); }
            Assert.That(match.Status, Is.EqualTo(MatchStatus.Escaped));
            Assert.That(match.Ticks, Is.EqualTo(StealthTicks));
        }
    }
}
