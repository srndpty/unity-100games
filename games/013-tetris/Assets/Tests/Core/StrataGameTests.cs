using System;
using NUnit.Framework;
using static Game.Core.Tests.StrataTestKit;

namespace Game.Core.Tests
{
    public sealed class StrataGameTests
    {
        private static readonly int[] _bottomRow = { 0 };
        private static readonly int[] _bottomFourRows = { 0, 1, 2, 3 };
        private static readonly GameCommand[] _commands =
        {
            GameCommand.MoveLeft,
            GameCommand.MoveRight,
            GameCommand.RotateClockwise,
            GameCommand.RotateCounterClockwise,
            GameCommand.SoftDropOn,
            GameCommand.SoftDropOff,
            GameCommand.HardDrop,
            GameCommand.Hold,
        };

        private static ActivePiece ActiveOf(StrataGame game) => game.Active!.Value;

        private static void MoveTimes(StrataGame game, GameCommand command, int count)
        {
            for (var i = 0; i < count; i++) { Assert.That(game.Apply(command), Is.True, $"{i} 回目"); }
        }

        // 出現と開始

        [Test]
        public void PieceAppearsOneRowBelowItsSpawnPosition()
        {
            var piece = ActiveOf(NewGame(PieceKind.T));
            Assert.That((piece.X, piece.Y, piece.Rotation), Is.EqualTo((3, 18, Rotation.North)));
        }

        [Test]
        public void PieceStaysOnTheSpawnRowWhenTheRowBelowIsBlocked()
        {
            var board = Board.Empty.With(3, 19, PieceKind.J);
            Assert.That(ActiveOf(NewGame(board, PieceKind.T)).Y, Is.EqualTo(19));
        }

        // 移動

        [Test]
        public void MovingIntoTheWallIsRejectedWithoutChangingTheState()
        {
            var game = NewGame(PieceKind.O);
            MoveTimes(game, GameCommand.MoveLeft, 4);
            var before = ActiveOf(game);
            var revision = game.Revision;

            Assert.That(game.Apply(GameCommand.MoveLeft), Is.False);
            Assert.That(ActiveOf(game), Is.EqualTo(before));
            Assert.That(game.Revision, Is.EqualTo(revision));
        }

        [Test]
        public void MovingIntoALockedBlockIsRejected()
        {
            var board = Board.Empty.With(6, 19, PieceKind.J);
            var game = NewGame(board, PieceKind.O);
            Assert.That(game.Apply(GameCommand.MoveRight), Is.False);
        }

        [Test]
        public void UndefinedCommandIsIgnored()
        {
            var game = NewGame(PieceKind.T);
            var before = ActiveOf(game);
            Assert.That(game.Apply((GameCommand)99), Is.False);
            Assert.That(ActiveOf(game), Is.EqualTo(before));
        }

        // 回転とキック

        [Test]
        public void TurnInOpenSpaceUsesNoOffset()
        {
            var game = NewGame(PieceKind.T);
            Assert.That(game.Apply(GameCommand.RotateClockwise), Is.True);
            var piece = ActiveOf(game);
            Assert.That((piece.X, piece.Y, piece.Rotation), Is.EqualTo((3, 18, Rotation.East)));
        }

        [Test]
        public void TurnAgainstTheLeftWallKicksRight()
        {
            var game = NewGame(PieceKind.T);
            game.Apply(GameCommand.RotateClockwise);
            MoveTimes(game, GameCommand.MoveLeft, 4);
            Assert.That(game.Apply(GameCommand.MoveLeft), Is.False);

            Assert.That(game.Apply(GameCommand.RotateClockwise), Is.True);

            var piece = ActiveOf(game);
            Assert.That((piece.X, piece.Y, piece.Rotation), Is.EqualTo((0, 18, Rotation.South)));
        }

        [Test]
        public void LongPieceAgainstTheRightWallKicksLeft()
        {
            var game = NewGame(PieceKind.I);
            game.Apply(GameCommand.RotateClockwise);
            MoveTimes(game, GameCommand.MoveRight, 4);
            Assert.That(game.Apply(GameCommand.MoveRight), Is.False);

            Assert.That(game.Apply(GameCommand.RotateCounterClockwise), Is.True);

            var piece = ActiveOf(game);
            Assert.That((piece.X, piece.Y, piece.Rotation), Is.EqualTo((6, 17, Rotation.North)));
        }

