using System;
using NUnit.Framework;
using static Game.Core.Tests.QixTestKit;

namespace Game.Core.Tests
{
    /// <summary>自機の移動・線・閉じる・ミス・勝敗。盤は 20×14、自機は (10, 0) から始まる。</summary>
    public sealed class QixGameTests
    {
        [Test]
        public void WalksAlongTheRimWithoutDrawing()
        {
            var game = WithEnemyAt(Small(), 4, 6);

            Hold(game, Direction.Left, draw: false, 3);
            Assert.That((game.PlayerX, game.PlayerY), Is.EqualTo((7, 0)));

            Hold(game, Direction.Left, draw: false, 10);
            Assert.That((game.PlayerX, game.PlayerY), Is.EqualTo((0, 0)), "角まで行けて、その先へは出ない");

            Hold(game, Direction.Up, draw: false, 5);
            Assert.That((game.PlayerX, game.PlayerY), Is.EqualTo((0, 5)));
            Assert.That(game.IsDrawing, Is.False);
        }

        [Test]
        public void CannotEnterSeaWithoutDrawKeyOrLeaveTheBoard()
        {
            var game = WithEnemyAt(Small(), 4, 6);

            Hold(game, Direction.Up, draw: false, 3);
            Hold(game, Direction.Down, draw: false, 3);

            Assert.That((game.PlayerX, game.PlayerY), Is.EqualTo((10, 0)));
            Assert.That(CountOpen(game), Is.EqualTo(game.InitialOpen));
        }

        [Test]
        public void StepTicksSetsWalkingSpeed()
        {
            var config = Small();
            config.PlayerStepTicks = 3;
            var game = WithEnemyAt(config, 4, 6);

            Hold(game, Direction.Left, draw: false, 7);

            Assert.That(game.PlayerX, Is.EqualTo(10 - 3), "1・4・7 Tick 目に進む");
        }

        [Test]
        public void DrawingLeavesTrailAndRaisesEvent()
        {
            var game = WithEnemyAt(Small(), 4, 6);

            Draw(game, (Direction.Up, 3));

            Assert.That(game.IsDrawing, Is.True);
            Assert.That((game.PlayerX, game.PlayerY), Is.EqualTo((10, 3)));
            Assert.That(game.Trail.Count, Is.EqualTo(3));
            Assert.That(game.CellAt(10, 2), Is.EqualTo(Cell.Trail));
            Assert.That(game.TakeEvents().Has(GameEventKind.DrawStarted), Is.True);
            Assert.That(game.TakeEvents().Kinds, Is.EqualTo(GameEventKind.None), "取り出したら消える");
        }

        [Test]
        public void UnclosedLineNeverClaimsAnything()
        {
            var game = WithEnemyAt(Small(), 4, 6);

            Draw(game, (Direction.Up, 6));
            Hold(game, Direction.None, draw: true, 100);
            Hold(game, Direction.Up, draw: false, 10);

            Assert.That((game.PlayerX, game.PlayerY), Is.EqualTo((10, 6)), "描画キーを離すとその場で止まる");
            Assert.That(game.ClaimedCells, Is.Zero);
            Assert.That(game.Score, Is.Zero);
            Assert.That(CountOpen(game), Is.EqualTo(game.InitialOpen - 6));
            Assert.That(game.TakeEvents().Has(GameEventKind.Closed), Is.False);
        }

        [Test]
        public void CannotReverseOrCrossOwnLine()
        {
            var game = WithEnemyAt(Small(), 4, 6);

            Draw(game, (Direction.Up, 4));
            Hold(game, Direction.Down, draw: true, 3);
            Assert.That((game.PlayerX, game.PlayerY), Is.EqualTo((10, 4)), "逆走できない");

            Draw(game, (Direction.Right, 2), (Direction.Down, 2), (Direction.Left, 3));
            Assert.That((game.PlayerX, game.PlayerY), Is.EqualTo((11, 2)), "自分の線の手前で止まる");
            Assert.That(game.Trail.Count, Is.EqualTo(4 + 2 + 2 + 1));
        }

