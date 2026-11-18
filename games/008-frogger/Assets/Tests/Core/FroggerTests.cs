using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class FroggerTests
    {
        public static FroggerConfig Still()
        {
            var config = new FroggerConfig();
            foreach (var lane in config.Lanes) { lane.Speed = 0; }
            return config;
        }
        private static void Up(FroggerState game, int count)
        { for (var i = 0; i < count; i++) { game.Step(MoveDirection.Up); } }
        [Test]
        public void Default_Crossing_WinsOnce()
        {
            var game = new FroggerState(new FroggerConfig());
            for (var i = 0; i < 200 && game.Result == RoundResult.Playing; i++)
            { game.Step(game.TickCount % 8 == 0 ? MoveDirection.Up : MoveDirection.None); }
            Assert.That(game.Result, Is.EqualTo(RoundResult.Won));
            var tick = game.TickCount; game.Step(MoveDirection.Down);
            Assert.That(game.Row, Is.EqualTo(8)); Assert.That(game.TickCount, Is.EqualTo(tick));
        }
        [Test]
        public void Log_RelativePosition_RemainsConstant()
        {
            var config = Still(); config.Lanes[3].Speed = .7f;
            var game = new FroggerState(config); Up(game, 5);
            var id = game.SupportId; var offset = game.X - game.ObjectX(3, id);
            for (var i = 0; i < 120; i++) { game.Step(); }
            Assert.That(game.Result, Is.EqualTo(RoundResult.Playing));
            Assert.That(game.X - game.ObjectX(3, id), Is.EqualTo(offset).Within(1e-10));
        }
        [Test]
        public void Log_HorizontalHop_DoesNotSnap()
        {
            var config = Still(); config.Lanes[3].Speed = .7f;
            var game = new FroggerState(config); Up(game, 5); var before = game.X;
            game.Step(MoveDirection.Right);
            Assert.That(game.X, Is.EqualTo(before + 1 + .7f / 60.0).Within(1e-10));
        }
        [Test]
        public void Log_LeavesScreen_FailsWithoutWrapping()
        {
            var config = Still(); config.Lanes[3].Speed = 2;
            var game = new FroggerState(config); Up(game, 5);
            for (var i = 0; i < 400 && game.Result == RoundResult.Playing; i++) { game.Step(); }
            Assert.That(game.Result, Is.EqualTo(RoundResult.CarriedOut)); Assert.That(game.X, Is.GreaterThan(10.75));
        }
        [Test]
        public void RiverWithoutLog_DiffersFromCarHit()
        {
            var config = Still(); config.Lanes[3].Phase = 2;
            var game = new FroggerState(config); Up(game, 5);
            Assert.That(game.Result, Is.EqualTo(RoundResult.Drowned));
            config.Lanes[0].Phase = 0; game = new FroggerState(config); game.Step(MoveDirection.Up);
            Assert.That(game.Result, Is.EqualTo(RoundResult.CarHit));
        }
        [TestCase(1.25, RoundResult.Playing)]
        [TestCase(1.250001, RoundResult.Drowned)]
        public void Log_EdgeRequiresWholeFrog(double shift, RoundResult expected)
        {
            var config = Still(); config.Lanes[3].Width = 3; config.Lanes[3].Phase = (float)shift;
            var game = new FroggerState(config); Up(game, 5);
            Assert.That(game.Result, Is.EqualTo(expected));
        }
        [Test]
        public void Car_SweptEdge_HitsBeforeEnd()
        {
            var config = Still(); config.Lanes[0] = new LaneConfig(12, 1, 3.2f);
            // 中央5.5に向かい、4.7から4.9へ移動。接触半径0.75。
            var game = new FroggerState(config); game.Step(MoveDirection.Up);
            Assert.That(game.Result, Is.EqualTo(RoundResult.CarHit));
        }
        [Test]
        public void Car_PeriodBoundary_DoesNotSweepAcrossBoard()
        {
            var config = Still(); config.Lanes[0] = new LaneConfig(12, 1, 15.9f);
            var game = new FroggerState(config); game.Step(MoveDirection.Left); game.Step(MoveDirection.Up);
            Assert.That(game.Result, Is.EqualTo(RoundResult.Playing));
        }
        [Test]
        public void Log_SwitchLaneAndDisembark_StopsOldCarry()
        {
            var config = Still(); config.Lanes[3].Speed = 1; config.Lanes[4].Speed = -1;
            var game = new FroggerState(config); Up(game, 5); var x = game.X;
            game.Step(MoveDirection.Up); Assert.That(game.X, Is.EqualTo(x - 1.0 / 60).Within(1e-10));
            game.Step(MoveDirection.Down); game.Step(MoveDirection.Down); x = game.X;
            game.Step(); Assert.That(game.X, Is.EqualTo(x)); Assert.That(game.SupportId, Is.EqualTo(-1));
        }
        [Test]
        public void InvalidAndOutsideMoves_DoNotChangePosition()
        {
            var game = new FroggerState(Still()); game.Step(MoveDirection.Down);
            for (var i = 0; i < 20; i++) { game.Step(MoveDirection.Left); }
            Assert.That(game.X, Is.EqualTo(.5)); Assert.That(game.Row, Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => game.Step((MoveDirection)99));
        }
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(-12.001f)]
        [TestCase(12.001f)]
        public void InvalidSpeed_Throws(float speed)
        { var c = Still(); c.Lanes[0].Speed = speed; Assert.Throws<ArgumentException>(() => c.Validate()); }
        [Test]
        public void InvalidConfig_Throws()
        {
            var c = Still(); c.FrogRadius = float.NaN; Assert.Throws<ArgumentOutOfRangeException>(() => c.Validate());
            c = Still(); c.Lanes[0].Width = 3.5001f; Assert.Throws<ArgumentException>(() => c.Validate());
            c = Still(); c.Lanes[0].Phase = 16; Assert.Throws<ArgumentException>(() => c.Validate());
            Assert.Throws<ArgumentNullException>(() => new FroggerState(null!));
        }
        [Test]
        public void ConfigurationAndSnapshot_AreIsolated()
        {
            var config = Still(); var game = new FroggerState(config); config.Lanes[0].Phase = 0;
            var copy = game.Copy(); copy.Step(MoveDirection.Up);
            Assert.That(game.Row, Is.Zero); Assert.That(copy.Result, Is.EqualTo(RoundResult.Playing));
        }
        [Test]
        public void MaximumAndVariedSettings_KeepPeriodicObjectsBounded()
        {
            var config = Still(); uint random = 82731;
            for (var sample = 0; sample < 100; sample++)
            {
                random ^= random << 13; random ^= random >> 17; random ^= random << 5;
                foreach (var lane in config.Lanes) { lane.Speed = (int)(random % 25) - 12; lane.Width = 3.5f; lane.Phase = random % 16; }
                config.Validate(); var game = new FroggerState(config);
                for (var t = 0; t < 120; t++) { game.Step(); }
                for (var lane = 0; lane < 6; lane++)
                {
                    for (var id = 0; id < 4; id++) { Assert.That(game.ObjectX(lane, id), Is.InRange(-2.5, 13.5)); }
                }
            }
        }
    }
}
