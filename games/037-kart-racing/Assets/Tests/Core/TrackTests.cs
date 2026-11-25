using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class TrackTests
    {
        [Test]
        public void ProductionTrackParses()
        {
            var track = TestKit.Circuit();
            Assert.That(track.StationCount, Is.GreaterThanOrEqualTo(4));
            Assert.That(track.CheckpointCount, Is.GreaterThanOrEqualTo(4));
            Assert.That(track.CheckpointStation(0), Is.EqualTo(0));
            Assert.That(track.Walls, Has.Length.EqualTo(track.StationCount * 2));
            Assert.That(track.TotalLength, Is.InRange(400_000, 600_000));
        }

        [Test]
        public void CenterlineStaysClearOfTheWalls()
        {
            var track = TestKit.Circuit();
            var config = new KartConfig();
            for (var i = 0; i < track.StationCount; i++)
            {
                var mid = track.PointAt(new TrackPlace(i, track.SegmentLength(i) / 2));
                AssertOutside(track, track.StationPosition(i), config.Radius);
                AssertOutside(track, mid, config.Radius);
            }
        }

        [Test]
        public void MovingOneStationChangesTheWall()
        {
            var text = TestKit.DataText("Track.txt");
            var moved = text.Replace("station 112000 0 5000", "station 112000 1000 5000", StringComparison.Ordinal);
            var original = Track.Parse(text, new KartConfig());
            var shifted = Track.Parse(moved, new KartConfig());
            Assert.That(shifted.StationPosition(1), Is.Not.EqualTo(original.StationPosition(1)));
            Assert.That(shifted.Left(1), Is.Not.EqualTo(original.Left(1)));
        }

        [TestCase("runoff 0\nstation 0 0 5000\nstation 4000 0 5000\nstation 4000 4000 5000\n", "点は 4 個以上")]
        [TestCase("runoff 0\nstation 0 0 5000\nstation 4000 0 5000\nstation 8000 0 5000\nstation 12000 0 5000\ncheckpoint 0\ncheckpoint 1\ncheckpoint 2\n", "チェックポイントは 4 個以上")]
        [TestCase("runoff 0\nstation 0 0 500\nstation 4000 0 500\nstation 4000 4000 500\nstation 0 4000 500\ncheckpoint 0\ncheckpoint 1\ncheckpoint 2\ncheckpoint 3\n", "半幅")]
        public void ParseRejects(string text, string fragment)
        {
            var error = Assert.Throws<FormatException>(() => Track.Parse(text, new KartConfig()));
            Assert.That(error!.Message, Does.Contain(fragment));
        }

        [Test]
        public void ParseRejectsACheckpointOutOfOrder()
        {
            var text = "runoff 0\nstation 0 0 5000\nstation 4000 0 5000\nstation 4000 4000 5000\nstation 0 4000 5000\n" +
                       "checkpoint 0\ncheckpoint 2\ncheckpoint 1\ncheckpoint 3\n";
            Assert.Throws<FormatException>(() => Track.Parse(text, new KartConfig()));
        }

        [Test]
        public void ParseRejectsASegmentThatIsTooLong()
        {
            var text = "runoff 0\nstation 0 0 5000\nstation 20000 0 5000\nstation 20000 4000 5000\nstation 0 4000 5000\n" +
                       "checkpoint 0\ncheckpoint 1\ncheckpoint 2\ncheckpoint 3\n";
            var error = Assert.Throws<FormatException>(() => Track.Parse(text, new KartConfig()));
            Assert.That(error!.Message, Does.Contain("区間の長さ"));
        }

        private static void AssertOutside(Track track, IntGeometry.Vec2i point, int radius)
        {
            for (var w = 0; w < track.Walls.Length; w++)
            {
                Assert.That(
                    WallContact.CloserThan(point, track.Walls[w].A, track.Walls[w].B, radius),
                    Is.False,
                    $"点 {point} が壁 {w} に食い込んでいます。");
            }
        }
    }
}
