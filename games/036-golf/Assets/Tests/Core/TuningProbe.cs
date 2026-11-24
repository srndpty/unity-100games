using System;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>設定の調整用に飛距離と転がりを書き出す（通常のテストでは走らない）。</summary>
    public sealed class TuningProbe
    {
        [Test]
        [Explicit("調整用")]
        public void PrintDistances()
        {
            var config = new GolfConfig();
            var field = GolfRound.OpenField();
            foreach (var surfaceName in new[] { "fairway", "green" })
            {
                var hole = Course.ParseHoles($"hole 1 par 4 size 600 40\npaint {surfaceName} rect 0 0 600 40\npaint tee rect 0 15 3 25\npaint green ellipse 590 20 5 5\ntee 1 20\ncup 590 20\n", config)[0];
                for (var club = 0; club < Clubs.Count; club++)
                {
                    foreach (var impactY in new[] { -2, 0, 2 })
                    {
                        var start = Launch.From(config, new ShotSetup(club, 0, 0, impactY, 1000, 0, false), hole.Tee, Surface.Tee);
                        var end = Predictor.Finish(start, hole, Vec2i.Zero, config);
                        var ball = start;
                        var top = 0;
                        while (ball.IsMoving)
                        {
                            BallSim.Step(ref ball, hole, Vec2i.Zero, config);
                            top = Math.Max(top, ball.Z);
                        }

                        TestContext.Out.WriteLine($"{surfaceName} club{club} y{impactY}: carry {TestKit.Meters(end.Landing.X - hole.Tee.X):F1} total {TestKit.Meters(end.Position.X - hole.Tee.X):F1} apex {TestKit.Meters(top):F1} ticks {end.Ticks}");
                    }
                }
            }

            foreach (var wind in new[] { -6, 6 })
            {
                var start = Launch.From(config, new ShotSetup(Clubs.Wood, 0, 0, 0, 1000, 0, false), field.Tee, Surface.Tee);
                var end = Predictor.Finish(start, field, new Vec2i(wind * config.WindStep, 0), config);
                var side = Predictor.Finish(start, field, new Vec2i(0, wind * config.WindStep), config);
                TestContext.Out.WriteLine($"wood wind {wind}: carry {TestKit.Meters(end.Landing.X - field.Tee.X):F1} side {TestKit.Meters(side.Landing.Y - field.Tee.Y):F1}");
            }

            foreach (var error in new[] { -8, -1, 1, 8 })
            {
                var start = Launch.From(config, new ShotSetup(Clubs.Iron, 0, 0, 0, 1000, error, false), field.Tee, Surface.Tee);
                var end = Predictor.Finish(start, field, Vec2i.Zero, config);
                TestContext.Out.WriteLine($"iron error {error}: side {TestKit.Meters(end.Landing.Y - field.Tee.Y):F1}");
            }

            var green = Course.ParseHoles("hole 1 par 3 size 100 40\npaint green rect 0 0 100 40\npaint tee rect 0 15 3 25\ntee 1 20\ncup 90 20\n", config)[0];
            foreach (var power in new[] { 100, 300, 500, 1000 })
            {
                var start = Launch.From(config, new ShotSetup(Clubs.Putter, 0, 0, 0, power, 0, false), green.Tee, Surface.Green);
                var end = Predictor.Finish(start, green, Vec2i.Zero, config);
                TestContext.Out.WriteLine($"putt {power}: {TestKit.Meters(end.Position.X - green.Tee.X):F2} m");
            }
        }
    }
}
