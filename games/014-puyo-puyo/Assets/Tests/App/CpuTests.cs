using System;
using Game.Core;
using NUnit.Framework;
using static Game.App.Tests.AppTestKit;

namespace Game.App.Tests
{
    public sealed class CpuTests
    {
        private const int MaxMatchTicks = 60 * 60 * 30;

        private static readonly GameCommand[] _threeClockwiseTurns =
        {
            GameCommand.RotateClockwise,
            GameCommand.RotateClockwise,
            GameCommand.RotateClockwise,
            GameCommand.SoftDropOn,
        };

        private static readonly GameCommand[] _oneCounterClockwiseTurn =
        {
            GameCommand.RotateCounterClockwise,
            GameCommand.SoftDropOn,
        };

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
        public void 押し出しで左回転と結果が違う三回転は右回転のまま実行する()
        {
            var board = StackedBoard(0, 4, 1, 12, 6, 4);
            var placement = PlacementAt(board, ActivePair.Spawn(EmberPair), 1, 11, PairOrientation.Left);

            Assert.That(placement.Commands, Is.EqualTo(_threeClockwiseTurns));

            var field = FieldAfter(board, placement);
            for (var i = 0; i < 2000 && field.Phase == FieldPhase.Falling; i++) { field.Tick(); }
            Assert.That(field.Board.At(1, 4), Is.EqualTo(Cell.Ember), "軸は1列目の積みの上に着く");
            Assert.That(field.Board.At(0, 0), Is.EqualTo(Cell.Ember), "子は0列目の底まで落ちる");
        }

        [Test]
        public void 左回転と結果が同じ三回転は左回転一回に短縮する()
        {
            var spawn = ActivePair.Spawn(EmberPair);
            var placement = PlacementAt(Board.Empty, spawn, spawn.X, spawn.Y, PairOrientation.Left);
            Assert.That(placement.Commands, Is.EqualTo(_oneCounterClockwiseTurn));
        }

        [Test]
        public void 列挙した置き方は操作の列を実行した位置と一致する()
        {
            for (var seed = 0; seed < 300; seed++)
            {
                var board = RandomStackedBoard(new SeededRandom(seed));
                foreach (var placement in PairPlanner.Placements(board, ActivePair.Spawn(EmberPair)))
                {
                    var field = FieldAfter(board, placement);
                    Assert.That(field.Active, Is.EqualTo(placement.Pair), $"シード {seed}");
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

        private static PairPlacement PlacementAt(Board board, ActivePair active, int x, int y, PairOrientation orientation)
        {
            foreach (var placement in PairPlanner.Placements(board, active))
            {
                var pair = placement.Pair;
                if (pair.X == x && pair.Y == y && pair.Orientation == orientation) { return placement; }
            }

            throw new InvalidOperationException($"({x}, {y}) {orientation} の置き方がありません。");
        }

        // Tick を挟まずに操作の列を実行し、ペアの位置を操作だけで動かす。
        private static PlayerField FieldAfter(Board board, PairPlacement placement)
        {
            var field = new PlayerField(new SporeConfig(), new FixedPairs(EmberPair), 1, board);
            foreach (var command in placement.Commands)
            {
                Assert.That(field.Apply(command), Is.True, command.ToString());
            }

            return field;
        }

        private static Board RandomStackedBoard(SeededRandom random)
        {
            var heights = new int[Board.Width];
            for (var x = 0; x < Board.Width; x++)
            {
                heights[x] = random.Next(x == Board.SpawnX ? Board.VisibleHeight - 1 : Board.Height);
            }

            return StackedBoard(heights);
        }

        private static Board StackedBoard(params int[] heights)
        {
            var board = Board.Empty;
            for (var x = 0; x < heights.Length; x++)
            {
                for (var y = 0; y < heights[x]; y++)
                {
                    board = board.With(x, y, (Cell)((int)Cell.Ember + ((x + y) % CellRules.SporeColorCount)));
                }
            }

            return board;
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
