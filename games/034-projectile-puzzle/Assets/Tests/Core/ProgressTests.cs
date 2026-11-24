using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class ProgressTests
    {
        [Test]
        public void OnlyTheFirstStageIsUnlockedAtFirst()
        {
            var progress = new Progress(5);
            Assert.That(progress.Unlocked, Is.EqualTo(1));
            Assert.That(progress.IsUnlocked(1), Is.False);
            Assert.That(progress.Best(0), Is.EqualTo(-1));
            Assert.Throws<InvalidOperationException>(() => progress.Record(1, 100, 1));
        }

        [Test]
        public void WinningUnlocksTheNextAndKeepsTheBest()
        {
            var progress = new Progress(3);
            Assert.That(progress.Record(0, 500, 1), Is.True);
            Assert.That(progress.Unlocked, Is.EqualTo(2));
            Assert.That(progress.Record(0, 400, 3), Is.False);
            Assert.That(progress.Best(0), Is.EqualTo(500));
            Assert.That(progress.Stars(0), Is.EqualTo(3), "星は最多を残す");
            Assert.That(progress.Record(0, 900, 2), Is.True);
            Assert.That(progress.Stars(0), Is.EqualTo(3));
            Assert.That(progress.Total, Is.EqualTo(900));
            progress.Record(1, 10, 1);
            progress.Record(2, 20, 1);
            Assert.That(progress.Unlocked, Is.EqualTo(3));
            Assert.That(progress.AllCleared, Is.True);
        }

        [Test]
        public void InvalidRecordsAreRejected()
        {
            var progress = new Progress(2);
            Assert.Throws<ArgumentOutOfRangeException>(() => progress.Record(0, -1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => progress.Record(0, 1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => progress.Record(0, 1, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => progress.Best(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Progress(0));
        }
    }
}
