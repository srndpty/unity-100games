using System;
using System.Text;
using IntGeometry;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>
    /// 予測線（ずれ 0）と実際の着地点の差の計測（完了判定「軌道予測と実軌道の差分記録」）。
    /// 平らなフェアウェイ・風なし・最大のパワーで、インパクトのずれ −8〜+8 Tick とミスショットの着地点の差を測る。
    /// 値は整数の計算なので Mono と .NET で同じ。README の表はこのテストの出力。
    /// </summary>
    public sealed class PredictionDifferenceTests
    {
        private static readonly GolfConfig _config = new();

        private static Vec2i Landing(int club, int error, bool mishit, Vec2i wind)
        {
            var field = GolfRound.OpenField();
            var start = Launch.From(_config, new ShotSetup(club, 0, 0, 0, 1000, error, mishit), field.Tee, Surface.Tee);
            var end = Predictor.Finish(start, field, wind, _config);
            Assert.That(end.Landed, Is.True);
            return end.Landing;
        }

        private static double Difference(Vec2i a, Vec2i b) => IntMath.Sqrt(Vec2i.DistanceSquared(a, b)) / (double)M;

        [Test]
        public void DifferenceIsZeroWithoutErrorAndGrowsWithTheError()
        {
            var table = new StringBuilder("クラブ | ずれ 0 | ±1 | ±2 | ±4 | ±8 | ミス\n");
            for (var club = Clubs.Wood; club <= Clubs.Wedge; club++)
            {
                foreach (var wind in new[] { Vec2i.Zero, new Vec2i(0, 4 * _config.WindStep) })
                {
                    var ideal = Landing(club, 0, false, wind);
                    Assert.That(Landing(club, 0, false, wind), Is.EqualTo(ideal), "同じ打ち方は同じ着地点");
                    var previous = 0.0;
                    for (var error = 1; error <= _config.ImpactWindow; error++)
                    {
                        var late = Difference(Landing(club, error, false, wind), ideal);
                        var early = Difference(Landing(club, -error, false, wind), ideal);
                        Assert.That(late, Is.GreaterThan(previous), $"クラブ {club} のずれ +{error}");
                        Assert.That(early, Is.GreaterThan(previous), $"クラブ {club} のずれ −{error}");
                        previous = Math.Min(late, early);
                    }

                    if (wind.IsZero)
                    {
                        table.Append(club switch { Clubs.Wood => "ウッド", Clubs.Iron => "アイアン", _ => "ウェッジ" });
                        foreach (var error in new[] { 0, 1, 2, 4, 8 })
                        {
                            var worst = Math.Max(Difference(Landing(club, error, false, wind), ideal), Difference(Landing(club, -error, false, wind), ideal));
                            table.Append(" | ").Append(worst.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                        }

                        table.Append(" | ").Append(Difference(Landing(club, _config.ImpactWindow, true, wind), ideal).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
                    }
                }
            }

            TestContext.Out.WriteLine(table.ToString());
        }

        [Test]
        public void PutterErrorTurnsTheLineByTheConfiguredAngle()
        {
            var green = Course.ParseHoles("hole 1 par 3 size 100 40\npaint green rect 0 0 100 40\npaint tee rect 0 15 3 25\ntee 1 20\ncup 90 20\n", _config)[0];
            var start = new Vec2i(5 * M, 20 * M);
            var ideal = Predictor.Finish(Launch.From(_config, new ShotSetup(Clubs.Putter, 0, 0, 0, 600, 0, false), start, Surface.Green), green, Vec2i.Zero, _config);
            var late = Predictor.Finish(Launch.From(_config, new ShotSetup(Clubs.Putter, 0, 0, 0, 600, 2, false), start, Surface.Green), green, Vec2i.Zero, _config);
            Assert.That(late.Position.Y, Is.LessThan(ideal.Position.Y), "遅いと右（−y）");
            var angle = Trig.Atan2(late.Position.Y - start.Y, late.Position.X - start.X, 0) - Trig.FullTurn;
            Assert.That(angle, Is.EqualTo(-2 * _config.PuttAnglePerErrorTick).Within(Trig.FullTurn / Trig.TableSize));
        }
    }
}
