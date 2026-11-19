using System;
using Game.Core;
using NUnit.Framework;
using static Game.App.Tests.AppTestKit;

namespace Game.App.Tests
{
    public sealed class CpuTests
    {
        private const int MaxMatchTicks = 60 * 60 * 30;

        [Test]
        public void 四個そろう置き方を選ぶ()
        {
            var board = Board.Empty
                .With(0, 0, Cell.Ember)
                .With(1, 0, Cell.Ember);
            var field = new PlayerField(new SporeConfig(), new FixedPairs(EmberPair), 1, board);
            foreach (var command in PairPlanner.Plan(new SporeConfig(), field)) { field.Apply(command); }
            for (var i = 0; i < 2000 && field.Score == 0; i++) { field.Tick(); }
            Assert.That(field.Score, Is.GreaterThan(0));
        }

        [Test]
        public void 操作の列はソフトドロップで終わる()
        {
            var field = new PlayerField(new SporeConfig(), new FixedPairs(EmberPair), 1);
            var plan = PairPlanner.Plan(new SporeConfig(), field);
            Assert.That(plan[plan.Count - 1], Is.EqualTo(GameCommand.SoftDropOn));
        }

        [Test]
        public void ペアがなければ何もしない()
        {
            var board = Board.Empty;
            for (var y = 0; y < Board.VisibleHeight; y++) { board = board.With(Board.SpawnX, y, y % 2 == 0 ? Cell.Moss : Cell.Tide); }
            var field = new PlayerField(new SporeConfig(), new FixedPairs(EmberPair), 1, board);
            Assert.That(PairPlanner.Plan(new SporeConfig(), field), Is.Empty);
        }

        [Test]
        public void 操作の列はすべて受け付けられる()
        {
            for (var seed = 0; seed < 20; seed++)
            {
                var field = new PlayerField(new SporeConfig(), new PairSequence(seed), seed);
                foreach (var command in PairPlanner.Plan(new SporeConfig(), field))
                {
                    Assert.That(field.Apply(command), Is.True, $"シード {seed} の {command}");
                }
            }
        }

        [Test]
        public void 同じシードなら同じ手を選ぶ()
        {
            Assert.That(PlayCpu(9), Is.EqualTo(PlayCpu(9)));
        }

        [Test]
        public void 不正な確率を拒否する()
        {
            var field = new PlayerField(new SporeConfig(), new FixedPairs(EmberPair), 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => PairPlanner.Plan(new SporeConfig(), field, new SeededRandom(1), 101));
            Assert.Throws<ArgumentNullException>(() => PairPlanner.Plan(new SporeConfig(), field, null, 50));
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void 自動対戦は上限時間内に決着する(int seed)
        {
            var config = new SporeConfig();
            var match = new VersusMatch(config, seed);
            var player = new CpuController(config, seed, 6, 25);
            var cpu = new CpuController(config, match.CpuSeed, 6, 25);
            for (var i = 0; i < MaxMatchTicks && !match.IsOver; i++)
            {
                player.Tick(match.Player);
                cpu.Tick(match.Cpu);
                match.Tick();
            }

            Assert.That(match.IsOver, Is.True);
        }

        private static uint PlayCpu(int seed)
        {
            var config = new SporeConfig();
            var match = new VersusMatch(config, seed);
            var cpu = new CpuController(config, match.CpuSeed, 4, 30);
            for (var i = 0; i < 3000 && !match.IsOver; i++)
            {
                cpu.Tick(match.Cpu);
                match.Tick();
            }

            return match.Cpu.Board.Fingerprint();
        }
    }
}