        [TestCase(4, 6, 11, 96)]
        [TestCase(15, 6, 1, 108)]
        public void ClosingClaimsTheSideWithoutTheEnemy(int enemyX, int enemyY, int claimedX, int claimedSea)
        {
            var game = WithEnemyAt(Small(), enemyX, enemyY);

            Draw(game, (Direction.Up, 13));

            var events = game.TakeEvents();
            Assert.That(events.Has(GameEventKind.Closed), Is.True);
            Assert.That(events.ClaimedCells, Is.EqualTo(claimedSea + 12));
            Assert.That(game.ClaimedCells, Is.EqualTo(claimedSea + 12));
            Assert.That(game.Score, Is.EqualTo(game.ClaimedCells));
            Assert.That(game.IsDrawing, Is.False);
            Assert.That((game.PlayerX, game.PlayerY), Is.EqualTo((10, 13)));
            Assert.That(game.CellAt(claimedX, 6), Is.EqualTo(Cell.Claimed));
            Assert.That(game.CellAt(enemyX, enemyY), Is.EqualTo(Cell.Open), "敵のいる側は消さない");
            Assert.That(CellsUnderEnemy(game), Has.All.EqualTo(Cell.Open));
            Assert.That(CountOpen(game), Is.EqualTo(game.InitialOpen - game.ClaimedCells));
        }

        [Test]
        public void ClaimedInteriorIsNotWalkable()
        {
            var game = WithEnemyAt(Small(), 4, 6);
            Draw(game, (Direction.Up, 13));

            Hold(game, Direction.Right, draw: false, 3);
            Assert.That(game.PlayerX, Is.EqualTo(10), "右側は陸の内部になり、縁ではない");

            Hold(game, Direction.Left, draw: false, 3);
            Assert.That(game.PlayerX, Is.EqualTo(7));
        }

        [Test]
        public void MovesToNearestRimWhenLandingCellIsNoLongerRim()
        {
            // 敵を左下の 2×2 の入り江に閉じ込め、線の終点は反対側の上の縁にする。
            var game = QixGame.WithEnemy(Small(targetPercent: 99), 1, 384, 384, 1, 0);
            Hold(game, Direction.Left, draw: false, 7);

            Draw(game, (Direction.Up, 3), (Direction.Left, 2), (Direction.Up, 10));

            Assert.That(game.TakeEvents().Has(GameEventKind.Closed), Is.True);
            Assert.That(game.ClaimedCells, Is.EqualTo(game.InitialOpen - 4));
            Assert.That(game.Status, Is.EqualTo(GameStatus.Playing));
            Assert.That(game.IsBoundary(1, 13), Is.False);
            Assert.That(game.IsBoundary(game.PlayerX, game.PlayerY), Is.True);
            Assert.That(game.PlayerY, Is.LessThanOrEqualTo(3), "入り江の周りの縁に移る");
        }

        [Test]
        public void EnemyTouchingTrailStopsAtContactAndCostsALife()
        {
            var config = Small();
            config.EnemyRadius = 384;
            var game = QixGame.WithEnemy(config, 1, 1152, 1664, 40, 0);
            Draw(game, (Direction.Up, 6));

            var contact = 0;
            for (var tick = 0; tick < 60 && contact == 0; tick++)
            {
                game.Tick();
                if (game.TakeEvents().Has(GameEventKind.Miss)) { contact = tick + 1; }
            }

            Assert.That(contact, Is.Not.Zero);
            Assert.That(game.Enemy.X, Is.EqualTo(10 * S - 384 + 1), "線に初めて重なる位置で止まる");
            Assert.That(game.Lives, Is.EqualTo(2));
            Assert.That(game.IsDrawing, Is.False);
            Assert.That((game.PlayerX, game.PlayerY), Is.EqualTo((10, 0)), "線の始点へ戻る");
            Assert.That(CountOpen(game), Is.EqualTo(game.InitialOpen), "線は海に戻る");
        }

