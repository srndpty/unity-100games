using System.Collections.Generic;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>題材固有テスト：同一シード・同一操作列なら地形と結果が一致する。</summary>
    public sealed class ReplayTests
    {
        /// <summary>無作為の操作列（無効な操作も混ぜる）。操作の選び方は試合と別の系列。</summary>
        internal static List<Command> RandomCommands(int seed, int count)
        {
            var rng = new GameRng(seed * 7919);
            var commands = new List<Command>();
            for (int i = 0; i < count; i++)
            {
                var roll = rng.Next(100);
                commands.Add(roll switch
                {
                    < 70 => Command.Move((Direction)rng.Next(8)),
                    < 75 => Command.Face((Direction)rng.Next(8)),
                    < 80 => Command.Wait(),
                    < 84 => Command.Pickup(),
                    < 90 => Command.Use(rng.Next(4)),
                    < 95 => Command.Throw(rng.Next(4)),
                    < 98 => Command.Drop(rng.Next(4)),
                    _ => Command.Descend(),
                });
            }

            return commands;
        }

        private static (uint Hash, string Trace) Play(int seed, List<Command> commands)
        {
            var run = Lab.NewRun(seed);
            var trace = new List<string>();
            foreach (var c in commands)
            {
                var outcome = run.Submit(c);
                trace.Add(outcome.Accepted ? "a" : "r");
                if (run.State != RunState.Playing) { break; }
            }

            trace.Add(run.State.ToString());
            trace.Add(run.Floor.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return (run.Hash(), string.Join(",", trace));
        }

        [Test]
        public void SameSeedAndCommandsGiveTheSameResult()
        {
            for (int seed = 1; seed <= 12; seed++)
            {
                var commands = RandomCommands(seed, 600);
                var a = Play(seed, commands);
                var b = Play(seed, commands);
                Assert.That(b.Hash, Is.EqualTo(a.Hash), $"シード {seed}");
                Assert.That(b.Trace, Is.EqualTo(a.Trace));
            }
        }

        [Test]
        public void DifferentSeedsOrCommandsGiveDifferentResults()
        {
            var commands = RandomCommands(1, 300);
            var hashes = new HashSet<uint>();
            for (int seed = 1; seed <= 10; seed++) { hashes.Add(Play(seed, commands).Hash); }
            Assert.That(hashes.Count, Is.EqualTo(10));
            Assert.That(Play(1, RandomCommands(2, 300)).Hash, Is.Not.EqualTo(Play(1, commands).Hash));
        }

        [Test]
        public void ConfigFingerprintIsPartOfTheHash()
        {
            var a = new CrawlRun(new CrawlConfig(), 1);
            var changed = new CrawlConfig { StoneDamage = 8 };
            var b = new CrawlRun(changed, 1);
            Assert.That(b.Hash(), Is.Not.EqualTo(a.Hash()), "盤面が同じでも規則が違えば別のハッシュ");
        }

        [Test]
        public void ConstructorRejectsInvalidConfig()
        {
            Assert.That(() => new CrawlRun(new CrawlConfig { Width = 10 }, 1), Throws.ArgumentException);
            Assert.That(() => new CrawlRun(null!, 1), Throws.ArgumentNullException);
        }
    }
}