        [Test]
        public void LongPieceOnTheFloorKicksUp()
        {
            var game = NewGame(PieceKind.I);
            Land(game);
            Assert.That(ActiveOf(game).Y, Is.EqualTo(-2));

            Assert.That(game.Apply(GameCommand.RotateClockwise), Is.True);

            var piece = ActiveOf(game);
            Assert.That((piece.X, piece.Y, piece.Rotation), Is.EqualTo((4, 0, Rotation.East)));
        }

        [Test]
        public void TurnBlockedByALockedBlockTakesTheNextCandidate()
        {
            var board = Board.Empty.With(4, 18, PieceKind.J);
            var game = NewGame(board, PieceKind.T);

            Assert.That(game.Apply(GameCommand.RotateClockwise), Is.True);

            var piece = ActiveOf(game);
            Assert.That((piece.X, piece.Y, piece.Rotation), Is.EqualTo((2, 18, Rotation.East)));
        }

        [Test]
        public void TurnIsRejectedWhenEveryCandidateIsBlocked()
        {
            var board = Board.Empty;
            for (var y = 15; y < Board.Height; y++)
            {
                for (var x = 0; x < Board.Width; x++)
                {
                    var isSpawnCell = (x == 4 && y == 21) || (y == 20 && x >= 3 && x <= 5);
                    if (!isSpawnCell) { board = board.With(x, y, PieceKind.J); }
                }
            }

            var game = NewGame(board, PieceKind.T);
            var before = ActiveOf(game);
            var revision = game.Revision;

            Assert.That(game.Apply(GameCommand.RotateClockwise), Is.False);
            Assert.That(game.Apply(GameCommand.RotateCounterClockwise), Is.False);
            Assert.That(ActiveOf(game), Is.EqualTo(before));
            Assert.That(game.Revision, Is.EqualTo(revision));
        }

        [Test]
        public void SquarePieceDoesNotTurn()
        {
            var game = NewGame(PieceKind.O);
            Assert.That(game.Apply(GameCommand.RotateClockwise), Is.False);
            Assert.That(ActiveOf(game).Rotation, Is.EqualTo(Rotation.North));
        }

        // 落下

        [Test]
        public void GravityMovesThePieceAfterTheConfiguredTicks()
        {
            var config = Config();
            config.GravityTicks = new[] { 3 };
            var game = new StrataGame(config, new ScriptedPieces(PieceKind.T), Board.Empty);

            Ticks(game, 2);
            Assert.That(ActiveOf(game).Y, Is.EqualTo(18));
            game.Tick();
            Assert.That(ActiveOf(game).Y, Is.EqualTo(17));
        }

        [Test]
        public void SoftDropScoresOnePointPerRow()
        {
            var game = NewGame(PieceKind.T);
            game.Apply(GameCommand.SoftDropOn);
            Ticks(game, 5);
            Assert.That(ActiveOf(game).Y, Is.EqualTo(13));
            Assert.That(game.Score, Is.EqualTo(5));
        }

        [Test]
        public void RepeatedSoftDropSwitchIsRejected()
        {
            var game = NewGame(PieceKind.T);
            Assert.That(game.Apply(GameCommand.SoftDropOff), Is.False);
            Assert.That(game.Apply(GameCommand.SoftDropOn), Is.True);
            Assert.That(game.Apply(GameCommand.SoftDropOn), Is.False);
            Assert.That(game.Apply(GameCommand.SoftDropOff), Is.True);
        }

        [Test]
        public void SoftDropDoesNotLockAGroundedPieceEarly()
        {
            var game = NewGame(PieceKind.O, PieceKind.T);
            game.Apply(GameCommand.SoftDropOn);
            Ticks(game, 19);
            Assert.That(ActiveOf(game).Y, Is.EqualTo(0));

            Ticks(game, 29);
            Assert.That(ActiveOf(game).Kind, Is.EqualTo(PieceKind.O));
        }

