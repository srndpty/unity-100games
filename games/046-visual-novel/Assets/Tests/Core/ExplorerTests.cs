using System.Linq;
using Game.Core.Runtime;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>網羅探索：全ての選択の列と結末。</summary>
    public sealed class ExplorerTests
    {
        [Test]
        public void EnumeratesEveryChoiceInOrder()
        {
            var script = Lab.Script();
            var r = PathExplorer.Explore(script, 100);
            Assert.That(r.Ok, Is.True, r.Error);
            Assert.That(r.Paths.Select(p => p.ChoicesText), Is.EqualTo(Lab.Of("0", "1", "2")));
            Assert.That(r.Paths.Select(p => script.Endings[p.Ending].Id), Is.EqualTo(Lab.Of("good", "normal", "bad")));
            Assert.That(r.Paths.Select(p => p.TextCount), Is.EqualTo(Lab.Ints(3, 3, 2)));
        }

        [Test]
        public void FinalHashMatchesReplay()
        {
            var script = Lab.Script();
            foreach (var p in PathExplorer.Explore(script, 100).Paths)
            {
                Assert.That(Lab.Play(script, p.Choices.ToArray()).Hash(), Is.EqualTo(p.FinalHash));
            }
        }

        [Test]
        public void StopsAtPathLimit()
        {
            var r = PathExplorer.Explore(Lab.Script(), 2);
            Assert.That(r.Ok, Is.False);
            Assert.That(r.Error, Does.Contain("2 本を超えました"));
        }

        [Test]
        public void CompileResultCarriesPaths()
        {
            Assert.That(Lab.Compile(Lab.Small).Paths.Count, Is.EqualTo(3));
        }
    }
}
