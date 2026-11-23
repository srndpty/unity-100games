using System.Collections.Generic;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>既定のチャンクが、出うる全ての速さ・入口のレーンで通り抜けられる。</summary>
    public sealed class ChunkLibraryTests
    {
        [Test]
        public void DefaultChunks_ArePassableAtEverySpeedTheyCanAppearAt()
        {
            var config = new RunConfig();
            var planner = new Planner(config, new MotionGraph(config));
            var problems = new List<string>();
            var specs = ChunkLibrary.Default();
            for (var i = 0; i < specs.Length; i++)
            {
                var problem = RunRules.FindUnpassable(config, planner, ChunkDef.Parse(i, specs[i], config.MaxTier));
                if (problem != null) { problems.Add(problem); }
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void DefaultRules_Build()
        {
            Assert.That(TestKit.Rules.Chunks.Count, Is.EqualTo(ChunkLibrary.Default().Length));
        }
    }
}