        [Test]
        public void HardDropLocksImmediatelyAndScoresTwoPointsPerRow()
        {
            var game = NewGame(PieceKind.O, PieceKind.T);
            Assert.That(game.Apply(GameCommand.HardDrop), Is.True);
            Assert.That(game.Board.At(4, 0), Is.EqualTo(PieceKind.O));
            Assert.That(game.Score, Is.EqualTo(38));
            Assert.That(ActiveOf(game).Kind, Is.EqualTo(PieceKind.T));
        }

        [Test]
        public void GhostShowsWhereAHardDropLands()
        {
            var board = Board.Empty.With(4, 5, PieceKind.J);
            var game = NewGame(board, PieceKind.O);
            Assert.That(game.Ghost!.Value.Y, Is.EqualTo(6));
            Assert.That(game.Ghost!.Value.X, Is.EqualTo(ActiveOf(game).X));
        }

        // 接地猶予

        [Test]
        public void GroundedPieceLocksExactlyWhenTheDelayEnds()
        {
            var game = NewGame(PieceKind.O, PieceKind.T);
            Land(game);

            Ticks(game, 29);
            Assert.That(ActiveOf(game).Kind, Is.EqualTo(PieceKind.O));
            game.Tick();
            Assert.That(ActiveOf(game).Kind, Is.EqualTo(PieceKind.T));
            Assert.That(game.Board.At(4, 0), Is.EqualTo(PieceKind.O));
        }

        [Test]
        public void MoveOnTheGroundRestartsTheDelay()
        {
            var game = NewGame(PieceKind.O, PieceKind.T);
            Land(game);
            Ticks(game, 29);

            game.Apply(GameCommand.MoveLeft);

            Assert.That(game.LockTimer, Is.EqualTo(0));
            Assert.That(game.LockResets, Is.EqualTo(1));
            Ticks(game, 29);
            Assert.That(ActiveOf(game).Kind, Is.EqualTo(PieceKind.O));
        }

        [Test]
        public void DelayStopsRestartingAfterTheResetLimit()
        {
            var game = NewGame(PieceKind.O, PieceKind.T);
            Land(game);
            for (var i = 0; i < 15; i++)
            {
                game.Tick();
                game.Apply(i % 2 == 0 ? GameCommand.MoveLeft : GameCommand.MoveRight);
            }

            Assert.That(game.LockResets, Is.EqualTo(15));
            Assert.That(game.LockTimer, Is.EqualTo(0));

            Ticks(game, 10);
            Assert.That(game.Apply(GameCommand.MoveLeft), Is.True);
            Assert.That(game.LockResets, Is.EqualTo(15));
            Assert.That(game.LockTimer, Is.EqualTo(10));

            Ticks(game, 19);
            Assert.That(ActiveOf(game).Kind, Is.EqualTo(PieceKind.O));
            game.Tick();
            Assert.That(ActiveOf(game).Kind, Is.EqualTo(PieceKind.T));
        }

        [Test]
        public void ReachingALowerRowRestoresTheResets()
        {
            var board = Board.Empty.With(4, 0, PieceKind.J).With(5, 0, PieceKind.J);
            var game = NewGame(board, PieceKind.O, PieceKind.T);
            Land(game);
            Assert.That(ActiveOf(game).Y, Is.EqualTo(1));
            game.Tick();
            game.Apply(GameCommand.MoveLeft);
            game.Tick();
            game.Apply(GameCommand.MoveLeft);
            Assert.That(game.LockResets, Is.EqualTo(2));

            game.Apply(GameCommand.SoftDropOn);
            game.Tick();

            Assert.That(ActiveOf(game).Y, Is.EqualTo(0));
            Assert.That(game.LockResets, Is.EqualTo(0));
        }

        // ライン消去