        [Test]
        public void DiagonalGrazeWithinOneTickIsDetected()
        {
            // 敵は 1 Tick で線の先端 (10, 5) の左上の角をかすめる。Tick の始めと終わりの位置ではどちらも線に重ならない。
            var game = QixGame.WithEnemy(Small(), 1, 1460, 436, 200, 200);
            Draw(game, (Direction.Up, 5));
            Assert.That((game.Enemy.X, game.Enemy.Y), Is.EqualTo((2460, 1436)));
            Assert.That(game.TakeEvents().Has(GameEventKind.Miss), Is.False);

            game.Tick();

            Assert.That(game.TakeEvents().Has(GameEventKind.Miss), Is.True);
            Assert.That(game.Enemy.X, Is.InRange(2460, 2660));
            Assert.That(game.Lives, Is.EqualTo(2));
        }

        [Test]
        public void WalkingIntoTheEnemyIsAMiss()
        {
            var game = WithEnemyAt(Small(), 10, 1);

            Hold(game, Direction.Up, draw: false, 1);
            Assert.That(game.Lives, Is.EqualTo(3), "描画キーなしでは海へ出ないので当たらない");

            Hold(game, Direction.Up, draw: true, 1);

            Assert.That(game.TakeEvents().Has(GameEventKind.Miss), Is.True);
            Assert.That(game.Lives, Is.EqualTo(2));
            Assert.That((game.PlayerX, game.PlayerY), Is.EqualTo((10, 0)));
            Assert.That(game.IsDrawing, Is.False);
        }

        [Test]
        public void LosingLastLifeEndsGameAndRejectsInput()
        {
            var config = Small();
            config.Lives = 1;
            var game = WithEnemyAt(config, 10, 1);

            Hold(game, Direction.Up, draw: true, 1);

            Assert.That(game.Status, Is.EqualTo(GameStatus.Lost));
            Assert.That(game.TakeEvents().Has(GameEventKind.Lost), Is.True);
            var hash = game.Hash();
            var ticks = game.Ticks;
            Assert.That(game.SetInput(Direction.Left, draw: false), Is.False);
            game.Tick();
            Assert.That(game.Hash(), Is.EqualTo(hash));
            Assert.That(game.Ticks, Is.EqualTo(ticks));
            Assert.That(game.TakeEvents().Kinds, Is.EqualTo(GameEventKind.None));
        }

        [TestCase(50, GameStatus.Won)]
        [TestCase(51, GameStatus.Playing)]
        public void WinsExactlyAtTargetPercent(int target, GameStatus expected)
        {
            // 216 セルの海のうち 108 セル（ちょうど 50%）を取る。
            var game = WithEnemyAt(Small(target), 4, 6);

            Draw(game, (Direction.Up, 13));

            Assert.That(game.ClaimedCells * 2, Is.EqualTo(game.InitialOpen));
            Assert.That(game.Status, Is.EqualTo(expected));
            Assert.That(game.TakeEvents().Has(GameEventKind.Won), Is.EqualTo(expected == GameStatus.Won));
            Assert.That(game.SetInput(Direction.Left, draw: false), Is.EqualTo(expected == GameStatus.Playing));
        }

        [Test]
        public void RejectsUndefinedDirectionAndInvalidConfig()
        {
            var game = WithEnemyAt(Small(), 4, 6);

            Assert.Throws<ArgumentOutOfRangeException>(() => game.SetInput((Direction)9, draw: false));
            Assert.Throws<ArgumentNullException>(() => _ = new QixGame(null!, 1));
            var config = Small();
            config.Lives = 0;
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new QixGame(config, 1));
        }

        [Test]
        public void ConfigChangesAfterStartDoNotAffectTheGame()
        {
            var config = Small();
            var game = WithEnemyAt(config, 4, 6);
            config.PlayerStepTicks = 30;
            config.TargetPercent = 1;

            Hold(game, Direction.Left, draw: false, 2);

            Assert.That(game.PlayerX, Is.EqualTo(8));
            Assert.That(game.TargetPercent, Is.EqualTo(75));
        }
    }
}
