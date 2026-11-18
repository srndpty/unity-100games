using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class MazeGameStateTests
    {
        private const int Never = int.MaxValue;
        private static readonly int[] _singleEntry = { 1 };

        /// <summary>猫を寝床に留め、準備時間を最短にした設定。</summary>
        private static MazeGameConfig Calm() => new()
        {
            ReadyTicks = 1,
            DyingTicks = 1,
            ReleaseTicks = new[] { 0, Never, Never, Never },
        };

        /// <summary>指定した座標にだけ餌を残した既定の迷路。</summary>
        private static MazeLayout WithPellets(params (int X, int Y, char Kind)[] pellets)
        {
            var rows = MazeLayout.CopyDefaultRows();
            for (var y = 0; y < rows.Length; y++) { rows[y] = rows[y].Replace('.', ' ').Replace('o', ' '); }
            foreach (var (x, y, kind) in pellets)
            {
                rows[y] = rows[y].Remove(x, 1).Insert(x, kind.ToString());
            }

            return new MazeLayout(rows);
        }

        private static void StartActive(MazeGameState game)
        {
            for (var i = 0; i < 600 && game.Phase == GamePhase.Ready; i++) { game.Step(); }
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Active));
        }

        private static void Steps(MazeGameState game, int count, Direction direction = Direction.None)
        {
            for (var i = 0; i < count; i++) { game.Step(direction); }
        }

        private static bool OnOpenCell(MazeGameState game, MazeActor actor) => game.Graph.Kind(actor.Cell) != CellKind.Wall;

        [Test]
        public void Player_DoesNotMoveBeforeFirstInput()
        {
            var game = new MazeGameState(Calm(), 1);
            StartActive(game);
            Steps(game, 30);
            Assert.That(game.Player.Cell, Is.EqualTo(game.Graph.PlayerCell));
            Assert.That(game.Player.Progress, Is.Zero);
        }

        [Test]
        public void Player_NeverEntersWallsOrDoor_FromAnyInput()
        {
            var random = new SeededRandom(7);
            var moves = new[] { Direction.Up, Direction.Left, Direction.Down, Direction.Right };
            var config = Calm();
            config.CatSpeed = 1;
            var game = new MazeGameState(config, 3);
            StartActive(game);
            for (var i = 0; i < 20000 && !game.IsOver; i++)
            {
                game.Step(moves[random.Next(moves.Length)]);
                Assert.That(game.Graph.Kind(game.Player.Cell), Is.EqualTo(CellKind.Path));
            }
        }

        [Test]
        public void Player_StopsAtWallAndKeepsCell()
        {
            var game = new MazeGameState(Calm(), 1, WithPellets((1, 1, '.')));
            StartActive(game);
            Steps(game, 600, Direction.Right);
            Assert.That(game.IsPlayerMoving, Is.False);
            Assert.That(game.Player.Progress, Is.Zero);
            Assert.That(game.Graph.Point(game.Player.Cell), Is.EqualTo(new GridPoint(19, 10)));
        }

        [Test]
        public void Player_ReversesImmediatelyBetweenCells()
        {
            var game = new MazeGameState(Calm(), 1, WithPellets((1, 1, '.')));
            StartActive(game);
            Steps(game, 2, Direction.Left);
            var x = game.Player.WorldX(game.Graph);
            game.Step(Direction.Right);
            Assert.That(game.Player.Heading, Is.EqualTo(Direction.Right));
            Assert.That(game.Player.WorldX(game.Graph), Is.EqualTo(x + 75));
        }

        [Test]
        public void Player_WrapsThroughTunnel()
        {
            var graph = new MazeGraph(MazeLayout.CreateDefault());
            var actor = new MazeActor(graph.CellId(0, 7), Direction.Left);
            actor.Advance(graph, 100, out var arrived);
            Assert.That(arrived, Is.False);
            Assert.That(actor.WorldX(graph), Is.EqualTo((29 * 600) - 100));
            actor.Advance(graph, 500, out arrived);
            Assert.That(arrived, Is.True);
            Assert.That(actor.Cell, Is.EqualTo(graph.CellId(28, 7)));
        }

        [Test]
        public void Touches_AcrossTunnelSeam()
        {
            var graph = new MazeGraph(MazeLayout.CreateDefault());
            var left = new MazeActor(graph.CellId(0, 7), Direction.Right);
            var right = new MazeActor(graph.CellId(28, 7), Direction.Left);
            Assert.That(left.Touches(right, graph, 450), Is.False);
            left.Reset(graph.CellId(0, 7), Direction.Left);
            left.Advance(graph, 300, out _);
            Assert.That(left.Touches(right, graph, 300), Is.True);
        }

        [Test]
        public void Touches_NotThroughWall()
        {
            var graph = new MazeGraph(MazeLayout.CreateDefault());
            var above = new MazeActor(graph.CellId(3, 1), Direction.Right);
            var below = new MazeActor(graph.CellId(5, 4), Direction.Right);
            Assert.That(above.Touches(below, graph, MazeGameConfig.MaxContactRadius), Is.False);
        }

        [Test]
        public void Reverse_AtCenterKeepsHeadingWhenBackIsBlocked()
        {
            var graph = new MazeGraph(MazeLayout.CreateDefault());
            var actor = new MazeActor(graph.CellId(1, 1), Direction.Right);
            actor.Reverse(graph, Traversal.Corridor);
            Assert.That(actor.Heading, Is.EqualTo(Direction.Right));
        }

        [Test]
        public void Cats_StayOnLegalCells_ThroughLongRandomPlay()
        {
            foreach (var seed in new[] { 1, 2, 3, 4, 5 })
            {
                var random = new SeededRandom(seed);
                var moves = new[] { Direction.Up, Direction.Left, Direction.Down, Direction.Right };
                var config = new MazeGameConfig { ReadyTicks = 1, DyingTicks = 1, Lives = 9 };
                var game = new MazeGameState(config, seed);
                for (var i = 0; i < 8000 && !game.IsOver; i++)
                {
                    game.Step(moves[random.Next(moves.Length)]);
                    for (var c = 0; c < MazeGameConfig.CatCount; c++)
                    {
                        var cat = game.Cat(c);
                        Assert.That(OnOpenCell(game, cat.Actor), Is.True);
                        if (cat.Mode == CatMode.Roaming || cat.Mode == CatMode.Frightened)
                        {
                            Assert.That(game.Graph.Kind(cat.Actor.Cell), Is.Not.EqualTo(CellKind.House));
                        }
                    }
                }
            }
        }

        [Test]
        public void Cats_LeaveHouseInConfiguredOrder()
        {
            var config = new MazeGameConfig { ReadyTicks = 1, PlayerSpeed = 1 };
            var game = new MazeGameState(config, 1);
            StartActive(game);
            Steps(game, 118);
            Assert.That(game.Cat(1).Mode, Is.EqualTo(CatMode.InHouse));
            Steps(game, 2);
            Assert.That(game.Cat(1).Mode, Is.EqualTo(CatMode.Leaving));
            Assert.That(game.Cat(2).Mode, Is.EqualTo(CatMode.InHouse));
            Assert.That(game.Cat(3).Mode, Is.EqualTo(CatMode.InHouse));
        }

        [Test]
        public void ModeSchedule_ChangesAndReversesRoamingCat()
        {
            var config = Calm();
            config.ModeTicks = new[] { 10, 20 };
            var game = new MazeGameState(config, 1, WithPellets((1, 1, '.')));
            StartActive(game);
            Assert.That(game.IsChase, Is.False);
            Steps(game, 8);
            var heading = game.Cat(0).Actor.Heading;
            var sawChange = false;
            for (var i = 0; i < 4 && !sawChange; i++)
            {
                game.Step();
                sawChange = (game.Events & GameEvents.ModeChanged) != 0;
            }

            Assert.That(sawChange, Is.True);
            Assert.That(game.IsChase, Is.True);
            Assert.That(game.Cat(0).Actor.Heading, Is.EqualTo(heading.Opposite()));
        }

        [Test]
        public void ModeSchedule_StaysChaseAfterLastEntry()
        {
            var config = Calm();
            config.ModeTicks = new[] { 3 };
            var game = new MazeGameState(config, 1, WithPellets((1, 1, '.')));
            StartActive(game);
            Steps(game, 100);
            Assert.That(game.IsChase, Is.True);
        }

        [Test]
        public void Power_FrightensOnlyRoamingCats()
        {
            var config = new MazeGameConfig { ReadyTicks = 1, ReleaseTicks = new[] { 0, 1, Never, Never }, LeavingSpeed = 1 };
            var game = new MazeGameState(config, 1, WithPellets((13, 10, 'o'), (1, 1, '.')));
            StartActive(game);
            for (var i = 0; i < 60 && (game.Events & GameEvents.PowerEaten) == 0; i++) { game.Step(Direction.Left); }
            Assert.That(game.Events & GameEvents.PowerEaten, Is.EqualTo(GameEvents.PowerEaten));
            Assert.That(game.Cat(0).Mode, Is.EqualTo(CatMode.Frightened));
            Assert.That(game.Cat(1).Mode, Is.EqualTo(CatMode.Leaving));
            Assert.That(game.Cat(2).Mode, Is.EqualTo(CatMode.InHouse));
        }

        [Test]
        public void Power_EndRestoresRoamingAndResumesPausedClock()
        {
            var config = Calm();
            config.PowerTicks = 30;
            config.ModeTicks = new[] { 100, 100 };
            var game = new MazeGameState(config, 1, WithPellets((13, 10, 'o'), (1, 1, '.')));
            StartActive(game);
            for (var i = 0; i < 60 && (game.Events & GameEvents.PowerEaten) == 0; i++) { game.Step(Direction.Left); }
            Assert.That(game.Cat(0).Mode, Is.EqualTo(CatMode.Frightened));
            Assert.That(game.IsChase, Is.False);

            var ended = 0;
            for (var i = 0; i < 40; i++)
            {
                game.Step();
                if ((game.Events & GameEvents.PowerEnded) != 0) { ended++; }
            }

            Assert.That(ended, Is.EqualTo(1));
            Assert.That(game.PowerTicksLeft, Is.Zero);
            Assert.That(game.Cat(0).Mode, Is.EqualTo(CatMode.Roaming));
            Assert.That(game.IsChase, Is.False, "逃走中は巡回の時計が止まっている");
        }

        [Test]
        public void Power_ReacquireRestartsTimer()
        {
            var config = Calm();
            config.PowerTicks = 200;
            var game = new MazeGameState(config, 1, WithPellets((13, 10, 'o'), (11, 10, 'o'), (1, 1, '.')));
            StartActive(game);
            for (var i = 0; i < 60 && (game.Events & GameEvents.PowerEaten) == 0; i++) { game.Step(Direction.Left); }
            game.Step(Direction.Left);
            for (var i = 0; i < 60 && (game.Events & GameEvents.PowerEaten) == 0; i++) { game.Step(Direction.Left); }
            Assert.That(game.PowerTicksLeft, Is.EqualTo(200));
        }

        [Test]
        public void Capture_ScoresChainAndSendsCatHome()
        {
            var config = Calm();
            config.CatSpeed = 1;
            config.FrightenedSpeed = 1;
            config.PowerTicks = 3000;
            var rows = MazeLayout.CopyDefaultRows();
            for (var y = 0; y < rows.Length; y++) { rows[y] = rows[y].Replace('.', ' ').Replace('o', ' '); }
            rows[4] = rows[4].Remove(11, 1).Insert(11, "o");
            rows[1] = rows[1].Remove(1, 1).Insert(1, ".");
            rows[10] = rows[10].Replace('P', ' ');
            rows[4] = rows[4].Remove(9, 1).Insert(9, "P");
            var game = new MazeGameState(config, 1, new MazeLayout(rows));
            StartActive(game);
            var captured = false;
            for (var i = 0; i < 400 && !captured; i++)
            {
                game.Step(Direction.Right);
                captured = (game.Events & GameEvents.CatCaptured) != 0;
            }

            Assert.That(captured, Is.True);
            Assert.That(game.Score, Is.EqualTo(config.PowerScore + config.CatScores[0]));
            Assert.That(game.Cat(0).Mode, Is.EqualTo(CatMode.Returning));
            Assert.That(game.Lives, Is.EqualTo(config.Lives));
        }

        [Test]
        public void ReturningCat_ComesBackAndLeavesAgain()
        {
            var config = Calm();
            config.CatSpeed = 1;
            config.FrightenedSpeed = 1;
            config.PowerTicks = 3000;
            var rows = MazeLayout.CopyDefaultRows();
            for (var y = 0; y < rows.Length; y++) { rows[y] = rows[y].Replace('.', ' ').Replace('o', ' '); }
            rows[4] = rows[4].Remove(11, 1).Insert(11, "o");
            rows[1] = rows[1].Remove(1, 1).Insert(1, ".");
            rows[10] = rows[10].Replace('P', ' ');
            rows[4] = rows[4].Remove(9, 1).Insert(9, "P");
            var game = new MazeGameState(config, 1, new MazeLayout(rows));
            StartActive(game);
            for (var i = 0; i < 400 && game.Cat(0).Mode != CatMode.Returning; i++) { game.Step(Direction.Right); }
            Assert.That(game.Cat(0).Mode, Is.EqualTo(CatMode.Returning));

            var sawLeaving = false;
            for (var i = 0; i < 2000 && game.Cat(0).Mode != CatMode.Roaming; i++)
            {
                game.Step();
                sawLeaving |= game.Cat(0).Mode == CatMode.Leaving;
            }

            Assert.That(sawLeaving, Is.True);
            Assert.That(game.Cat(0).Mode, Is.EqualTo(CatMode.Roaming));
        }

        [Test]
        public void LastPellet_WinsOnce_EvenWhenContactHappensSameInstant()
        {
            // 自機は3Tickで最後の餌へ着き、同じ部分ステップでトラとの距離がちょうど判定半径になる。
            var config = new MazeGameConfig
            {
                ReadyTicks = 1,
                PlayerSpeed = 200,
                CatSpeed = 160,
                ContactRadius = 120,
                ReleaseTicks = new[] { 0, Never, Never, Never },
                ModeTicks = Array.Empty<int>(),
            };
            var rows = MazeLayout.CopyDefaultRows();
            for (var y = 0; y < rows.Length; y++) { rows[y] = rows[y].Replace('.', ' ').Replace('o', ' '); }
            rows[10] = rows[10].Replace('P', ' ');
            rows[4] = rows[4].Remove(12, 2).Insert(12, "P.");
            var game = new MazeGameState(config, 1, new MazeLayout(rows));
            StartActive(game);

            var wins = 0;
            for (var i = 0; i < 10; i++)
            {
                game.Step(Direction.Right);
                if ((game.Events & GameEvents.Won) != 0) { wins++; }
            }

            Assert.That(wins, Is.EqualTo(1));
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Won));
            Assert.That(game.Lives, Is.EqualTo(config.Lives));
        }

        [Test]
        public void FinishedGame_IgnoresFurtherSteps()
        {
            var game = new MazeGameState(Calm(), 1, WithPellets((13, 10, '.')));
            StartActive(game);
            Steps(game, 30, Direction.Left);
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Won));
            var tick = game.TickCount;
            var score = game.Score;
            Steps(game, 10, Direction.Right);
            Assert.That(game.TickCount, Is.EqualTo(tick));
            Assert.That(game.Score, Is.EqualTo(score));
            Assert.That(game.Events, Is.EqualTo(GameEvents.None));
        }

        [Test]
        public void Miss_KeepsEatenPelletsAndResetsPositions()
        {
            var config = new MazeGameConfig { ReadyTicks = 1, DyingTicks = 1, CatSpeed = 200, ReleaseTicks = new[] { 0, Never, Never, Never } };
            var game = new MazeGameState(config, 1);
            StartActive(game);
            var remaining = game.PelletsRemaining;
            for (var i = 0; i < 4000 && game.Lives == config.Lives; i++) { game.Step(Direction.Left); }
            Assert.That(game.Lives, Is.EqualTo(config.Lives - 1));
            Assert.That(game.PelletsRemaining, Is.LessThan(remaining));
            var eaten = game.PelletsRemaining;
            Steps(game, 2);
            Assert.That(game.Player.Cell, Is.EqualTo(game.Graph.PlayerCell));
            Assert.That(game.PelletsRemaining, Is.EqualTo(eaten));
        }

        [Test]
        public void LosingAllLives_EndsGameOnce()
        {
            var config = new MazeGameConfig { ReadyTicks = 1, DyingTicks = 1, Lives = 1, CatSpeed = 200, ReleaseTicks = new[] { 0, Never, Never, Never } };
            var game = new MazeGameState(config, 1);
            var overs = 0;
            for (var i = 0; i < 6000 && !game.IsOver; i++)
            {
                game.Step();
                if ((game.Events & GameEvents.GameOver) != 0) { overs++; }
            }

            Assert.That(game.Phase, Is.EqualTo(GamePhase.GameOver));
            Assert.That(overs, Is.EqualTo(1));
            Assert.That(game.Lives, Is.Zero);
        }

        [Test]
        public void SameSeedAndInputs_ProduceSameGame()
        {
            var first = Run(42);
            var second = Run(42);
            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void Step_RejectsInvalidDirection()
        {
            var game = new MazeGameState(Calm(), 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => game.Step((Direction)9));
        }

        [TestCase(nameof(MazeGameConfig.PlayerSpeed), 0)]
        [TestCase(nameof(MazeGameConfig.PlayerSpeed), MazeGameConfig.MaxSpeed + 1)]
        [TestCase(nameof(MazeGameConfig.ContactRadius), MazeGameConfig.MinContactRadius - 1)]
        [TestCase(nameof(MazeGameConfig.ContactRadius), MazeGameConfig.MaxContactRadius + 1)]
        [TestCase(nameof(MazeGameConfig.Lives), 0)]
        public void Config_RejectsOutOfRange(string field, int value)
        {
            var config = new MazeGameConfig();
            typeof(MazeGameConfig).GetField(field)!.SetValue(config, value);
            Assert.Throws<ArgumentOutOfRangeException>(config.Validate);
        }

        [Test]
        public void Config_RejectsWrongArrayLengths()
        {
            Assert.Throws<ArgumentException>(new MazeGameConfig { CatScores = _singleEntry }.Validate);
            Assert.Throws<ArgumentException>(new MazeGameConfig { ReleaseTicks = _singleEntry }.Validate);
        }

        private static string Run(int seed)
        {
            var random = new SeededRandom(seed);
            var moves = new[] { Direction.Up, Direction.Left, Direction.Down, Direction.Right };
            var game = new MazeGameState(new MazeGameConfig { ReadyTicks = 1, DyingTicks = 1 }, seed);
            for (var i = 0; i < 3000 && !game.IsOver; i++) { game.Step(moves[random.Next(moves.Length)]); }
            var text = game.TickCount + ":" + game.Score + ":" + game.Lives + ":" + game.PelletsRemaining;
            for (var c = 0; c < MazeGameConfig.CatCount; c++)
            {
                text += ":" + game.Cat(c).Actor.Cell + "/" + game.Cat(c).Actor.Progress + "/" + game.Cat(c).Mode;
            }

            return text;
        }
    }
}