        [Test]
        public void CompletedRowIsShownThenRemovedAfterTheClearDelay()
        {
            var board = FillRow(Board.Empty, 0, 3, 4, 5, 6).With(0, 1, PieceKind.S);
            var game = NewGame(board, PieceKind.I, PieceKind.T);

            game.Apply(GameCommand.HardDrop);

            Assert.That(game.Phase, Is.EqualTo(GamePhase.Clearing));
            Assert.That(game.ClearingRows, Is.EqualTo(_bottomRow));
            Assert.That(game.Active, Is.Null);
            Assert.That(game.Lines, Is.EqualTo(1));
            Assert.That(game.Score, Is.EqualTo(38 + 100));

            Ticks(game, 17);
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Clearing));
            Assert.That(game.Board.At(3, 0), Is.EqualTo(PieceKind.I));

            game.Tick();
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Falling));
            Assert.That(game.ClearingRows, Is.Empty);
            Assert.That(game.Board.At(0, 0), Is.EqualTo(PieceKind.S));
            Assert.That(game.Board.At(0, 1), Is.Null);
            Assert.That(ActiveOf(game).Kind, Is.EqualTo(PieceKind.T));
        }

        [Test]
        public void FourRowsAtOnceScoreTheHighestBonus()
        {
            var board = Board.Empty;
            for (var y = 0; y < 4; y++) { board = FillRow(board, y, 9); }
            var game = NewGame(board, PieceKind.I, PieceKind.T);
            game.Apply(GameCommand.RotateClockwise);
            MoveTimes(game, GameCommand.MoveRight, 4);

            game.Apply(GameCommand.HardDrop);

            Assert.That(game.ClearingRows, Is.EqualTo(_bottomFourRows));
            Assert.That(game.LastClearCount, Is.EqualTo(4));
            Assert.That(game.Score, Is.EqualTo(34 + 800));
        }

        [Test]
        public void ScoreUsesTheLevelBeforeTheClearAndLevelRisesAfterwards()
        {
            var config = Config();
            config.GravityTicks = new[] { 600, 500 };
            config.LinesPerLevel = 1;
            var board = FillRow(Board.Empty, 0, 3, 4, 5, 6);
            var game = new StrataGame(config, new ScriptedPieces(PieceKind.I, PieceKind.T), board);

            game.Apply(GameCommand.HardDrop);

            Assert.That(game.Score, Is.EqualTo(38 + 100));
            Assert.That(game.Level, Is.EqualTo(2));
            Assert.That(game.TakeEvents() & GameEvents.LevelUp, Is.EqualTo(GameEvents.LevelUp));
        }

        [Test]
        public void LevelStopsAtTheEndOfTheGravityTable()
        {
            var config = Config();
            config.LinesPerLevel = 1;
            var board = FillRow(Board.Empty, 0, 3, 4, 5, 6);
            var game = new StrataGame(config, new ScriptedPieces(PieceKind.I, PieceKind.T), board);

            game.Apply(GameCommand.HardDrop);

            Assert.That(game.Level, Is.EqualTo(1));
            Assert.That(game.TakeEvents() & GameEvents.LevelUp, Is.EqualTo(GameEvents.None));
        }

        [Test]
        public void ZeroClearDelayRemovesTheRowImmediately()
        {
            var config = Config();
            config.LineClearTicks = 0;
            var board = FillRow(Board.Empty, 0, 3, 4, 5, 6);
            var game = new StrataGame(config, new ScriptedPieces(PieceKind.I, PieceKind.T), board);

            game.Apply(GameCommand.HardDrop);

            Assert.That(game.Phase, Is.EqualTo(GamePhase.Falling));
            Assert.That(game.Board, Is.EqualTo(Board.Empty));
        }

        [Test]
        public void PieceCommandsAreRejectedWhileClearing()
        {
            var board = FillRow(Board.Empty, 0, 3, 4, 5, 6);
            var game = NewGame(board, PieceKind.I, PieceKind.T);
            game.Apply(GameCommand.HardDrop);

            Assert.That(game.Apply(GameCommand.MoveLeft), Is.False);
            Assert.That(game.Apply(GameCommand.MoveRight), Is.False);
            Assert.That(game.Apply(GameCommand.RotateClockwise), Is.False);
            Assert.That(game.Apply(GameCommand.RotateCounterClockwise), Is.False);
            Assert.That(game.Apply(GameCommand.HardDrop), Is.False);
            Assert.That(game.Apply(GameCommand.Hold), Is.False);
        }

        [Test]
        public void SoftDropKeyHeldDuringTheClearAppliesToTheNextPiece()
        {
            var board = FillRow(Board.Empty, 0, 3, 4, 5, 6);
            var game = NewGame(board, PieceKind.I, PieceKind.T);
            game.Apply(GameCommand.HardDrop);

            Assert.That(game.Apply(GameCommand.SoftDropOn), Is.True);
            Ticks(game, 18);
            var startY = ActiveOf(game).Y;
            game.Tick();

            Assert.That(ActiveOf(game).Y, Is.EqualTo(startY - 1));
        }

        // ホールド

        [Test]
        public void FirstHoldStoresThePieceAndBringsTheNextOne()
        {
            var game = NewGame(PieceKind.T, PieceKind.I, PieceKind.O);
            Assert.That(game.Apply(GameCommand.Hold), Is.True);
            Assert.That(game.HoldKind, Is.EqualTo(PieceKind.T));
            Assert.That(ActiveOf(game).Kind, Is.EqualTo(PieceKind.I));
            Assert.That(game.CanHold, Is.False);
        }

        [Test]
        public void HoldWorksOnlyOncePerPiece()
        {
            var game = NewGame(PieceKind.T, PieceKind.I, PieceKind.O);
            game.Apply(GameCommand.Hold);
            Assert.That(game.Apply(GameCommand.Hold), Is.False);
            Assert.That(game.HoldKind, Is.EqualTo(PieceKind.T));
            Assert.That(ActiveOf(game).Kind, Is.EqualTo(PieceKind.I));
        }

        [Test]
        public void HoldBecomesAvailableAgainAfterALockAndSwapsFromTheSpawnPosition()
        {
            var game = NewGame(PieceKind.T, PieceKind.I, PieceKind.O, PieceKind.S);
            game.Apply(GameCommand.RotateClockwise);
            game.Apply(GameCommand.Hold);
            game.Apply(GameCommand.HardDrop);
            Assert.That(game.CanHold, Is.True);
            Assert.That(ActiveOf(game).Kind, Is.EqualTo(PieceKind.O));

            game.Apply(GameCommand.Hold);

            var piece = ActiveOf(game);
            Assert.That(game.HoldKind, Is.EqualTo(PieceKind.O));
            Assert.That((piece.Kind, piece.Rotation, piece.X, piece.Y), Is.EqualTo((PieceKind.T, Rotation.North, 3, 18)));
        }

        [Test]
        public void HoldingIntoABlockedSpawnEndsTheGame()
        {
            var board = Board.Empty.With(6, 20, PieceKind.J);
            var game = NewGame(board, PieceKind.T, PieceKind.I);

            Assert.That(game.Apply(GameCommand.Hold), Is.True);

            Assert.That(game.Phase, Is.EqualTo(GamePhase.GameOver));
            Assert.That(game.OverReason, Is.EqualTo(GameOverReason.BlockOut));
        }

        // 次のピース

        [Test]
        public void PreviewShowsThePiecesInTheOrderTheyAppear()
        {
            var game = new StrataGame(Config(), 123);
            var expected = new PieceKind[StrataGame.PreviewCount];
            for (var i = 0; i < expected.Length; i++) { expected[i] = game.Preview(i); }

            foreach (var kind in expected)
            {
                game.Apply(GameCommand.HardDrop);
                Assert.That(ActiveOf(game).Kind, Is.EqualTo(kind));
            }
        }

        // 敗北と終了後の操作

        [Test]
        public void BlockedSpawnEndsTheGame()
        {
            var game = NewGame(Board.Empty.With(4, 20, PieceKind.J), PieceKind.T);
            Assert.That(game.Phase, Is.EqualTo(GamePhase.GameOver));
            Assert.That(game.OverReason, Is.EqualTo(GameOverReason.BlockOut));
            Assert.That(game.Active, Is.Null);
            Assert.That(game.Ghost, Is.Null);
        }

        [Test]
        public void LockingEntirelyAboveTheVisibleAreaEndsTheGame()
        {
            var board = Board.Empty.With(3, 19, PieceKind.J).With(4, 19, PieceKind.J).With(5, 19, PieceKind.J);
            var game = NewGame(board, PieceKind.T, PieceKind.O);

            game.Apply(GameCommand.HardDrop);

            Assert.That(game.Phase, Is.EqualTo(GamePhase.GameOver));
            Assert.That(game.OverReason, Is.EqualTo(GameOverReason.LockOut));
        }

        [Test]
        public void LockingPartlyInsideTheVisibleAreaContinuesTheGame()
        {
            var board = Board.Empty.With(0, 18, PieceKind.J).With(1, 18, PieceKind.J).With(2, 18, PieceKind.J);
            var game = NewGame(board, PieceKind.T, PieceKind.T);
            MoveTimes(game, GameCommand.MoveLeft, 3);

            game.Apply(GameCommand.HardDrop);

            Assert.That(game.Phase, Is.EqualTo(GamePhase.Falling));
            Assert.That(game.Board.At(1, 20), Is.EqualTo(PieceKind.T));
        }

        [Test]
        public void StackingToTheTopEventuallyEndsTheGame()
        {
            var game = new StrataGame(Config(), 5);
            var drops = 0;
            while (game.Phase != GamePhase.GameOver && drops < 200)
            {
                game.Apply(GameCommand.HardDrop);
                drops++;
            }

            Assert.That(game.Phase, Is.EqualTo(GamePhase.GameOver));
        }

        [Test]
        public void NothingChangesAfterTheGameIsOver()
        {
            var game = NewGame(Board.Empty.With(4, 20, PieceKind.J), PieceKind.T);
            var board = game.Board;
            var revision = game.Revision;
            var ticks = game.TickCount;

            foreach (var command in _commands)
            {
                Assert.That(game.Apply(command), Is.False, command.ToString());
            }

            game.Tick();
            Assert.That(game.Board, Is.SameAs(board));
            Assert.That(game.Revision, Is.EqualTo(revision));
            Assert.That(game.TickCount, Is.EqualTo(ticks));
        }

        [Test]
        public void ConstructorRejectsMissingArguments()
        {
            Assert.Throws<ArgumentNullException>(() => new StrataGame(null!, 1));
            Assert.Throws<ArgumentNullException>(() => new StrataGame(Config(), null!, Board.Empty));
            Assert.Throws<ArgumentNullException>(() => new StrataGame(Config(), new ScriptedPieces(PieceKind.T), null!));
        }

        // 再現性

        [Test]
        public void SameSeedAndInputsGiveTheSameFinalBoard()
        {
            var first = PlayScript(2024);
            var second = PlayScript(2024);
            Assert.That(second.Board, Is.EqualTo(first.Board));
            Assert.That(second.Score, Is.EqualTo(first.Score));
            Assert.That(second.TickCount, Is.EqualTo(first.TickCount));
        }

        [Test]
        public void DifferentSeedsGiveDifferentBoards()
        {
            Assert.That(PlayScript(1).Board, Is.Not.EqualTo(PlayScript(2).Board));
        }

        [Test]
        public void ScriptedGameMatchesTheRecordedResultOnEveryRuntime()
        {
            var game = PlayScript(2024);
            Assert.That(game.Board.Fingerprint(), Is.EqualTo(3139383851u));
            Assert.That(game.Score, Is.EqualTo(335L));
            Assert.That(game.TickCount, Is.EqualTo(782));
        }

        // シードから作った入力列で遊ぶ。ゲーム本体とは別の乱数を使う。
        private static StrataGame PlayScript(int seed)
        {
            var game = new StrataGame(new StrataConfig(), seed);
            var inputs = new SeededRandom(seed ^ 0x5A5A5A);
            for (var step = 0; step < 6000 && game.Phase != GamePhase.GameOver; step++)
            {
                if (inputs.Next(4) == 0) { game.Apply(_commands[inputs.Next(_commands.Length)]); }
                game.Tick();
            }

            return game;
        }
    }
}
