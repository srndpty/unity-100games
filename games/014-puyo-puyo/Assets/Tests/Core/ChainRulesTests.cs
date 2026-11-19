using NUnit.Framework;
using static Game.Core.Tests.SporeTestKit;

namespace Game.Core.Tests
{
    public sealed class ChainRulesTests
    {
        [Test]
        public void 同色3個では消えない()
        {
            Assert.That(GroupFinder.Find(Parse("EEE...")), Is.Empty);
        }

        [Test]
        public void 同色4個で消える()
        {
            var groups = GroupFinder.Find(Parse("EEEE.."));
            Assert.That(groups, Has.Count.EqualTo(1));
            Assert.That(groups[0].Cells, Has.Count.EqualTo(4));
            Assert.That(groups[0].Color, Is.EqualTo(Cell.Ember));
        }

        [TestCase("E.....|E.....|EE....")]
        [TestCase(".E....|EEE...")]
        [TestCase("EE....|EE....")]
        public void L字やT字や四角も連結する(string rows)
        {
            Assert.That(GroupFinder.Find(Parse(rows.Split('|'))), Has.Count.EqualTo(1));
        }

        [Test]
        public void 斜めは連結しない()
        {
            Assert.That(GroupFinder.Find(Parse("E.E...", ".E.E..")), Is.Empty);
        }

        [Test]
        public void 別の色は連結しない()
        {
            Assert.That(GroupFinder.Find(Parse("EEMM..", "MMEE..")), Is.Empty);
        }

        [Test]
        public void 枯れ胞子は並んでも消えない()
        {
            Assert.That(GroupFinder.Find(Parse("AAAAAA")), Is.Empty);
        }

        [Test]
        public void 非表示の段は連結に数えない()
        {
            var board = Board.Empty
                .With(0, Board.VisibleHeight - 1, Cell.Ember)
                .With(1, Board.VisibleHeight - 1, Cell.Ember)
                .With(2, Board.VisibleHeight - 1, Cell.Ember)
                .With(2, Board.VisibleHeight, Cell.Ember);
            Assert.That(GroupFinder.Find(board), Is.Empty);
        }

        [Test]
        public void 隣の枯れ胞子だけが一緒に消える()
        {
            var board = Parse("A.....", "AEEEEA", "..A...");
            var popped = ChainResolver.Pop(board, GroupFinder.Find(board));
            Assert.That(popped.At(0, 1), Is.EqualTo(Cell.Empty), "左隣");
            Assert.That(popped.At(5, 1), Is.EqualTo(Cell.Empty), "右隣");
            Assert.That(popped.At(2, 0), Is.EqualTo(Cell.Empty), "下隣");
            Assert.That(popped.At(0, 2), Is.EqualTo(Cell.Ash), "斜めは残る");
        }

        [Test]
        public void 浮いた胞子は列ごとに落ちる()
        {
            var result = BoardGravity.Settle(Parse("E.....", "......", "M....T", "......"));
            Assert.That(result.Board, Is.EqualTo(Parse("E.....", "M....T")));
            Assert.That(result.MaxDistance, Is.EqualTo(2));
            Assert.That(result.Moves, Has.Count.EqualTo(3));
        }

        [Test]
        public void 空の盤面は落下しても変わらない()
        {
            var result = BoardGravity.Settle(Board.Empty);
            Assert.That(result.Board, Is.EqualTo(Board.Empty));
            Assert.That(result.Moves, Is.Empty);
        }

        [Test]
        public void 満杯の列は落下しても崩れない()
        {
            var board = Board.Empty;
            for (var y = 0; y < Board.Height; y++) { board = board.With(3, y, y % 2 == 0 ? Cell.Ember : Cell.Moss); }
            var result = BoardGravity.Settle(board);
            Assert.That(result.Board, Is.EqualTo(board));
            Assert.That(result.Moves, Is.Empty);
        }

        [Test]
        public void 四個消去の後に落ちてできた連結を2連鎖として検出する()
        {
            var board = Parse(
                "M.....",
                "M.....",
                "M.....",
                "E.....",
                "E.....",
                "E.....",
                "EM....");
            var outcome = ChainResolver.ResolveAll(new SporeConfig(), board);
            Assert.That(outcome.Chains, Is.EqualTo(2));
            Assert.That(outcome.Popped, Is.EqualTo(8));
            Assert.That(outcome.Score, Is.EqualTo(40 + 160));
            Assert.That(outcome.Board, Is.EqualTo(Board.Empty));
        }

        [Test]
        public void 三連鎖を検出する()
        {
            var board = Parse(
                "T.....",
                "T.....",
                "T.....",
                "M.....",
                "M.....",
                "M.....",
                "E.....",
                "E.....",
                "ET....",
                "EM....");
            var outcome = ChainResolver.ResolveAll(new SporeConfig(), board);
            Assert.That(outcome.Chains, Is.EqualTo(3));
            Assert.That(outcome.Popped, Is.EqualTo(12));
            Assert.That(outcome.Score, Is.EqualTo(40 + 160 + 320));
            Assert.That(outcome.Board, Is.EqualTo(Board.Empty));
        }

        [Test]
        public void 消えるものがなければ連鎖しない()
        {
            var board = Parse("EEM...");
            var outcome = ChainResolver.ResolveAll(new SporeConfig(), board);
            Assert.That(outcome.Chains, Is.EqualTo(0));
            Assert.That(outcome.Score, Is.EqualTo(0));
            Assert.That(outcome.Board, Is.EqualTo(board));
        }

        [Test]
        public void 同時に2色消すと色数ボーナスが付く()
        {
            var groups = GroupFinder.Find(Parse("EEEE..", "MMMM.."));
            Assert.That(groups, Has.Count.EqualTo(2));
            Assert.That(ScoreRules.ChainScore(new SporeConfig(), 1, groups), Is.EqualTo(8 * 10 * 3));
        }

        [TestCase(1, 40)]
        [TestCase(2, 160)]
        [TestCase(3, 320)]
        [TestCase(4, 640)]
        [TestCase(5, 1280)]
        [TestCase(99, 5120)]
        public void 連鎖数ごとの得点(int chain, long expected)
        {
            var groups = GroupFinder.Find(Parse("EEEE.."));
            Assert.That(ScoreRules.ChainScore(new SporeConfig(), chain, groups), Is.EqualTo(expected));
        }

        [Test]
        public void 大きい連結には個数のボーナスが付く()
        {
            var groups = GroupFinder.Find(Parse("EEEEEE"));
            Assert.That(ScoreRules.ChainScore(new SporeConfig(), 1, groups), Is.EqualTo(6 * 10 * 2));
        }
    }
}
