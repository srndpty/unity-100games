using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class SokobanGameTests
    {
        private static readonly Direction[] _directions = { Direction.Up, Direction.Down, Direction.Left, Direction.Right };

        private static SokobanGame OpenRoom() => LevelText.Game(
            "#######",
            "#     #",
            "# @$ .#",
            "#     #",
            "#######");

        private static void AssertUnchanged(SokobanGame game, BoardState board, int moves, int pushes, int depth)
        {
            Assert.That(game.Board, Is.EqualTo(board));
            Assert.That(game.MoveCount, Is.EqualTo(moves));
            Assert.That(game.PushCount, Is.EqualTo(pushes));
            Assert.That(game.UndoDepth, Is.EqualTo(depth));
        }

        [Test]
        public void NewGame_StartsAtTheInitialBoard()
        {
            var game = OpenRoom();
            AssertUnchanged(game, game.Map.Initial, 0, 0, 0);
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Playing));
            Assert.That(game.Facing, Is.EqualTo(Direction.Down));
        }

        [Test]
        public void NewGame_RejectsNullMap() =>
            Assert.Throws<ArgumentNullException>(() => new SokobanGame(null!));

        [Test]
        public void Move_WalksOntoFreeFloor()
        {
            var game = OpenRoom();
            Assert.That(game.Move(Direction.Up), Is.EqualTo(MoveResult.Moved));
            Assert.That(game.Board.Player, Is.EqualTo(new Cell(2, 1)));
            Assert.That(game.MoveCount, Is.EqualTo(1));
            Assert.That(game.PushCount, Is.Zero);
            Assert.That(game.Facing, Is.EqualTo(Direction.Up));
        }

        [Test]
        public void Move_CannotCrossAWall()
        {
            var game = LevelText.Game(
                "######",
                "#@#$.#",
                "#    #",
                "######");
            var before = game.Board;

            Assert.That(game.Move(Direction.Right), Is.EqualTo(MoveResult.Rejected));
            Assert.That(game.Move(Direction.Up), Is.EqualTo(MoveResult.Rejected));
            Assert.That(game.Move(Direction.Left), Is.EqualTo(MoveResult.Rejected));
            AssertUnchanged(game, before, 0, 0, 0);
        }

        [Test]
        public void Move_PushesOneBox()
        {
            var game = OpenRoom();
            Assert.That(game.Move(Direction.Right), Is.EqualTo(MoveResult.Pushed));
            Assert.That(game.Board.Player, Is.EqualTo(new Cell(3, 2)));
            Assert.That(game.Board.HasBox(new Cell(4, 2)), Is.True);
            Assert.That(game.Board.HasBox(new Cell(3, 2)), Is.False);
            Assert.That(game.PushCount, Is.EqualTo(1));
        }

        [Test]
        public void Move_CannotPushTwoBoxesInARow()
        {
            var game = LevelText.Game(
                "########",
                "#@$$ ..#",
                "########");
            var before = game.Board;

            Assert.That(game.Move(Direction.Right), Is.EqualTo(MoveResult.Rejected));
            AssertUnchanged(game, before, 0, 0, 0);
        }

        [Test]
        public void Move_CannotPushABoxIntoAWall()
        {
            var game = LevelText.Game(
                "######",
                "#. @$#",
                "######");
            var before = game.Board;

            Assert.That(game.Move(Direction.Right), Is.EqualTo(MoveResult.Rejected));
            AssertUnchanged(game, before, 0, 0, 0);
        }

        [Test]
        public void Move_CanPushABoxOffAGoal()
        {
            var game = LevelText.Game(
                "#######",
                "#@*$ .#",
                "#     #",
                "#######");
            Assert.That(game.Move(Direction.Down), Is.EqualTo(MoveResult.Moved));
            LevelText.Play(game, "RRRU");
            Assert.That(game.Move(Direction.Left), Is.EqualTo(MoveResult.Rejected), "箱が2個並ぶ向きには押せない");

            var single = LevelText.Game(
                "#######",
                "#@*  .#",
                "#  $  #",
                "#     #",
                "#######");
            Assert.That(single.Move(Direction.Right), Is.EqualTo(MoveResult.Pushed));
            Assert.That(single.Board.HasBox(new Cell(2, 1)), Is.False);
            Assert.That(single.Map.IsGoal(new Cell(2, 1)), Is.True, "目標のレイヤーは箱が離れても残る");
        }

        [Test]
        public void Move_PlayerCanWalkOverAGoal()
        {
            var game = LevelText.Game(
                "#######",
                "#@. $ #",
                "#     #",
                "#######");
            Assert.That(game.Move(Direction.Right), Is.EqualTo(MoveResult.Moved));
            Assert.That(game.Board.Player, Is.EqualTo(new Cell(2, 1)));
            Assert.That(game.IsCleared, Is.False);
        }

        [Test]
        public void Move_RejectsUndefinedDirection()
        {
            var game = OpenRoom();
            Assert.That(game.Move((Direction)99), Is.EqualTo(MoveResult.Rejected));
            AssertUnchanged(game, game.Map.Initial, 0, 0, 0);
        }

        [Test]
        public void Clear_HappensWhenTheLastBoxReachesAGoal()
        {
            var game = LevelText.Game(
                "########",
                "#@$ .  #",
                "# $.   #",
                "#      #",
                "########");

            Assert.That(game.Move(Direction.Right), Is.EqualTo(MoveResult.Pushed));
            Assert.That(game.Move(Direction.Right), Is.EqualTo(MoveResult.Pushed));
            Assert.That(game.IsCleared, Is.False, "1個だけではクリアにならない");
            LevelText.Play(game, "LLD");
            Assert.That(game.Move(Direction.Right), Is.EqualTo(MoveResult.Cleared));
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Cleared));
        }

        [Test]
        public void AfterClear_MoveAndUndoAreRejected()
        {
            var game = OpenRoom();
            Assert.That(LevelText.Play(game, "RR"), Is.EqualTo(MoveResult.Cleared));
            var board = game.Board;

            Assert.That(game.Move(Direction.Left), Is.EqualTo(MoveResult.Rejected));
            Assert.That(game.Undo(), Is.False);
            AssertUnchanged(game, board, 2, 2, 2);
            Assert.That(game.IsCleared, Is.True);
        }

        [Test]
        public void Undo_WithNoHistoryIsRejected()
        {
            var game = OpenRoom();
            var revision = game.Revision;
            Assert.That(game.Undo(), Is.False);
            Assert.That(game.Revision, Is.EqualTo(revision));
        }

        [Test]
        public void Undo_RevertsAWalk()
        {
            var game = OpenRoom();
            game.Move(Direction.Up);
            Assert.That(game.Undo(), Is.True);
            AssertUnchanged(game, game.Map.Initial, 0, 0, 0);
            Assert.That(game.LastAction, Is.EqualTo(GameAction.Undo));
        }

        [Test]
        public void Undo_RestoresBoxAndGoalState()
        {
            var game = LevelText.Game(
                "########",
                "#@$ .  #",
                "# $.   #",
                "#      #",
                "########");
            LevelText.Play(game, "RR");
            Assert.That(game.Map.IsGoal(game.Board.Boxes[0]), Is.True, "箱が目標に載っている");

            Assert.That(game.Undo(), Is.True);

            Assert.That(game.Board.Boxes[0], Is.EqualTo(new Cell(3, 1)));
            Assert.That(game.Map.IsGoal(game.Board.Boxes[0]), Is.False);
            Assert.That(game.Map.IsGoal(new Cell(4, 1)), Is.True, "目標のレイヤーは変わらない");
            Assert.That(game.PushCount, Is.EqualTo(1));
            Assert.That(game.MoveCount, Is.EqualTo(1));
        }

        [Test]
        public void Undo_RepeatedlyReturnsToTheStartThenStops()
        {
            var game = OpenRoom();
            LevelText.Play(game, "UDR");
            Assert.That(game.Undo(), Is.True);
            Assert.That(game.Undo(), Is.True);
            Assert.That(game.Undo(), Is.True);
            Assert.That(game.Undo(), Is.False);
            AssertUnchanged(game, game.Map.Initial, 0, 0, 0);
        }

        [Test]
        public void Undo_ThenMoveContinuesFromTheRevertedBoard()
        {
            var game = OpenRoom();
            LevelText.Play(game, "R");
            game.Undo();
            Assert.That(game.Move(Direction.Up), Is.EqualTo(MoveResult.Moved));
            Assert.That(game.UndoDepth, Is.EqualTo(1));
            Assert.That(game.Board.HasBox(new Cell(3, 2)), Is.True);
        }

        [Test]
        public void RejectedMove_DoesNotAddHistory()
        {
            var game = LevelText.Game(
                "######",
                "#@ $.#",
                "######");
            game.Move(Direction.Up);
            game.Move(Direction.Left);
            Assert.That(game.UndoDepth, Is.Zero);
            Assert.That(game.Undo(), Is.False);
        }

        [Test]
        public void RandomPlay_UndoAllReturnsToTheInitialBoard([Range(0, LevelCount - 1)] int level)
        {
            var map = LevelCatalog.Maps[level];
            var game = new SokobanGame(map);
            var random = new TestRandom(unchecked((uint)(level + 1) * 2654435761u));
            var boards = new Stack<BoardState>();

            for (var i = 0; i < 2000 && !game.IsCleared; i++)
            {
                var before = game.Board;
                if (game.Move(_directions[random.Next(_directions.Length)]) != MoveResult.Rejected) { boards.Push(before); }
                AssertBoardInvariants(map, game.Board);
            }

            if (game.IsCleared) { game.Restart(); boards.Clear(); }
            while (boards.Count > 0)
            {
                Assert.That(game.Undo(), Is.True);
                Assert.That(game.Board, Is.EqualTo(boards.Pop()));
            }

            AssertUnchanged(game, map.Initial, 0, 0, 0);
            Assert.That(game.Undo(), Is.False);
        }

        [Test]
        public void Restart_ReturnsToTheInitialBoardAndClearsHistory()
        {
            var game = OpenRoom();
            LevelText.Play(game, "RU");

            game.Restart();

            AssertUnchanged(game, game.Map.Initial, 0, 0, 0);
            Assert.That(game.Undo(), Is.False, "やり直しは取り消せない");
            Assert.That(game.LastRecord, Is.Null);
        }

        [Test]
        public void Restart_TwiceIsTheSameAsOnce()
        {
            var game = OpenRoom();
            LevelText.Play(game, "R");
            game.Restart();
            game.Restart();
            AssertUnchanged(game, game.Map.Initial, 0, 0, 0);
        }

        [Test]
        public void Restart_AfterClearAllowsPlayingAgain()
        {
            var game = OpenRoom();
            LevelText.Play(game, "RR");

            game.Restart();

            Assert.That(game.Phase, Is.EqualTo(GamePhase.Playing));
            Assert.That(LevelText.Play(game, "RR"), Is.EqualTo(MoveResult.Cleared));
        }

        [Test]
        public void SameMoves_ReproduceTheSameBoard()
        {
            var first = new SokobanGame(LevelCatalog.Maps[7]);
            var second = new SokobanGame(LevelCatalog.Maps[7]);
            var random = new TestRandom(12345);
            for (var i = 0; i < 500; i++)
            {
                var direction = _directions[random.Next(_directions.Length)];
                Assert.That(first.Move(direction), Is.EqualTo(second.Move(direction)));
                Assert.That(first.Board, Is.EqualTo(second.Board));
            }
        }

        private const int LevelCount = 10;

        private static void AssertBoardInvariants(LevelMap map, BoardState board)
        {
            Assert.That(board.Boxes.Count, Is.EqualTo(map.Initial.Boxes.Count));
            Assert.That(map.IsFloor(board.Player), Is.True);
            Assert.That(board.HasBox(board.Player), Is.False);
            var seen = new HashSet<Cell>();
            foreach (var box in board.Boxes)
            {
                Assert.That(map.IsFloor(box), Is.True);
                Assert.That(seen.Add(box), Is.True, "箱が重なっている");
            }
        }

        private sealed class TestRandom
        {
            private uint _state;

            public TestRandom(uint seed) => _state = seed == 0 ? 1u : seed;

            public int Next(int count)
            {
                _state ^= _state << 13;
                _state ^= _state >> 17;
                _state ^= _state << 5;
                return (int)(_state % (uint)count);
            }
        }
    }
}
