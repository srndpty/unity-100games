using NUnit.Framework;
using static Game.Core.Tests.SporeTestKit;

namespace Game.Core.Tests
{
    public sealed class VersusMatchTests
    {
        private const int SettleSteps = 40;

        private static Board NearlyFull()
        {
            var board = Board.Empty;
            for (var y = 0; y < Board.VisibleHeight - 2; y++) { board = board.With(Board.SpawnX, y, y % 2 == 0 ? Cell.Moss : Cell.Tide); }
            return board;
        }

        private static void Run(VersusMatch match)
        {
            for (var i = 0; i < SettleSteps && !match.IsOver; i++) { match.Tick(); }
        }

        private static void StartTwoChain(PlayerField field)
        {
            field.Apply(GameCommand.MoveLeft);
            field.Apply(GameCommand.SoftDropOn);
        }

        private static int CountAsh(Board board)
        {
            var count = 0;
            foreach (var cell in board.ToCells())
            {
                if (cell == Cell.Ash) { count++; }
            }

            return count;
        }

        [Test]
        public void 両者は同じペア列を使う()
        {
            var match = new VersusMatch(new SporeConfig(), 5);
            for (var i = 0; i < 8; i++) { Assert.That(match.Player.Next(i), Is.EqualTo(match.Cpu.Next(i))); }
        }

        [Test]
        public void 相手が窒息すると勝つ()
        {
            var match = new VersusMatch(InstantConfig(), new FixedPairs(EmberPair), 1, null, NearlyFull());
            match.Cpu.Apply(GameCommand.SoftDropOn);
            Run(match);
            Assert.That(match.Outcome, Is.EqualTo(MatchOutcome.PlayerWin));
        }

        [Test]
        public void 自分が窒息すると負ける()
        {
            var match = new VersusMatch(InstantConfig(), new FixedPairs(EmberPair), 1, NearlyFull());
            match.Player.Apply(GameCommand.SoftDropOn);
            Run(match);
            Assert.That(match.Outcome, Is.EqualTo(MatchOutcome.CpuWin));
        }

        [Test]
        public void 同じTickに両者が窒息すると引き分け()
        {
            var match = new VersusMatch(InstantConfig(), new FixedPairs(EmberPair), 1, NearlyFull(), NearlyFull());
            match.Player.Apply(GameCommand.SoftDropOn);
            match.Cpu.Apply(GameCommand.SoftDropOn);
            Run(match);
            Assert.That(match.Outcome, Is.EqualTo(MatchOutcome.Draw));
        }

        [Test]
        public void 決着後は進まない()
        {
            var match = new VersusMatch(InstantConfig(), new FixedPairs(EmberPair), 1, NearlyFull());
            match.Player.Apply(GameCommand.SoftDropOn);
            Run(match);
            var ticks = match.Cpu.TickCount;
            match.Tick();
            Assert.That(match.Cpu.TickCount, Is.EqualTo(ticks));
        }

        [Test]
        public void 連鎖の攻撃が相手の予告に届く()
        {
            var match = new VersusMatch(InstantConfig(), new FixedPairs(EmberPair, TidePair), 1, TwoChainBoard());
            StartTwoChain(match.Player);
            Run(match);
            Assert.That(match.Player.Score, Is.EqualTo(200));
            Assert.That(match.Cpu.PendingGarbage, Is.EqualTo(2));
        }

        [Test]
        public void 攻撃は自分の予告と相殺してから届く()
        {
            var match = new VersusMatch(InstantConfig(), new FixedPairs(EmberPair, TidePair), 1, TwoChainBoard());
            match.Player.ReceiveGarbage(5);
            StartTwoChain(match.Player);
            Run(match);
            Assert.That(match.Cpu.PendingGarbage, Is.EqualTo(0));
            Assert.That(CountAsh(match.Player.Board), Is.EqualTo(3), "相殺の残りが着弾する");
        }

        [Test]
        public void 同じTickの攻撃は処理順に関係なく両者へ届く()
        {
            var board = TwoChainBoard();
            var match = new VersusMatch(InstantConfig(), new FixedPairs(EmberPair, TidePair), 1, board, board);
            StartTwoChain(match.Player);
            StartTwoChain(match.Cpu);
            Run(match);
            Assert.That(match.Player.Score, Is.EqualTo(match.Cpu.Score));
            Assert.That(match.Player.Board, Is.EqualTo(match.Cpu.Board));
            Assert.That(match.Player.PendingGarbage, Is.EqualTo(2));
            Assert.That(match.Cpu.PendingGarbage, Is.EqualTo(2));
        }

        [Test]
        public void 同じシードと入力なら同じ盤面になる()
        {
            Assert.That(Replay(77), Is.EqualTo(Replay(77)));
            Assert.That(Replay(77), Is.Not.EqualTo(Replay(78)));
        }

        [Test]
        public void 最終盤面のハッシュは実行環境によらず一致する()
        {
            Assert.That(Replay(77), Is.EqualTo(196083508u));
        }

        private static uint Replay(int seed)
        {
            var match = new VersusMatch(new SporeConfig(), seed);
            var random = new SeededRandom(seed);
            for (var i = 0; i < 6000 && !match.IsOver; i++)
            {
                if (i % 7 == 0) { match.Player.Apply((GameCommand)random.Next(5)); }
                if (i % 11 == 0) { match.Cpu.Apply((GameCommand)random.Next(5)); }
                match.Tick();
            }

            return unchecked(match.Player.Board.Fingerprint() ^ (match.Cpu.Board.Fingerprint() * 31u));
        }
    }
}
