using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class MinesweeperGameTests
    {
        // 5×5 で右下の角 (4,4) だけが爆弾の盤面。
        private static MinesweeperGame CornerMine() => MinesweeperGame.FromField(MineField.FromMines(5, 5, 24));

        private static CellFace[] Faces(MinesweeperGame game)
        {
            var faces = new CellFace[game.Grid.CellCount];
            for (var cell = 0; cell < faces.Length; cell++) { faces[cell] = game.Face(game.Grid.X(cell), game.Grid.Y(cell)); }
            return faces;
        }

        [Test]
        public void NewGame_IsReadyWithoutMines()
        {
            var game = new MinesweeperGame(new BoardConfig(), 1);
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Ready));
            Assert.That(game.Field, Is.Null);
            Assert.That(game.RemainingMines, Is.EqualTo(40));
        }

        [Test]
        public void FirstReveal_PlacesMinesAndOpensProtectedArea()
        {
            var game = new MinesweeperGame(new BoardConfig(), 5);
            Assert.That(game.Reveal(8, 8), Is.EqualTo(MoveResult.Changed));
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Playing));
            Assert.That(game.Field, Is.Not.Null);
            Assert.That(game.Face(8, 8), Is.EqualTo(CellFace.Open0));
            Assert.That(game.LastOpenedCount, Is.GreaterThanOrEqualTo(9));
        }

        [Test]
        public void FirstReveal_NeverExplodes_ForAnySeedOrCell()
        {
            var config = new BoardConfig { Width = 6, Height = 6, MineCount = 27 };
            for (var seed = 0; seed < 200; seed++)
            {
                var game = new MinesweeperGame(config, seed);
                Assert.That(game.Reveal(seed % 6, (seed / 6) % 6), Is.Not.EqualTo(MoveResult.Exploded));
            }
        }

        [Test]
        public void FirstReveal_AtMaximumDensity_WinsImmediately()
        {
            var game = new MinesweeperGame(new BoardConfig { Width = 5, Height = 5, MineCount = 16 }, 3);
            Assert.That(game.Reveal(2, 2), Is.EqualTo(MoveResult.Won));
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Won));
        }

        [Test]
        public void Cascade_OpensConnectedSafeArea()
        {
            var game = CornerMine();
            Assert.That(game.Reveal(0, 0), Is.EqualTo(MoveResult.Won));
            Assert.That(game.LastOpenedCount, Is.EqualTo(24));
            Assert.That(game.Face(3, 3), Is.EqualTo(CellFace.Open1));
        }

        [Test]
        public void Cascade_StopsAtNumberedCells()
        {
            // 中央の縦列が爆弾なので、左側だけが開く。
            var game = MinesweeperGame.FromField(MineField.FromMines(5, 5, 2, 7, 12, 17, 22));
            game.Reveal(0, 0);
            Assert.That(game.Face(1, 2), Is.EqualTo(CellFace.Open3));
            Assert.That(game.Face(3, 2), Is.EqualTo(CellFace.Hidden));
            Assert.That(game.LastOpenedCount, Is.EqualTo(10));
        }

        [Test]
        public void Cascade_DoesNotOpenFlaggedCell()
        {
            var game = CornerMine();
            game.ToggleFlag(2, 2);
            Assert.That(game.Reveal(0, 0), Is.EqualTo(MoveResult.Changed));
            Assert.That(game.Face(2, 2), Is.EqualTo(CellFace.Flagged));
            Assert.That(game.SafeCellsLeft, Is.EqualTo(1));
        }

        [Test]
        public void Cascade_DoesNotPassThroughFlagWall()
        {
            // x=2 の列に旗を立てると、右側へは展開しない。
            var game = MinesweeperGame.FromField(MineField.FromMines(6, 4, 23));
            for (var y = 0; y < 4; y++) { game.ToggleFlag(2, y); }
            game.Reveal(0, 0);
            Assert.That(game.Face(1, 1), Is.EqualTo(CellFace.Open0));
            Assert.That(game.Face(3, 1), Is.EqualTo(CellFace.Hidden));
        }

        [Test]
        public void Cascade_ContinuesAfterFlagRemoved()
        {
            var game = MinesweeperGame.FromField(MineField.FromMines(6, 4, 23));
            for (var y = 0; y < 4; y++) { game.ToggleFlag(2, y); }
            game.Reveal(0, 0);
            game.ToggleFlag(2, 1);
            game.Reveal(2, 1);
            Assert.That(game.Face(3, 1), Is.EqualTo(CellFace.Open0));
            Assert.That(game.Face(2, 0), Is.EqualTo(CellFace.Flagged));
        }

        [Test]
        public void Cascade_LargestBoardWithOneMine_Completes()
        {
            var game = MinesweeperGame.FromField(MineField.FromMines(32, 32, 1023));
            Assert.That(game.Reveal(0, 0), Is.EqualTo(MoveResult.Won));
            Assert.That(game.LastOpenedCount, Is.EqualTo(1023));
        }

        [Test]
        public void Win_DoesNotRequireFlags_AndShowsRemainingMinesAsFlags()
        {
            var game = CornerMine();
            game.Reveal(0, 0);
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Won));
            Assert.That(game.FlagCount, Is.Zero);
            Assert.That(game.Face(4, 4), Is.EqualTo(CellFace.Flagged));
            Assert.That(game.RemainingMines, Is.Zero);
        }

        [Test]
        public void Win_RequiresEverySafeCell()
        {
            var game = MinesweeperGame.FromField(MineField.FromMines(4, 4, 0, 2));
            Assert.That(game.Reveal(1, 0), Is.EqualTo(MoveResult.Changed));
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Playing));
        }

        [Test]
        public void Lose_ShowsExplodedMinesAndWrongFlags()
        {
            var game = MinesweeperGame.FromField(MineField.FromMines(4, 4, 0, 3, 15));
            game.ToggleFlag(3, 0);
            game.ToggleFlag(1, 1);
            Assert.That(game.Reveal(0, 0), Is.EqualTo(MoveResult.Exploded));
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Lost));
            Assert.That(game.Face(0, 0), Is.EqualTo(CellFace.Exploded));
            Assert.That(game.Face(3, 3), Is.EqualTo(CellFace.Mine));
            Assert.That(game.Face(3, 0), Is.EqualTo(CellFace.Flagged));
            Assert.That(game.Face(1, 1), Is.EqualTo(CellFace.WrongFlag));
            Assert.That(game.Face(2, 2), Is.EqualTo(CellFace.Hidden));
        }

        [Test]
        public void Faces_DuringPlay_DoNotRevealMines()
        {
            var game = MinesweeperGame.FromField(MineField.FromMines(4, 4, 0, 15));
            game.Reveal(1, 0);
            foreach (var face in Faces(game))
            {
                Assert.That(face, Is.Not.EqualTo(CellFace.Mine).And.Not.EqualTo(CellFace.Exploded).And.Not.EqualTo(CellFace.WrongFlag));
            }

            Assert.That(game.Face(0, 0), Is.EqualTo(game.Face(3, 3)), "爆弾のセルと未公開の安全なセルは同じ面に見える");
            Assert.That(game.Face(0, 0), Is.EqualTo(CellFace.Hidden));
        }

        [Test]
        public void Flag_BeforeFirstReveal_IsAllowedAndBlocksThatCell()
        {
            var game = new MinesweeperGame(new BoardConfig(), 1);
            Assert.That(game.ToggleFlag(4, 4), Is.EqualTo(MoveResult.Changed));
            Assert.That(game.Reveal(4, 4), Is.EqualTo(MoveResult.Rejected));
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Ready));
            Assert.That(game.Field, Is.Null);
        }

        [Test]
        public void Flag_TogglesAndAllowsNegativeRemaining()
        {
            var game = MinesweeperGame.FromField(MineField.FromMines(4, 4, 15));
            game.ToggleFlag(0, 0);
            game.ToggleFlag(1, 0);
            Assert.That(game.RemainingMines, Is.EqualTo(-1));
            game.ToggleFlag(1, 0);
            Assert.That(game.FlagCount, Is.EqualTo(1));
            Assert.That(game.Face(1, 0), Is.EqualTo(CellFace.Hidden));
        }

        [TestCase(-1, 0)]
        [TestCase(0, -1)]
        [TestCase(5, 0)]
        [TestCase(0, 5)]
        public void Moves_OutsideBoard_AreRejected(int x, int y)
        {
            var game = CornerMine();
            var before = Faces(game);
            Assert.That(game.Reveal(x, y), Is.EqualTo(MoveResult.Rejected));
            Assert.That(game.ToggleFlag(x, y), Is.EqualTo(MoveResult.Rejected));
            Assert.That(Faces(game), Is.EqualTo(before));
        }

        [Test]
        public void Moves_OnOpenCell_AreRejected()
        {
            var game = MinesweeperGame.FromField(MineField.FromMines(4, 4, 0));
            game.Reveal(1, 0);
            var left = game.SafeCellsLeft;
            Assert.That(game.Reveal(1, 0), Is.EqualTo(MoveResult.Rejected), "同じセルの重複操作");
            Assert.That(game.ToggleFlag(1, 0), Is.EqualTo(MoveResult.Rejected));
            Assert.That(game.SafeCellsLeft, Is.EqualTo(left));
            Assert.That(game.FlagCount, Is.Zero);
        }

        [Test]
        public void Moves_AfterGameOver_AreRejected()
        {
            var game = MinesweeperGame.FromField(MineField.FromMines(4, 4, 0));
            game.Reveal(0, 0);
            var before = Faces(game);
            Assert.That(game.Reveal(3, 3), Is.EqualTo(MoveResult.Rejected));
            Assert.That(game.ToggleFlag(3, 3), Is.EqualTo(MoveResult.Rejected));
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Lost));
            Assert.That(Faces(game), Is.EqualTo(before));
        }

        [Test]
        public void Moves_AfterWin_AreRejected()
        {
            var game = CornerMine();
            game.Reveal(0, 0);
            Assert.That(game.Reveal(4, 4), Is.EqualTo(MoveResult.Rejected));
            Assert.That(game.ToggleFlag(4, 4), Is.EqualTo(MoveResult.Rejected));
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Won));
        }

        [Test]
        public void SameSeedAndMoves_ProduceSameFaces()
        {
            var first = new MinesweeperGame(new BoardConfig(), 99);
            var second = new MinesweeperGame(new BoardConfig(), 99);
            first.Reveal(7, 7);
            second.Reveal(7, 7);
            Assert.That(Faces(first), Is.EqualTo(Faces(second)));
        }
    }
}
